using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Newtonsoft.Json;
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
                providers.Add(new PreLoadedRestoreRequestProvider(providerCache, RestoreGraphItems.Select(GetMSBuildItem).ToArray()));

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
    }
}
