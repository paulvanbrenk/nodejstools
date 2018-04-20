﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections;
using System.Runtime.InteropServices;

namespace Microsoft.VisualStudioTools.Project.Automation
{
    /// <summary>
    /// This can navigate a collection object only (partial implementation of ProjectItems interface)
    /// </summary>
    [ComVisible(true)]
    public class OANavigableProjectItems : EnvDTE.ProjectItems
    {
        #region fields
        private readonly OAProject project;
        private readonly HierarchyNode nodeWithItems;
        #endregion

        #region properties
        /// <summary>
        /// Defines a relationship to the associated project.
        /// </summary>
        internal OAProject Project => this.project;

        /// <summary>
        /// Defines the node that contains the items
        /// </summary>
        internal HierarchyNode NodeWithItems => this.nodeWithItems;
        #endregion

        #region ctor
        /// <summary>
        /// Constructor.
        /// </summary>
        /// <param name="project">The associated project.</param>
        /// <param name="nodeWithItems">The node that defines the items.</param>
        internal OANavigableProjectItems(OAProject project, HierarchyNode nodeWithItems)
        {
            this.project = project;
            this.nodeWithItems = nodeWithItems;
        }

        #endregion

        #region EnvDTE.ProjectItems

        /// <summary>
        /// Gets a value indicating the number of objects in the collection.
        /// </summary>
        public virtual int Count
        {
            get
            {
                var count = 0;

                this.project.ProjectNode.Site.GetUIThread().Invoke(() =>
                {
                    for (var child = this.NodeWithItems.FirstChild; child != null; child = child.NextSibling)
                    {
                        if (!child.IsNonMemberItem && child.GetAutomationObject() is EnvDTE.ProjectItem)
                        {
                            count += 1;
                        }
                    }
                });
                return count;
            }
        }

        /// <summary>
        /// Gets the immediate parent object of a ProjectItems collection.
        /// </summary>
        public virtual object Parent => this.nodeWithItems.GetAutomationObject();

        /// <summary>
        /// Gets an enumeration indicating the type of object.
        /// </summary>
        public virtual string Kind =>
                // TODO:  Add OAProjectItems.Kind getter implementation
                null;

        /// <summary>
        /// Gets the top-level extensibility object.
        /// </summary>
        public virtual EnvDTE.DTE DTE => (EnvDTE.DTE)this.project.DTE;

        /// <summary>
        /// Gets the project hosting the project item or items.
        /// </summary>
        public virtual EnvDTE.Project ContainingProject => this.project;

        /// <summary>
        /// Adds one or more ProjectItem objects from a directory to the ProjectItems collection. 
        /// </summary>
        /// <param name="directory">The directory from which to add the project item.</param>
        /// <returns>A ProjectItem object.</returns>
        public virtual EnvDTE.ProjectItem AddFromDirectory(string directory)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Creates a new project item from an existing item template file and adds it to the project. 
        /// </summary>
        /// <param name="fileName">The full path and file name of the template project file.</param>
        /// <param name="name">The file name to use for the new project item.</param>
        /// <returns>A ProjectItem object. </returns>
        public virtual EnvDTE.ProjectItem AddFromTemplate(string fileName, string name)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Creates a new folder in Solution Explorer. 
        /// </summary>
        /// <param name="name">The name of the folder node in Solution Explorer.</param>
        /// <param name="kind">The type of folder to add. The available values are based on vsProjectItemsKindConstants and vsProjectItemKindConstants</param>
        /// <returns>A ProjectItem object.</returns>
        public virtual EnvDTE.ProjectItem AddFolder(string name, string kind)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Copies a source file and adds it to the project. 
        /// </summary>
        /// <param name="filePath">The path and file name of the project item to be added.</param>
        /// <returns>A ProjectItem object. </returns>
        public virtual EnvDTE.ProjectItem AddFromFileCopy(string filePath)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Adds a project item from a file that is installed in a project directory structure. 
        /// </summary>
        /// <param name="fileName">The file name of the item to add as a project item. </param>
        /// <returns>A ProjectItem object. </returns>
        public virtual EnvDTE.ProjectItem AddFromFile(string fileName)
        {
            throw new NotImplementedException();
        }

        /// <summary>
        /// Get Project Item from index
        /// </summary>
        /// <param name="index">Either index by number (1-based) or by name can be used to get the item</param>
        /// <returns>Project Item. ArgumentException if invalid index is specified</returns>
        public virtual EnvDTE.ProjectItem Item(object index)
        {
            // Changed from MPFProj: throws ArgumentException instead of returning null (http://mpfproj10.codeplex.com/workitem/9158)
            if (index is int)
            {
                var realIndex = (int)index - 1;
                if (realIndex >= 0)
                {
                    for (var child = this.NodeWithItems.FirstChild; child != null; child = child.NextSibling)
                    {
                        if (child.IsNonMemberItem)
                        {
                            continue;
                        }
                        if (child.GetAutomationObject() is EnvDTE.ProjectItem item)
                        {
                            if (realIndex == 0)
                            {
                                return item;
                            }
                            realIndex -= 1;
                        }
                    }
                }
            }
            else if (index is string)
            {
                var name = (string)index;
                for (var child = this.NodeWithItems.FirstChild; child != null; child = child.NextSibling)
                {
                    if (child.IsNonMemberItem)
                    {
                        continue;
                    }
                    if (child.GetAutomationObject() is EnvDTE.ProjectItem item && StringComparer.OrdinalIgnoreCase.Equals(item.Name, name))
                    {
                        return item;
                    }
                }
            }
            throw new ArgumentException($"Failed to find item: {index}", nameof(index));
        }

        /// <summary>
        /// Returns an enumeration for items in a collection. 
        /// </summary>
        /// <returns>An IEnumerator for this object.</returns>
        public virtual IEnumerator GetEnumerator()
        {
            for (var child = this.NodeWithItems.FirstChild; child != null; child = child.NextSibling)
            {
                if (child.IsNonMemberItem)
                {
                    continue;
                }
                if (child.GetAutomationObject() is EnvDTE.ProjectItem item)
                {
                    yield return item;
                }
            }
        }

        #endregion
    }
}
