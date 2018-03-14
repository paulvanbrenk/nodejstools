// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudioTools.Project.Automation;
using VsCommands2K = Microsoft.VisualStudio.VSConstants.VSStd2KCmdID;
using VSConstants = Microsoft.VisualStudio.VSConstants;

namespace Microsoft.VisualStudioTools.Project
{
    internal class CommonFileNode : FileNode
    {
        private OAVSProjectItem _vsProjectItem;
        private CommonProjectNode _project;

        public CommonFileNode(CommonProjectNode root, ProjectElement e)
            : base(root, e)
        {
            this._project = root;
        }

        #region properties
        /// <summary>
        /// Returns bool indicating whether this node is of subtype "Form"
        /// </summary>
        public bool IsFormSubType
        {
            get
            {
                var result = this.ItemNode.GetMetadata(ProjectFileConstants.SubType);
                if (!string.IsNullOrEmpty(result) && StringComparer.InvariantCultureIgnoreCase.Equals(result, ProjectFileAttributeValue.Form))
                {
                    return true;
                }
                else
                {
                    return false;
                }
            }
        }
        /// <summary>
        /// Returns the SubType of a dynamic FileNode. It is 
        /// </summary>
        public string SubType
        {
            get
            {
                return this.ItemNode.GetMetadata(ProjectFileConstants.SubType);
            }
            set
            {
                this.ItemNode.SetMetadata(ProjectFileConstants.SubType, value);
            }
        }

        protected internal VSLangProj.VSProjectItem VSProjectItem
        {
            get
            {
                if (null == this._vsProjectItem)
                {
                    this._vsProjectItem = new OAVSProjectItem(this);
                }
                return this._vsProjectItem;
            }
        }

        public override __VSPROVISIONALVIEWINGSTATUS ProvisionalViewingStatus => __VSPROVISIONALVIEWINGSTATUS.PVS_Enabled;

        #endregion

        #region overridden properties

        internal override object Object => this.VSProjectItem;

        #endregion

        #region overridden methods

        protected override bool SupportsIconMonikers => true;
        protected virtual ImageMoniker CodeFileIconMoniker => KnownMonikers.Document;
        protected virtual ImageMoniker StartupCodeFileIconMoniker => this.CodeFileIconMoniker;
        protected virtual ImageMoniker FormFileIconMoniker => KnownMonikers.WindowsForm;
        protected override ImageMoniker GetIconMoniker(bool open)
        {
            if (this.ItemNode.IsExcluded)
            {
                return KnownMonikers.HiddenFile;
            }
            else if (!File.Exists(this.Url))
            {
                return KnownMonikers.DocumentWarning;
            }
            else if (this.IsFormSubType)
            {
                return this.FormFileIconMoniker;
            }
            else if (this._project.IsCodeFile(this.FileName))
            {
                if (CommonUtils.IsSamePath(this.Url, this._project.GetStartupFile()))
                {
                    return this.StartupCodeFileIconMoniker;
                }
                else
                {
                    return this.CodeFileIconMoniker;
                }
            }
            return default(ImageMoniker);
        }

        /// <summary>
        /// Open a file depending on the SubType property associated with the file item in the project file
        /// </summary>
        protected override void DoDefaultAction()
        {
            var manager = this.GetDocumentManager() as FileDocumentManager;
            Utilities.CheckNotNull(manager, "Could not get the FileDocumentManager");

            var viewGuid =
                (this.IsFormSubType ? VSConstants.LOGVIEWID_Designer : VSConstants.LOGVIEWID_Code);
            manager.Open(false, false, viewGuid, out var frame, WindowFrameShowAction.Show);
        }

        private static Guid CLSID_VsTextBuffer = new Guid("{8E7B96A8-E33D-11d0-A6D5-00C04FB67F6A}");

        /// <summary>
        /// Gets the text buffer for the file opening the document if necessary.
        /// </summary>
        public ITextBuffer GetTextBuffer(bool create = true)
        {
            // http://pytools.codeplex.com/workitem/672
            // When we FindAndLockDocument we marshal on the main UI thread, and the docdata we get
            // back is marshalled back so that we'll marshal any calls on it back.  When we pass it
            // into IVsEditorAdaptersFactoryService we don't go through a COM boundary (it's a managed
            // call) and we therefore don't get the marshaled value, and it doesn't know what we're
            // talking about.  So run the whole operation on the UI thread.
            return this.ProjectMgr.Site.GetUIThread().Invoke(() => GetTextBufferOnUIThread(create));
        }

