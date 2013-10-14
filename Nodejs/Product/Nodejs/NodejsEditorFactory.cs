﻿/* ****************************************************************************
 *
 * Copyright (c) Microsoft Corporation. 
 *
 * This source code is subject to terms and conditions of the Apache License, Version 2.0. A 
 * copy of the license can be found in the License.html file at the root of this distribution. If 
 * you cannot locate the Apache License, Version 2.0, please send an email to 
 * vspython@microsoft.com. By using this source code in any fashion, you are agreeing to be bound 
 * by the terms of the Apache License, Version 2.0.
 *
 * You must not remove this notice, or any other, from this software.
 *
 * ***************************************************************************/

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.NodejsTools.Project;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Operations;
using Microsoft.VisualStudio.Text.Projection;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudio.Utilities;
using Microsoft.VisualStudioTools;
using IOleServiceProvider = Microsoft.VisualStudio.OLE.Interop.IServiceProvider;

namespace Microsoft.NodejsTools {
    /// <summary>
    /// Common factory for creating our editor
    /// </summary>    
    [Guid(GuidList.guidNodeEditorFactoryString)]
    class NodejsEditorFactory : IVsEditorFactory {
        private NodejsPackage _package;
        private ServiceProvider _serviceProvider;
        private readonly bool _promptEncodingOnLoad;

        public NodejsEditorFactory(NodejsPackage package) {
            _package = package;
        }

        public NodejsEditorFactory(NodejsPackage package, bool promptEncodingOnLoad) {
            _package = package;
            _promptEncodingOnLoad = promptEncodingOnLoad;
        }

        #region IVsEditorFactory Members

        public virtual int SetSite(Microsoft.VisualStudio.OLE.Interop.IServiceProvider psp) {
            _serviceProvider = new ServiceProvider(psp);
            return VSConstants.S_OK;
        }

        public virtual object GetService(Type serviceType) {
            return _serviceProvider.GetService(serviceType);
        }

        // This method is called by the Environment (inside IVsUIShellOpenDocument::
        // OpenStandardEditor and OpenSpecificEditor) to map a LOGICAL view to a 
        // PHYSICAL view. A LOGICAL view identifies the purpose of the view that is
        // desired (e.g. a view appropriate for Debugging [LOGVIEWID_Debugging], or a 
        // view appropriate for text view manipulation as by navigating to a find
        // result [LOGVIEWID_TextView]). A PHYSICAL view identifies an actual type 
        // of view implementation that an IVsEditorFactory can create. 
        //
        // NOTE: Physical views are identified by a string of your choice with the 
        // one constraint that the default/primary physical view for an editor  
        // *MUST* use a NULL string as its physical view name (*pbstrPhysicalView = NULL).
        //
        // NOTE: It is essential that the implementation of MapLogicalView properly
        // validates that the LogicalView desired is actually supported by the editor.
        // If an unsupported LogicalView is requested then E_NOTIMPL must be returned.
        //
        // NOTE: The special Logical Views supported by an Editor Factory must also 
        // be registered in the local registry hive. LOGVIEWID_Primary is implicitly 
        // supported by all editor types and does not need to be registered.
        // For example, an editor that supports a ViewCode/ViewDesigner scenario
        // might register something like the following:
        //        HKLM\Software\Microsoft\VisualStudio\9.0\Editors\
        //            {...guidEditor...}\
        //                LogicalViews\
        //                    {...LOGVIEWID_TextView...} = s ''
        //                    {...LOGVIEWID_Code...} = s ''
        //                    {...LOGVIEWID_Debugging...} = s ''
        //                    {...LOGVIEWID_Designer...} = s 'Form'
        //
        public virtual int MapLogicalView(ref Guid logicalView, out string physicalView) {
            // initialize out parameter
            physicalView = null;

            bool isSupportedView = false;
            // Determine the physical view
            if (VSConstants.LOGVIEWID_Primary == logicalView ||
                VSConstants.LOGVIEWID_Debugging == logicalView ||
                VSConstants.LOGVIEWID_Code == logicalView ||
                VSConstants.LOGVIEWID_TextView == logicalView) {
                // primary view uses NULL as pbstrPhysicalView
                isSupportedView = true;
            } else if (VSConstants.LOGVIEWID_Designer == logicalView) {
                physicalView = "Design";
                isSupportedView = true;
            }

            if (isSupportedView)
                return VSConstants.S_OK;
            else {
                // E_NOTIMPL must be returned for any unrecognized rguidLogicalView values
                return VSConstants.E_NOTIMPL;
            }
        }

