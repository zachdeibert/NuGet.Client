using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Commands;
using NuGet.Configuration;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;

namespace NuGet.Build.Tasks
{
    /// <summary>
    /// .NET Core compatible restore task for csproj + project.json.
    /// </summary>
    public class RestoreTask : Task
    {
        /// <summary>
        /// DG file entries
        /// </summary>
        [Required]
        public ITaskItem[] RestoreGraphItems { get; set; }

        /// <summary>
        /// NuGet sources, ; delimited
        /// </summary>
        public string RestoreSources { get; set; }

        /// <summary>
        /// User packages folder
        /// </summary>
        public string RestorePackagesPath { get; set; }

        /// <summary>
        /// Disable parallel project restores and downloads
        /// </summary>
        public bool RestoreDisableParallel { get; set; }

        /// <summary>
        /// NuGet.Config path
        /// </summary>
        public string RestoreConfigFile { get; set; }

        /// <summary>
        /// Disable the web cache
        /// </summary>
        public bool RestoreNoCache { get; set; }

        /// <summary>
        /// Ignore errors from package sources
        /// </summary>
        public bool RestoreIgnoreFailedSources { get; set; }

        public override bool Execute()
        {
            if (RestoreGraphItems.Length < 1)
            {
                Log.LogWarning("Unable to find a project to restore!");
                return true;
            }

            var log = new MSBuildLogger(Log);

            // Log inputs
            log.LogDebug($"(in) RestoreSources '{RestoreSources}'");
            log.LogDebug($"(in) RestorePackagesPath '{RestorePackagesPath}'");
            log.LogDebug($"(in) RestoreDisableParallel '{RestoreDisableParallel}'");
            log.LogDebug($"(in) RestoreConfigFile '{RestoreConfigFile}'");
            log.LogDebug($"(in) RestoreNoCache '{RestoreNoCache}'");
            log.LogDebug($"(in) RestoreIgnoreFailedSources '{RestoreIgnoreFailedSources}'");

            // Log the graph input
            Dump(RestoreGraphItems, log);

            //var graphLines = RestoreGraphItems;
            var providerCache = new RestoreCommandProvidersCache();

            using (var cacheContext = new SourceCacheContext())
            {
                cacheContext.NoCache = RestoreNoCache;
                cacheContext.IgnoreFailedSources = RestoreIgnoreFailedSources;

                // Pre-loaded request provider containing the graph file
                var providers = new List<IPreLoadedRestoreRequestProvider>();

                var dgFile = CreateDGFile(RestoreGraphItems.Select(GetMSBuildItem));

                providers.Add(new PreLoadedRestoreRequestProvider(providerCache, dgFile));

                var defaultSettings = Settings.LoadDefaultSettings(root: null, configFileName: null, machineWideSettings: null);
                var sourceProvider = new CachingSourceProvider(new PackageSourceProvider(defaultSettings));

                var restoreContext = new RestoreArgs()
                {
                    CacheContext = cacheContext,
                    LockFileVersion = LockFileFormat.Version,
                    ConfigFile = GetNullForEmpty(RestoreConfigFile),
                    DisableParallel = RestoreDisableParallel,
                    GlobalPackagesFolder = RestorePackagesPath,
                    Log = log,
                    MachineWideSettings = new XPlatMachineWideSetting(),
                    PreLoadedRequestProviders = providers,
                    CachingSourceProvider = sourceProvider
                };

                if (!string.IsNullOrEmpty(RestoreSources))
                {
                    var sources = RestoreSources.Split(new char[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
                    restoreContext.Sources.AddRange(sources);
                }

                if (restoreContext.DisableParallel)
                {
                    HttpSourceResourceProvider.Throttle = SemaphoreSlimThrottle.CreateBinarySemaphore();
                }

                var restoreSummaries = RestoreRunner.Run(restoreContext).Result;

                // Summary
                RestoreSummary.Log(log, restoreSummaries);

                return restoreSummaries.All(x => x.Success);
            }
        }

        private static JObject CreateDGFile(IEnumerable<MSBuildItem> items)
        {
            var dgFile = new JObject();
            var dgFileProjects = new JObject();
            var dgFileRestoreSpecs = new JArray();
            dgFile.Add("project", dgFileProjects);
            dgFile.Add("restore", dgFileRestoreSpecs);

            // Index items
            var restoreSpecs = new List<MSBuildItem>();
            var projectSpecs = new List<MSBuildItem>();
            var indexBySpecId = new Dictionary<string, List<MSBuildItem>>(StringComparer.OrdinalIgnoreCase);
            var projectsByPath = new Dictionary<string, List<MSBuildItem>>();

            // All projects
            var externalProjects = new Dictionary<string, ExternalProjectReference>(StringComparer.Ordinal);

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
                    // Restore spec
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

            // Create request for UWP
            foreach (var projectPath in projectsByPath.Keys)
            {
                JObject specJson = null;

                var specItems = projectsByPath[projectPath];

                if (specItems.Any(item => "uap".Equals(GetProperty(item, "OutputType"), StringComparison.OrdinalIgnoreCase)))
                {
                    // This must contain exactly one item for UWP
                    var specItem = specItems.SingleOrDefault();

                    if (specItem == null)
                    {
                        throw new InvalidDataException($"Invalid restore data for {projectPath}.");
                    }

                    var projectSpecId = specItem.Metadata["ProjectSpecId"];
                    var projectJsonPath = specItem.Metadata["ProjectJsonPath"];
                    var projectName = Path.GetFileNameWithoutExtension(projectPath);

                    specJson = GetJson(projectJsonPath);

                    // Get project references
                    var projectReferences = new HashSet<string>(StringComparer.Ordinal);

                    List<MSBuildItem> itemsForSpec;
                    if (indexBySpecId.TryGetValue(projectSpecId, out itemsForSpec))
                    {
                        foreach (var item in itemsForSpec)
                        {
                            var type = item.Metadata["Type"].ToLowerInvariant();

                            if (type == "projectreference")
                            {
                                projectReferences.Add(item.Metadata["ProjectPath"]);
                            }
                        }
                    }

                    if (specJson.Property("msbuild") == null)
                    {
                        var msbuildObj = new JObject();
                        specJson.Add("msbuild", msbuildObj);

                        msbuildObj.Add("projectRestoreGuid", projectSpecId);
                        msbuildObj.Add("projectPath", projectPath);
                        msbuildObj.Add("projectJsonPath", projectJsonPath);
                        msbuildObj.Add("outputType", "uap");

                        var projRefs = new JObject();
                        msbuildObj.Add("projectReferences", projRefs);

                        foreach (var referencePath in projectReferences)
                        {
                            var projRef = new JObject();
                            projRefs.Add(referencePath, projRefs);

                            projRef.Add("projectPath", referencePath);
                        }
                    }
                }

                // Add project to file
                dgFileProjects.Add(projectPath, specJson);
            }

            foreach (var restoreSpec in restoreSpecs)
            {
                var restoreSpecJson = new JObject();

                var projectPath = restoreSpec.Metadata["ProjectPath"];

                restoreSpecJson.Add("projectPath", projectPath);

                dgFileRestoreSpecs.Add(restoreSpecJson);
            }

            return dgFile;
        }

        private static JObject GetJson(string path)
        {
            using (var streamReader = new StreamReader(File.OpenRead(path)))
            using (var textReader = new JsonTextReader(streamReader))
            {
                return JObject.Load(textReader);
            }
        }

        /// <summary>
        /// Convert empty strings to null
        /// </summary>
        private static string GetNullForEmpty(string s)
        {
            return string.IsNullOrEmpty(s) ? null : s;
        }

        private static MSBuildItem GetMSBuildItem(ITaskItem item)
        {
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            foreach (var key in item.MetadataNames.OfType<string>())
            {
                try
                {
                    var val = item.GetMetadata(key);

                    if (!string.IsNullOrEmpty(val))
                    {
                        properties.Add(key, val);
                    }
                }
                catch
                {
                    // Ignore errors
                }
            }

            return new MSBuildItem(item.ItemSpec, properties);
        }

        private static void Dump(ITaskItem[] items, MSBuildLogger log)
        {
            foreach (var item in items)
            {
                log.LogDebug($"Item: {item.ItemSpec}");

                foreach (var key in item.MetadataNames.OfType<string>())
                {
                    try
                    {
                        var val = item.GetMetadata(key);

                        if (!string.IsNullOrEmpty(val))
                        {
                            log.LogDebug($"  {key}={val}");
                        }
                    }
                    catch
                    {
                        // Ignore errors
                    }
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
