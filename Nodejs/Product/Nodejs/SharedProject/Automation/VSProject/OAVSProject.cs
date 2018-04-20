// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Runtime.InteropServices;
using EnvDTE;
using VSLangProj;

namespace Microsoft.VisualStudioTools.Project.Automation
{
    /// <summary>
    /// Represents an automation friendly version of a language-specific project.
    /// </summary>
    [ComVisible(true), CLSCompliant(false)]
    public class OAVSProject : VSProject
    {
        #region fields
        private readonly ProjectNode project;
        private OAVSProjectEvents events;
        #endregion

        #region ctors
        internal OAVSProject(ProjectNode project)
        {
            this.project = project;
        }
        #endregion

        #region VSProject Members

        public virtual ProjectItem AddWebReference(string bstrUrl)
        {
            throw new NotImplementedException();
        }

        public virtual BuildManager BuildManager => throw new NotImplementedException();

        public virtual void CopyProject(string bstrDestFolder, string bstrDestUNCPath, prjCopyProjectOption copyProjectOption, string bstrUsername, string bstrPassword)
        {
            throw new NotImplementedException();
        }

        public virtual ProjectItem CreateWebReferencesFolder()
        {
            throw new NotImplementedException();
        }

        public virtual DTE DTE => (EnvDTE.DTE)this.project.Site.GetService(typeof(EnvDTE.DTE));

        public virtual VSProjectEvents Events
        {
            get
            {
                if (this.events == null)
                {
                    this.events = new OAVSProjectEvents(this);
                }

                return this.events;
            }
        }

        public virtual void Exec(prjExecCommand command, int bSuppressUI, object varIn, out object pVarOut)
        {
            throw new NotImplementedException(); ;
        }

        public virtual void GenerateKeyPairFiles(string strPublicPrivateFile, string strPublicOnlyFile)
        {
            throw new NotImplementedException(); ;
        }

        public virtual string GetUniqueFilename(object pDispatch, string bstrRoot, string bstrDesiredExt)
        {
            throw new NotImplementedException(); ;
        }

        public virtual Imports Imports => throw new NotImplementedException();

        public virtual EnvDTE.Project Project => this.project.GetAutomationObject() as EnvDTE.Project;

        public virtual References References
        {
            get
            {
                var references = this.project.GetReferenceContainer() as ReferenceContainerNode;
                if (null == references)
                {
                    return new OAReferences(null, this.project);
                }
                return references.Object as References;
            }
        }

        public virtual void Refresh()
        {
        }

        public virtual string TemplatePath => throw new NotImplementedException();

        public virtual ProjectItem WebReferencesFolder => throw new NotImplementedException();

        public virtual bool WorkOffline
        {
            get
            {
                throw new NotImplementedException();
            }
            set
            {
                throw new NotImplementedException();
            }
        }

        #endregion
    }

    /// <summary>
    /// Provides access to language-specific project events
    /// </summary>
    [ComVisible(true), CLSCompliant(false)]
    public class OAVSProjectEvents : VSProjectEvents
    {
        #region fields
        private readonly OAVSProject vsProject;
        #endregion

        #region ctors
        public OAVSProjectEvents(OAVSProject vsProject)
        {
            this.vsProject = vsProject;
        }
        #endregion

        #region VSProjectEvents Members

        public virtual BuildManagerEvents BuildManagerEvents => this.vsProject.BuildManager as BuildManagerEvents;

        public virtual ImportsEvents ImportsEvents => throw new NotImplementedException();

        public virtual ReferencesEvents ReferencesEvents => this.vsProject.References as ReferencesEvents;

        #endregion
    }
}