        public virtual int Close() {
            return VSConstants.S_OK;
        }

        /// <summary>
        /// 
        /// </summary>
        /// <param name="grfCreateDoc"></param>
        /// <param name="pszMkDocument"></param>
        /// <param name="pszPhysicalView"></param>
        /// <param name="pvHier"></param>
        /// <param name="itemid"></param>
        /// <param name="punkDocDataExisting"></param>
        /// <param name="ppunkDocView"></param>
        /// <param name="ppunkDocData"></param>
        /// <param name="pbstrEditorCaption"></param>
        /// <param name="pguidCmdUI"></param>
        /// <param name="pgrfCDW"></param>
        /// <returns></returns>
        public virtual int CreateEditorInstance(
                        uint createEditorFlags,
                        string documentMoniker,
                        string physicalView,
                        IVsHierarchy hierarchy,
                        uint itemid,
                        System.IntPtr docDataExisting,
                        out System.IntPtr docView,
                        out System.IntPtr docData,
                        out string editorCaption,
                        out Guid commandUIGuid,
                        out int createDocumentWindowFlags) {
            // Initialize output parameters
            docView = IntPtr.Zero;
            docData = IntPtr.Zero;
            commandUIGuid = this.GetType().GUID;
            createDocumentWindowFlags = 0;
            editorCaption = null;

            // Validate inputs
            if ((createEditorFlags & (VSConstants.CEF_OPENFILE | VSConstants.CEF_SILENT)) == 0) {
                return VSConstants.E_INVALIDARG;
            }

            // Get a text buffer
            IVsTextLines textLines = GetTextBuffer(docDataExisting, documentMoniker);

            // Assign docData IntPtr to either existing docData or the new text buffer
            if (docDataExisting != IntPtr.Zero) {
                docData = docDataExisting;
                Marshal.AddRef(docData);
            } else {
                docData = Marshal.GetIUnknownForObject(textLines);
            }

            try {
                docView = CreateDocumentView(documentMoniker, physicalView, hierarchy, itemid, textLines, docDataExisting == IntPtr.Zero, out editorCaption, out commandUIGuid);
            } finally {
                if (docView == IntPtr.Zero) {
                    if (docDataExisting != docData && docData != IntPtr.Zero) {
                        // Cleanup the instance of the docData that we have addref'ed
                        Marshal.Release(docData);
                        docData = IntPtr.Zero;
                    }
                }
            }
            return VSConstants.S_OK;
        }


        #endregion

        #region Helper methods

        private IVsTextLines GetTextBuffer(System.IntPtr docDataExisting, string filename) {
            IVsTextLines textLines;
            if (docDataExisting == IntPtr.Zero) {
                // Create a new IVsTextLines buffer.
                Type textLinesType = typeof(IVsTextLines);
                Guid riid = textLinesType.GUID;
                Guid clsid = typeof(VsTextBufferClass).GUID;
                textLines = _package.CreateInstance(ref clsid, ref riid, textLinesType) as IVsTextLines;

                // set the buffer's site
                ((IObjectWithSite)textLines).SetSite(_serviceProvider.GetService(typeof(IOleServiceProvider)));
            } else {
                // Use the existing text buffer
                Object dataObject = Marshal.GetObjectForIUnknown(docDataExisting);
                textLines = dataObject as IVsTextLines;
                if (textLines == null) {
                    // Try get the text buffer from textbuffer provider
                    IVsTextBufferProvider textBufferProvider = dataObject as IVsTextBufferProvider;
                    if (textBufferProvider != null) {
                        textBufferProvider.GetTextBuffer(out textLines);
                    }
                }
                if (textLines == null) {
                    // Unknown docData type then, so we have to force VS to close the other editor.
                    ErrorHandler.ThrowOnFailure((int)VSConstants.VS_E_INCOMPATIBLEDOCDATA);
                }

            }
            return textLines;
        }

