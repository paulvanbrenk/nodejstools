﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Permissions;
using System.Text;
using System.Windows;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using IOleDataObject = Microsoft.VisualStudio.OLE.Interop.IDataObject;
using OleConstants = Microsoft.VisualStudio.OLE.Interop.Constants;

namespace Microsoft.VisualStudioTools.Project
{
    /// <summary>
    /// Manages the CopyPaste and Drag and Drop scenarios for a Project.
    /// </summary>
    /// <remarks>This is a partial class.</remarks>
    internal partial class ProjectNode : IVsUIHierWinClipboardHelperEvents
    {
        private uint copyPasteCookie;
        private DropDataType _dropType;
        /// <summary>
        /// Current state of whether we have initiated a cut/copy from within our hierarchy.
        /// </summary>
        private CopyCutState _copyCutState;
        /// <summary>
        /// True if we initiated a drag from within our project, false if the drag
        /// was initiated from another project or there is currently no drag/drop operation
        /// in progress.
        /// </summary>
        private bool _dragging;

        private enum CopyCutState
        {
            /// <summary>
            /// Nothing has been copied to the clipboard from our project
            /// </summary>
            None,
            /// <summary>
            /// Something was cut from our project
            /// </summary>
            Cut,
            /// <summary>
            /// Something was copied from our project
            /// </summary>
            Copied
        }

        #region override of IVsHierarchyDropDataTarget methods
        /// <summary>
        /// Called as soon as the mouse drags an item over a new hierarchy or hierarchy window
        /// </summary>
        /// <param name="pDataObject">reference to interface IDataObject of the item being dragged</param>
        /// <param name="grfKeyState">Current state of the keyboard and the mouse modifier keys. See docs for a list of possible values</param>
        /// <param name="itemid">Item identifier for the item currently being dragged</param>
        /// <param name="pdwEffect">On entry, a pointer to the current DropEffect. On return, must contain the new valid DropEffect</param>
        /// <returns>If the method succeeds, it returns S_OK. If it fails, it returns an error code.</returns>
        public int DragEnter(IOleDataObject pDataObject, uint grfKeyState, uint itemid, ref uint pdwEffect)
        {
            pdwEffect = (uint)DropEffect.None;

            var item = NodeFromItemId(itemid);

            if (item.GetDragTargetHandlerNode().CanAddFiles)
            {
                this._dropType = QueryDropDataType(pDataObject);
                if (this._dropType != DropDataType.None)
                {
                    pdwEffect = (uint)QueryDropEffect(grfKeyState);
                }
            }

            return VSConstants.S_OK;
        }

        /// <summary>
        /// Called when one or more items are dragged out of the hierarchy or hierarchy window, or when the drag-and-drop operation is cancelled or completed.
        /// </summary>
        /// <returns>If the method succeeds, it returns S_OK. If it fails, it returns an error code.</returns>
        public int DragLeave()
        {
            this._dropType = DropDataType.None;
            return VSConstants.S_OK;
        }

        /// <summary>
        /// Called when one or more items are dragged over the target hierarchy or hierarchy window. 
        /// </summary>
        /// <param name="grfKeyState">Current state of the keyboard keys and the mouse modifier buttons. See <seealso cref="IVsHierarchyDropDataTarget"/></param>
        /// <param name="itemid">Item identifier of the drop data target over which the item is being dragged</param>
        /// <param name="pdwEffect"> On entry, reference to the value of the pdwEffect parameter of the IVsHierarchy object, identifying all effects that the hierarchy supports. 
        /// On return, the pdwEffect parameter must contain one of the effect flags that indicate the result of the drop operation. For a list of pwdEffects values, see <seealso cref="DragEnter"/></param>
        /// <returns>If the method succeeds, it returns S_OK. If it fails, it returns an error code.</returns>
        public int DragOver(uint grfKeyState, uint itemid, ref uint pdwEffect)
        {
            pdwEffect = (uint)DropEffect.None;

            // Dragging items to a project that is being debugged is not supported
            // (see VSWhidbey 144785)            
            var dbgMode = VsShellUtilities.GetDebugMode(this.Site) & ~DBGMODE.DBGMODE_EncMask;
            if (dbgMode == DBGMODE.DBGMODE_Run || dbgMode == DBGMODE.DBGMODE_Break)
            {
                return VSConstants.S_OK;
            }

            if (this.isClosed)
            {
                return VSConstants.E_UNEXPECTED;
            }

            // TODO: We should also analyze if the node being dragged over can accept the drop.

            pdwEffect = (uint)QueryDropEffect(grfKeyState);

            return VSConstants.S_OK;
        }

        /// <summary>
        /// Called when one or more items are dropped into the target hierarchy or hierarchy window when the mouse button is released.
        /// </summary>
        /// <param name="pDataObject">Reference to the IDataObject interface on the item being dragged. This data object contains the data being transferred in the drag-and-drop operation. 
        /// If the drop occurs, then this data object (item) is incorporated into the target hierarchy or hierarchy window.</param>
        /// <param name="grfKeyState">Current state of the keyboard and the mouse modifier keys. See <seealso cref="IVsHierarchyDropDataTarget"/></param>
        /// <param name="itemid">Item identifier of the drop data target over which the item is being dragged</param>
        /// <param name="pdwEffect">Visual effects associated with the drag-and drop-operation, such as a cursor, bitmap, and so on. 
        /// The value of dwEffects passed to the source object via the OnDropNotify method is the value of pdwEffects returned by the Drop method</param>
        /// <returns>If the method succeeds, it returns S_OK. If it fails, it returns an error code. </returns>
        public int Drop(IOleDataObject pDataObject, uint grfKeyState, uint itemid, ref uint pdwEffect)
        {
            if (pDataObject == null)
            {
                return VSConstants.E_INVALIDARG;
            }

            pdwEffect = (uint)DropEffect.None;

            // Get the node that is being dragged over and ask it which node should handle this call
            var targetNode = NodeFromItemId(itemid);
            if (targetNode == null)
            {
                // There is no target node. The drop can not be completed.
                return VSConstants.S_FALSE;
            }

            int returnValue;
            try
            {
                pdwEffect = (uint)QueryDropEffect(grfKeyState);
                var dropDataType = ProcessSelectionDataObject(pDataObject, targetNode, true, (DropEffect)pdwEffect);
                if (dropDataType == DropDataType.None)
                {
                    pdwEffect = (uint)DropEffect.None;
                }

                // If it is a drop from windows and we get any kind of error we return S_FALSE and dropeffect none. This
                // prevents bogus messages from the shell from being displayed
                returnValue = (dropDataType != DropDataType.Shell) ? VSConstants.E_FAIL : VSConstants.S_OK;
            }
            catch (System.IO.FileNotFoundException e)
            {
                Trace.WriteLine("Exception : " + e.Message);

                if (!Utilities.IsInAutomationFunction(this.Site))
                {
                    var message = e.Message;
                    var title = string.Empty;
                    var icon = OLEMSGICON.OLEMSGICON_CRITICAL;
                    var buttons = OLEMSGBUTTON.OLEMSGBUTTON_OK;
                    var defaultButton = OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST;
                    Utilities.ShowMessageBox(this.Site, title, message, icon, buttons, defaultButton);
                }

                returnValue = VSConstants.E_FAIL;
            }

            this._dragging = false;

            return returnValue;
        }
        #endregion

        #region override of IVsHierarchyDropDataSource2 methods
        /// <summary>
        /// Returns information about one or more of the items being dragged
        /// </summary>
        /// <param name="pdwOKEffects">Pointer to a DWORD value describing the effects displayed while the item is being dragged, 
        /// such as cursor icons that change during the drag-and-drop operation. 
        /// For example, if the item is dragged over an invalid target point 
        /// (such as the item's original location), the cursor icon changes to a circle with a line through it. 
        /// Similarly, if the item is dragged over a valid target point, the cursor icon changes to a file or folder.</param>
        /// <param name="ppDataObject">Pointer to the IDataObject interface on the item being dragged. 
        /// This data object contains the data being transferred in the drag-and-drop operation. 
        /// If the drop occurs, then this data object (item) is incorporated into the target hierarchy or hierarchy window.</param>
        /// <param name="ppDropSource">Pointer to the IDropSource interface of the item being dragged.</param>
        /// <returns>If the method succeeds, it returns S_OK. If it fails, it returns an error code.</returns>
        public int GetDropInfo(out uint pdwOKEffects, out IOleDataObject ppDataObject, out IDropSource ppDropSource)
        {
            //init out params
            pdwOKEffects = (uint)DropEffect.None;
            ppDataObject = null;
            ppDropSource = null;

            IOleDataObject dataObject = PackageSelectionDataObject(false);
            if (dataObject == null)
            {
                return VSConstants.E_NOTIMPL;
            }

            this._dragging = true;
            pdwOKEffects = (uint)(DropEffect.Move | DropEffect.Copy);

            ppDataObject = dataObject;
            return VSConstants.S_OK;
        }

        /// <summary>
        /// Notifies clients that the dragged item was dropped. 
        /// </summary>
        /// <param name="fDropped">If true, then the dragged item was dropped on the target. If false, then the drop did not occur.</param>
        /// <param name="dwEffects">Visual effects associated with the drag-and-drop operation, such as cursors, bitmaps, and so on. 
        /// The value of dwEffects passed to the source object via OnDropNotify method is the value of pdwEffects returned by Drop method.</param>
        /// <returns>If the method succeeds, it returns S_OK. If it fails, it returns an error code. </returns>
        public int OnDropNotify(int fDropped, uint dwEffects)
        {
            if (dwEffects == (uint)DropEffect.Move)
            {
                foreach (var item in this.ItemsDraggedOrCutOrCopied)
                {
                    item.Remove(true);
                }
            }
            this.ItemsDraggedOrCutOrCopied.Clear();
            this._dragging = false;

            return VSConstants.S_OK;
        }

