using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using NuGet.Frameworks;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectManagement;

namespace NuGet.PackageManagement.UI
{
    public class NuGetProjectUpgradeWindowModel : INotifyPropertyChanged
    {
        private IEnumerable<string> _allPackages;
        private IEnumerable<PackageUpgradeIssues> _analysisResults;
        private bool _collapseDependencies;
        private IEnumerable<string> _dependencyPackages;
        private IEnumerable<string> _includedCollapsedPackages;
        private IEnumerable<NuGetProjectUpgradeDependencyItem> _upgradeDependencyItems;

        public NuGetProjectUpgradeWindowModel(NuGetProject project, IList<PackageDependencyInfo> packageDependencyInfos,
            bool collapseDependencies)
        {
            PackageDependencyInfos = packageDependencyInfos;
            Project = project;
            _collapseDependencies = collapseDependencies;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        public NuGetProject Project { get; }

        public IList<PackageDependencyInfo> PackageDependencyInfos { get; }

        // Changing CollapseDependencies updates the list of included packages
        public bool CollapseDependencies
        {
            get { return _collapseDependencies; }
            set
            {
                _collapseDependencies = value;
                OnPropertyChanged(nameof(IncludedPackages));
                OnPropertyChanged(nameof(ExcludedPackages));
            }
        }

        public bool HasErrors => AnalysisResults.SelectMany(r => r.Issues).Any(i => i.Severity == NuGetProjectUpgradeIssueSeverity.Error);

        public IEnumerable<PackageUpgradeIssues> AnalysisResults => _analysisResults ?? (_analysisResults = GetNuGetUpgradeIssues());

        public IEnumerable<NuGetProjectUpgradeDependencyItem> UpgradeDependencyItems
            => _upgradeDependencyItems ?? (_upgradeDependencyItems = GetUpgradeDependencyItems());

        public IEnumerable<string> IncludedPackages => CollapseDependencies ? IncludedCollapsedPackages : AllPackages;

        public IEnumerable<string> ExcludedPackages => CollapseDependencies ? DependencyPackages : null;

        private IEnumerable<string> DependencyPackages => _dependencyPackages ?? (_dependencyPackages = GetDependencyPackages());

        private IEnumerable<string> AllPackages => _allPackages ?? (_allPackages = UpgradeDependencyItems.Select(d => d.Package.ToString()));

        private IEnumerable<string> IncludedCollapsedPackages => _includedCollapsedPackages ?? (_includedCollapsedPackages = GetIncludedCollapsedPackages());

        private IEnumerable<string> GetDependencyPackages()
        {
            return UpgradeDependencyItems.Where(d => d.DependingPackages.Any()).Select(d => d.ToString());
        }

        private IEnumerable<string> GetIncludedCollapsedPackages()
        {
            return UpgradeDependencyItems
                .Where(upgradeDependencyItem => !upgradeDependencyItem.DependingPackages.Any())
                .Select(upgradeDependencyItem => upgradeDependencyItem.Package.ToString());
        }

        private IEnumerable<PackageUpgradeIssues> GetNuGetUpgradeIssues()
        {
            var msBuildNuGetProject = (MSBuildNuGetProject) Project;
            var framework = msBuildNuGetProject.MSBuildNuGetProjectSystem.TargetFramework;
            var folderNuGetProject = msBuildNuGetProject.FolderNuGetProject;

            foreach (var packageIdentity in PackageDependencyInfos)
            {
                var packageUpgradeIssues =
                    GetPackageUpgradeIssues(folderNuGetProject, packageIdentity, framework).ToList();
                if (packageUpgradeIssues.Any())
                {
                    yield return new PackageUpgradeIssues(packageIdentity, packageUpgradeIssues);
                }
            }
        }

        private static IEnumerable<PackageUpgradeIssue> GetPackageUpgradeIssues(
            FolderNuGetProject folderNuGetProject,
            PackageIdentity packageIdentity,
            NuGetFramework framework)
        {
            // Confirm package exists
            var packagePath = folderNuGetProject.GetInstalledPackageFilePath(packageIdentity);
            if (string.IsNullOrEmpty(packagePath))
            {
                yield return new PackageUpgradeIssue
                {
                    Severity = NuGetProjectUpgradeIssueSeverity.Error,
                    Description = Resources.NuGetUpgradeError_CannotFindPackage
                };
            }
            else
            {
                var reader = new PackageArchiveReader(packagePath);

                // Check if it has content files
                var contentFilesGroup = MSBuildNuGetProjectSystemUtility.GetMostCompatibleGroup(framework,
                    reader.GetContentItems());
                if (MSBuildNuGetProjectSystemUtility.IsValid(contentFilesGroup) && contentFilesGroup.Items.Any())
                {
                    yield return new PackageUpgradeIssue
                    {
                        Severity = NuGetProjectUpgradeIssueSeverity.Warning,
                        Description = Resources.NuGetUpgradeWarning_HasContentFiles
                    };
                }

                // Check if it has an install.ps1 file
                var toolItemsGroup = MSBuildNuGetProjectSystemUtility.GetMostCompatibleGroup(framework,
                    reader.GetToolItems());
                toolItemsGroup = MSBuildNuGetProjectSystemUtility.Normalize(toolItemsGroup);
                var isValid = MSBuildNuGetProjectSystemUtility.IsValid(toolItemsGroup);
                var hasInstall = isValid &&
                                 toolItemsGroup.Items.Any(
                                     p =>
                                         p.EndsWith(Path.DirectorySeparatorChar + PowerShellScripts.Install,
                                             StringComparison.OrdinalIgnoreCase));
                if (hasInstall)
                {
                    yield return new PackageUpgradeIssue
                    {
                        Severity = NuGetProjectUpgradeIssueSeverity.Warning,
                        Description = Resources.NuGetUpgradeWarning_HasInstallScript
                    };
                }
            }
        }

        private IEnumerable<NuGetProjectUpgradeDependencyItem> GetUpgradeDependencyItems()
        {
            var upgradeDependencyItems =
                PackageDependencyInfos.Select(
                    p => new NuGetProjectUpgradeDependencyItem(new PackageIdentity(p.Id, p.Version))).ToList();
            foreach (var packageDependencyInfo in PackageDependencyInfos)
            {
                foreach (var dependency in packageDependencyInfo.Dependencies)
                {
                    var matchingDependencyItem =
                        upgradeDependencyItems.FirstOrDefault(
                            d =>
                                d.Package.Id == dependency.Id && d.Package.Version == dependency.VersionRange.MinVersion);
                    matchingDependencyItem?.DependingPackages.Add(new PackageIdentity(packageDependencyInfo.Id,
                        packageDependencyInfo.Version));
                }
            }

            return upgradeDependencyItems;
        }

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}