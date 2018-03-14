// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System.Runtime.InteropServices;
using System.Text;
using Microsoft.NodejsTools.Npm;
using Microsoft.VisualStudioTools.Project;

namespace Microsoft.NodejsTools.Project
{
    [ComVisible(true)]
    [ClassInterface(ClassInterfaceType.AutoDual)]
    [Guid("CA6C9721-2F64-4A1F-99C9-C087F698CB34")]
    public class DependencyNodeProperties : NodeProperties
    {
        internal DependencyNodeProperties(DependencyNode node) : base(node) { }

        private DependencyNode DependencyNode => this.Node as DependencyNode;
        private IPackage Package => this.DependencyNode.Package;
        public override string GetClassName()
        {
            return this.IsSubPackage
                ? Resources.PropertiesClassLocalSubPackage
                : Resources.PropertiesClassLocalPackage;
        }

        [SRCategory(SR.General)]
        [ResourcesDisplayName(nameof(Resources.NpmPackageName))]
        [ResourcesDescription(nameof(Resources.NpmPackageNameDescription))]
        public string PackageName => null == this.Package ? null : this.Package.Name;

        [ResourcesCategory(nameof(Resources.CategoryVersion))]
        [ResourcesDisplayName(nameof(Resources.NpmPackageVersion))]
        [ResourcesDescription(nameof(Resources.NpmPackageVersionDescription))]
        public string PackageVersion => null == this.Package ? null : this.Package.Version.ToString();

        [ResourcesCategory(nameof(Resources.CategoryVersion))]
        [ResourcesDisplayName(nameof(Resources.NpmPackageRequestedVersionRange))]
        [ResourcesDescription(nameof(Resources.NpmPackageRequestedVersionRangeDescription))]
        public string RequestedVersionRange
        {
            get
            {
                var range = null == this.Package ? null : this.Package.RequestedVersionRange;
                return range ?? Resources.RequestedVersionRangeNone;
            }
        }

        // TODO Retrieving the package information is currently too slow to include in properties pane.
        //[ResourcesCategory(nameof(Resources.CategoryVersion))]
        //[ResourcesDisplayName(nameof(Resources.NpmPackageNewVersionAvailable))]
        //[ResourcesDescription(nameof(Resources.NpmPackageNewVersionAvailableDescription))]
        //public string NewVersionAvailable {
        //    get {
        //        if (IsSubPackage) {
        //            return SR.GetString(SR.NewVersionNotApplicableSubpackage);
        //        }

        //        var package = Package;
        //        var catalog = MostRecentlyLoadedCatalog;
        //        if (null == catalog || null == package) {
        //            return SR.GetString(SR.NewVersionUnknown);
        //        }

        //        var listed = catalog[package.Name];
        //        if (null == listed) {
        //            return SR.GetString(SR.NewVersionUnknown);
        //        }

        //        return listed.Version > package.Version
        //            ? SR.GetString(SR.NewVersionYes, listed.Version)
        //            : SR.GetString(SR.NewVersionNo);
        //    }
        //}

        [SRCategory(SR.General)]
        [ResourcesDisplayName(nameof(Resources.NpmPackageDescription))]
        [ResourcesDescription(nameof(Resources.NpmPackageDescriptionDescription))]
        public string Description => null == this.Package ? null : this.Package.Description;

        [SRCategory(SR.General)]
        [ResourcesDisplayName(nameof(Resources.NpmPackageKeywords))]
        [ResourcesDescription(nameof(Resources.NpmPackageKeywordsDescription))]
        public string Keywords
        {
            get
            {
                if (null == this.Package)
                {
                    return null;
                }

                var buff = new StringBuilder();
                foreach (var keyword in this.Package.Keywords)
                {
                    if (buff.Length > 0)
                    {
                        buff.Append(", ");
                    }
                    buff.Append(keyword);
                }
                return buff.ToString();
            }
        }

        [SRCategory(SR.General)]
        [ResourcesDisplayName(nameof(Resources.NpmPackageAuthor))]
        [ResourcesDescription(nameof(Resources.NpmPackageAuthorDescription))]
        public string Author
        {
            get
            {
                var author = null == this.Package ? null : this.Package.Author;
                return null == author ? null : author.ToString();
            }
        }

        [SRCategory(SR.General)]
        [ResourcesDisplayName(nameof(Resources.NpmPackagePath))]
        [ResourcesDescription(nameof(Resources.NpmPackagePathDescription))]
        public string Path => null == this.Package ? null : this.Package.Path;

        internal bool IsSubPackage
        {
            get
            {
                if (this.DependencyNode is HierarchyNode node && node.Parent is DependencyNode)
                {
                    return true;
                }
                return false;
            }
        }

        [SRCategory(SR.General)]
        [ResourcesDisplayName(nameof(Resources.NpmPackageType))]
        [ResourcesDescription(nameof(Resources.NpmPackageTypeDescription))]
        public string PackageType => this.IsSubPackage
                    ? Resources.PackageTypeLocalSubpackage
                    : Resources.PackageTypeLocal;

        [SRCategory(SR.General)]
        [ResourcesDisplayName(nameof(Resources.NpmPackageLinkStatus))]
        [ResourcesDescription(nameof(Resources.NpmPackageLinkStatusDescription))]
        public string LinkStatus
        {
            get
            {
                if (this.IsSubPackage)
                {
                    return Resources.LinkStatusNotApplicableSubPackages;
                }
                return Resources.LinkStatusUnknown;
            }
        }

        [ResourcesCategory(nameof(Resources.CategoryStatus))]
        [ResourcesDisplayName(nameof(Resources.NpmPackageIsListedInParentPackageJson))]
        [ResourcesDescription(nameof(Resources.NpmPackageIsListedInParentPackageJsonDescription))]
        public bool IsListedInParentPackageJson => null != this.Package && this.Package.IsListedInParentPackageJson;

        [ResourcesCategory(nameof(Resources.CategoryStatus))]
        [ResourcesDisplayName(nameof(Resources.NpmPackageIsMissing))]
        [ResourcesDescription(nameof(Resources.NpmPackageIsMissingDescription))]
        public bool IsMissing => null != this.Package && this.Package.IsMissing;

        [ResourcesCategory(nameof(Resources.CategoryStatus))]
        [ResourcesDisplayName(nameof(Resources.NpmPackageIsDevDependency))]
        [ResourcesDescription(nameof(Resources.NpmPackageIsDevDependencyDescription))]
        public bool IsDevDependency => null != this.Package && this.Package.IsDevDependency;

        [ResourcesCategory(nameof(Resources.CategoryStatus))]
        [ResourcesDisplayName(nameof(Resources.NpmPackageIsOptionalDependency))]
        [ResourcesDescription(nameof(Resources.NpmPackageIsOptionalDependencyDescription))]
        public bool IsOptionalDependency => null != this.Package && this.Package.IsOptionalDependency;

        [ResourcesCategory(nameof(Resources.CategoryStatus))]
        [ResourcesDisplayName(nameof(Resources.NpmPackageIsBundledDependency))]
        [ResourcesDescription(nameof(Resources.NpmPackageIsBundledDependencyDescription))]
        public bool IsBundledDependency => null != this.Package && this.Package.IsBundledDependency;
    }
}
