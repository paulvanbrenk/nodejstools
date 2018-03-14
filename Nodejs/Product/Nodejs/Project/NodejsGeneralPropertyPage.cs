﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Microsoft.VisualStudioTools;
using Microsoft.VisualStudioTools.Project;

namespace Microsoft.NodejsTools.Project
{
    [Guid("62E8E091-6914-498E-A47B-6F198DC1873D")]
    internal class NodejsGeneralPropertyPage : CommonPropertyPage
    {
        private readonly NodejsGeneralPropertyPageControl control;

        public NodejsGeneralPropertyPage()
        {
            this.control = new NodejsGeneralPropertyPageControl(this);
        }

        protected override Control CreateControl()
        {
            return this.control;
        }

        public override Control Control => this.control;

        protected override Type ControlType => typeof(NodejsGeneralPropertyPageControl);

        internal override CommonProjectNode Project
        {
            get
            {
                return base.Project;
            }
            set
            {
                if (value == null && base.Project != null)
                {
                    base.Project.PropertyPage = null;
                }
                base.Project = value;
                if (value != null)
                {
                    ((NodejsProjectNode)value).PropertyPage = this;
                }
            }
        }

        protected override void Apply()
        {
            this.Project.SetProjectProperty(CommonConstants.StartupFile, this.control.ScriptFile);
            this.Project.SetProjectProperty(NodeProjectProperty.ScriptArguments, this.control.ScriptArguments);
            this.Project.SetProjectProperty(NodeProjectProperty.NodejsPort, this.control.NodejsPort);
            this.Project.SetProjectProperty(NodeProjectProperty.StartWebBrowser, this.control.StartWebBrowser.ToString());
            this.Project.SetProjectProperty(CommonConstants.WorkingDirectory, this.control.WorkingDirectory);
            this.Project.SetProjectProperty(NodeProjectProperty.LaunchUrl, this.control.LaunchUrl);
            this.Project.SetProjectProperty(NodeProjectProperty.Environment, this.control.Environment);

            var saveNodeSettingsInProject = this.control.SaveNodeSettingsInProject;

            if (this.Project.SetProjectProperty(NodeProjectProperty.SaveNodeJsSettingsInProjectFile, saveNodeSettingsInProject.ToString()))
            {
                // We move the settings first to the right file, and then write them.
                // Ideally we would delete them first, and then write the correct value to the correct file,
                // however there is some caching happening in the Project Code, which we can't reliably break.
                // Further, writing them is a no-op if the value hasn't changed.
                if (saveNodeSettingsInProject)
                {
                    this.Project.MovePropertyToProjectFile(NodeProjectProperty.NodeExePath);
                    this.Project.MovePropertyToProjectFile(NodeProjectProperty.NodeExeArguments);
                    this.Project.MovePropertyToProjectFile(NodeProjectProperty.DebuggerPort);
                }
                else
                {
                    this.Project.MovePropertyToUserFile(NodeProjectProperty.NodeExePath);
                    this.Project.MovePropertyToUserFile(NodeProjectProperty.NodeExeArguments);
                    this.Project.MovePropertyToUserFile(NodeProjectProperty.DebuggerPort);
                }
            }

            if (saveNodeSettingsInProject)
            {
                // ensure we only have the properties in one location
                this.Project.SetProjectProperty(NodeProjectProperty.NodeExePath, this.control.NodeExePath);
                this.Project.SetProjectProperty(NodeProjectProperty.NodeExeArguments, this.control.NodeExeArguments);
                this.Project.SetProjectProperty(NodeProjectProperty.DebuggerPort, this.control.DebuggerPort);
            }
            else
            {
                this.Project.SetUserProjectProperty(NodeProjectProperty.NodeExePath, this.control.NodeExePath);
                this.Project.SetUserProjectProperty(NodeProjectProperty.NodeExeArguments, this.control.NodeExeArguments);
                this.Project.SetUserProjectProperty(NodeProjectProperty.DebuggerPort, this.control.DebuggerPort);
            }

            this.control.IsDirty = false;
        }

        public override void LoadSettings()
        {
            this.control.NodeExeArguments = this.Project.GetUnevaluatedProperty(NodeProjectProperty.NodeExeArguments);
            this.control.NodeExePath = this.Project.GetUnevaluatedProperty(NodeProjectProperty.NodeExePath);
            this.control.ScriptFile = this.Project.GetUnevaluatedProperty(CommonConstants.StartupFile);
            this.control.ScriptArguments = this.Project.GetUnevaluatedProperty(NodeProjectProperty.ScriptArguments);
            this.control.WorkingDirectory = this.Project.GetUnevaluatedProperty(CommonConstants.WorkingDirectory);
            this.control.LaunchUrl = this.Project.GetUnevaluatedProperty(NodeProjectProperty.LaunchUrl);
            this.control.NodejsPort = this.Project.GetUnevaluatedProperty(NodeProjectProperty.NodejsPort);
            this.control.DebuggerPort = this.Project.GetUnevaluatedProperty(NodeProjectProperty.DebuggerPort);
            this.control.Environment = this.Project.GetUnevaluatedProperty(NodeProjectProperty.Environment);
            this.control.StartWebBrowser = GetBoolProperty(this.Project, NodeProjectProperty.StartWebBrowser);
            this.control.SaveNodeSettingsInProject = GetBoolProperty(this.Project, NodeProjectProperty.SaveNodeJsSettingsInProjectFile);

            bool GetBoolProperty(ProjectNode node, string property)
            {
                // Attempt to parse the boolean.  If we fail, assume it is true.
                return bool.TryParse(node.GetUnevaluatedProperty(property), out var result) ? result : true;
            }
        }

        protected override string Title => "General";
    }
}
