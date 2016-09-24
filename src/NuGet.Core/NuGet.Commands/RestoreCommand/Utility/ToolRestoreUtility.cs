using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.ProjectModel;
using NuGet.Versioning;

namespace NuGet.Commands
{
    public static class ToolRestoreUtility
    {
        /// <summary>
        /// Build a package spec in memory to execute the tool restore as if it were
        /// its own project. For now, we always restore for a null runtime and a single
        /// constant framework.
        /// </summary>
        public static PackageSpec GetSpec(string id, VersionRange versionRange, NuGetFramework framework)
        {
            return new PackageSpec(new JObject())
            {
                Name = $"{id}-{Guid.NewGuid().ToString()}", // make sure this package never collides with a dependency
                Dependencies = new List<LibraryDependency>(),
                Tools = new List<ToolDependency>(),
                TargetFrameworks =
                {
                    new TargetFrameworkInformation
                    {
                        FrameworkName = framework,
                        Dependencies = new List<LibraryDependency>
                        {
                            new LibraryDependency
                            {
                                LibraryRange = new LibraryRange(id, versionRange, LibraryDependencyTarget.Package)
                            }
                        }
                    }
                }
            };
        }

        /// <summary>
        /// Only one output can win per packages folder/version range. Between colliding requests take
        /// the intersection of the inputs used.
        /// </summary>
        /// <returns></returns>
        public static IReadOnlyList<RestoreSummaryRequest> GetSubSetRequests(IEnumerable<RestoreSummaryRequest> requestSummaries)
        {
            var results = new List<RestoreSummaryRequest>();
            var tools = new List<RestoreSummaryRequest>();

            foreach (var requestSummary in requestSummaries)
            {
                if (requestSummary.Request.Project.RestoreMetadata?.OutputType == RestoreOutputType.DotnetCliTool)
                {
                    tools.Add(requestSummary);
                }
                else
                {
                    // Pass non-tools to the output
                    results.Add(requestSummary);
                }
            }

            foreach (var toolIdGroup in tools.GroupBy(e => GetToolIdOrNullFromSpec(e.Request.Project), StringComparer.OrdinalIgnoreCase))
            {
                if (string.IsNullOrEmpty(toolIdGroup.Key))
                {
                    // Pass problem requests on to fail with a better error message
                    results.AddRange(toolIdGroup);
                }
                else
                {
                    // Actually narrow down the requests now
                    results.AddRange(GetSubSetRequestsForSingleId(toolIdGroup));
                }
            }

            return results;
        }

        public static IReadOnlyList<RestoreSummaryRequest> GetSubSetRequestsForSingleId(IEnumerable<RestoreSummaryRequest> requests)
        {
            var results = new List<RestoreSummaryRequest>();

            // Unique by packages folder
            foreach (var packagesFolderGroup in requests.GroupBy(e => e.Request.PackagesDirectory, StringComparer.Ordinal))
            {
                // Unique by version range
                foreach (var versionRangeGroup in packagesFolderGroup.GroupBy(e => 
                    GetToolDependencyOrNullFromSpec(e.Request.Project)?.LibraryRange?.VersionRange))
                {
                    // This could be improved in the future, for now take the request with the least sources
                    // to ensure that if this is going to fail anywhere it will *probably* consistently fail.
                    // Take requests with no imports over requests that do need imports to increase the chance
                    // of failing.
                    var bestRequest = versionRangeGroup
                        .OrderBy(e => e.Request.Project.TargetFrameworks.Any(f => f.FrameworkName is FallbackFramework) ? 1 : 0)
                        .ThenBy(e => e.Request.DependencyProviders.RemoteProviders.Count)
                        .First();

                    results.Add(bestRequest);
                }
            }

            return results;
        }

        /// <summary>
        /// Returns the name of the single dependency in the spec or null.
        /// </summary>
        public static string GetToolIdOrNullFromSpec(PackageSpec spec)
        {
            return GetToolDependencyOrNullFromSpec(spec)?.Name;
        }

        /// <summary>
        /// Returns the name of the single dependency in the spec or null.
        /// </summary>
        public static LibraryDependency GetToolDependencyOrNullFromSpec(PackageSpec spec)
        {
            if (spec == null)
            {
                return null;
            }

            return spec.Dependencies.Concat(spec.TargetFrameworks.SelectMany(e => e.Dependencies)).SingleOrDefault();
        }

        /// <summary>
        /// Get the best matching version that exists or null.
        /// </summary>
        public static FileInfo GetToolLockFilePath(DirectoryInfo toolsDir, string id, NuGetFramework framework, VersionRange range)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentNullException(nameof(id));
            }

            if (toolsDir == null)
            {
                throw new ArgumentNullException(nameof(toolsDir));
            }

            if (framework == null)
            {
                throw new ArgumentNullException(nameof(framework));
            }

            if (range == null)
            {
                throw new ArgumentNullException(nameof(range));
            }

            var possible = GetToolLockFilePaths(toolsDir, id)
                .Where(path => framework.Equals(GetFrameworkOrNullFromPath(path)));

            var versions = possible
                .Select(path => GetVersionOrNullFromPath(path))
                .Where(v => v != null)
                .ToList();

            var bestVersion = range.FindBestMatch(versions);

            if (bestVersion != null)
            {
                return possible.First(path => bestVersion.Equals(GetVersionOrNullFromPath(path)));
            }

            return null;
        }

        /// <summary>
        /// Return all lock file paths for a tool id.
        /// </summary>
        public static IReadOnlyList<FileInfo> GetToolLockFilePaths(DirectoryInfo toolsDir, string id)
        {
            if (string.IsNullOrEmpty(id))
            {
                throw new ArgumentNullException(nameof(id));
            }

            if (toolsDir == null)
            {
                throw new ArgumentNullException(nameof(toolsDir));
            }

            var results = new List<FileInfo>();

            if (toolsDir.Exists)
            {
                foreach (var file in toolsDir.GetFiles("project.lock.json", SearchOption.AllDirectories)
                    .Where(f => toolsDir == f.Directory.Parent.Parent.Parent))
                {
                    var foundId = GetIdOrNullFromPath(file);

                    if (id.Equals(foundId, StringComparison.OrdinalIgnoreCase))
                    {
                        // Verify the rest of the path is valid
                        if (GetFrameworkOrNullFromPath(file)?.IsSpecificFramework == true
                            && GetVersionOrNullFromPath(file) != null)
                        {
                            results.Add(file);
                        }
                    }
                }
            }

            return results;
        }

        public static string GetIdOrNullFromPath(FileInfo filePath)
        {
            return filePath?.Directory?.Parent?.Parent?.Name;
        }

        public static NuGetVersion GetVersionOrNullFromPath(FileInfo filePath)
        {
            var dirName = filePath?.Directory?.Parent?.Name;

            if (string.IsNullOrEmpty(dirName))
            {
                return null;
            }

            NuGetVersion version;
            NuGetVersion.TryParse(dirName, out version);

            return version;
        }

        public static NuGetFramework GetFrameworkOrNullFromPath(FileInfo filePath)
        {
            var dirName = filePath?.Directory?.Name;

            if (string.IsNullOrEmpty(dirName))
            {
                return null;
            }

            return NuGetFramework.ParseFolder(dirName);
        }
    }
}
