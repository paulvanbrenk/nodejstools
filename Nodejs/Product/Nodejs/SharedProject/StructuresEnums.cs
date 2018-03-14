﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio.Shell.Interop;

namespace Microsoft.VisualStudioTools.Project
{
    #region structures
    [StructLayoutAttribute(LayoutKind.Sequential)]
    internal struct _DROPFILES
    {
        public Int32 pFiles;
        public Int32 X;
        public Int32 Y;
        public Int32 fNC;
        public Int32 fWide;
    }
    #endregion

    #region enums

    /// <summary>
    /// Defines the currect state of a property page.
    /// </summary>
    [Flags]
    public enum PropPageStatus
    {
        Dirty = 0x1,

        Validate = 0x2,

        Clean = 0x4
    }

    /// <summary>
    /// Defines the status of the command being queried
    /// </summary>
    [Flags]
    public enum QueryStatusResult
    {
        /// <summary>
        /// The command is not supported.
        /// </summary>
        NOTSUPPORTED = 0,

        /// <summary>
        /// The command is supported
        /// </summary>
        SUPPORTED = 1,

        /// <summary>
        /// The command is enabled
        /// </summary>
        ENABLED = 2,

        /// <summary>
        /// The command is toggled on
        /// </summary>
        LATCHED = 4,

        /// <summary>
        /// The command is toggled off (the opposite of LATCHED).
        /// </summary>
        NINCHED = 8,

        /// <summary>
        /// The command is invisible.
        /// </summary>
        INVISIBLE = 16
    }

    /// <summary>
    /// Defines the type of item to be added to the hierarchy.
    /// </summary>
    public enum HierarchyAddType
    {
        AddNewItem,
        AddExistingItem,
    }

    /// <summary>
    /// Defines the component from which a command was issued.
    /// </summary>
    public enum CommandOrigin
    {
        UiHierarchy,
        OleCommandTarget,
    }

    /// <summary>
    /// Defines the current status of the build process.
    /// </summary>
    public enum MSBuildResult
    {
        /// <summary>
        /// The build is currently suspended.
        /// </summary>
        Suspended,

        /// <summary>
        /// The build has been restarted.
        /// </summary>
        Resumed,

        /// <summary>
        /// The build failed.
        /// </summary>
        Failed,

        /// <summary>
        /// The build was successful.
        /// </summary>
        Successful,
    }

    /// <summary>
    /// Defines the type of action to be taken in showing the window frame.
    /// </summary>
    public enum WindowFrameShowAction
    {
        DoNotShow,
        Show,
        ShowNoActivate,
        Hide,
    }

    /// <summary>
    /// Defines drop types
    /// </summary>
    internal enum DropDataType
    {
        None,
        Shell,
        VsStg,
        VsRef,
    }

    /// <summary>
    /// Used by the hierarchy node to decide which element to redraw.
    /// </summary>
    [Flags]
    public enum UIHierarchyElement
    {
        None = 0,

        /// <summary>
        /// This will be translated to VSHPROPID_IconIndex
        /// </summary>
        Icon = 1,

        /// <summary>
        /// This will be translated to VSHPROPID_StateIconIndex
        /// </summary>
        SccState = 2,

        /// <summary>
        /// This will be translated to VSHPROPID_Caption
        /// </summary>
        Caption = 4,

        /// <summary>
        /// This will be translated to VSHPROPID_OverlayIconIndex
        /// </summary>
        OverlayIcon = 8,
    }

    /// <summary>
    /// Defines the global propeties used by the msbuild project.
    /// </summary>
    public enum GlobalProperty
    {
        /// <summary>
        /// Property specifying that we are building inside VS.
        /// </summary>
        BuildingInsideVisualStudio,

        /// <summary>
        /// The VS installation directory. This is the same as the $(DevEnvDir) macro.
        /// </summary>
        DevEnvDir,

        /// <summary>
        /// The name of the solution the project is created. This is the same as the $(SolutionName) macro.
        /// </summary>
        SolutionName,

        /// <summary>
        /// The file name of the solution. This is the same as $(SolutionFileName) macro.
        /// </summary>
        SolutionFileName,

        /// <summary>
        /// The full path of the solution. This is the same as the $(SolutionPath) macro.
        /// </summary>
        SolutionPath,

        /// <summary>
        /// The directory of the solution. This is the same as the $(SolutionDir) macro.
        /// </summary>               
        SolutionDir,

        /// <summary>
        /// The extension of teh directory. This is the same as the $(SolutionExt) macro.
        /// </summary>
        SolutionExt,

        /// <summary>
        /// The fxcop installation directory.
        /// </summary>
        FxCopDir,

        /// <summary>
        /// The ResolvedNonMSBuildProjectOutputs msbuild property
        /// </summary>
        VSIDEResolvedNonMSBuildProjectOutputs,

        /// <summary>
        /// The Configuartion property.
        /// </summary>
        Configuration,

        /// <summary>
        /// The platform property.
        /// </summary>
        Platform,

        /// <summary>
        /// The RunCodeAnalysisOnce property
        /// </summary>
        RunCodeAnalysisOnce,

        /// <summary>
        /// The VisualStudioStyleErrors property
        /// </summary>
        VisualStudioStyleErrors,
    }
    #endregion

    public sealed class AfterProjectFileOpenedEventArgs : EventArgs
    {
    }

    public sealed class BeforeProjectFileClosedEventArgs : EventArgs
    {
        /// <summary>
        /// true if the project was removed from the solution before the solution was closed. false if the project was removed from the solution while the solution was being closed.
        /// </summary>
        internal readonly bool Removed;
        internal readonly IVsHierarchy Hierarchy;

        internal BeforeProjectFileClosedEventArgs(IVsHierarchy hierarchy, bool removed)
        {
            this.Removed = removed;
            this.Hierarchy = hierarchy;
        }
    }

    /// <summary>
    /// Argument of the event raised when a project property is changed.
    /// </summary>
    public sealed class ProjectPropertyChangedArgs : EventArgs
    {
        internal ProjectPropertyChangedArgs(string propertyName, string oldValue, string newValue)
        {
            this.PropertyName = propertyName;
            this.OldValue = oldValue;
            this.NewValue = newValue;
        }

        public readonly string NewValue;
        public readonly string OldValue;
        public readonly string PropertyName;
    }

    /// <summary>
    /// This class is used for the events raised by a HierarchyNode object.
    /// </summary>
    internal sealed class HierarchyNodeEventArgs : EventArgs
    {
        public readonly HierarchyNode Child;

        internal HierarchyNodeEventArgs(HierarchyNode child)
        {
            this.Child = child;
        }
    }

    /// <summary>
    /// Event args class for triggering file change event arguments.
    /// </summary>
    public sealed class FileChangedOnDiskEventArgs : EventArgs
    {
        /// <summary>
        /// File name that was changed on disk.
        /// </summary>
        public readonly string FileName;

        /// <summary>
        /// The item ide of the file that has changed.
        /// </summary>
        public readonly uint ItemID;

        /// <summary>
        /// The reason the file has changed on disk.
        /// </summary>
        public readonly _VSFILECHANGEFLAGS FileChangeFlag;

        /// <summary>
        /// Constructs a new event args.
        /// </summary>
        /// <param name="fileName">File name that was changed on disk.</param>
        /// <param name="id">The item id of the file that was changed on disk.</param>
        internal FileChangedOnDiskEventArgs(string fileName, uint id, _VSFILECHANGEFLAGS flag)
        {
            this.FileName = fileName;
            this.ItemID = id;
            this.FileChangeFlag = flag;
        }
    }
}
