using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Newtonsoft.Json;
using NuGet.Commands;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace NuGet.Build.Tasks
{
    public class GetProjectJsonTask : Task
    {
        /// <summary>
        /// Full path to the msbuild project.
        /// </summary>
        [Required]
        public string RestoreProjectPath { get; set; }

        /// <summary>
        /// .dg file entries
        /// </summary>
        [Output]
        public string[] RestoreGraphItemsFromProjectJson { get; set; }

        public override bool Execute()
        {
            var entries = new List<string>();

            var packageSpec = GetPackageSpec(RestoreProjectPath);

            if (packageSpec != null)
            {
                var entryGroups = MSBuildPackageSpecUtility.GetDGFileEntryGroups(packageSpec);

                entries.AddRange(entryGroups.SelectMany(e => e.Value));
            }

            RestoreGraphItemsFromProjectJson = entries.ToArray();

            return true;
        }

        private PackageSpec GetPackageSpec(string msbuildProjectPath)
        {
            PackageSpec result = null;
            var directory = Path.GetDirectoryName(msbuildProjectPath);
            var projectName = Path.GetFileNameWithoutExtension(msbuildProjectPath);

            if (msbuildProjectPath.EndsWith(XProjUtility.XProjExtension, StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"{msbuildProjectPath} : xproj is not supported.");
            }

            // Allow project.json or projectName.project.json
            var path = ProjectJsonPathUtilities.GetProjectConfigPath(directory, projectName);

            if (File.Exists(path))
            {
                result = JsonPackageSpecReader.GetPackageSpec(projectName, path);
            }

            return result;
        }
    }
}
