using System;
using System.Collections.Generic;
using System.Linq;
using NuGet.LibraryModel;
using NuGet.ProjectModel;

namespace NuGet.Commands
{
    public static class MSBuildPackageSpecUtility
    {
        /// <summary>
        /// Convert a project.json file to .dg file entries.
        /// </summary>
        public static Dictionary<string, List<string>> GetDGFileEntryGroups(PackageSpec packageSpec)
        {
            var entryGroups = new Dictionary<string, List<string>>();

            var targetGraphs = new List<Tuple<TargetFrameworkInformation, string>>();

            foreach (var frameworkInfo in packageSpec.TargetFrameworks)
            {
                targetGraphs.Add(new Tuple<TargetFrameworkInformation, string>(frameworkInfo, null));

                //foreach (var rid in packageSpec.RuntimeGraph.Runtimes.Keys)
                //{
                //    targetGraphs.Add(new Tuple<TargetFrameworkInformation, string>(frameworkInfo, rid));
                //}
            }

            foreach (var targetGraph in targetGraphs)
            {
                var entries = new List<string>();

                var frameworkInfo = targetGraph.Item1;
                var runtime = targetGraph.Item2;

                var targetName = frameworkInfo.FrameworkName.GetShortFolderName();

                if (!string.IsNullOrEmpty(targetGraph.Item2))
                {
                    targetName += $"/{targetGraph.Item2}";
                }

                entryGroups.Add(targetName, entries);

                // Create a new output section
                entries.Add($"$:{targetName}");
                entries.Add($"+:ProjectJsonPath|{packageSpec.FilePath}");

                entries.Add($"+:Framework|{frameworkInfo.FrameworkName.GetShortFolderName()}");
                // entries.Add($"+:Runtime|{targetGraph.Item2}");

                if (frameworkInfo.Imports.Count > 0)
                {
                    var imports = string.Join("|", frameworkInfo.Imports.Select(f => f.GetShortFolderName()));

                    entries.Add($"+:FrameworkImports|{imports}");
                }

                var added = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var dependency in frameworkInfo.Dependencies.Concat(packageSpec.Dependencies))
                {
                    // Filter to only packages/projects that have not yet been added, this skips framework assemblies
                    if (dependency.LibraryRange.TypeConstraintAllowsAnyOf(LibraryDependencyTarget.PackageProjectExternal)
                        && !added.Add(dependency.Name))
                    {
                        entries.Add(GetDependency(dependency));
                    }
                }
            }

            return entryGroups;
        }

        private static string GetDependency(LibraryDependency dependency)
        {
            var includeFlags = LibraryIncludeFlagUtils.GetFlagString(dependency.IncludeType);
            var suppressParent = LibraryIncludeFlagUtils.GetFlagString(dependency.SuppressParent);
            var target = LibraryDependencyTargetUtils.GetFlagString(dependency.LibraryRange.TypeConstraint);

            return $"@:{dependency.Name}|{dependency.LibraryRange.VersionRange.ToNormalizedString()}|{target}|{includeFlags}|{suppressParent}";
        }
    }
}
