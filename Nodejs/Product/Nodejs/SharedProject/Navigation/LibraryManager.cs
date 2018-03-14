﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.ComponentModel.Design;
using System.Diagnostics.Contracts;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ComponentModelHost;
using Microsoft.VisualStudio.Editor;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.TextManager.Interop;
using Microsoft.VisualStudioTools.Project;

namespace Microsoft.VisualStudioTools.Navigation
{
    /// <summary>
    /// Inplementation of the service that builds the information to expose to the symbols
    /// navigation tools (class view or object browser) from the source files inside a
    /// hierarchy.
    /// </summary>
    internal abstract partial class LibraryManager : IDisposable, IVsRunningDocTableEvents
    {
        private readonly CommonPackage _package;
        private readonly Dictionary<uint, TextLineEventListener> _documents;
        private readonly Dictionary<IVsHierarchy, HierarchyInfo> _hierarchies = new Dictionary<IVsHierarchy, HierarchyInfo>();
        private readonly Dictionary<ModuleId, LibraryNode> _files;
        private readonly Library _library;
        private readonly IVsEditorAdaptersFactoryService _adapterFactory;
        private uint _objectManagerCookie;
        private uint _runningDocTableCookie;

        public LibraryManager(CommonPackage package)
        {
            Contract.Assert(package != null);
            this._package = package;
            this._documents = new Dictionary<uint, TextLineEventListener>();
            this._library = new Library(new Guid(CommonConstants.LibraryGuid));
            this._library.LibraryCapabilities = (_LIB_FLAGS2)_LIB_FLAGS.LF_PROJECT;
            this._files = new Dictionary<ModuleId, LibraryNode>();

            var model = ((IServiceContainer)package).GetService(typeof(SComponentModel)) as IComponentModel;
            this._adapterFactory = model.GetService<IVsEditorAdaptersFactoryService>();

            // Register our library now so it'll be available for find all references
            RegisterLibrary();
        }

        protected abstract LibraryNode CreateLibraryNode(LibraryNode parent, IScopeNode subItem, string namePrefix, IVsHierarchy hierarchy, uint itemid);

        public virtual LibraryNode CreateFileLibraryNode(LibraryNode parent, HierarchyNode hierarchy, string name, string filename, LibraryNodeType libraryNodeType)
        {
            return new LibraryNode(null, name, filename, libraryNodeType);
        }

        private object GetPackageService(Type type)
        {
            return ((System.IServiceProvider)this._package).GetService(type);
        }

        private void RegisterForRDTEvents()
        {
            if (0 != this._runningDocTableCookie)
            {
                return;
            }
            if (GetPackageService(typeof(SVsRunningDocumentTable)) is IVsRunningDocumentTable rdt)
            {
                // Do not throw here in case of error, simply skip the registration.
                rdt.AdviseRunningDocTableEvents(this, out this._runningDocTableCookie);
            }
        }

        private void UnregisterRDTEvents()
        {
            if (0 == this._runningDocTableCookie)
            {
                return;
            }
            if (GetPackageService(typeof(SVsRunningDocumentTable)) is IVsRunningDocumentTable rdt)
            {
                // Do not throw in case of error.
                rdt.UnadviseRunningDocTableEvents(this._runningDocTableCookie);
            }
            this._runningDocTableCookie = 0;
        }

        #region ILibraryManager Members

        public void RegisterHierarchy(IVsHierarchy hierarchy)
        {
            if ((null == hierarchy) || this._hierarchies.ContainsKey(hierarchy))
            {
                return;
            }

            RegisterLibrary();
            var commonProject = hierarchy.GetProject().GetCommonProject();
            var listener = new HierarchyListener(hierarchy, this);
            var node = this._hierarchies[hierarchy] = new HierarchyInfo(
                listener,
                new ProjectLibraryNode(commonProject)
            );
            this._library.AddNode(node.ProjectLibraryNode);
            listener.StartListening(true);
            RegisterForRDTEvents();
        }

        private void RegisterLibrary()
        {
            if (0 == this._objectManagerCookie)
            {
                var objManager = GetPackageService(typeof(SVsObjectManager)) as IVsObjectManager2;
                if (null == objManager)
                {
                    return;
                }
                Microsoft.VisualStudio.ErrorHandler.ThrowOnFailure(
                    objManager.RegisterSimpleLibrary(this._library, out this._objectManagerCookie));
            }
        }

