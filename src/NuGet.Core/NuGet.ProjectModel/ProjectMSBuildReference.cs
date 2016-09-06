namespace NuGet.ProjectModel
{
    public class ProjectMSBuildReference
    {
        /// <summary>
        /// Project unique name.
        /// </summary>
        public string ProjectUniqueName { get; set; }

        /// <summary>
        /// Full path to the msbuild project file.
        /// </summary>
        public string ProjectPath { get; set; }
    }
}
