using System.Collections.Generic;
using NuGet.Packaging.Core;

namespace NuGet.PackageManagement.UI
{
    public class NuGetProjectUpgradeDependencyItem
    {
        public PackageIdentity Package { get; }
        public IList<PackageIdentity> DependingPackages { get; }

        public NuGetProjectUpgradeDependencyItem(PackageIdentity package, IList<PackageIdentity> dependingPackages = null)
        {
            Package = package;
            DependingPackages = dependingPackages ?? new List<PackageIdentity>();
        }
    }
}