        public void UnregisterHierarchy(IVsHierarchy hierarchy)
        {
            if ((null == hierarchy) || !this._hierarchies.ContainsKey(hierarchy))
            {
                return;
            }
            var info = this._hierarchies[hierarchy];
            if (null != info)
            {
                info.Listener.Dispose();
            }
            this._hierarchies.Remove(hierarchy);
            this._library.RemoveNode(info.ProjectLibraryNode);
            if (0 == this._hierarchies.Count)
            {
                UnregisterRDTEvents();
            }
            lock (this._files)
            {
                var keys = new ModuleId[this._files.Keys.Count];
                this._files.Keys.CopyTo(keys, 0);
                foreach (var id in keys)
                {
                    if (hierarchy.Equals(id.Hierarchy))
                    {
                        this._library.RemoveNode(this._files[id]);
                        this._files.Remove(id);
                    }
                }
            }
            // Remove the document listeners.
            var docKeys = new uint[this._documents.Keys.Count];
            this._documents.Keys.CopyTo(docKeys, 0);
            foreach (var id in docKeys)
            {
                var docListener = this._documents[id];
                if (hierarchy.Equals(docListener.FileID.Hierarchy))
                {
                    this._documents.Remove(id);
                    docListener.Dispose();
                }
            }
        }

        public void RegisterLineChangeHandler(uint document,
            TextLineChangeEvent lineChanged, Action<IVsTextLines> onIdle)
        {
            this._documents[document].OnFileChangedImmediate += delegate (object sender, TextLineChange[] changes, int fLast)
            {
                lineChanged(sender, changes, fLast);
            };
            this._documents[document].OnFileChanged += (sender, args) => onIdle(args.TextBuffer);
        }

        #endregion

        #region Library Member Production

        /// <summary>
        /// Overridden in the base class to receive notifications of when a file should
        /// be analyzed for inclusion in the library.  The derived class should queue
        /// the parsing of the file and when it's complete it should call FileParsed
        /// with the provided LibraryTask and an IScopeNode which provides information
        /// about the members of the file.
        /// </summary>
        protected virtual void OnNewFile(LibraryTask task)
        {
        }

        /// <summary>
        /// Called by derived class when a file has been parsed.  The caller should
        /// provide the LibraryTask received from the OnNewFile call and an IScopeNode
        /// which represents the contents of the library.
        /// 
        /// It is safe to call this method from any thread.
        /// </summary>
        protected void FileParsed(LibraryTask task, IScopeNode scope)
        {
            try
            {
                var project = task.ModuleId.Hierarchy.GetProject().GetCommonProject();

                HierarchyNode fileNode = fileNode = project.NodeFromItemId(task.ModuleId.ItemId);
                if (fileNode == null || !this._hierarchies.TryGetValue(task.ModuleId.Hierarchy, out var parent))
                {
                    return;
                }

                var module = CreateFileLibraryNode(
                    parent.ProjectLibraryNode,
                    fileNode,
                    System.IO.Path.GetFileName(task.FileName),
                    task.FileName,
                    LibraryNodeType.Package | LibraryNodeType.Classes
                );

                // TODO: Creating the module tree should be done lazily as needed
                // Currently we replace the entire tree and rely upon the libraries
                // update count to invalidate the whole thing.  We could do this
                // finer grained and only update the changed nodes.  But then we
                // need to make sure we're not mutating lists which are handed out.

                CreateModuleTree(module, scope, task.FileName + ":", task.ModuleId);
                if (null != task.ModuleId)
                {
                    LibraryNode previousItem = null;
                    lock (this._files)
                    {
                        if (this._files.TryGetValue(task.ModuleId, out previousItem))
                        {
                            this._files.Remove(task.ModuleId);
                            parent.ProjectLibraryNode.RemoveNode(previousItem);
                        }
                    }
                }
                parent.ProjectLibraryNode.AddNode(module);
                this._library.Update();
                if (null != task.ModuleId)
                {
                    lock (this._files)
                    {
                        this._files.Add(task.ModuleId, module);
                    }
                }
            }
            catch (COMException)
            {
                // we're shutting down and can't get the project
            }
        }

