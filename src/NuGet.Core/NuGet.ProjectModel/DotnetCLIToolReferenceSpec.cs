using NuGet.Versioning;

namespace NuGet.ProjectModel
{
    public class DotnetCLIToolReferenceSpec
    {
        public string Id { get; set; }

        public VersionRange Version { get; set; }

        public string ProjectPath { get; set; }

        public override string ToString()
        {
            return $"{Id} {Version} {ProjectPath}";
        }
    }
}