        /// <summary>
        /// Allows the drag source to prompt to save unsaved items being dropped. 
        /// Notifies the source hierarchy that information dragged from it is about to be dropped on a target. 
        /// This method is called immediately after the mouse button is released on a drop. 
        /// </summary>
        /// <param name="o">Reference to the IDataObject interface on the item being dragged. 
        /// This data object contains the data being transferred in the drag-and-drop operation. 
        /// If the drop occurs, then this data object (item) is incorporated into the hierarchy window of the new hierarchy.</param>
        /// <param name="dwEffect">Current state of the keyboard and the mouse modifier keys.</param>
        /// <param name="fCancelDrop">If true, then the drop is cancelled by the source hierarchy. If false, then the drop can continue.</param>
        /// <returns>If the method succeeds, it returns S_OK. If it fails, it returns an error code. </returns>
        public int OnBeforeDropNotify(IOleDataObject o, uint dwEffect, out int fCancelDrop)
        {
            // If there is nothing to be dropped just return that drop should be cancelled.
            if (this.ItemsDraggedOrCutOrCopied == null)
            {
                fCancelDrop = 1;
                return VSConstants.S_OK;
            }

            fCancelDrop = 0;
            var dirty = false;
            foreach (var node in this.ItemsDraggedOrCutOrCopied)
            {
                if (node.IsLinkFile)
                {
                    continue;
                }

                var manager = node.GetDocumentManager();
                if (manager != null &&
                    manager.IsDirty &&
                    manager.IsOpenedByUs)
                {
                    dirty = true;
                    break;
                }
            }

            // if there are no dirty docs we are ok to proceed
            if (!dirty)
            {
                return VSConstants.S_OK;
            }

            // Prompt to save if there are dirty docs
            var message = SR.GetString(SR.SaveModifiedDocuments);
            var title = string.Empty;
            var icon = OLEMSGICON.OLEMSGICON_WARNING;
            var buttons = OLEMSGBUTTON.OLEMSGBUTTON_YESNOCANCEL;
            var defaultButton = OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST;
            var result = Utilities.ShowMessageBox(this.Site, title, message, icon, buttons, defaultButton);
            switch (result)
            {
                case NativeMethods.IDYES:
                    break;

                case NativeMethods.IDNO:
                    return VSConstants.S_OK;

                case NativeMethods.IDCANCEL:
                    goto default;

                default:
                    fCancelDrop = 1;
                    this.ItemsDraggedOrCutOrCopied.Clear();
                    return VSConstants.S_OK;
            }

            // Save all dirty documents
            foreach (var node in this.ItemsDraggedOrCutOrCopied)
            {
                var manager = node.GetDocumentManager();
                if (manager != null)
                {
                    manager.Save(true);
                }
            }

            return VSConstants.S_OK;
        }

        #endregion

        #region IVsUIHierWinClipboardHelperEvents Members
        /// <summary>
        /// Called after your cut/copied items has been pasted
        /// </summary>
        ///<param name="wasCut">If true, then the IDataObject has been successfully pasted into a target hierarchy. 
        /// If false, then the cut or copy operation was cancelled.</param>
        /// <param name="dropEffect">Visual effects associated with the drag and drop operation, such as cursors, bitmaps, and so on. 
        /// These should be the same visual effects used in OnDropNotify</param>
        /// <returns>If the method succeeds, it returns S_OK. If it fails, it returns an error code. </returns>
        public virtual int OnPaste(int wasCut, uint dropEffect)
        {
            if (dropEffect == (uint)DropEffect.None)
            {
                return OnClear(wasCut);
            }

            // Check both values here.  If the paste is coming from another project system then
            // they should always pass Move, and we'll know whether or not it's a cut from wasCut.
            // If they copied it from the project system wasCut will be false, and DropEffect
            // will still be Move, resulting in a copy.
            if (wasCut != 0 && dropEffect == (uint)DropEffect.Move)
            {
                // If we just did a cut, then we need to free the data object. Otherwise, we leave it
                // alone so that you can continue to paste the data in new locations.
                CleanAndFlushClipboard();
                foreach (var node in this.ItemsDraggedOrCutOrCopied)
                {
                    node.Remove(true);
                }
                this.ItemsDraggedOrCutOrCopied.Clear();
                ClearCopyCutState();
            }

            return VSConstants.S_OK;
        }

        /// <summary>
        /// Called when your cut/copied operation is canceled
        /// </summary>
        /// <param name="wasCut">This flag informs the source that the Cut method was called (true), 
        /// rather than Copy (false), so the source knows whether to "un-cut-highlight" the items that were cut.</param>
        /// <returns>If the method succeeds, it returns S_OK. If it fails, it returns an error code. </returns>
        public virtual int OnClear(int wasCut)
        {
            if (wasCut != 0)
            {
                AssertHasParentHierarchy();
                var w = UIHierarchyUtilities.GetUIHierarchyWindow(this.site, HierarchyNode.SolutionExplorer);
                if (w != null)
                {
                    foreach (var node in this.ItemsDraggedOrCutOrCopied)
                    {
                        node.ExpandItem(EXPANDFLAGS.EXPF_UnCutHighlightItem);
                    }
                }
            }

            this.ItemsDraggedOrCutOrCopied.Clear();

            ClearCopyCutState();
            return VSConstants.S_OK;
        }
        #endregion

        #region virtual methods

        /// <summary>
        /// Returns a dataobject from selected nodes
        /// </summary>
        /// <param name="cutHighlightItems">boolean that defines if the selected items must be cut</param>
        /// <returns>data object for selected items</returns>
        private DataObject PackageSelectionDataObject(bool cutHighlightItems)
        {
            var sb = new StringBuilder();

            DataObject dataObject = null;

            var selectedNodes = this.GetSelectedNodes();
            if (selectedNodes != null)
            {
                this.InstantiateItemsDraggedOrCutOrCopiedList();

                // If there is a selection package the data
                foreach (var node in selectedNodes)
                {
                    var selectionContent = node.PrepareSelectedNodesForClipBoard();
                    if (selectionContent != null)
                    {
                        sb.Append(selectionContent);
                    }
                }
            }

            // Add the project items first.
            var ptrToItems = this.PackageSelectionData(sb, false);
            if (ptrToItems == IntPtr.Zero)
            {
                return null;
            }

            var fmt = DragDropHelper.CreateFormatEtc(DragDropHelper.CF_VSSTGPROJECTITEMS);
            dataObject = new DataObject();
            dataObject.SetData(fmt, ptrToItems);

            // Now add the project path that sourced data. We just write the project file path.
            var ptrToProjectPath = this.PackageSelectionData(new StringBuilder(this.GetMkDocument()), true);

            if (ptrToProjectPath != IntPtr.Zero)
            {
                dataObject.SetData(DragDropHelper.CreateFormatEtc(DragDropHelper.CF_VSPROJECTCLIPDESCRIPTOR), ptrToProjectPath);
            }

            if (cutHighlightItems)
            {
                var first = true;
                foreach (var node in this.ItemsDraggedOrCutOrCopied)
                {
                    node.ExpandItem(first ? EXPANDFLAGS.EXPF_CutHighlightItem : EXPANDFLAGS.EXPF_AddCutHighlightItem);
                    first = false;
                }
            }
            return dataObject;
        }

        private class ProjectReferenceFileAdder
        {
            /// <summary>
            /// This hierarchy which is having items added/moved
            /// </summary>
            private readonly ProjectNode Project;
            /// <summary>
            /// The node which we're adding/moving the items to
            /// </summary>
            private readonly HierarchyNode TargetNode;
            /// <summary>
            /// The references we're adding, using the format {Guid}|project|folderPath
            /// </summary>
            private readonly string[] ProjectReferences;
            /// <summary>
            /// True if this is the result of a mouse drop, false if this is the result of a paste
            /// </summary>
            private readonly bool MouseDropping;
            /// <summary>
            /// Move or Copy
            /// </summary>
            private readonly DropEffect DropEffect;
            private bool? OverwriteAllItems;

            public ProjectReferenceFileAdder(ProjectNode project, HierarchyNode targetNode, string[] projectReferences, bool mouseDropping, DropEffect dropEffect)
            {
                Utilities.ArgumentNotNull(nameof(targetNode), targetNode);
                Utilities.ArgumentNotNull(nameof(project), project);
                Utilities.ArgumentNotNull(nameof(projectReferences), projectReferences);

                this.TargetNode = targetNode;
                this.Project = project;
                this.ProjectReferences = projectReferences;
                this.MouseDropping = mouseDropping;
                this.DropEffect = dropEffect;
            }

