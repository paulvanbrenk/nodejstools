﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

namespace Microsoft.NodejsTools.Npm.SPI
{
    internal class GenericNpmCommand : NpmCommand
    {
        public GenericNpmCommand(
            string fullPathToRootPackageDirectory,
            string arguments,
            bool showConsole,
            string pathToNpm = null) 
            : base(
            fullPathToRootPackageDirectory,
            showConsole,
            pathToNpm)
        {
            this.Arguments = arguments;
        }
    }
}
