using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement;
using NuGet.ProjectModel;

namespace NuGet.PackageManagement.UI
{
    internal static class NuGetProjectUpgrader
    {
        internal static async Task<string> DoUpgrade(
            INuGetUIContext context,
            INuGetUI uiService,
            NuGetProject nuGetProject,
            IEnumerable<NuGetProjectUpgradeDependencyItem> upgradeDependencyItems,
            bool collapseDependencies,
            IProgress<ProgressDialogData> progress,
            CancellationToken token)
        {
            var dependencyItems = upgradeDependencyItems as IList<NuGetProjectUpgradeDependencyItem> ?? upgradeDependencyItems.ToList();

            // 1. Backup existing packages.config
            var msBuildNuGetProject = (MSBuildNuGetProject) nuGetProject;
            var msBuildNuGetProjectSystem = msBuildNuGetProject.MSBuildNuGetProjectSystem;
            var projectPath = msBuildNuGetProjectSystem.ProjectFullPath;
            var backupPath = Path.Combine(projectPath, "NuGetUpgradeBackup");

            Directory.CreateDirectory(backupPath);
            var packagesConfigFullPath = msBuildNuGetProject.PackagesConfigNuGetProject.FullPath;
            var targetPath = Path.GetFileName(packagesConfigFullPath);
            File.Copy(packagesConfigFullPath, Path.Combine(backupPath, targetPath), true);

            // 2. Uninstall all packages currently in packages.config
            var progressData = new ProgressDialogData(Resources.NuGetUpgrade_WaitMessage, Resources.NuGetUpgrade_Progress_Uninstalling);
            progress.Report(progressData);

            foreach (var upgradeDependencyItem in dependencyItems)
            {
                await nuGetProject.UninstallPackageAsync(upgradeDependencyItem.Package, new EmptyNuGetProjectContext(), token);
            }

            // 3. Create stub project.json file
            progressData = new ProgressDialogData(Resources.NuGetUpgrade_WaitMessage, Resources.NuGetUpgrade_Progress_CreatingProjectJson);
            progress.Report(progressData);

            var json = new JObject();

            // Target framework
            var targetNuGetFramework = msBuildNuGetProjectSystem.TargetFramework;
            json["frameworks"] = new JObject {[targetNuGetFramework.GetShortFolderName()] = new JObject()};

            // Runtimes
            var runtimeStub = msBuildNuGetProjectSystem.GetPropertyValue("TargetPlatformIdentifier") == "UAP"
                ? "win10"
                : "win";
            var runtimes = new JObject();
            var supportedPlatforms = msBuildNuGetProjectSystem.SupportedPlatforms;

            if (supportedPlatforms.Any())
            {
                foreach (var supportedPlatformString in supportedPlatforms)
                {
                    if (string.IsNullOrEmpty(supportedPlatformString) || supportedPlatformString == "Any CPU")
                    {
                        runtimes[runtimeStub] = new JObject();
                    }
                    else
                    {
                        runtimes[runtimeStub + "-" + supportedPlatformString.ToLowerInvariant()] = new JObject();
                    }
                }
            }
            else
            {
                runtimes[runtimeStub] = new JObject();
            }

            json["runtimes"] = runtimes;

            // Write out project.json and add it to the project
            var projectJsonFileName = Path.Combine(projectPath, PackageSpec.PackageSpecFileName);
            using (var textWriter = new StreamWriter(projectJsonFileName, false, Encoding.UTF8))
            using (var jsonWriter = new JsonTextWriter(textWriter))
            {
                jsonWriter.Formatting = Formatting.Indented;
                json.WriteTo(jsonWriter);
            }
            msBuildNuGetProjectSystem.AddExistingFile(PackageSpec.PackageSpecFileName);

            // Reload the project, and get a reference to the reloaded project
            var uniqueName = msBuildNuGetProjectSystem.ProjectUniqueName;
            msBuildNuGetProjectSystem.Save();
            msBuildNuGetProjectSystem.Reload();
            nuGetProject = context.SolutionManager.GetNuGetProject(uniqueName);

            // Ensure we use the updated project for installing, and don't display preview or license acceptance windows.
            context.Projects = new[] {nuGetProject};
            var nuGetUI = (NuGetUI) uiService;
            nuGetUI.Projects = new[] {nuGetProject};
            nuGetUI.DisplayPreviewWindow = false;
            nuGetUI.DisplayLicenseAcceptanceWindow = false;

            // 4. Install the requested packages
            progressData = new ProgressDialogData(Resources.NuGetUpgrade_WaitMessage, Resources.NuGetUpgrade_Progress_Installing);
            progress.Report(progressData);

            foreach (var packageIdentity in GetPackagesToInstall(dependencyItems, collapseDependencies))
            {
                var action = UserAction.CreateInstallAction(packageIdentity.Id, packageIdentity.Version);
                await context.UIActionEngine.PerformActionAsync(uiService, action, null, CancellationToken.None);
            }

            return backupPath;
        }

        private static IEnumerable<PackageIdentity> GetPackagesToInstall(
            IEnumerable<NuGetProjectUpgradeDependencyItem> upgradeDependencyItems, bool collapseDependencies)
        {
            return
                upgradeDependencyItems.Where(
                    upgradeDependencyItem => !collapseDependencies || !upgradeDependencyItem.DependingPackages.Any())
                    .Select(upgradeDependencyItem => upgradeDependencyItem.Package);
        }
    }
}