namespace NuGet.PackageManagement.UI
{
    public class PackageUpgradeIssue
    {
        public string Description;
        public NuGetProjectUpgradeIssueSeverity Severity;

        public override string ToString()
        {
            return Severity + ": " + Description;
        }
    }
}