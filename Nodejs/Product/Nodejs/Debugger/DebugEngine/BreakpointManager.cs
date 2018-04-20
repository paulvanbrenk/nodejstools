// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using Microsoft.VisualStudio.Debugger.Interop;

namespace Microsoft.NodejsTools.Debugger.DebugEngine
{
    // This class manages breakpoints for the engine. 
    internal class BreakpointManager
    {
        private readonly AD7Engine m_engine;
        private readonly System.Collections.Generic.List<AD7PendingBreakpoint> m_pendingBreakpoints;
        private readonly Dictionary<NodeBreakpoint, AD7PendingBreakpoint> _breakpointMap = new Dictionary<NodeBreakpoint, AD7PendingBreakpoint>();
        private readonly Dictionary<NodeBreakpointBinding, AD7BoundBreakpoint> _breakpointBindingMap = new Dictionary<NodeBreakpointBinding, AD7BoundBreakpoint>();

        public BreakpointManager(AD7Engine engine)
        {
            this.m_engine = engine;
            this.m_pendingBreakpoints = new System.Collections.Generic.List<AD7PendingBreakpoint>();
        }

        // A helper method used to construct a new pending breakpoint.
        public void CreatePendingBreakpoint(IDebugBreakpointRequest2 pBPRequest, out IDebugPendingBreakpoint2 ppPendingBP)
        {
            var pendingBreakpoint = new AD7PendingBreakpoint(pBPRequest, this.m_engine, this);
            ppPendingBP = (IDebugPendingBreakpoint2)pendingBreakpoint;
            this.m_pendingBreakpoints.Add(pendingBreakpoint);
        }

        // Called from the engine's detach method to remove the debugger's breakpoint instructions.
        public void ClearBreakpointBindingResults()
        {
            foreach (var pendingBreakpoint in this.m_pendingBreakpoints)
            {
                pendingBreakpoint.ClearBreakpointBindingResults();
            }
        }

        public void AddPendingBreakpoint(NodeBreakpoint breakpoint, AD7PendingBreakpoint pendingBreakpoint)
        {
            this._breakpointMap[breakpoint] = pendingBreakpoint;
        }

        public void RemovePendingBreakpoint(NodeBreakpoint breakpoint)
        {
            this._breakpointMap.Remove(breakpoint);
        }

        public AD7PendingBreakpoint GetPendingBreakpoint(NodeBreakpoint breakpoint)
        {
            return this._breakpointMap[breakpoint];
        }

        public void AddBoundBreakpoint(NodeBreakpointBinding breakpointBinding, AD7BoundBreakpoint boundBreakpoint)
        {
            this._breakpointBindingMap[breakpointBinding] = boundBreakpoint;
        }

        public void RemoveBoundBreakpoint(NodeBreakpointBinding breakpointBinding)
        {
            this._breakpointBindingMap.Remove(breakpointBinding);
        }

        public AD7BoundBreakpoint GetBoundBreakpoint(NodeBreakpointBinding breakpointBinding)
        {
            return this._breakpointBindingMap.TryGetValue(breakpointBinding, out var boundBreakpoint) ? boundBreakpoint : null;
        }
    }
}
