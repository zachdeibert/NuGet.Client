namespace NuGet.PackageManagement.UI
{
    public class PackageUpgradeIssue
    {
        public string Description { get; set; }
        public NuGetProjectUpgradeIssueSeverity Severity { get; set; }
    }
}