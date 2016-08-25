using NuGet.Configuration;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.ProjectModel;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using NuGet.Versioning;

namespace NuGet.Commands
{
    /// <summary>
    /// In Memory dg file provider.
    /// </summary>
    public class PreLoadedRestoreRequestProvider : IPreLoadedRestoreRequestProvider
    {
        private readonly MSBuildItem[] _items;
        private readonly RestoreCommandProvidersCache _providerCache;

        public PreLoadedRestoreRequestProvider(
            RestoreCommandProvidersCache providerCache,
            MSBuildItem[] items)
        {
            _items = items;
            _providerCache = providerCache;
        }

        public Task<IReadOnlyList<RestoreSummaryRequest>> CreateRequests(RestoreArgs restoreContext)
        {
            var requests = GetRequestsFromItems(restoreContext, _items);

            return Task.FromResult<IReadOnlyList<RestoreSummaryRequest>>(requests);
        }

        private IReadOnlyList<RestoreSummaryRequest> GetRequestsFromItems(RestoreArgs restoreContext, MSBuildItem[] items)
        {
            var requests = new List<RestoreSummaryRequest>();

            // Index items
            var restoreSpecs = new List<MSBuildItem>();
            var projectSpecs = new List<MSBuildItem>();
            var indexBySpecId = new Dictionary<string, List<MSBuildItem>>(StringComparer.OrdinalIgnoreCase);
            var projectsByPath = new Dictionary<string, List<MSBuildItem>>();

            foreach (var item in items)
            {
                var type = item.Metadata["Type"].ToLowerInvariant();

                if (type == "projectspec")
                {
                    var id = item.Metadata["ProjectSpecId"];

                    var projectPath = item.Metadata["ProjectPath"];

                    List<MSBuildItem> projectEntries;
                    if (!projectsByPath.TryGetValue(projectPath, out projectEntries))
                    {
                        projectEntries = new List<MSBuildItem>(1);

                        projectsByPath.Add(projectPath, projectEntries);
                    }

                    projectEntries.Add(item);
                }
                else if (type == "restorespec")
                {
                    restoreSpecs.Add(item);
                }
                else
                {
                    var id = item.Metadata["ProjectSpecId"];

                    List<MSBuildItem> idItems;
                    if (!indexBySpecId.TryGetValue(id, out idItems))
                    {
                        idItems = new List<MSBuildItem>(1);
                        indexBySpecId.Add(id, idItems);
                    }

                    idItems.Add(item);
                }
            }

            // Create requests
            foreach (var projectPath in projectsByPath.Keys)
            {
                var projectItems = projectsByPath[projectPath];

                var projectReferences = new Dictionary<NuGetFramework, IReadOnlyList<string>>();

                var dependencies = new Dictionary<NuGetFramework, IList<LibraryDependency>>();
                var properties = new Dictionary<NuGetFramework, IDictionary<string, string>>();
                var specFrameworksById = new Dictionary<string, NuGetFramework>(StringComparer.OrdinalIgnoreCase);

                // Find all target frameworks
                foreach (var spec in GetItemByType(projectItems, "ProjectSpec"))
                {
                    var framework = NuGetFramework.Parse(spec.Metadata["TargetFramework"]);

                    dependencies.Add(framework, new List<LibraryDependency>());
                    properties.Add(framework, spec.Metadata);
                    specFrameworksById.Add(spec.Metadata["ProjectSpecId"], framework);
                }

                // Package dependencies
                foreach (var item in GetItemByType(projectItems, "Dependency"))
                {
                    var specId = item.Metadata["ProjectSpecId"];
                    var framework = specFrameworksById[specId];

                    // Target
                    var typeConstraint = LibraryDependencyTarget.PackageProjectExternal;
                    var typeConstraintString = GetProperty(item, "Target");

                    if (typeConstraintString != null)
                    {
                        typeConstraint = LibraryDependencyTargetUtils.Parse(typeConstraintString);
                    }

                    var libraryRange = new LibraryRange(
                        name: item.Metadata["Id"],
                        versionRange: VersionRange.Parse(item.Metadata["VersionRange"]),
                        typeConstraint: typeConstraint);


                    var dependency = new LibraryDependency();
                    dependency.LibraryRange = libraryRange;

                    var includeFlagsString = GetProperty(item, "IncludeFlags");

                    if (includeFlagsString != null)
                    {
                        dependency.IncludeType = LibraryIncludeFlagUtils.GetFlags(includeFlagsString.Split(',').Select(s => s.Trim()));
                    }

                    var suppressParentString = GetProperty(item, "SuppressParent");

                    if (suppressParentString != null)
                    {
                        dependency.SuppressParent = LibraryIncludeFlagUtils.GetFlags(suppressParentString.Split(',').Select(s => s.Trim()));
                    }

                    dependencies[framework].Add(dependency);
                }

                // Project dependencies
                foreach (var item in GetItemByType(projectItems, "ProjectReference"))
                {
                    var specId = item.Metadata["ProjectSpecId"];
                    var framework = specFrameworksById[specId];
                    var referencePath = item.Metadata["ProjectPath"];

                    // Target
                    var typeConstraint = LibraryDependencyTarget.ExternalProject;

                    var libraryRange = new LibraryRange(
                        name: referencePath,
                        versionRange: VersionRange.All,
                        typeConstraint: typeConstraint);


                    var dependency = new LibraryDependency();
                    dependency.LibraryRange = libraryRange;

                    var includeFlagsString = GetProperty(item, "IncludeFlags");

                    if (includeFlagsString != null)
                    {
                        dependency.IncludeType = LibraryIncludeFlagUtils.GetFlags(includeFlagsString.Split(',').Select(s => s.Trim()));
                    }

                    var suppressParentString = GetProperty(item, "SuppressParent");

                    if (suppressParentString != null)
                    {
                        dependency.SuppressParent = LibraryIncludeFlagUtils.GetFlags(suppressParentString.Split(',').Select(s => s.Trim()));
                    }

                    dependencies[framework].Add(dependency);
                }

                // Framework assembly dependencies
                foreach (var item in GetItemByType(projectItems, "FrameworkAssembly"))
                {
                    var specId = item.Metadata["ProjectSpecId"];
                    var framework = specFrameworksById[specId];

                    // Get optional range
                    var versionRange = VersionRange.All;
                    var versionRangeString = GetProperty(item, "VersionRange");

                    if (versionRangeString != null)
                    {
                        versionRange = VersionRange.Parse(versionRangeString);
                    }

                    var libraryRange = new LibraryRange(
                        name: item.Metadata["Id"],
                        versionRange: versionRange,
                        typeConstraint: LibraryDependencyTarget.Reference);


                    var dependency = new LibraryDependency();
                    dependency.LibraryRange = libraryRange;

                    var suppressParentString = GetProperty(item, "SuppressParent");

                    if (suppressParentString != null)
                    {
                        dependency.SuppressParent = LibraryIncludeFlagUtils.GetFlags(suppressParentString.Split(',').Select(s => s.Trim()));
                    }

                    dependencies[framework].Add(dependency);
                }

                // Create the project spec
                var projectSpec = new ProjectSpec(
                    projectPath,
                    projectPath,
                    dependencies.Keys,
                    dependencies,
                    properties);
            }

            foreach (var project in projectsByPath.Values)
            {
                var request = Create(
                    project,
                    restoreContext,
                    settingsOverride: null);

                requests.Add(request);
            }

            return requests;
        }

