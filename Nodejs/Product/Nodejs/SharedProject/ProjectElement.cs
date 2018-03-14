﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.VisualStudio;

namespace Microsoft.VisualStudioTools.Project
{
    /// <summary>
    /// This class represent a project item (usualy a file) and allow getting and
    /// setting attribute on it.
    /// This class allow us to keep the internal details of our items hidden from
    /// our derived classes.
    /// While the class itself is public so it can be manipulated by derived classes,
    /// its internal constructors make sure it can only be created from within the assembly.
    /// </summary>
    internal abstract class ProjectElement
    {
        private readonly ProjectNode _itemProject;
        private bool _deleted;

        internal ProjectElement(ProjectNode project)
        {
            Utilities.ArgumentNotNull(nameof(project), project);

            this._itemProject = project;
        }

        public event EventHandler ItemTypeChanged;
        public string ItemTypeName
        {
            get
            {
                if (HasItemBeenDeleted())
                {
                    return string.Empty;
                }
                else
                {
                    return this.ItemType;
                }
            }
            set
            {
                if (!HasItemBeenDeleted())
                {
                    // Check out the project file.
                    if (!this._itemProject.QueryEditProjectFile(false))
                    {
                        throw Marshal.GetExceptionForHR(VSConstants.OLE_E_PROMPTSAVECANCELLED);
                    }

                    this.ItemType = value;
                }
            }
        }

        protected virtual void OnItemTypeChanged()
        {
            var evt = ItemTypeChanged;
            if (evt != null)
            {
                evt(this, EventArgs.Empty);
            }
        }

        protected abstract string ItemType
        {
            get;
            set;
        }

        internal ProjectNode ItemProject => this._itemProject;

        protected virtual bool Deleted => this._deleted;

        /// <summary>
        /// Calling this method remove this item from the project file.
        /// Once the item is delete, you should not longer be using it.
        /// Note that the item should be removed from the hierarchy prior to this call.
        /// </summary>
        public virtual void RemoveFromProjectFile()
        {
            this._deleted = true;
        }

        public virtual bool IsExcluded => false;

        /// <summary>
        /// Set an attribute on the project element
        /// </summary>
        /// <param name="attributeName">Name of the attribute to set</param>
        /// <param name="attributeValue">Value to give to the attribute</param>
        public abstract void SetMetadata(string attributeName, string attributeValue);

        /// <summary>
        /// Get the value of an attribute on a project element
        /// </summary>
        /// <param name="attributeName">Name of the attribute to get the value for</param>
        /// <returns>Value of the attribute</returns>
        public abstract string GetMetadata(string attributeName);

        public abstract void Rename(string newPath);

        /// <summary>
        /// Reevaluate all properties for the current item
        /// This should be call if you believe the property for this item
        /// may have changed since it was created/refreshed, or global properties
        /// this items depends on have changed.
        /// Be aware that there is a perf cost in calling this function.
        /// </summary>
        public virtual void RefreshProperties()
        {
        }

        /// <summary>
        /// Return an absolute path for the passed in element.
        /// If the element is already an absolute path, it is returned.
        /// Otherwise, it is unrelativized using the project directory
        /// as the base.
        /// Note that any ".." in the paths will be resolved.
        /// 
        /// For non-file system based project, it may make sense to override.
        /// </summary>
        /// <returns>FullPath</returns>
        public virtual string Url
        {
            get
            {
                var path = this.GetMetadata(ProjectFileConstants.Include);

                // we use Path.GetFileName and reverse it because it's much faster 
                // than Path.GetDirectoryName
                var filename = Path.GetFileName(path);
                if (path.IndexOf('.', 0, path.Length - filename.Length) != -1)
                {
                    // possibly non-canonical form...
                    return CommonUtils.GetAbsoluteFilePath(this._itemProject.ProjectHome, path);
                }

                // fast path, we know ProjectHome is canonical, and with no dots
                // in the directory name, so is path.
                return Path.Combine(this._itemProject.ProjectHome, path);
            }
        }

        /// <summary>
        /// Has the item been deleted
        /// </summary>
        private bool HasItemBeenDeleted()
        {
            return this._deleted;
        }

        public static bool operator ==(ProjectElement element1, ProjectElement element2)
        {
            // Do they reference the same element?
            if (Object.ReferenceEquals(element1, element2))
            {
                return true;
            }

            // Verify that they are not null (cast to object first to avoid stack overflow)
            if (element1 as object == null || element2 as object == null)
            {
                return false;
            }

            return element1.Equals(element2);
        }

        public static bool operator !=(ProjectElement element1, ProjectElement element2)
        {
            return !(element1 == element2);
        }

        public abstract override bool Equals(object obj);
        public abstract override int GetHashCode();
    }
}