        private void CreateModuleTree(LibraryNode current, IScopeNode scope, string namePrefix, ModuleId moduleId)
        {
            if ((null == scope) || (null == scope.NestedScopes))
            {
                return;
            }

            foreach (var subItem in scope.NestedScopes)
            {
                var newNode = CreateLibraryNode(current, subItem, namePrefix, moduleId.Hierarchy, moduleId.ItemId);
                var newNamePrefix = namePrefix;

                current.AddNode(newNode);
                if ((newNode.NodeType & LibraryNodeType.Classes) != LibraryNodeType.None)
                {
                    newNamePrefix = namePrefix + newNode.Name + ".";
                }

                // Now use recursion to get the other types.
                CreateModuleTree(newNode, subItem, newNamePrefix, moduleId);
            }
        }
        #endregion

        #region Hierarchy Events

        private void OnNewFile(object sender, HierarchyEventArgs args)
        {
            var hierarchy = sender as IVsHierarchy;
            if (null == hierarchy || IsNonMemberItem(hierarchy, args.ItemID))
            {
                return;
            }

            ITextBuffer buffer = null;
            if (null != args.TextBuffer)
            {
                buffer = this._adapterFactory.GetDocumentBuffer(args.TextBuffer);
            }

            var id = new ModuleId(hierarchy, args.ItemID);
            OnNewFile(new LibraryTask(args.CanonicalName, buffer, new ModuleId(hierarchy, args.ItemID)));
        }

        /// <summary>
        /// Handles the delete event, checking to see if this is a project item.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="args"></param>
        private void OnDeleteFile(object sender, HierarchyEventArgs args)
        {
            var hierarchy = sender as IVsHierarchy;
            if (null == hierarchy || IsNonMemberItem(hierarchy, args.ItemID))
            {
                return;
            }

            OnDeleteFile(hierarchy, args);
        }

        /// <summary>
        /// Does a delete w/o checking if it's a non-meber item, for handling the
        /// transition from member item to non-member item.
        /// </summary>
        private void OnDeleteFile(IVsHierarchy hierarchy, HierarchyEventArgs args)
        {
            var id = new ModuleId(hierarchy, args.ItemID);
            LibraryNode node = null;
            lock (this._files)
            {
                if (this._files.TryGetValue(id, out node))
                {
                    this._files.Remove(id);
                    if (this._hierarchies.TryGetValue(hierarchy, out var parent))
                    {
                        parent.ProjectLibraryNode.RemoveNode(node);
                    }
                }
            }
            if (null != node)
            {
                this._library.RemoveNode(node);
            }
        }

        private void IsNonMemberItemChanged(object sender, HierarchyEventArgs args)
        {
            var hierarchy = sender as IVsHierarchy;
            if (null == hierarchy)
            {
                return;
            }

            if (!IsNonMemberItem(hierarchy, args.ItemID))
            {
                OnNewFile(hierarchy, args);
            }
            else
            {
                OnDeleteFile(hierarchy, args);
            }
        }

        /// <summary>
        /// Checks whether this hierarchy item is a project member (on disk items from show all
        /// files aren't considered project members).
        /// </summary>
        protected bool IsNonMemberItem(IVsHierarchy hierarchy, uint itemId)
        {
            var hr = hierarchy.GetProperty(itemId, (int)__VSHPROPID.VSHPROPID_IsNonMemberItem, out var val);
            return ErrorHandler.Succeeded(hr) && (bool)val;
        }

        #endregion

        #region IVsRunningDocTableEvents Members

        public int OnAfterAttributeChange(uint docCookie, uint grfAttribs)
        {
            if ((grfAttribs & (uint)(__VSRDTATTRIB.RDTA_MkDocument)) == (uint)__VSRDTATTRIB.RDTA_MkDocument)
            {
                if (GetPackageService(typeof(SVsRunningDocumentTable)) is IVsRunningDocumentTable rdt)
                {
                    var docData = IntPtr.Zero;
                    int hr;
                    try
                    {
                        hr = rdt.GetDocumentInfo(docCookie, out var flags, out var readLocks, out var editLocks, out var moniker, out var hier, out var itemid, out docData);
                        if (this._documents.TryGetValue(docCookie, out var listner))
                        {
                            listner.FileName = moniker;
                        }
                    }
                    finally
                    {
                        if (IntPtr.Zero != docData)
                        {
                            Marshal.Release(docData);
                        }
                    }
                }
            }
            return VSConstants.S_OK;
        }

        public int OnAfterDocumentWindowHide(uint docCookie, IVsWindowFrame pFrame)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterFirstDocumentLock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
        {
            return VSConstants.S_OK;
        }

        public int OnAfterSave(uint docCookie)
        {
            return VSConstants.S_OK;
        }

