// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Collections.Generic;
using EnvDTE;
using Microsoft.NodejsTools.TestFrameworks;
using Microsoft.VisualStudio.TemplateWizard;

namespace Microsoft.NodejsTools.ProjectWizard
{
    public sealed class UnitTestFileWizard : IWizard
    {
        private string _framework;

        public void BeforeOpeningFile(ProjectItem projectItem)
        {
            EnvDTE.Project project = projectItem.ContainingProject;
        }

        public void ProjectFinishedGenerating(EnvDTE.Project project) { }

        public void ProjectItemFinishedGenerating(ProjectItem projectItem)
        {
            EnvDTE.Property property = projectItem.Properties.Item("TestFramework");
            property.Value = this._framework;
        }

        public void RunFinished() { }

        public void RunStarted(object automationObject, Dictionary<string, string> replacementsDictionary, WizardRunKind runKind, object[] customParams)
        {
            if (!replacementsDictionary.TryGetValue("TestFramework", out this._framework) ||
                string.IsNullOrWhiteSpace(this._framework))
            {
                this._framework = TestFrameworkDirectories.ExportRunnerFrameworkName;
            }
        }

        public bool ShouldAddProjectItem(string filePath)
        {
            return true;
        }
    }
}
