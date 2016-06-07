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
        private IEnumerable<PackageUpgradeIssues> _analysisResults;
        private IEnumerable<NuGetProjectUpgradeDependencyItem> _upgradeDependencyItems;
        private IEnumerable<NuGetProjectUpgradePackageStatus> _flatPackages;
        private IEnumerable<NuGetProjectUpgradePackageStatus> _collapsedPackages;
        private bool _collapseDependencies;

        public NuGetProjectUpgradeWindowModel(NuGetProject project, IList<PackageDependencyInfo> packageDependencyInfos, bool collapseDependencies)
        {
            PackageDependencyInfos = packageDependencyInfos;
            Project = project;
            _collapseDependencies = collapseDependencies;
        }

        public NuGetProject Project { get; }

        public IList<PackageDependencyInfo> PackageDependencyInfos { get; }

        // Changing CollapseDependencies updates the list of included packages
        public bool CollapseDependencies {
            get
            {
                return _collapseDependencies;
            }
            set
            {
                var isChanged = value != _collapseDependencies;
                _collapseDependencies = value;
                if (isChanged)
                {
                    OnPropertyChanged(nameof(Packages));
                }
            }
        }

        public bool HasErrors
        {
            get { return AnalysisResults.Any(r => r.Issues.Any(i => i.IssueSeverity == NuGetProjectUpgradeIssueSeverity.Error)); }
        }

        public IEnumerable<PackageUpgradeIssues> AnalysisResults => _analysisResults ?? (_analysisResults = GetNuGetUpgradeIssues());

        public IEnumerable<NuGetProjectUpgradeDependencyItem> UpgradeDependencyItems => _upgradeDependencyItems ?? (_upgradeDependencyItems = GetUpgradeDependencyItems());

        public IEnumerable<NuGetProjectUpgradePackageStatus> Packages => CollapseDependencies ? CollapsedPackages : FlatPackages;

        private IEnumerable<NuGetProjectUpgradePackageStatus> CollapsedPackages => _collapsedPackages ?? (_collapsedPackages = GetCollapsedPackages());

        private IEnumerable<NuGetProjectUpgradePackageStatus> FlatPackages => _flatPackages ?? (_flatPackages = GetFlatPackages());

        private IEnumerable<NuGetProjectUpgradePackageStatus> GetCollapsedPackages()
        {
            foreach (var upgradeDependencyItem in UpgradeDependencyItems)
            {
                string status;
                if (upgradeDependencyItem.DependingPackages.Any())
                {
                    var dependingPackagesString = string.Join(", ", upgradeDependencyItem.DependingPackages.Select(p => p.ToString()));
                    status = string.Format(CultureInfo.CurrentCulture, Resources.NuGetUpgrade_PackageExcluded, dependingPackagesString);
                }
                else
                {
                    status = Resources.NuGetUpgrade_PackageIncluded;
                }
                yield return new NuGetProjectUpgradePackageStatus(upgradeDependencyItem.Package.ToString(), status);
            }
        }

        private IEnumerable<NuGetProjectUpgradePackageStatus> GetFlatPackages()
        {
            return UpgradeDependencyItems.Select(d => new NuGetProjectUpgradePackageStatus(d.Package.ToString(), Resources.NuGetUpgrade_PackageIncluded));
        }

        private IEnumerable<PackageUpgradeIssues> GetNuGetUpgradeIssues()
        {
            var msBuildNuGetProject = (MSBuildNuGetProject)Project;
            var framework = msBuildNuGetProject.MSBuildNuGetProjectSystem.TargetFramework;
            var folderNuGetProject = msBuildNuGetProject.FolderNuGetProject;

            foreach (var packageIdentity in PackageDependencyInfos)
            {
                var packageUpgradeIssues = GetPackageUpgradeIssues(folderNuGetProject, packageIdentity, framework).ToList();
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
                yield return new PackageUpgradeIssue()
                {
                    IssueSeverity = NuGetProjectUpgradeIssueSeverity.Error,
                    IssueDescription = Resources.NuGetUpgradeError_CannotFindPackage
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
                    yield return new PackageUpgradeIssue()
                    {
                        IssueSeverity = NuGetProjectUpgradeIssueSeverity.Warning,
                        IssueDescription = Resources.NuGetUpgradeWarning_HasContentFiles
                    };
                }

                // Check if it has an install.ps1 file
                var toolItemsGroup = MSBuildNuGetProjectSystemUtility.GetMostCompatibleGroup(framework, reader.GetToolItems());
                toolItemsGroup = MSBuildNuGetProjectSystemUtility.Normalize(toolItemsGroup);
                var isValid = MSBuildNuGetProjectSystemUtility.IsValid(toolItemsGroup);
                var hasInstall = isValid && toolItemsGroup.Items.Any(p => p.EndsWith(Path.DirectorySeparatorChar + PowerShellScripts.Install,StringComparison.OrdinalIgnoreCase));
                if (hasInstall)
                {
                    yield return new PackageUpgradeIssue()
                    {
                        IssueSeverity = NuGetProjectUpgradeIssueSeverity.Warning,
                        IssueDescription = Resources.NuGetUpgradeWarning_HasInstallScript
                    };
                }
            }
        }

        private IEnumerable<NuGetProjectUpgradeDependencyItem> GetUpgradeDependencyItems()
        {
            var upgradeDependencyItems = PackageDependencyInfos.Select(p => new NuGetProjectUpgradeDependencyItem(new PackageIdentity(p.Id, p.Version))).ToList();
            foreach (var packageDependencyInfo in PackageDependencyInfos)
            {
                foreach (var dependency in packageDependencyInfo.Dependencies)
                {
                    var matchingDependencyItem = upgradeDependencyItems.FirstOrDefault(d => d.Package.Id == dependency.Id && d.Package.Version == dependency.VersionRange.MinVersion);
                    matchingDependencyItem?.DependingPackages.Add(new PackageIdentity(packageDependencyInfo.Id, packageDependencyInfo.Version));
                }
            }

            return upgradeDependencyItems;
        }

        public event PropertyChangedEventHandler PropertyChanged;

        protected virtual void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }
}
