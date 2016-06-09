using System.Collections.Generic;
using NuGet.Packaging.Core;
using System.Globalization;
using System.Linq;

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

        public override string ToString()
        {
            return !DependingPackages.Any()
                ? Package.ToString()
                : Package + " " + string.Format(CultureInfo.CurrentCulture, Resources.NuGetUpgrade_PackageDependencyOf, string.Join(", ", DependingPackages));
        }
    }
}