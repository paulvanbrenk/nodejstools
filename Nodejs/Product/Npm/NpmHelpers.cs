﻿// Copyright (c) Microsoft.  All Rights Reserved.  Licensed under the Apache License, Version 2.0.  See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudioTools.Project;

namespace Microsoft.NodejsTools.Npm
{
    public static class NpmHelpers
    {
        internal static async Task<IEnumerable<string>> ExecuteNpmCommandAsync(
            Redirector redirector,
            string pathToNpm,
            string executionDirectory,
            string[] arguments,
            bool showConsole,
            ManualResetEvent cancellationResetEvent = null)
        {
            IEnumerable<string> standardOutputLines = null;

            using (var process = ProcessOutput.Run(
                pathToNpm,
                arguments,
                executionDirectory,
                /*env*/ null,
                showConsole,
                redirector,
                quoteArgs: false,
                outputEncoding: Encoding.UTF8 // npm uses UTF-8 regardless of locale if its output is redirected
                ))
            {
                var whnd = process.WaitHandle;
                if (whnd == null)
                {
                    // Process failed to start, and any exception message has
                    // already been sent through the redirector
                    redirector?.WriteErrorLine(Resources.ErrCannotStartNpm);
                }
                else
                {
                    var handles = cancellationResetEvent != null ? new[] { whnd, cancellationResetEvent } : new[] { whnd };
                    var i = await Task.Run(() => WaitHandle.WaitAny(handles));
                    if (i == 0)
                    {
                        Debug.Assert(process.ExitCode.HasValue, "npm process has not really exited");
                        process.Wait();

                        if (process.StandardOutputLines != null)
                        {
                            standardOutputLines = process.StandardOutputLines.ToList();
                        }

                        redirector?.WriteLine(string.Format(CultureInfo.InvariantCulture,
                            "\r\n===={0}====\r\n\r\n",
                            string.Format(CultureInfo.InvariantCulture, Resources.NpmCommandCompletedWithExitCode, process.ExitCode ?? -1)
                            ));
                    }
                    else
                    {
                        process.Kill();

                        redirector?.WriteErrorLine(string.Format(CultureInfo.InvariantCulture,
                        "\r\n===={0}====\r\n\r\n",
                        Resources.NpmCommandCancelled));

                        if (cancellationResetEvent != null)
                        {
                            cancellationResetEvent.Reset();
                        }
                        throw new OperationCanceledException();
                    }
                }
            }
            return standardOutputLines;
        }

        public static string GetPathToNpm(string nodePath = null)
        {
            var path = GetNpmPathFromNodePath(nodePath);
            if (!string.IsNullOrEmpty(path))
            {
                return path;
            }

            string executable = "npm.cmd";
            path = Nodejs.GetPathToNodeExecutableFromEnvironment(executable);

            if (string.IsNullOrEmpty(path))
            {
                throw new NpmNotFoundException(
                    string.Format(CultureInfo.CurrentCulture,
                        "Cannot find {0} in the registry, your path, or under " +
                        "program files in the nodejs folder.  Ensure Node.js is installed.",
                        executable
                    )
                );
            }

            return path;
        }

        private static string GetNpmPathFromNodePath(string nodePath)
        {
            if (!string.IsNullOrEmpty(nodePath) && File.Exists(nodePath))
            {
                var dir = Path.GetDirectoryName(nodePath);
                var npmPath = string.IsNullOrEmpty(dir) ? null : Path.Combine(dir, "npm.cmd");
                if (npmPath != null && File.Exists(npmPath))
                {
                    return npmPath;
                }
            }
            return null;
        }
    }
}
