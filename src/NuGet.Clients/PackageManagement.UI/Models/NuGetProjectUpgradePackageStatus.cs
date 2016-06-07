namespace NuGet.PackageManagement.UI
{
    public class NuGetProjectUpgradePackageStatus
    {
        public string PackageDesc { get; }
        public string Status { get; }

        public NuGetProjectUpgradePackageStatus(string packageDesc, string status)
        {
            PackageDesc = packageDesc;
            Status = status;
        }
    }
}