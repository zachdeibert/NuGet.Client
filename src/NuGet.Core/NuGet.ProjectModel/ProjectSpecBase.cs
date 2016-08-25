using System;
using System.IO;

namespace NuGet.ProjectModel
{
    public class ProjectSpecBase : IEquatable<ProjectSpecBase>, IComparable<ProjectSpecBase>
    {
        public ProjectSpecBase(
            string uniqueName,
            string msbuildProjectPath)
        {
            if (uniqueName == null)
            {
                throw new ArgumentNullException(nameof(uniqueName));
            }

            UniqueName = uniqueName;
            MSBuildProjectPath = msbuildProjectPath;
        }

        /// <summary>
        /// Unique project identifier.
        /// </summary>
        public string UniqueName { get; }

        /// <summary>
        /// Path to the msbuild project file if one exists. Ex: xproj, csproj.
        /// </summary>
        public string MSBuildProjectPath { get; }

        /// <summary>
        /// Project name.
        /// </summary>
        public virtual string ProjectName
        {
            get
            {
                return Path.GetFileNameWithoutExtension(MSBuildProjectPath)
                        ?? UniqueName;
            }
        }

        public override string ToString()
        {
            return UniqueName;
        }

        public override bool Equals(object obj)
        {
            return Equals(obj as ProjectSpecBase);
        }

        public override int GetHashCode()
        {
            return StringComparer.Ordinal.GetHashCode(UniqueName);
        }

        public bool Equals(ProjectSpecBase other)
        {
            if (other == null)
            {
                return false;
            }

            if (ReferenceEquals(this, other))
            {
                return true;
            }

            return UniqueName.Equals(other.UniqueName, StringComparison.Ordinal);
        }

        public int CompareTo(ProjectSpecBase other)
        {
            return StringComparer.Ordinal.Compare(UniqueName, other?.UniqueName);
        }
    }
}