            internal bool AddFiles()
            {
                // Collect all of the additions.
                var additions = new List<Addition>();
                var folders = new List<string>();
                // process folders first
                foreach (var projectReference in this.ProjectReferences)
                {
                    if (projectReference == null)
                    {
                        // bad projectref, bail out
                        return false;
                    }
                    if (CommonUtils.HasEndSeparator(projectReference))
                    {
                        var addition = CanAddFolderFromProjectReference(projectReference);
                        if (addition == null)
                        {
                            return false;
                        }
                        additions.Add(addition);
                        if (addition is FolderAddition folderAddition)
                        {
                            folders.Add(folderAddition.SourceFolder);
                        }
                    }
                }
                foreach (var projectReference in this.ProjectReferences)
                {
                    if (projectReference == null)
                    {
                        // bad projectref, bail out
                        return false;
                    }
                    if (!CommonUtils.HasEndSeparator(projectReference))
                    {
                        var addition = CanAddFileFromProjectReference(projectReference, this.TargetNode.GetDragTargetHandlerNode().FullPathToChildren);
                        if (addition == null)
                        {
                            return false;
                        }
                        var add = true;
                        if (addition is FileAddition fileAddition)
                        {
                            foreach (var folder in folders)
                            {
                                if (fileAddition.SourceMoniker.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                                {
                                    // this will be moved/copied by the folder, it doesn't need another move/copy
                                    add = false;
                                    break;
                                }
                            }
                        }
                        if (add)
                        {
                            additions.Add(addition);
                        }
                    }
                }

                var result = true;
                bool? overwrite = null;
                foreach (var addition in additions)
                {
                    try
                    {
                        addition.DoAddition(ref overwrite);
                    }
                    catch (CancelPasteException)
                    {
                        return false;
                    }
                    if (addition is SkipOverwriteAddition)
                    {
                        result = false;
                    }
                }

                return result;
            }

            [Serializable]
            private sealed class CancelPasteException : Exception
            {
            }

            /// <summary>
            /// Tests to see if we can add the folder to the project.  Returns true if it's ok, false if it's not.
            /// </summary>
            /// <param name="folderToAdd">Project reference (from data object) using the format: {Guid}|project|folderPath</param>
            /// <param name="targetNode">Node to add the new folder to</param>
            private Addition CanAddFolderFromProjectReference(string folderToAdd)
            {
                Utilities.ArgumentNotNullOrEmpty(nameof(folderToAdd), folderToAdd);

                var targetFolderNode = this.TargetNode.GetDragTargetHandlerNode();
                GetPathAndHierarchy(folderToAdd, out var folder, out var sourceHierarchy);

                // Ensure we don't end up in an endless recursion
                if (Utilities.IsSameComObject(this.Project, sourceHierarchy))
                {
                    if (StringComparer.OrdinalIgnoreCase.Equals(folder, targetFolderNode.FullPathToChildren))
                    {
                        if (this.DropEffect == DropEffect.Move &&
                            IsBadMove(targetFolderNode.FullPathToChildren, folder, false))
                        {
                            return null;
                        }
                    }

                    if (targetFolderNode.FullPathToChildren.StartsWith(folder, StringComparison.OrdinalIgnoreCase) &&
                        !StringComparer.OrdinalIgnoreCase.Equals(targetFolderNode.FullPathToChildren, folder))
                    {
                        // dragging a folder into a child, that's not allowed
                        Utilities.ShowMessageBox(
                            this.Project.Site,
                            SR.GetString(SR.CannotMoveIntoSubfolder, CommonUtils.GetFileOrDirectoryName(folder)),
                            null,
                            OLEMSGICON.OLEMSGICON_CRITICAL,
                            OLEMSGBUTTON.OLEMSGBUTTON_OK,
                            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                        return null;
                    }
                }

                var targetPath = Path.Combine(targetFolderNode.FullPathToChildren, CommonUtils.GetFileOrDirectoryName(folder));
                if (File.Exists(targetPath))
                {
                    Utilities.ShowMessageBox(
                       this.Project.Site,
                       SR.GetString(SR.CannotAddFileExists, CommonUtils.GetFileOrDirectoryName(folder)),
                       null,
                       OLEMSGICON.OLEMSGICON_CRITICAL,
                       OLEMSGBUTTON.OLEMSGBUTTON_OK,
                       OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    return null;
                }

                if (Directory.Exists(targetPath))
                {
                    if (this.DropEffect == DropEffect.Move)
                    {
                        if (targetPath == folderToAdd)
                        {
                            CannotMoveSameLocation(folderToAdd);
                        }
                        else
                        {
                            Utilities.ShowMessageBox(
                               this.Project.Site,
                               SR.GetString(SR.CannotMoveFolderExists, CommonUtils.GetFileOrDirectoryName(folder)),
                               null,
                               OLEMSGICON.OLEMSGICON_CRITICAL,
                               OLEMSGBUTTON.OLEMSGBUTTON_OK,
                               OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                        }
                        return null;
                    }

                    var dialog = new OverwriteFileDialog(
                        SR.GetString(SR.OverwriteFilesInExistingFolder, CommonUtils.GetFileOrDirectoryName(folder)),
                        false
                    );
                    dialog.Owner = Application.Current.MainWindow;
                    var res = dialog.ShowDialog();
                    if (res == null)
                    {
                        // cancel, abort the whole copy
                        return null;
                    }
                    else if (!dialog.ShouldOverwrite)
                    {
                        // no, don't copy the folder
                        return SkipOverwriteAddition.Instance;
                    }
                    // otherwise yes, and we'll prompt about the files.
                }

                var targetFileName = CommonUtils.GetFileOrDirectoryName(folder);
                if (Utilities.IsSameComObject(this.Project, sourceHierarchy) &&
                    StringComparer.OrdinalIgnoreCase.Equals(targetFolderNode.FullPathToChildren, folder))
                {
                    // copying a folder onto its self, make a copy
                    targetFileName = GetCopyName(targetFolderNode.FullPathToChildren);
                }

                var additions = new List<Addition>();
                if (ErrorHandler.Failed(sourceHierarchy.ParseCanonicalName(folder, out var folderId)))
                {
                    // the folder may have been deleted between the copy & paste
                    ReportMissingItem(folder);
                    return null;
                }

                if (Path.Combine(targetFolderNode.FullPathToChildren, targetFileName).Length >= NativeMethods.MAX_FOLDER_PATH)
                {
                    Utilities.ShowMessageBox(
                        this.Project.Site,
                        SR.GetString(SR.FolderPathTooLongShortMessage),
                        null,
                        OLEMSGICON.OLEMSGICON_CRITICAL,
                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    return null;
                }

                if (!WalkSourceProjectAndAdd(sourceHierarchy, folderId, targetFolderNode.FullPathToChildren, false, additions, targetFileName))
                {
                    return null;
                }

                if (additions.Count == 1)
                {
                    return (FolderAddition)additions[0];
                }

                Debug.Assert(additions.Count == 0);
                return null;
            }

            private void ReportMissingItem(string folder)
            {
                Utilities.ShowMessageBox(
                    this.Project.Site,
                    SR.GetString(SR.SourceUrlNotFound, CommonUtils.GetFileOrDirectoryName(folder)),
                    null,
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }

            /// <summary>
            /// Recursive method that walk a hierarchy and add items it find to our project.
            /// Note that this is meant as an helper to the Copy&Paste/Drag&Drop functionality.
            /// </summary>
            /// <param name="sourceHierarchy">Hierarchy to walk</param>
            /// <param name="itemId">Item ID where to start walking the hierarchy</param>
            /// <param name="targetNode">Node to start adding to</param>
            /// <param name="addSibblings">Typically false on first call and true after that</param>
            private bool WalkSourceProjectAndAdd(IVsHierarchy sourceHierarchy, uint itemId, string targetPath, bool addSiblings, List<Addition> additions, string name = null)
            {
                Utilities.ArgumentNotNull(nameof(sourceHierarchy), sourceHierarchy);

                if (itemId != VSConstants.VSITEMID_NIL)
                {
                    // Before we start the walk, add the current node
                    object variant = null;

                    // Calculate the corresponding path in our project
                    ErrorHandler.ThrowOnFailure(((IVsProject)sourceHierarchy).GetMkDocument(itemId, out var source));
                    if (name == null)
                    {
                        name = CommonUtils.GetFileOrDirectoryName(source);
                    }

                    ErrorHandler.ThrowOnFailure(sourceHierarchy.GetGuidProperty(itemId, (int)__VSHPROPID.VSHPROPID_TypeGuid, out var guidType));

                    if (this.Project.GetService(typeof(IVsSolution)) is IVsSolution solution)
                    {
                        if (guidType == VSConstants.GUID_ItemType_PhysicalFile)
                        {
                            ErrorHandler.ThrowOnFailure(solution.GetProjrefOfItem(sourceHierarchy, itemId, out var projRef));
                            var addition = CanAddFileFromProjectReference(projRef, targetPath);
                            if (addition == null)
                            {
                                // cancelled
                                return false;
                            }
                            additions.Add(addition);
                        }
                    }

                    // Start with child nodes (depth first)
                    if (guidType == VSConstants.GUID_ItemType_PhysicalFolder)
                    {
                        variant = null;
                        ErrorHandler.ThrowOnFailure(sourceHierarchy.GetProperty(itemId, (int)__VSHPROPID.VSHPROPID_FirstVisibleChild, out variant));
                        var currentItemID = (uint)(int)variant;

                        var nestedAdditions = new List<Addition>();

                        var newPath = Path.Combine(targetPath, name);

                        if (!WalkSourceProjectAndAdd(sourceHierarchy, currentItemID, newPath, true, nestedAdditions))
                        {
                            // cancelled
                            return false;
                        }

                        if (!this.Project.Tracker.CanRenameItem(
                            source,
                            newPath,
                            VSRENAMEFILEFLAGS.VSRENAMEFILEFLAGS_Directory))
                        {
                            return false;
                        }

                        additions.Add(new FolderAddition(this.Project, Path.Combine(targetPath, name), source, this.DropEffect, nestedAdditions.ToArray()));
                    }

                    if (addSiblings)
                    {
                        // Then look at siblings
                        var currentItemID = itemId;
                        while (currentItemID != VSConstants.VSITEMID_NIL)
                        {
                            variant = null;
                            // http://mpfproj10.codeplex.com/workitem/11618 - pass currentItemID instead of itemId
                            ErrorHandler.ThrowOnFailure(sourceHierarchy.GetProperty(currentItemID, (int)__VSHPROPID.VSHPROPID_NextVisibleSibling, out variant));
                            currentItemID = (uint)(int)variant;
                            if (!WalkSourceProjectAndAdd(sourceHierarchy, currentItemID, targetPath, false, additions))
                            {
                                // cancelled
                                return false;
                            }
                        }
                    }
                }
                return true;
            }

            private static string GetCopyName(string existingFullPath)
            {
                string newDir, name, extension;
                if (CommonUtils.HasEndSeparator(existingFullPath))
                {
                    name = CommonUtils.GetFileOrDirectoryName(existingFullPath);
                    extension = "";
                }
                else
                {
                    extension = Path.GetExtension(existingFullPath);
                    name = Path.GetFileNameWithoutExtension(existingFullPath);
                }

                var folder = CommonUtils.GetParent(existingFullPath);
                var copyCount = 1;
                do
                {
                    var newName = name + " - Copy";
                    if (copyCount != 1)
                    {
                        newName += " (" + copyCount + ")";
                    }
                    newName += extension;
                    copyCount++;
                    newDir = Path.Combine(folder, newName);
                } while (File.Exists(newDir) || Directory.Exists(newDir));
                return newDir;
            }

            /// <summary>
            /// This is used to recursively add a folder from an other project.
            /// Note that while we copy the folder content completely, we only
            /// add to the project items which are part of the source project.
            /// </summary>
            private class FolderAddition : Addition
            {
                private readonly ProjectNode Project;
                private readonly string NewFolderPath;
                public readonly string SourceFolder;
                private readonly Addition[] Additions;
                private readonly DropEffect DropEffect;

                public FolderAddition(ProjectNode project, string newFolderPath, string sourceFolder, DropEffect dropEffect, Addition[] additions)
                {
                    this.Project = project;
                    this.NewFolderPath = newFolderPath;
                    this.SourceFolder = sourceFolder;
                    this.Additions = additions;
                    this.DropEffect = dropEffect;
                }

                public override void DoAddition(ref bool? overwrite)
                {
                    var wasExpanded = false;
                    HierarchyNode newNode;
                    var sourceFolder = this.Project.FindNodeByFullPath(this.SourceFolder) as FolderNode;
                    if (sourceFolder == null || this.DropEffect != DropEffect.Move)
                    {
                        newNode = this.Project.CreateFolderNodes(this.NewFolderPath);
                    }
                    else
                    {
                        // Rename the folder & reparent our existing FolderNode w/ potentially w/ a new ID,
                        // but don't update the children as we'll handle that w/ our file additions...
                        wasExpanded = sourceFolder.GetIsExpanded();
                        Directory.CreateDirectory(this.NewFolderPath);
                        sourceFolder.ReparentFolder(this.NewFolderPath);

                        sourceFolder.ExpandItem(wasExpanded ? EXPANDFLAGS.EXPF_ExpandFolder : EXPANDFLAGS.EXPF_CollapseFolder);
                        newNode = sourceFolder;
                    }

                    foreach (var addition in this.Additions)
                    {
                        addition.DoAddition(ref overwrite);
                    }

                    if (sourceFolder != null)
                    {
                        if (sourceFolder.IsNonMemberItem)
                        {
                            // copying or moving an existing excluded folder, new folder
                            // is excluded too.
                            ErrorHandler.ThrowOnFailure(newNode.ExcludeFromProject());
                        }
                        else if (sourceFolder.Parent.IsNonMemberItem)
                        {
                            // We've moved an included folder to a show all files folder,
                            //     add the parent to the project   
                            ErrorHandler.ThrowOnFailure(sourceFolder.Parent.IncludeInProject(false));
                        }

                        if (this.DropEffect == DropEffect.Move)
                        {
                            Directory.Delete(this.SourceFolder);

                            // we just handled the delete, the updated folder has the new filename,
                            // and we don't want to delete where we just moved stuff...
                            this.Project.ItemsDraggedOrCutOrCopied.Remove(sourceFolder);
                        }
                    }

                    // Send OnItemRenamed for the folder now, after all of the children have been renamed
                    this.Project.Tracker.OnItemRenamed(this.SourceFolder, this.NewFolderPath, VSRENAMEFILEFLAGS.VSRENAMEFILEFLAGS_Directory);

                    if (sourceFolder != null && this.Project.ParentHierarchy != null)
                    {
                        sourceFolder.ExpandItem(wasExpanded ? EXPANDFLAGS.EXPF_ExpandFolder : EXPANDFLAGS.EXPF_CollapseFolder);
                    }
                }
            }

            /// <summary>
            /// Given the reference used for drag and drop returns the path to the item and it's
            /// containing hierarchy.
            /// </summary>
            /// <param name="projectReference"></param>
            /// <param name="path"></param>
            /// <param name="sourceHierarchy"></param>
            private void GetPathAndHierarchy(string projectReference, out string path, out IVsHierarchy sourceHierarchy)
            {

                GetPathAndProjectId(projectReference, out var projectInstanceGuid, out path);
                // normalize the casing in case the project system gave us casing different from the file system
                if (CommonUtils.HasEndSeparator(path))
                {
                    try
                    {
                        var trimmedPath = CommonUtils.TrimEndSeparator(path);
                        foreach (var dir in Directory.EnumerateDirectories(Path.GetDirectoryName(trimmedPath), Path.GetFileName(trimmedPath)))
                        {
                            if (StringComparer.OrdinalIgnoreCase.Equals(dir, trimmedPath))
                            {
                                path = dir + Path.DirectorySeparatorChar;
                                break;
                            }
                        }
                    }
                    catch
                    {
                    }
                }
                else
                {
                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(Path.GetDirectoryName(path)))
                        {
                            if (StringComparer.OrdinalIgnoreCase.Equals(file, path))
                            {
                                path = file;
                                break;
                            }
                        }
                    }
                    catch
                    {
                    }
                }

                // Retrieve the project from which the items are being copied

                var solution = (IVsSolution)this.Project.GetService(typeof(SVsSolution));
                ErrorHandler.ThrowOnFailure(solution.GetProjectOfGuid(ref projectInstanceGuid, out sourceHierarchy));
            }

            private static void GetPathAndProjectId(string projectReference, out Guid projectInstanceGuid, out string folder)
            {
                // Split the reference in its 3 parts
                var index1 = Guid.Empty.ToString("B").Length;
                if (index1 + 1 >= projectReference.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(projectReference));
                }

                // Get the Guid
                var guidString = projectReference.Substring(1, index1 - 2);
                projectInstanceGuid = new Guid(guidString);

                // Get the project path
                var index2 = projectReference.IndexOf('|', index1 + 1);
                if (index2 < 0 || index2 + 1 >= projectReference.Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(projectReference));
                }

                // Finally get the source path
                folder = projectReference.Substring(index2 + 1);
            }

            /// <summary>
            /// Adds an item from a project refererence to target node.
            /// </summary>
            /// <param name="projectRef"></param>
            /// <param name="targetNode"></param>
            private Addition CanAddFileFromProjectReference(string projectRef, string targetFolder, bool fromFolder = false)
            {
                Utilities.ArgumentNotNullOrEmpty(nameof(projectRef), projectRef);

                var solution = this.Project.GetService(typeof(IVsSolution)) as IVsSolution;
                Utilities.CheckNotNull(solution);
                var reason = new VSUPDATEPROJREFREASON[1];
                if (ErrorHandler.Failed(solution.GetItemOfProjref(projectRef, out var hierarchy, out var itemidLoc, out var str, reason)))
                {
                    GetPathAndProjectId(projectRef, out var projectGuid, out var path);
                    ReportMissingItem(path);
                    return null;
                }

                Utilities.CheckNotNull(hierarchy);

                // This will throw invalid cast exception if the hierrachy is not a project.
                var project = (IVsProject)hierarchy;
                var isLink = false;
                if (ErrorHandler.Succeeded(((IVsHierarchy)project).GetProperty(itemidLoc, (int)__VSHPROPID2.VSHPROPID_IsLinkFile, out var isLinkValue)))
                {
                    if (isLinkValue is bool)
                    {
                        isLink = (bool)isLinkValue;
                    }
                }

                ErrorHandler.ThrowOnFailure(project.GetMkDocument(itemidLoc, out var moniker));

                if (this.DropEffect == DropEffect.Move && IsBadMove(targetFolder, moniker, true))
                {
                    return null;
                }

                if (!File.Exists(moniker))
                {
                    Utilities.ShowMessageBox(
                            this.Project.Site,
                            string.Format("The item '{0}' does not exist in the project directory. It may have been moved, renamed or deleted.", Path.GetFileName(moniker)),
                            null,
                            OLEMSGICON.OLEMSGICON_CRITICAL,
                            OLEMSGBUTTON.OLEMSGBUTTON_OK,
                            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                    return null;
                }

                // Check that the source and destination paths aren't the same since we can't move an item to itself.
                // If they are in fact the same location, throw an error that copy/move will not work correctly.
                if (this.DropEffect == DropEffect.Move && !CommonUtils.IsSamePath(Path.GetDirectoryName(moniker), Path.GetDirectoryName(targetFolder)))
                {
                    try
                    {
                        var sourceLinkTarget = NativeMethods.GetAbsolutePathToDirectory(Path.GetDirectoryName(moniker));
                        string destinationLinkTarget = null;

                        // if the directory doesn't exist, just skip this.  We will create it later.
                        if (Directory.Exists(targetFolder))
                        {
                            try
                            {
                                destinationLinkTarget = NativeMethods.GetAbsolutePathToDirectory(targetFolder);
                            }
                            catch (FileNotFoundException)
                            {
                                // This can occur if the user had a symlink'd directory and deleted the backing directory.
                                Utilities.ShowMessageBox(
                                            this.Project.Site,
                                            string.Format(
                                                "Unable to find the destination folder."),
                                            null,
                                            OLEMSGICON.OLEMSGICON_CRITICAL,
                                            OLEMSGBUTTON.OLEMSGBUTTON_OK,
                                            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                                return null;
                            }
                        }

                        // If the paths are the same, we can't really move the file...
                        if (destinationLinkTarget != null && CommonUtils.IsSamePath(sourceLinkTarget, destinationLinkTarget))
                        {
                            CannotMoveSameLocation(moniker);
                            return null;
                        }
                    }
                    catch (Exception ex) when (!ExceptionExtensions.IsCriticalException(ex))
                    {
                        return null;
                    }
                }

                // Begin the move operation now that we are past pre-checks.
                var existingChild = this.Project.FindNodeByFullPath(moniker);
                if (isLink)
                {
                    // links we just want to update the link node for...
                    if (existingChild != null)
                    {
                        if (ComUtilities.IsSameComObject(this.Project, project))
                        {
                            if (this.DropEffect != DropEffect.Move)
                            {
                                Utilities.ShowMessageBox(
                                        this.Project.Site,
                                        string.Format("Cannot copy linked files within the same project. You cannot have more than one link to the same file in a project."),
                                        null,
                                        OLEMSGICON.OLEMSGICON_CRITICAL,
                                        OLEMSGBUTTON.OLEMSGBUTTON_OK,
                                        OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                                return null;
                            }
                        }
                        else
                        {
                            Utilities.ShowMessageBox(
                                    this.Project.Site,
                                    string.Format("There is already a link to '{0}'. You cannot have more than one link to the same file in a project.", moniker),
                                    null,
                                    OLEMSGICON.OLEMSGICON_CRITICAL,
                                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                            return null;
                        }
                    }

                    return new ReparentLinkedFileAddition(this.Project, targetFolder, moniker);
                }

                var newPath = Path.Combine(targetFolder, Path.GetFileName(moniker));
                if (File.Exists(newPath) &&
                    CommonUtils.IsSamePath(
                        NativeMethods.GetAbsolutePathToDirectory(newPath),
                        NativeMethods.GetAbsolutePathToDirectory(moniker)))
                {
                    newPath = GetCopyName(newPath);
                }

                var ok = false;
                if (this.DropEffect == DropEffect.Move && Utilities.IsSameComObject(project, this.Project))
                {
                    if (existingChild != null && existingChild.ItemNode != null && existingChild.ItemNode.IsExcluded)
                    {
                        // https://nodejstools.codeplex.com/workitem/271
                        // The item is excluded, so we don't need to ask if we can rename it.
                        ok = true;
                    }
                    else
                    {
                        ok = this.Project.Tracker.CanRenameItem(moniker, newPath, VSRENAMEFILEFLAGS.VSRENAMEFILEFLAGS_NoFlags);
                    }
                }
                else
                {
                    ok = this.Project.Tracker.CanAddItems(
                        new[] { newPath },
                        new VSQUERYADDFILEFLAGS[] { VSQUERYADDFILEFLAGS.VSQUERYADDFILEFLAGS_NoFlags });
                }

                if (ok)
                {
                    if (File.Exists(newPath))
                    {
                        if (this.DropEffect == DropEffect.Move &&
                            Utilities.IsSameComObject(project, this.Project) &&
                            this.Project.FindNodeByFullPath(newPath) != null)
                        {
                            // if we're overwriting an item, we're moving it, make sure that's ok.
                            // OverwriteFileAddition will handle the remove from the hierarchy
                            if (!this.Project.Tracker.CanRemoveItems(new[] { newPath }, new[] { VSQUERYREMOVEFILEFLAGS.VSQUERYREMOVEFILEFLAGS_NoFlags }))
                            {
                                return null;
                            }
                        }
                        var overwrite = this.OverwriteAllItems;

                        if (overwrite == null)
                        {
                            if (!PromptOverwriteFile(moniker, out var dialog))
                            {
                                return null;
                            }

                            overwrite = dialog.ShouldOverwrite;

                            if (dialog.AllItems)
                            {
                                this.OverwriteAllItems = overwrite;
                            }
                        }

                        if (overwrite.Value)
                        {
                            return new OverwriteFileAddition(this.Project, targetFolder, this.DropEffect, moniker, Path.GetFileName(newPath), project);
                        }
                        else
                        {
                            return SkipOverwriteAddition.Instance;
                        }
                    }
                    else if (Directory.Exists(newPath))
                    {
                        Utilities.ShowMessageBox(
                            this.Project.Site,
                            SR.GetString(SR.DirectoryExists, CommonUtils.GetFileOrDirectoryName(newPath)),
                            null,
                            OLEMSGICON.OLEMSGICON_CRITICAL,
                            OLEMSGBUTTON.OLEMSGBUTTON_OK,
                            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                        return null;
                    }

                    if (newPath.Length >= NativeMethods.MAX_PATH)
                    {
                        Utilities.ShowMessageBox(
                            this.Project.Site,
                            SR.GetString(SR.PathTooLongShortMessage),
                            null,
                            OLEMSGICON.OLEMSGICON_CRITICAL,
                            OLEMSGBUTTON.OLEMSGBUTTON_OK,
                            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                        return null;
                    }
                    return new FileAddition(this.Project, targetFolder, this.DropEffect, moniker, Path.GetFileName(newPath), project);
                }
                return null;
            }

            /// <summary>
            /// Prompts if the file should be overwriten.  Returns false if the user cancels, true if the user answered yes/no
            /// </summary>
            /// <param name="filename"></param>
            /// <param name="dialog"></param>
            /// <returns></returns>
            private static bool PromptOverwriteFile(string filename, out OverwriteFileDialog dialog)
            {
                dialog = new OverwriteFileDialog(SR.GetString(SR.FileAlreadyExists, Path.GetFileName(filename)), true);
                dialog.Owner = Application.Current.MainWindow;
                var dialogResult = dialog.ShowDialog();

                if (dialogResult != null && !dialogResult.Value)
                {
                    // user cancelled
                    return false;
                }
                return true;
            }

            private bool IsBadMove(string targetFolder, string moniker, bool file)
            {
                if (this.TargetNode.GetMkDocument() == moniker)
                {
                    // we are moving the file onto it's self.  If it's a single file via mouse
                    // we'll ignore it.  If it's multiple files, or a cut and paste, then we'll
                    // report the error.
                    if (this.ProjectReferences.Length > 1 || !this.MouseDropping)
                    {
                        CannotMoveSameLocation(moniker);
                    }
                    return true;
                }

                if ((file || !this.MouseDropping) &&
                    Directory.Exists(targetFolder) &&
                    CommonUtils.IsSameDirectory(Path.GetDirectoryName(moniker), targetFolder))
                {
                    // we're moving a file into it's own folder, report an error.
                    CannotMoveSameLocation(moniker);
                    return true;
                }
                return false;
            }

            private void CannotMoveSameLocation(string moniker)
            {
                Utilities.ShowMessageBox(
                    this.Project.Site,
                    SR.GetString(SR.CannotMoveIntoSameDirectory, CommonUtils.GetFileOrDirectoryName(moniker)),
                    null,
                    OLEMSGICON.OLEMSGICON_CRITICAL,
                    OLEMSGBUTTON.OLEMSGBUTTON_OK,
                    OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
            }

            private bool IsOurProject(IVsProject project)
            {
                project.GetMkDocument((uint)VSConstants.VSITEMID.Root, out var projectDoc);
                return projectDoc == this.Project.Url;
            }

            private abstract class Addition
            {
                public abstract void DoAddition(ref bool? overwrite);
            }

            /// <summary>
            /// Addition which doesn't add anything.  It's used when the user answers no to
            /// overwriting a file, which results in us reporting an overall failure to the 
            /// copy and paste.  This causes the file not to be deleted if it was a move.
            /// 
            /// This also means that if you're moving multiple files and answer no to one 
            /// of them but not the other that the files are not removed from the source
            /// hierarchy.
            /// </summary>
            private class SkipOverwriteAddition : Addition
            {
                internal static SkipOverwriteAddition Instance = new SkipOverwriteAddition();

                public override void DoAddition(ref bool? overwrite)
                {
                }
            }

            private class ReparentLinkedFileAddition : Addition
            {
                private readonly ProjectNode Project;
                private readonly string TargetFolder;
                private readonly string Moniker;

                public ReparentLinkedFileAddition(ProjectNode project, string targetFolder, string moniker)
                {
                    this.Project = project;
                    this.TargetFolder = targetFolder;
                    this.Moniker = moniker;
                }

                public override void DoAddition(ref bool? overwrite)
                {
                    var existing = this.Project.FindNodeByFullPath(this.Moniker);
                    var created = false;
                    if (existing != null)
                    {
                        this.Project.OnItemDeleted(existing);
                        existing.Parent.RemoveChild(existing);
                        this.Project.Site.GetUIThread().MustBeCalledFromUIThread();
                        existing.ID = this.Project.ItemIdMap.Add(existing);
                    }
                    else
                    {
                        existing = this.Project.CreateFileNode(this.Moniker);
                        created = true;
                    }

                    var newParent = this.TargetFolder == this.Project.ProjectHome ? this.Project : this.Project.FindNodeByFullPath(this.TargetFolder);
                    newParent.AddChild(existing);
                    if (this.Project.ItemsDraggedOrCutOrCopied != null)
                    {
                        this.Project.ItemsDraggedOrCutOrCopied.Remove(existing); // we don't need to remove the file after Paste
                    }

                    var link = existing.ItemNode.GetMetadata(ProjectFileConstants.Link);
                    if (link != null || created)
                    {
                        // update the link to the new location within solution explorer
                        existing.ItemNode.SetMetadata(
                            ProjectFileConstants.Link,
                            Path.Combine(
                                CommonUtils.GetRelativeDirectoryPath(
                                    this.Project.ProjectHome,
                                    this.TargetFolder
                                ),
                                Path.GetFileName(this.Moniker)
                            )
                        );
                    }
                }
            }

            private class FileAddition : Addition
            {
                public readonly ProjectNode Project;
                public readonly string TargetFolder;
                public readonly DropEffect DropEffect;
                public readonly string SourceMoniker;
                public readonly IVsProject SourceHierarchy;
                public readonly string NewFileName;

                public FileAddition(ProjectNode project, string targetFolder, DropEffect dropEffect, string sourceMoniker, string newFileName, IVsProject sourceHierarchy)
                {
                    this.Project = project;
                    this.TargetFolder = targetFolder;
                    this.DropEffect = dropEffect;
                    this.SourceMoniker = sourceMoniker;
                    this.SourceHierarchy = sourceHierarchy;
                    this.NewFileName = newFileName;
                }

                public override void DoAddition(ref bool? overwrite)
                {
                    var newPath = Path.Combine(this.TargetFolder, this.NewFileName);

                    DirectoryInfo dirInfo = null;

                    try
                    {
                        dirInfo = Directory.CreateDirectory(this.TargetFolder);
                    }
                    catch (ArgumentException)
                    {
                    }
                    catch (UnauthorizedAccessException)
                    {
                    }
                    catch (IOException)
                    {
                    }
                    catch (NotSupportedException)
                    {
                    }

                    if (dirInfo == null)
                    {
                        //Something went wrong and we failed to create the new directory
                        //   Inform the user and cancel the addition
                        Utilities.ShowMessageBox(
                                            this.Project.Site,
                                            SR.GetString(SR.FolderCannotBeCreatedOnDisk, CommonUtils.GetFileOrDirectoryName(this.TargetFolder)),
                                            null,
                                            OLEMSGICON.OLEMSGICON_CRITICAL,
                                            OLEMSGBUTTON.OLEMSGBUTTON_OK,
                                            OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);
                        return;
                    }

                    if (this.DropEffect == DropEffect.Move && Utilities.IsSameComObject(this.Project, this.SourceHierarchy))
                    {
                        // we are doing a move, we need to remove the old item, and add the new.
                        // This also allows us to have better behavior if the user is selectively answering
                        // no to files within the hierarchy.  We can do the rename of the individual items
                        // which the user opts to move and not touch the ones they don't.  With a cross
                        // hierarchy move if the user answers no to any of the items none of the items
                        // are removed from the source hierarchy.
                        var fileNode = this.Project.FindNodeByFullPath(this.SourceMoniker);
                        Debug.Assert(fileNode is FileNode);

                        this.Project.ItemsDraggedOrCutOrCopied.Remove(fileNode); // we don't need to remove the file after Paste                        

                        if (File.Exists(newPath))
                        {
                            // we checked before starting the copy, but somehow a file has snuck in.  Could be a race,
                            // or the user could have cut and pasted 2 files from different folders into the same folder.
                            bool shouldOverwrite;
                            if (overwrite == null)
                            {
                                if (!PromptOverwriteFile(Path.GetFileName(newPath), out var dialog))
                                {
                                    // user cancelled
                                    fileNode.ExpandItem(EXPANDFLAGS.EXPF_UnCutHighlightItem);
                                    throw new CancelPasteException();
                                }

                                if (dialog.AllItems)
                                {
                                    overwrite = dialog.ShouldOverwrite;
                                }

                                shouldOverwrite = dialog.ShouldOverwrite;
                            }
                            else
                            {
                                shouldOverwrite = overwrite.Value;
                            }

                            if (!shouldOverwrite)
                            {
                                fileNode.ExpandItem(EXPANDFLAGS.EXPF_UnCutHighlightItem);
                                return;
                            }

                            var existingNode = this.Project.FindNodeByFullPath(newPath);
                            if (existingNode != null)
                            {
                                existingNode.Remove(true);
                            }
                            else
                            {
                                File.Delete(newPath);
                            }
                        }

                        var file = fileNode as FileNode;
                        file.RenameInStorage(fileNode.Url, newPath);
                        file.RenameFileNode(fileNode.Url, newPath);

                        this.Project.Tracker.OnItemRenamed(this.SourceMoniker, newPath, VSRENAMEFILEFLAGS.VSRENAMEFILEFLAGS_NoFlags);
                    }
                    else
                    {
                        // we are copying and adding a new file node
                        File.Copy(this.SourceMoniker, newPath, true);

                        // best effort to reset the ReadOnly attribute
                        try
                        {
                            File.SetAttributes(newPath, File.GetAttributes(newPath) & ~FileAttributes.ReadOnly);
                        }
                        catch (ArgumentException)
                        {
                        }
                        catch (UnauthorizedAccessException)
                        {
                        }
                        catch (IOException)
                        {
                        }

                        var existing = this.Project.FindNodeByFullPath(newPath);
                        if (existing == null)
                        {
                            var fileNode = this.Project.CreateFileNode(newPath);
                            if (StringComparer.OrdinalIgnoreCase.Equals(this.TargetFolder, this.Project.FullPathToChildren))
                            {
                                this.Project.AddChild(fileNode);
                            }
                            else
                            {
                                var targetFolder = this.Project.CreateFolderNodes(this.TargetFolder);

                                //If a race occurrs simply treat the source as a non-included item
                                var wasMemberItem = false;
                                var sourceItem = this.Project.FindNodeByFullPath(this.SourceMoniker);
                                if (sourceItem != null)
                                {
                                    wasMemberItem = !sourceItem.IsNonMemberItem;
                                }

                                if (wasMemberItem && targetFolder.IsNonMemberItem)
                                {
                                    // dropping/pasting folder into non-member folder, non member folder
                                    // should get included into the project.
                                    ErrorHandler.ThrowOnFailure(targetFolder.IncludeInProject(false));
                                }

                                targetFolder.AddChild(fileNode);
                                if (!wasMemberItem)
                                {
                                    // added child by default is included,
                                    //   non-member copies are not added to the project
                                    ErrorHandler.ThrowOnFailure(fileNode.ExcludeFromProject());
                                }
                            }
                            this.Project.tracker.OnItemAdded(fileNode.Url, VSADDFILEFLAGS.VSADDFILEFLAGS_NoFlags);
                        }
                        else if (existing.IsNonMemberItem)
                        {
                            // replacing item that already existed, just include it in the project.
                            existing.IncludeInProject(false);
                        }
                    }
                }
            }

            private class OverwriteFileAddition : FileAddition
            {
                public OverwriteFileAddition(ProjectNode project, string targetFolder, DropEffect dropEffect, string sourceMoniker, string newFileName, IVsProject sourceHierarchy)
                    : base(project, targetFolder, dropEffect, sourceMoniker, newFileName, sourceHierarchy)
                {
                }

                public override void DoAddition(ref bool? overwrite)
                {
                    if (this.DropEffect == DropEffect.Move)
                    {
                        // File.Move won't overwrite, do it now.
                        File.Delete(Path.Combine(this.TargetFolder, Path.GetFileName(this.NewFileName)));

                        HierarchyNode existingNode;
                        if (Utilities.IsSameComObject(this.SourceHierarchy, this.Project) &&
                            (existingNode = this.Project.FindNodeByFullPath(Path.Combine(this.TargetFolder, this.NewFileName))) != null)
                        {
                            // remove the existing item from the hierarchy, base.DoAddition will add a new one
                            existingNode.Remove(true);
                        }
                    }
                    base.DoAddition(ref overwrite);
                }
            }
        }

        /// <summary>
        /// Add an existing item (file/folder) to the project if it already exist in our storage.
        /// </summary>
        /// <param name="parentNode">Node to that this item to</param>
        /// <param name="name">Name of the item being added</param>
        /// <param name="targetPath">Path of the item being added</param>
        /// <returns>Node that was added</returns>
        protected virtual HierarchyNode AddNodeIfTargetExistInStorage(HierarchyNode parentNode, string name, string targetPath)
        {
            if (parentNode == null)
            {
                return null;
            }

            var newNode = parentNode;
            // If the file/directory exist, add a node for it
            if (File.Exists(targetPath))
            {
                var result = new VSADDRESULT[1];
                ErrorHandler.ThrowOnFailure(this.AddItem(parentNode.ID, VSADDITEMOPERATION.VSADDITEMOP_OPENFILE, name, 1, new string[] { targetPath }, IntPtr.Zero, result));
                if (result[0] != VSADDRESULT.ADDRESULT_Success)
                {
                    throw new Exception();
                }

                newNode = this.FindNodeByFullPath(targetPath);
                if (newNode == null)
                {
                    throw new Exception();
                }
            }
            else if (Directory.Exists(targetPath))
            {
                newNode = this.CreateFolderNodes(targetPath);
            }
            return newNode;
        }

        #endregion

        #region non-virtual methods
        /// <summary>
        /// Handle the Cut operation to the clipboard
        /// </summary>
        protected internal int CutToClipboard()
        {
            var returnValue = (int)OleConstants.OLECMDERR_E_NOTSUPPORTED;

            this.RegisterClipboardNotifications(true);

            // Create our data object and change the selection to show item(s) being cut
            IOleDataObject dataObject = this.PackageSelectionDataObject(true);
            if (dataObject != null)
            {
                this._copyCutState = CopyCutState.Cut;

                // Add our cut item(s) to the clipboard
                this.Site.GetClipboardService().SetClipboard(dataObject);

                // Inform VS (UiHierarchyWindow) of the cut
                var clipboardHelper = (IVsUIHierWinClipboardHelper)GetService(typeof(SVsUIHierWinClipboardHelper));
                if (clipboardHelper == null)
                {
                    return VSConstants.E_FAIL;
                }

                returnValue = ErrorHandler.ThrowOnFailure(clipboardHelper.Cut(dataObject));
            }

            return returnValue;
        }

        /// <summary>
        /// Handle the Copy operation to the clipboard
        /// </summary>
        protected internal int CopyToClipboard()
        {
            var returnValue = (int)OleConstants.OLECMDERR_E_NOTSUPPORTED;
            this.RegisterClipboardNotifications(true);

            // Create our data object and change the selection to show item(s) being copy
            IOleDataObject dataObject = this.PackageSelectionDataObject(false);
            if (dataObject != null)
            {
                this._copyCutState = CopyCutState.Copied;

                // Add our copy item(s) to the clipboard
                this.Site.GetClipboardService().SetClipboard(dataObject);

                // Inform VS (UiHierarchyWindow) of the copy
                var clipboardHelper = (IVsUIHierWinClipboardHelper)GetService(typeof(SVsUIHierWinClipboardHelper));
                if (clipboardHelper == null)
                {
                    return VSConstants.E_FAIL;
                }
                returnValue = ErrorHandler.ThrowOnFailure(clipboardHelper.Copy(dataObject));
            }
            return returnValue;
        }

        /// <summary>
        /// Handle the Paste operation to a targetNode
        /// </summary>
        protected internal int PasteFromClipboard(HierarchyNode targetNode)
        {
            var returnValue = (int)OleConstants.OLECMDERR_E_NOTSUPPORTED;

            if (targetNode == null)
            {
                return VSConstants.E_INVALIDARG;
            }

            //Get the clipboardhelper service and use it after processing dataobject
            var clipboardHelper = (IVsUIHierWinClipboardHelper)GetService(typeof(SVsUIHierWinClipboardHelper));
            if (clipboardHelper == null)
            {
                return VSConstants.E_FAIL;
            }

            try
            {
                //Get dataobject from clipboard
                var dataObject = this.Site.GetClipboardService().GetClipboard();
                if (dataObject == null)
                {
                    return VSConstants.E_UNEXPECTED;
                }

                var dropEffect = DropEffect.None;
                var dropDataType = DropDataType.None;
                try
                {
                    // if we didn't initiate the cut, default to Move.  If we're dragging to another
                    // project then their IVsUIHierWinClipboardHelperEvents.OnPaste method will
                    // check both the drop effect AND whether or not a cut was initiated, and only
                    // do a move if both are true.  Otherwise if we have a value non-None _copyCurState the
                    // cut/copy initiated from within our project system and we're now pasting
                    // back into ourselves, so we should simply respect it's value.
                    dropEffect = this._copyCutState == CopyCutState.Copied ? DropEffect.Copy : DropEffect.Move;
                    dropDataType = this.ProcessSelectionDataObject(dataObject, targetNode, false, dropEffect);
                    if (dropDataType == DropDataType.None)
                    {
                        dropEffect = DropEffect.None;
                    }
                }
                catch (ExternalException e)
                {
                    Trace.WriteLine("Exception : " + e.Message);

                    // If it is a drop from windows and we get any kind of error ignore it. This
                    // prevents bogus messages from the shell from being displayed
                    if (dropDataType != DropDataType.Shell)
                    {
                        throw;
                    }
                }
                finally
                {
                    // Inform VS (UiHierarchyWindow) of the paste 
                    returnValue = clipboardHelper.Paste(dataObject, (uint)dropEffect);
                }
            }
            catch (COMException e)
            {
                Trace.WriteLine("Exception : " + e.Message);

                returnValue = e.ErrorCode;
            }

            return returnValue;
        }

        /// <summary>
        /// Determines if the paste command should be allowed.
        /// </summary>
        /// <returns></returns>
        protected internal bool AllowPasteCommand()
        {
            try
            {
                var dataObject = this.Site.GetClipboardService().GetClipboard();
                if (dataObject == null)
                {
                    return false;
                }

                // First see if this is a set of storage based items
                var format = DragDropHelper.CreateFormatEtc((ushort)DragDropHelper.CF_VSSTGPROJECTITEMS);
                if (dataObject.QueryGetData(new FORMATETC[] { format }) == VSConstants.S_OK)
                {
                    return true;
                }
                // Try reference based items
                format = DragDropHelper.CreateFormatEtc((ushort)DragDropHelper.CF_VSREFPROJECTITEMS);
                if (dataObject.QueryGetData(new FORMATETC[] { format }) == VSConstants.S_OK)
                {
                    return true;
                }
                // Try windows explorer files format
                format = DragDropHelper.CreateFormatEtc((ushort)NativeMethods.CF_HDROP);
                return (dataObject.QueryGetData(new FORMATETC[] { format }) == VSConstants.S_OK);
            }
            // We catch External exceptions since it might be that it is not our data on the clipboard.
            catch (ExternalException e)
            {
                Trace.WriteLine("Exception :" + e.Message);
                return false;
            }
        }

        /// <summary>
        /// Register/Unregister for Clipboard events for the UiHierarchyWindow (solution explorer)
        /// </summary>
        /// <param name="register">true for register, false for unregister</param>
        protected internal void RegisterClipboardNotifications(bool register)
        {
            // Get the UiHierarchy window clipboard helper service
            var clipboardHelper = (IVsUIHierWinClipboardHelper)GetService(typeof(SVsUIHierWinClipboardHelper));
            if (clipboardHelper == null)
            {
                return;
            }

            if (register && this.copyPasteCookie == 0)
            {
                // Register
                ErrorHandler.ThrowOnFailure(clipboardHelper.AdviseClipboardHelperEvents(this, out this.copyPasteCookie));
                Debug.Assert(this.copyPasteCookie != 0, "AdviseClipboardHelperEvents returned an invalid cookie");
            }
            else if (!register && this.copyPasteCookie != 0)
            {
                // Unregister
                ErrorHandler.ThrowOnFailure(clipboardHelper.UnadviseClipboardHelperEvents(this.copyPasteCookie));
                this.copyPasteCookie = 0;
            }
        }

        /// <summary>
        /// Process dataobject from Drag/Drop/Cut/Copy/Paste operation
        /// 
        /// drop indicates if it is a drag/drop or a cut/copy/paste.
        /// </summary>
        /// <remarks>The targetNode is set if the method is called from a drop operation, otherwise it is null</remarks>
        internal DropDataType ProcessSelectionDataObject(IOleDataObject dataObject, HierarchyNode targetNode, bool drop, DropEffect dropEffect)
        {
            Utilities.ArgumentNotNull(nameof(targetNode), targetNode);

            var dropDataType = DropDataType.None;
            var isWindowsFormat = false;

            // Try to get it as a directory based project.
            var filesDropped = DragDropHelper.GetDroppedFiles(DragDropHelper.CF_VSSTGPROJECTITEMS, dataObject, out dropDataType);
            if (filesDropped.Count == 0)
            {
                filesDropped = DragDropHelper.GetDroppedFiles(DragDropHelper.CF_VSREFPROJECTITEMS, dataObject, out dropDataType);
            }
            if (filesDropped.Count == 0)
            {
                filesDropped = DragDropHelper.GetDroppedFiles(NativeMethods.CF_HDROP, dataObject, out dropDataType);
                isWindowsFormat = (filesDropped.Count > 0);
            }

            if (dropDataType != DropDataType.None && filesDropped.Count > 0)
            {
                var filesDroppedAsArray = filesDropped.ToArray();

                var node = targetNode;

                // For directory based projects the content of the clipboard is a double-NULL terminated list of Projref strings.
                if (isWindowsFormat)
                {
                    DropFilesOrFolders(filesDroppedAsArray, node);

                    return dropDataType;
                }
                else
                {
                    if (AddFilesFromProjectReferences(node, filesDroppedAsArray, drop, dropEffect))
                    {
                        return dropDataType;
                    }
                }
            }

            // If we reached this point then the drop data must be set to None.
            // Otherwise the OnPaste will be called with a valid DropData and that would actually delete the item.
            return DropDataType.None;
        }

        internal void DropFilesOrFolders(string[] filesDropped, HierarchyNode ontoNode)
        {
            var waitDialog = (IVsThreadedWaitDialog)this.Site.GetService(typeof(SVsThreadedWaitDialog));
            var waitResult = waitDialog.StartWaitDialog(
                "Adding files and folders...",
                "Adding files to your project, this may take several seconds...",
                null,
                0,
                null,
                null
            );
            try
            {
                ontoNode = ontoNode.GetDragTargetHandlerNode();
                var nodePath = ontoNode.FullPathToChildren;
                var droppingExistingDirectory = true;
                foreach (var droppedFile in filesDropped)
                {
                    if (!Directory.Exists(droppedFile) ||
                        !StringComparer.OrdinalIgnoreCase.Equals(Path.GetDirectoryName(droppedFile), nodePath))
                    {
                        droppingExistingDirectory = false;
                        break;
                    }
                }

                if (droppingExistingDirectory)
                {
                    // we're dragging a directory/directories that already exist
                    // into the location where they exist, we can do this via a fast path,
                    // and pop up a nice progress bar.
                    AddExistingDirectories(ontoNode, filesDropped);
                }
                else
                {
                    foreach (var droppedFile in filesDropped)
                    {
                        if (Directory.Exists(droppedFile) &&
                            CommonUtils.IsSubpathOf(droppedFile, nodePath))
                        {
                            var cancelled = 0;
                            waitDialog.EndWaitDialog(ref cancelled);
                            waitResult = VSConstants.E_FAIL; // don't end twice

                            Utilities.ShowMessageBox(
                                this.Site,
                                SR.GetString(SR.CannotAddFolderAsDescendantOfSelf, CommonUtils.GetFileOrDirectoryName(droppedFile)),
                                null,
                                OLEMSGICON.OLEMSGICON_CRITICAL,
                                OLEMSGBUTTON.OLEMSGBUTTON_OK,
                                OLEMSGDEFBUTTON.OLEMSGDEFBUTTON_FIRST);

                            return;
                        }
                    }

                    // This is the code path when source is windows explorer
                    var vsaddresults = new VSADDRESULT[1];
                    vsaddresults[0] = VSADDRESULT.ADDRESULT_Failure;
                    var addResult = AddItem(ontoNode.ID, VSADDITEMOPERATION.VSADDITEMOP_OPENFILE, null, (uint)filesDropped.Length, filesDropped, IntPtr.Zero, vsaddresults);
                    if (addResult != VSConstants.S_OK && addResult != VSConstants.S_FALSE && addResult != (int)OleConstants.OLECMDERR_E_CANCELED
                        && vsaddresults[0] != VSADDRESULT.ADDRESULT_Success)
                    {
                        ErrorHandler.ThrowOnFailure(addResult);
                    }
                }
            }
            finally
            {
                if (ErrorHandler.Succeeded(waitResult))
                {
                    var cancelled = 0;
                    waitDialog.EndWaitDialog(ref cancelled);
                }
            }
        }

        internal void AddExistingDirectories(HierarchyNode node, string[] filesDropped)
        {
            var addedItems = new List<KeyValuePair<HierarchyNode, HierarchyNode>>();

            var oldTriggerFlag = this.EventTriggeringFlag;
            this.EventTriggeringFlag |= ProjectNode.EventTriggering.DoNotTriggerHierarchyEvents;
            try
            {
                foreach (var dir in filesDropped)
                {
                    AddExistingDirectory(GetOrAddDirectory(node, addedItems, dir), dir, addedItems);
                }
            }
            finally
            {
                this.EventTriggeringFlag = oldTriggerFlag;
            }

            if (addedItems.Count > 0)
            {
                foreach (var item in addedItems)
                {
                    OnItemAdded(item.Key, item.Value);
                    this.tracker.OnItemAdded(item.Value.Url, VSADDFILEFLAGS.VSADDFILEFLAGS_NoFlags);
                }
                OnInvalidateItems(node);
            }
        }

        private void AddExistingDirectory(HierarchyNode node, string path, List<KeyValuePair<HierarchyNode, HierarchyNode>> addedItems)
        {
            foreach (var dir in Directory.EnumerateDirectories(path))
            {
                var existingDir = GetOrAddDirectory(node, addedItems, dir);

                AddExistingDirectory(existingDir, dir, addedItems);
            }

            foreach (var file in Directory.EnumerateFiles(path))
            {
                var existingFile = node.FindImmediateChildByName(Path.GetFileName(file));
                if (existingFile == null)
                {
                    existingFile = CreateFileNode(file);
                    addedItems.Add(new KeyValuePair<HierarchyNode, HierarchyNode>(node, existingFile));
                    node.AddChild(existingFile);
                }
            }
        }

        private HierarchyNode GetOrAddDirectory(HierarchyNode node, List<KeyValuePair<HierarchyNode, HierarchyNode>> addedItems, string dir)
        {
            var existingDir = node.FindImmediateChildByName(Path.GetFileName(dir));
            if (existingDir == null)
            {
                existingDir = CreateFolderNode(dir);
                addedItems.Add(new KeyValuePair<HierarchyNode, HierarchyNode>(node, existingDir));
                node.AddChild(existingDir);
            }
            return existingDir;
        }

        /// <summary>
        /// Get the dropdatatype from the dataobject
        /// </summary>
        /// <param name="pDataObject">The dataobject to be analysed for its format</param>
        /// <returns>dropdatatype or none if dataobject does not contain known format</returns>
        internal static DropDataType QueryDropDataType(IOleDataObject pDataObject)
        {
            if (pDataObject == null)
            {
                return DropDataType.None;
            }

            // known formats include File Drops (as from WindowsExplorer),
            // VSProject Reference Items and VSProject Storage Items.
            var fmt = DragDropHelper.CreateFormatEtc(NativeMethods.CF_HDROP);

            if (DragDropHelper.QueryGetData(pDataObject, ref fmt) == VSConstants.S_OK)
            {
                return DropDataType.Shell;
            }

            fmt.cfFormat = DragDropHelper.CF_VSREFPROJECTITEMS;
            if (DragDropHelper.QueryGetData(pDataObject, ref fmt) == VSConstants.S_OK)
            {
                // Data is from a Ref-based project.
                return DropDataType.VsRef;
            }

            fmt.cfFormat = DragDropHelper.CF_VSSTGPROJECTITEMS;
            if (DragDropHelper.QueryGetData(pDataObject, ref fmt) == VSConstants.S_OK)
            {
                return DropDataType.VsStg;
            }

            return DropDataType.None;
        }

        /// <summary>
        /// Returns the drop effect.
        /// </summary>
        /// <remarks>
        /// // A directory based project should perform as follow:
        ///		NO MODIFIER 
        ///			- COPY if not from current hierarchy, 
        ///			- MOVE if from current hierarchy
        ///		SHIFT DRAG - MOVE
        ///		CTRL DRAG - COPY
        ///		CTRL-SHIFT DRAG - NO DROP (used for reference based projects only)
        /// </remarks>
        internal DropEffect QueryDropEffect(uint grfKeyState)
        {
            //Validate the dropdatatype
            if ((this._dropType != DropDataType.Shell) && (this._dropType != DropDataType.VsRef) && (this._dropType != DropDataType.VsStg))
            {
                return DropEffect.None;
            }

            // CTRL-SHIFT
            if ((grfKeyState & NativeMethods.MK_CONTROL) != 0 && (grfKeyState & NativeMethods.MK_SHIFT) != 0)
            {
                // Because we are not referenced base, we don't support link
                return DropEffect.None;
            }

            // CTRL
            if ((grfKeyState & NativeMethods.MK_CONTROL) != 0)
            {
                return DropEffect.Copy;
            }

            // SHIFT
            if ((grfKeyState & NativeMethods.MK_SHIFT) != 0)
            {
                return DropEffect.Move;
            }

            // no modifier
            if (this._dragging)
            {
                // we are dragging from our project to our project, default to a Move
                return DropEffect.Move;
            }
            else
            {
                // we are dragging, but we didn't initiate it, so it's cross project.  Default to
                // a copy.
                return DropEffect.Copy;
            }
        }

        /// <summary>
        /// Moves files from one part of our project to another.
        /// </summary>
        /// <param name="targetNode">the targetHandler node</param>
        /// <param name="projectReferences">List of projectref string</param>
        /// <returns>true if succeeded</returns>
        internal bool AddFilesFromProjectReferences(HierarchyNode targetNode, string[] projectReferences, bool mouseDropping, DropEffect dropEffect)
        {
            //Validate input
            Utilities.ArgumentNotNull(nameof(projectReferences), projectReferences);
            Utilities.CheckNotNull(targetNode);

            if (!QueryEditProjectFile(false))
            {
                throw Marshal.GetExceptionForHR(VSConstants.OLE_E_PROMPTSAVECANCELLED);
            }

            return new ProjectReferenceFileAdder(this, targetNode, projectReferences, mouseDropping, dropEffect).AddFiles();
        }

        #endregion

        #region private helper methods
        /// <summary>
        /// Empties all the data structures added to the clipboard and flushes the clipboard.
        /// </summary>
        private void CleanAndFlushClipboard()
        {
            var clippy = this.Site.GetClipboardService();
            var oleDataObject = clippy.GetClipboard();
            if (oleDataObject == null)
            {
                return;
            }

            var sourceProjectPath = DragDropHelper.GetSourceProjectPath(oleDataObject);

            if (!string.IsNullOrEmpty(sourceProjectPath) && CommonUtils.IsSamePath(sourceProjectPath, this.GetMkDocument()))
            {
                clippy.FlushClipboard();
                var opened = false;
                try
                {
                    opened = clippy.OpenClipboard();
                    clippy.EmptyClipboard();
                }
                finally
                {
                    if (opened)
                    {
                        clippy.CloseClipboard();
                    }
                }
            }
        }

        private IntPtr PackageSelectionData(StringBuilder sb, bool addEndFormatDelimiter)
        {
            if (sb == null || sb.ToString().Length == 0 || this.ItemsDraggedOrCutOrCopied.Count == 0)
            {
                return IntPtr.Zero;
            }

            // Double null at end.
            if (addEndFormatDelimiter)
            {
                if (sb.ToString()[sb.Length - 1] != '\0')
                {
                    sb.Append('\0');
                }
            }

            // We request unmanaged permission to execute the below.
            new SecurityPermission(SecurityPermissionFlag.UnmanagedCode).Demand();

            var df = new _DROPFILES();
            var dwSize = Marshal.SizeOf(df);
            Int16 wideChar = 0;
            var dwChar = Marshal.SizeOf(wideChar);
            var structSize = dwSize + ((sb.Length + 1) * dwChar);
            var ptr = Marshal.AllocHGlobal(structSize);
            df.pFiles = dwSize;
            df.fWide = 1;
            var data = IntPtr.Zero;
            try
            {
                data = UnsafeNativeMethods.GlobalLock(ptr);
                Marshal.StructureToPtr(df, data, false);
                var strData = new IntPtr((long)data + dwSize);
                DragDropHelper.CopyStringToHGlobal(sb.ToString(), strData, structSize);
            }
            finally
            {
                if (data != IntPtr.Zero)
                {
                    UnsafeNativeMethods.GlobalUnLock(data);
                }
            }

            return ptr;
        }

        #endregion

        /// <summary>
        /// Clears our current copy/cut state - happens after a paste
        /// </summary>
        private void ClearCopyCutState()
        {
            this._copyCutState = CopyCutState.None;
        }
    }
}
