﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudioTools;
using MSBuild = Microsoft.Build.Evaluation;

namespace Microsoft.NodejsTools.TypeScript
{
    internal static class TypeScriptHelpers
    {
        internal static bool IsTypeScriptFile(string filename)
        {
            var extension = Path.GetExtension(filename);

            return StringComparer.OrdinalIgnoreCase.Equals(extension, NodejsConstants.TypeScriptExtension)
                || StringComparer.OrdinalIgnoreCase.Equals(extension, NodejsConstants.TypeScriptJsxExtension);
        }

        internal static bool IsTsJsConfigJsonFile(string filePath)
        {
            var fileName = Path.GetFileName(filePath);
            return StringComparer.OrdinalIgnoreCase.Equals(fileName, NodejsConstants.TsConfigJsonFile) ||
                StringComparer.OrdinalIgnoreCase.Equals(fileName, NodejsConstants.JsConfigJsonFile);
        }

        internal static string GetTypeScriptBackedJavaScriptFile(MSBuild.Project project, string pathToFile)
        {
            var typeScriptOutDir = project.GetPropertyValue(NodeProjectProperty.TypeScriptOutDir);
            return GetTypeScriptBackedJavaScriptFile(project.DirectoryPath, typeScriptOutDir, pathToFile);
        }

        internal static string GetTypeScriptBackedJavaScriptFile(IVsProject project, string pathToFile)
        {
            //Need to deal with the format being relative and explicit
            var props = (IVsBuildPropertyStorage)project;
            ErrorHandler.ThrowOnFailure(props.GetPropertyValue(NodeProjectProperty.TypeScriptOutDir, null, 0, out var outDir));

            var projHome = GetProjectHome(project);

            return GetTypeScriptBackedJavaScriptFile(projHome, outDir, pathToFile);
        }

        private static string GetTypeScriptBackedJavaScriptFile(string projectHome, string typeScriptOutDir, string pathToFile)
        {
            var jsFilePath = Path.ChangeExtension(pathToFile, NodejsConstants.JavaScriptExtension);

            if (string.IsNullOrEmpty(typeScriptOutDir))
            {
                //No setting for OutDir
                //  .js file is created next to .ts file
                return jsFilePath;
            }

            //Get the full path to outDir
            //  If outDir is rooted then outDirPath is going to be outDir ending with backslash
            var outDirPath = CommonUtils.GetAbsoluteDirectoryPath(projectHome, typeScriptOutDir);

            //Find the relative path to the file from projectRoot
            //  This folder structure will be mirrored in the TypeScriptOutDir
            var relativeJSFilePath = CommonUtils.GetRelativeFilePath(projectHome, jsFilePath);

            return Path.Combine(outDirPath, relativeJSFilePath);
        }

        private static string GetProjectHome(IVsProject project)
        {
            Debug.Assert(project != null);
            var hier = (IVsHierarchy)project;
            ErrorHandler.ThrowOnFailure(hier.GetProperty(
                (uint)VSConstants.VSITEMID.Root,
                (int)__VSHPROPID.VSHPROPID_ExtObject,
                out var extObject
            ));
            var proj = extObject as EnvDTE.Project;
            if (proj == null)
            {
                return null;
            }
            var props = proj.Properties;
            if (props == null)
            {
                return null;
            }
            var projHome = props.Item("ProjectHome");
            if (projHome == null)
            {
                return null;
            }

            return projHome.Value as string;
        }
    }
}
