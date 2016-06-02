using System;
using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.Shell;
using Newtonsoft.Json.Linq;
using NuGet.ProjectManagement;
using Task = System.Threading.Tasks.Task;

namespace NuGet.PackageManagement.UI
{
    internal static class NuGetProjectUpgrader
    {
        internal static async Task DoUpgrade(UpgradeInformationWindowModel upgradeInformationWindowModel, IProgress<ThreadedWaitDialogProgressData> progress, CancellationToken token)
        {
            // 1. Uninstall all packages currently in packages.config
            var progressData = new ThreadedWaitDialogProgressData("Upgrading Project", "Uninstalling packages from packages.config", string.Empty, false, 0, 0);
            progress.Report(progressData);

            var nuGetProject = upgradeInformationWindowModel.Project;
            var upgradeDependencyItems = upgradeInformationWindowModel.UpgradeDependencyItems;
            foreach (var upgradeDependencyItem in upgradeDependencyItems)
            {
                await nuGetProject.UninstallPackageAsync(upgradeDependencyItem.Package, new EmptyNuGetProjectContext(), token);
            }

            // 2. Create stub project.json file
            progressData = new ThreadedWaitDialogProgressData("Upgrading Project", "Creating project.json", string.Empty, false, 0, 0);
            progress.Report(progressData);

            JObject json = new JObject();

            // Target framework
            var msBuildNuGetProject = (MSBuildNuGetProject)nuGetProject;
            var targetNuGetFramework = msBuildNuGetProject.MSBuildNuGetProjectSystem.TargetFramework;
            json["frameworks"] = new JObject { [targetNuGetFramework.GetShortFolderName()] = new JObject() };

            // Runtimes
            var runtimeStub = msBuildNuGetProject.MSBuildNuGetProjectSystem.GetPropertyValue("TargetPlatformIdentifier") == "UAP" ? "win10" : "win";
            var runtimes = new JObject();
            var supportedPlatforms = msBuildNuGetProject.MSBuildNuGetProjectSystem.SupportedPlatforms;

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
        }
    }
}