        private IntPtr CreateDocumentView(string documentMoniker, string physicalView, IVsHierarchy hierarchy, uint itemid, IVsTextLines textLines, bool createdDocData, out string editorCaption, out Guid cmdUI) {
            //Init out params
            editorCaption = string.Empty;
            cmdUI = Guid.Empty;

            if (string.IsNullOrEmpty(physicalView)) {
                // create code window as default physical view
                return CreateCodeView(documentMoniker, textLines, hierarchy, itemid, createdDocData, ref editorCaption, ref cmdUI);
            }

            // We couldn't create the view
            // Return special error code so VS can try another editor factory.
            ErrorHandler.ThrowOnFailure((int)VSConstants.VS_E_UNSUPPORTEDFORMAT);

            return IntPtr.Zero;
        }

        private IntPtr CreateCodeView(string documentMoniker, IVsTextLines textLines, IVsHierarchy hierarchy, uint itemid, bool createdDocData, ref string editorCaption, ref Guid cmdUI) {
            Type codeWindowType = typeof(IVsCodeWindow);
            Guid riid = codeWindowType.GUID;
            Guid clsid = typeof(VsCodeWindowClass).GUID;
            var compModel = (IComponentModel)_package.GetService(typeof(SComponentModel));
            var adapterService = compModel.GetService<IVsEditorAdaptersFactoryService>();

            var window = adapterService.CreateVsCodeWindowAdapter((IOleServiceProvider)_serviceProvider.GetService(typeof(IOleServiceProvider)));
            ErrorHandler.ThrowOnFailure(window.SetBuffer(textLines));
            ErrorHandler.ThrowOnFailure(window.SetBaseEditorCaption(null));
            ErrorHandler.ThrowOnFailure(window.GetEditorCaption(READONLYSTATUS.ROSTATUS_Unknown, out editorCaption));

            IVsUserData userData = textLines as IVsUserData;
            if (userData != null) {
                if (_promptEncodingOnLoad) {
                    var guid = VSConstants.VsTextBufferUserDataGuid.VsBufferEncodingPromptOnLoad_guid;
                    userData.SetData(ref guid, (uint)1);
                }
            }
            var textMgr = (IVsTextManager)_package.GetService(typeof(SVsTextManager));

            var bufferEventListener = new TextBufferEventListener(compModel, textLines, textMgr, window, hierarchy, itemid);
            if (!createdDocData) {
                // we have a pre-created buffer, go ahead and initialize now as the buffer already
                // exists and is initialized.
                bufferEventListener.OnLoadCompleted(0);
            }

            cmdUI = VSConstants.GUID_TextEditorFactory;

            return Marshal.GetIUnknownForObject(window);
        }

        #endregion

        /// <summary>
        /// Listens for the text buffer to finish loading and then sets up our projection
        /// buffer.
        /// </summary>
        internal sealed class TextBufferEventListener : IVsTextBufferDataEvents {
            private readonly IVsTextLines _textLines;
            private readonly uint _cookie;
            private readonly IConnectionPoint _cp;
            private readonly IComponentModel _compModel;
            private readonly IVsTextManager _textMgr;
            private readonly IVsCodeWindow _window;
            private readonly IVsHierarchy _hierarchy;
            private readonly uint _itemid;

            public TextBufferEventListener(IComponentModel compModel, IVsTextLines textLines, IVsTextManager textMgr, IVsCodeWindow window, IVsHierarchy hierarchy, uint itemid) {
                _textLines = textLines;
                _compModel = compModel;
                _textMgr = textMgr;
                _window = window;
                _hierarchy = hierarchy;
                _itemid = itemid;

                var cpc = textLines as IConnectionPointContainer;
                var bufferEventsGuid = typeof(IVsTextBufferDataEvents).GUID;
                cpc.FindConnectionPoint(ref bufferEventsGuid, out _cp);
                _cp.Advise(this, out _cookie);
            }

            #region IVsTextBufferDataEvents

