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
using Newtonsoft.Json.Linq;
using System.Diagnostics;

namespace NuGet.Commands
{
    /// <summary>
    /// In Memory dg file provider.
    /// </summary>
    public class PreLoadedRestoreRequestProvider : IPreLoadedRestoreRequestProvider
    {
        private readonly JObject _dgFile;
        private readonly RestoreCommandProvidersCache _providerCache;
        private readonly Dictionary<string, PackageSpec> _projectJsonCache = new Dictionary<string, PackageSpec>(StringComparer.Ordinal);

        public PreLoadedRestoreRequestProvider(
            RestoreCommandProvidersCache providerCache,
            JObject dgFile)
        {
            _dgFile = dgFile;
            _providerCache = providerCache;
        }

        public Task<IReadOnlyList<RestoreSummaryRequest>> CreateRequests(RestoreArgs restoreContext)
        {
            var requests = GetRequestsFromItems(restoreContext, _dgFile);

            return Task.FromResult<IReadOnlyList<RestoreSummaryRequest>>(requests);
        }

        private IReadOnlyList<RestoreSummaryRequest> GetRequestsFromItems(RestoreArgs restoreContext, JObject dgFile)
        {
            var requests = new List<RestoreSummaryRequest>();

            // All projects
            var externalProjects = new Dictionary<string, ExternalProjectReference>(StringComparer.Ordinal);

            foreach (var project in dgFile.GetValue<JObject>("projects").Properties())
            {
                var uniqueName = project.Name;
                var specJson = (JObject)project.Value;
                var msbuild = specJson.GetValue<JObject>("msbuild");

                var projectPath = msbuild.GetValue<string>("projectPath");
                var projectJsonPath = msbuild.GetValue<string>("projectJsonPath");

                var spec = new PackageSpec(specJson);
                spec.Name = Path.GetFileNameWithoutExtension(projectPath);
                spec.FilePath = projectJsonPath;

                var projectReferences = new HashSet<string>(
                    msbuild.GetValue<JObject>("projectReferences")
                        .Properties()
                        .Select(p => p.GetValue<string>("projectPath")),
                    StringComparer.Ordinal);

                var reference = new ExternalProjectReference(uniqueName, spec, projectPath, projectReferences);

                externalProjects.Add(reference.UniqueName, reference);
            }

            return requests;
        }

        private RestoreSummaryRequest Create(
            ExternalProjectReference project,
            HashSet<ExternalProjectReference> projectReferenceClosure,
            RestoreArgs restoreContext,
            ISettings settingsOverride)
        {
            // Get settings relative to the input file
            var rootPath = Path.GetDirectoryName(project.PackageSpec.FilePath);

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

            // Create request
            var request = new RestoreRequest(
                project.PackageSpec,
                sharedCache,
                restoreContext.Log,
                disposeProviders: false);

            restoreContext.ApplyStandardProperties(request);

            // Add project references
            request.ExternalProjects = projectReferenceClosure.ToList();

            // Set output type
            request.RestoreOutputType = RestoreOutputType.UAP;
            request.RestoreOutputPath = rootPath;
            request.LockFilePath = Path.Combine(request.RestoreOutputPath, "project.lock.json");

            // The lock file is loaded later since this is an expensive operation
            var summaryRequest = new RestoreSummaryRequest(
                request,
                project.MSBuildProjectPath,
                settings,
                sources);

            return summaryRequest;
        }

        /// <summary>
        /// Return all references for a given project path.
        /// References is modified by this method.
        /// This includes the root project.
        /// </summary>
        private static void CollectReferences(
            ExternalProjectReference root,
            Dictionary<string, ExternalProjectReference> allProjects,
            HashSet<ExternalProjectReference> references)
        {
            if (references.Add(root))
            {
                foreach (var child in root.ExternalProjectReferences)
                {
                    ExternalProjectReference childProject;
                    if (!allProjects.TryGetValue(child, out childProject))
                    {
                        // Let the resolver handle this later
                        Debug.Fail($"Missing project {childProject}");
                    }

                    // Recurse down
                    CollectReferences(childProject, allProjects, references);
                }
            }
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
