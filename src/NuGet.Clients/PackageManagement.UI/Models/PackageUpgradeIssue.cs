namespace NuGet.PackageManagement.UI
{
    public class PackageUpgradeIssue
    {
        public string IssueDescription;
        public NuGetProjectUpgradeIssueSeverity IssueSeverity;

        public override string ToString()
        {
            return IssueSeverity + ": " + IssueDescription;
        }
    }
}