            public void OnFileChanged(uint grfChange, uint dwFileAttrs) {
            }

            public int OnLoadCompleted(int fReload) {
                _cp.Unadvise(_cookie);

                var adapterService = _compModel.GetService<IVsEditorAdaptersFactoryService>();
                ITextBuffer diskBuffer = adapterService.GetDocumentBuffer(_textLines);

                var factService = _compModel.GetService<IProjectionBufferFactoryService>();

                var contentRegistry = _compModel.GetService<IContentTypeRegistryService>();

                var fileExtRegistry = _compModel.GetService<IFileExtensionRegistryService>();

                IEditorOperationsFactoryService factory = _compModel.GetService<IEditorOperationsFactoryService>();

                IContentType contentType = SniffContentType(diskBuffer) ??
                                           contentRegistry.GetContentType("JavaScript");

                var proj = VsExtensions.GetCommonProject(Extensions.GetProject(_hierarchy)) as NodejsProjectNode;

                var projBuffer = new NodejsProjectionBuffer(
                    contentRegistry, 
                    factService, 
                    diskBuffer, 
                    _compModel.GetService<IBufferGraphFactoryService>(), 
                    contentType, 
                    proj != null ? proj._referenceFilename : NodejsPackage.NodejsReferencePath
                );

                diskBuffer.Properties.AddProperty(typeof(NodejsProjectionBuffer), projBuffer);

                Guid langSvcGuid = NodejsPackage._jsLangSvcGuid;
                _textLines.SetLanguageServiceID(ref langSvcGuid);

                adapterService.SetDataBuffer(_textLines, projBuffer.EllisionBuffer);

                diskBuffer.ChangeContentType(contentRegistry.GetContentType("text"), null);

                IVsTextView view;
                ErrorHandler.ThrowOnFailure(_window.GetPrimaryView(out view));
                var wpfView = adapterService.GetWpfTextView(view);
                var intellisenseStack = _compModel.GetService<IIntellisenseSessionStackMapService>().GetStackForTextView(wpfView);
                EditFilter editFilter = new EditFilter(wpfView, factory.GetEditorOperations(wpfView), intellisenseStack, _compModel);
                editFilter.AttachKeyboardFilter(view);

                return VSConstants.S_OK;
            }

            private IContentType SniffContentType(ITextBuffer diskBuffer) {
                // try and sniff the content type from a double extension, and if we can't
                // do that then return null to indicate we couldn't figure out the type,
                // then we'll default to JavaScript.
                IContentType contentType = null;
                ITextDocument textDocument;
                if (diskBuffer.Properties.TryGetProperty<ITextDocument>(typeof(ITextDocument), out textDocument)) {
                    string subExt = Path.GetExtension(textDocument.FilePath).Substring(1);

                    var fileExtRegistry = _compModel.GetService<IFileExtensionRegistryService>();

                    contentType = fileExtRegistry.GetContentTypeForExtension(subExt);
                }
                return contentType;
            }

            #endregion
        }
    }


    [Guid("C8576E92-EFB6-4414-8F63-C84D474A539E")]
    class NodejsEditorFactoryPromptForEncoding : NodejsEditorFactory {
        public NodejsEditorFactoryPromptForEncoding(NodejsPackage package) : base(package, true) { }
        public override int CreateEditorInstance(uint createEditorFlags, string documentMoniker, string physicalView, VisualStudio.Shell.Interop.IVsHierarchy hierarchy, uint itemid, IntPtr docDataExisting, out IntPtr docView, out IntPtr docData, out string editorCaption, out Guid commandUIGuid, out int createDocumentWindowFlags) {
            if (docDataExisting != IntPtr.Zero) {
                docView = IntPtr.Zero;
                docData = IntPtr.Zero;
                editorCaption = null;
                commandUIGuid = Guid.Empty;
                createDocumentWindowFlags = 0;
                return VSConstants.VS_E_INCOMPATIBLEDOCDATA;
            }

            return base.CreateEditorInstance(createEditorFlags, documentMoniker, physicalView, hierarchy, itemid, docDataExisting, out docView, out docData, out editorCaption, out commandUIGuid, out createDocumentWindowFlags);
        }
    }
}