using System.Collections.Generic;
using NuGet.Configuration;

namespace NuGet.ProjectModel
{
    public class ProjectMSBuildMetadata
    {
        /// <summary>
        /// Restore behavior type.
        /// </summary>
        public RestoreOutputType OutputType { get; set; } = RestoreOutputType.Unknown;

        /// <summary>
        /// MSBuild project file path.
        /// </summary>
        public string ProjectPath { get; set; }

        /// <summary>
        /// Full path to the project.json file if it exists.
        /// </summary>
        public string ProjectJsonPath { get; set; }

        /// <summary>
        /// Assets file output path.
        /// </summary>
        public string OutputPath { get; set; }

        /// <summary>
        /// Friendly project name.
        /// </summary>
        public string ProjectName { get; set; }

        /// <summary>
        /// Name unique to the project across the solution.
        /// </summary>
        public string ProjectUniqueName { get; set; }

        /// <summary>
        /// Package feed sources.
        /// </summary>
        public IReadOnlyList<PackageSource> Sources { get; set; } = new List<PackageSource>();

        /// <summary>
        /// User packages folder path.
        /// </summary>
        public IReadOnlyList<string> PackagesPath { get; set; }

        /// <summary>
        /// Fallback folders.
        /// </summary>
        public IReadOnlyList<string> FallbackFolders { get; set; } = new List<string>();

        /// <summary>
        /// Project reference metadata. This will be added to the non-msbuild dependency reference in the package spec.
        /// </summary>
        public IReadOnlyList<ProjectMSBuildReference> ProjectReferences { get; set; } = new List<ProjectMSBuildReference>();
    }
}