        private ITextBuffer GetTextBufferOnUIThread(bool create)
        {
            var textMgr = (IVsTextManager)GetService(typeof(SVsTextManager));
            var model = GetService(typeof(SComponentModel)) as IComponentModel;
            var adapter = model.GetService<IVsEditorAdaptersFactoryService>();

            if (this.ProjectMgr.GetService(typeof(SVsRunningDocumentTable)) is IVsRunningDocumentTable rdt)
            {
                IVsPersistDocData persistDocData;
                uint cookie;
                var docInRdt = true;
                var docData = IntPtr.Zero;
                var hr = NativeMethods.E_FAIL;
                try
                {
                    //Getting a read lock on the document. Must be released later.
                    hr = rdt.FindAndLockDocument((uint)_VSRDTFLAGS.RDT_ReadLock, GetMkDocument(), out var hier, out var itemid, out docData, out cookie);
                    if (ErrorHandler.Failed(hr) || docData == IntPtr.Zero)
                    {
                        if (!create)
                        {
                            return null;
                        }
                        var iid = VSConstants.IID_IUnknown;
                        cookie = 0;
                        docInRdt = false;
                        var localReg = this.ProjectMgr.GetService(typeof(SLocalRegistry)) as ILocalRegistry;
                        ErrorHandler.ThrowOnFailure(localReg.CreateInstance(CLSID_VsTextBuffer, null, ref iid, (uint)CLSCTX.CLSCTX_INPROC_SERVER, out docData));
                    }
                    persistDocData = Marshal.GetObjectForIUnknown(docData) as IVsPersistDocData;
                }
                finally
                {
                    if (docData != IntPtr.Zero)
                    {
                        Marshal.Release(docData);
                    }
                }

                //Try to get the Text lines
                var srpTextLines = persistDocData as IVsTextLines;
                if (srpTextLines == null)
                {
                    // Try getting a text buffer provider first
                    if (persistDocData is IVsTextBufferProvider srpTextBufferProvider)
                    {
                        hr = srpTextBufferProvider.GetTextBuffer(out srpTextLines);
                    }
                }

                // Unlock the document in the RDT if necessary
                if (docInRdt && rdt != null)
                {
                    ErrorHandler.ThrowOnFailure(rdt.UnlockDocument((uint)(_VSRDTFLAGS.RDT_ReadLock | _VSRDTFLAGS.RDT_Unlock_NoSave), cookie));
                }

                if (srpTextLines != null)
                {
                    return adapter.GetDocumentBuffer(srpTextLines);
                }
            }

            var view = GetTextView();

            return view.TextBuffer;
        }

        public IWpfTextView GetTextView()
        {
            var model = GetService(typeof(SComponentModel)) as IComponentModel;
            var adapter = model.GetService<IVsEditorAdaptersFactoryService>();
            var uiShellOpenDocument = GetService(typeof(SVsUIShellOpenDocument)) as IVsUIShellOpenDocument;

            VsShellUtilities.OpenDocument(
                this.ProjectMgr.Site,
                this.GetMkDocument(),
                Guid.Empty,
                out var hierarchy,
                out var itemid,
                out var pWindowFrame,
                out var viewAdapter);

            ErrorHandler.ThrowOnFailure(pWindowFrame.Show());
            return adapter.GetWpfTextView(viewAdapter);
        }

        public new CommonProjectNode ProjectMgr => (CommonProjectNode)base.ProjectMgr;

        /// <summary>
        /// Handles the exclude from project command.
        /// </summary>
        /// <returns></returns>
        internal override int ExcludeFromProject()
        {
            Debug.Assert(this.ProjectMgr != null, "The project item " + this.ToString() + " has not been initialised correctly. It has a null ProjectMgr");
            if (!this.ProjectMgr.QueryEditProjectFile(false) ||
                !this.ProjectMgr.Tracker.CanRemoveItems(new[] { this.Url }, new[] { VSQUERYREMOVEFILEFLAGS.VSQUERYREMOVEFILEFLAGS_NoFlags }))
            {
                return VSConstants.E_FAIL;
            }

            ResetNodeProperties();
            this.ItemNode.RemoveFromProjectFile();
            if (!File.Exists(this.Url) || this.IsLinkFile)
            {
                this.ProjectMgr.OnItemDeleted(this);
                this.Parent.RemoveChild(this);
            }
            else
            {
                this.ItemNode = new AllFilesProjectElement(this.Url, this.ItemNode.ItemTypeName, this.ProjectMgr);
                if (!this.ProjectMgr.IsShowingAllFiles)
                {
                    this.IsVisible = false;
                    this.ProjectMgr.OnInvalidateItems(this.Parent);
                }
                this.ProjectMgr.ReDrawNode(this, UIHierarchyElement.Icon);
                this.ProjectMgr.OnPropertyChanged(this, (int)__VSHPROPID.VSHPROPID_IsNonMemberItem, 0);
            }
            ((IVsUIShell)GetService(typeof(SVsUIShell))).RefreshPropertyBrowser(0);
            return VSConstants.S_OK;
        }

