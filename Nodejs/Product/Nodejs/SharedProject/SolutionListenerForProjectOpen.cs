// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Diagnostics;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudioTools.Project.Automation;
using IServiceProvider = System.IServiceProvider;

namespace Microsoft.VisualStudioTools.Project
{
    internal class SolutionListenerForProjectOpen : SolutionListener
    {
        public SolutionListenerForProjectOpen(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
        }

        public override int OnAfterOpenProject(IVsHierarchy hierarchy, int added)
        {
            // If this is our project, notify it that it has been opened.
            if (hierarchy.GetProject() != null)
            {
                if (hierarchy.GetProject() is OAProject oaProject && oaProject.Project is ProjectNode)
                {
                    ((ProjectNode)oaProject.Project).OnAfterProjectOpen();
                }
            }

            // If this is a new project and our project. We use here that it is only our project that will implemnet the "internal"  IBuildDependencyOnProjectContainer.
            if (added != 0 && hierarchy is IBuildDependencyUpdate)
            {
                var uiHierarchy = hierarchy as IVsUIHierarchy;
                Debug.Assert(uiHierarchy != null, "The ProjectNode should implement IVsUIHierarchy");
                if (uiHierarchy == null)
                {
                    return VSConstants.E_FAIL;
                }
                // Expand and select project node
                var uiWindow = UIHierarchyUtilities.GetUIHierarchyWindow(this.ServiceProvider, HierarchyNode.SolutionExplorer);
                if (uiWindow != null)
                {
                    __VSHIERARCHYITEMSTATE state;
                    if (uiWindow.GetItemState(uiHierarchy, VSConstants.VSITEMID_ROOT, (uint)__VSHIERARCHYITEMSTATE.HIS_Expanded, out var stateAsInt) == VSConstants.S_OK)
                    {
                        state = (__VSHIERARCHYITEMSTATE)stateAsInt;
                        if (state != __VSHIERARCHYITEMSTATE.HIS_Expanded)
                        {
                            int hr;
                            hr = uiWindow.ExpandItem(uiHierarchy, VSConstants.VSITEMID_ROOT, EXPANDFLAGS.EXPF_ExpandParentsToShowItem);
                            if (ErrorHandler.Failed(hr))
                            {
                                Trace.WriteLine("Failed to expand project node");
                            }

                            hr = uiWindow.ExpandItem(uiHierarchy, VSConstants.VSITEMID_ROOT, EXPANDFLAGS.EXPF_SelectItem);
                            if (ErrorHandler.Failed(hr))
                            {
                                Trace.WriteLine("Failed to select project node");
                            }

                            return hr;
                        }
                    }
                }
            }
            return VSConstants.S_OK;
        }
    }
}