        private RestoreSummaryRequest Create(
            ExternalProjectReference project,
            RestoreArgs restoreContext,
            ISettings settingsOverride)
        {
            // Get settings relative to the input file
            var rootPath = Path.GetDirectoryName(project.PackageSpecPath);

            var settings = settingsOverride;

            if (settings == null)
            {
                settings = restoreContext.GetSettings(rootPath);
            }

            var globalPath = restoreContext.GetEffectiveGlobalPackagesFolder(rootPath, settings);
            var fallbackPaths = restoreContext.GetEffectiveFallbackPackageFolders(settings);

            var sources = restoreContext.GetEffectiveSources(settings);

            var sharedCache = _providerCache.GetOrCreate(
                globalPath,
                fallbackPaths,
                sources,
                restoreContext.CacheContext,
                restoreContext.Log);

            var request = new RestoreRequest(
                project.PackageSpec,
                sharedCache,
                restoreContext.Log,
                disposeProviders: false);

            restoreContext.ApplyStandardProperties(request);

            // Find all external references
            //var externalReferences = msbuildProvider.GetReferences(project.MSBuildProjectPath).ToList();
            //request.ExternalProjects = externalReferences;

            // Set output type
            if (StringComparer.OrdinalIgnoreCase.Equals("netcore", GetPropertyValue(project, "OutputType")))
            {
                request.RestoreOutputType = RestoreOutputType.NETCore;
                request.RestoreOutputPath = GetPropertyValue(project, "OutputPath");
                request.LockFilePath = Path.Combine(request.RestoreOutputPath, "project.assets.json");
            }

            // The lock file is loaded later since this is an expensive operation
            var summaryRequest = new RestoreSummaryRequest(
                request,
                project.MSBuildProjectPath,
                settings,
                sources);

            return summaryRequest;
        }

        private static IEnumerable<MSBuildItem> GetItemByType(IEnumerable<MSBuildItem> items, string type)
        {
            return items.Where(e => e.Metadata["Type"].Equals(type, StringComparison.OrdinalIgnoreCase));
        }

        private static string GetProperty(MSBuildItem item, string key)
        {
            string val;
            if (item.Metadata.TryGetValue(key, out val) && !string.IsNullOrEmpty(val))
            {
                return val;
            }

            return null;
        }
    }
}