        internal override int IncludeInProject(bool includeChildren)
        {
            if (this.Parent.ItemNode != null && this.Parent.ItemNode.IsExcluded)
            {
                // if our parent is excluded it needs to first be included
                var hr = this.Parent.IncludeInProject(false);
                if (ErrorHandler.Failed(hr))
                {
                    return hr;
                }
            }

            if (!this.ProjectMgr.QueryEditProjectFile(false) ||
                !this.ProjectMgr.Tracker.CanAddItems(new[] { this.Url }, new[] { VSQUERYADDFILEFLAGS.VSQUERYADDFILEFLAGS_NoFlags }))
            {
                return VSConstants.E_FAIL;
            }

            ResetNodeProperties();

            this.ItemNode = this.ProjectMgr.CreateMsBuildFileItem(
                CommonUtils.GetRelativeFilePath(this.ProjectMgr.ProjectHome, this.Url), this.ProjectMgr.GetItemType(this.Url)
            );

            this.IsVisible = true;
            this.ProjectMgr.ReDrawNode(this, UIHierarchyElement.Icon);
            this.ProjectMgr.OnPropertyChanged(this, (int)__VSHPROPID.VSHPROPID_IsNonMemberItem, 0);

            // https://nodejstools.codeplex.com/workitem/273, refresh the property browser...
            ((IVsUIShell)GetService(typeof(SVsUIShell))).RefreshPropertyBrowser(0);

            if (CommonUtils.IsSamePath(this.ProjectMgr.GetStartupFile(), this.Url))
            {
                this.ProjectMgr.BoldItem(this, true);
            }

            // On include, the file should be added to source control.
            this.ProjectMgr.Tracker.OnItemAdded(this.Url, VSADDFILEFLAGS.VSADDFILEFLAGS_NoFlags);

            return VSConstants.S_OK;
        }

        /// <summary>
        /// Handles the menuitems
        /// </summary>
        internal override int QueryStatusOnNode(Guid guidCmdGroup, uint cmd, IntPtr pCmdText, ref QueryStatusResult result)
        {
            if (guidCmdGroup == Microsoft.VisualStudio.Shell.VsMenus.guidStandardCommandSet2K)
            {
                switch ((VsCommands2K)cmd)
                {
                    case VsCommands2K.RUNCUSTOMTOOL:
                        result |= QueryStatusResult.NOTSUPPORTED | QueryStatusResult.INVISIBLE;
                        return VSConstants.S_OK;
                    case VsCommands2K.EXCLUDEFROMPROJECT:
                        if (this.ItemNode.IsExcluded)
                        {
                            result |= QueryStatusResult.NOTSUPPORTED | QueryStatusResult.INVISIBLE;
                            return VSConstants.S_OK;
                        }
                        break;
                    case VsCommands2K.INCLUDEINPROJECT:
                        if (this.ItemNode.IsExcluded)
                        {
                            result |= QueryStatusResult.SUPPORTED | QueryStatusResult.ENABLED;
                            return VSConstants.S_OK;
                        }
                        break;
                }
            }

            return base.QueryStatusOnNode(guidCmdGroup, cmd, pCmdText, ref result);
        }

        /// <summary>
        /// Common File Node can only be deleted from file system.
        /// </summary>        
        internal override bool CanDeleteItem(__VSDELETEITEMOPERATION deleteOperation)
        {
            if (this.IsLinkFile)
            {
                // we don't delete link items, we only remove them from the project.  If we were
                // to return true when queried for both delete from storage and remove from project
                // the user would be prompted about which they would like to do.
                return deleteOperation == __VSDELETEITEMOPERATION.DELITEMOP_RemoveFromProject;
            }
            return deleteOperation == __VSDELETEITEMOPERATION.DELITEMOP_DeleteFromStorage;
        }
        #endregion

        public override int QueryService(ref Guid guidService, out object result)
        {
            if (guidService == typeof(VSLangProj.VSProject).GUID)
            {
                result = this.ProjectMgr.VSProject;
                return VSConstants.S_OK;
            }

            return base.QueryService(ref guidService, out result);
        }
    }
}