        public int OnBeforeDocumentWindowShow(uint docCookie, int fFirstShow, IVsWindowFrame pFrame)
        {
            // Check if this document is in the list of the documents.
            if (this._documents.ContainsKey(docCookie))
            {
                return VSConstants.S_OK;
            }
            // Get the information about this document from the RDT.
            if (GetPackageService(typeof(SVsRunningDocumentTable)) is IVsRunningDocumentTable rdt)
            {
                var hr = rdt.GetDocumentInfo(docCookie, out var flags, out var readLocks, out var writeLoks,
                                             out var documentMoniker, out var hierarchy, out var itemId, out var unkDocData);
                try
                {
                    if (Microsoft.VisualStudio.ErrorHandler.Failed(hr) || (IntPtr.Zero == unkDocData))
                    {
                        return VSConstants.S_OK;
                    }
                    // Check if the herarchy is one of the hierarchies this service is monitoring.
                    if (!this._hierarchies.ContainsKey(hierarchy))
                    {
                        // This hierarchy is not monitored, we can exit now.
                        return VSConstants.S_OK;
                    }

                    // Check the file to see if a listener is required.
                    if (this._package.IsRecognizedFile(documentMoniker))
                    {
                        return VSConstants.S_OK;
                    }

                    // Create the module id for this document.
                    var docId = new ModuleId(hierarchy, itemId);

                    // Try to get the text buffer.
                    var buffer = Marshal.GetObjectForIUnknown(unkDocData) as IVsTextLines;

                    // Create the listener.
                    var listener = new TextLineEventListener(buffer, documentMoniker, docId);
                    // Set the event handler for the change event. Note that there is no difference
                    // between the AddFile and FileChanged operation, so we can use the same handler.
                    listener.OnFileChanged += new EventHandler<HierarchyEventArgs>(this.OnNewFile);
                    // Add the listener to the dictionary, so we will not create it anymore.
                    this._documents.Add(docCookie, listener);
                }
                finally
                {
                    if (IntPtr.Zero != unkDocData)
                    {
                        Marshal.Release(unkDocData);
                    }
                }
            }
            // Always return success.
            return VSConstants.S_OK;
        }

        public int OnBeforeLastDocumentUnlock(uint docCookie, uint dwRDTLockType, uint dwReadLocksRemaining, uint dwEditLocksRemaining)
        {
            if ((0 != dwEditLocksRemaining) || (0 != dwReadLocksRemaining))
            {
                return VSConstants.S_OK;
            }
            if (!this._documents.TryGetValue(docCookie, out var listener) || (null == listener))
            {
                return VSConstants.S_OK;
            }
            using (listener)
            {
                this._documents.Remove(docCookie);
                // Now make sure that the information about this file are up to date (e.g. it is
                // possible that Class View shows something strange if the file was closed without
                // saving the changes).
                var args = new HierarchyEventArgs(listener.FileID.ItemId, listener.FileName);
                OnNewFile(listener.FileID.Hierarchy, args);
            }
            return VSConstants.S_OK;
        }

        #endregion

        public void OnIdle(IOleComponentManager compMgr)
        {
            foreach (var listener in this._documents.Values)
            {
                if (compMgr.FContinueIdle() == 0)
                {
                    break;
                }

                listener.OnIdle();
            }
        }

        #region IDisposable Members

        public void Dispose()
        {
            // Dispose all the listeners.
            foreach (var info in this._hierarchies.Values)
            {
                info.Listener.Dispose();
            }
            this._hierarchies.Clear();

            foreach (var textListener in this._documents.Values)
            {
                textListener.Dispose();
            }
            this._documents.Clear();

            // Remove this library from the object manager.
            if (0 != this._objectManagerCookie)
            {
                if (GetPackageService(typeof(SVsObjectManager)) is IVsObjectManager2 mgr)
                {
                    mgr.UnregisterLibrary(this._objectManagerCookie);
                }
                this._objectManagerCookie = 0;
            }

            // Unregister this object from the RDT events.
            UnregisterRDTEvents();
        }

        #endregion

        private sealed class HierarchyInfo
        {
            public readonly HierarchyListener Listener;
            public readonly ProjectLibraryNode ProjectLibraryNode;

            public HierarchyInfo(HierarchyListener listener, ProjectLibraryNode projectLibNode)
            {
                this.Listener = listener;
                this.ProjectLibraryNode = projectLibNode;
            }
        }
    }
}
