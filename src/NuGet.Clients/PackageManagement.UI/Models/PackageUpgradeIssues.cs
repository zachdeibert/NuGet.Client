using System.Collections.Generic;
using NuGet.Packaging.Core;

namespace NuGet.PackageManagement.UI
{
    public class PackageUpgradeIssues
    {
        public PackageIdentity Package { get; }

        public IEnumerable<PackageUpgradeIssue> Issues { get; }

        public PackageUpgradeIssues(PackageIdentity packageIdentity, IEnumerable<PackageUpgradeIssue> issues)
        {
            Package = packageIdentity;
            Issues = issues;
        }
    }
}
