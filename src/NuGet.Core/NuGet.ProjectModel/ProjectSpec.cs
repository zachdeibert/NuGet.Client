using NuGet.Frameworks;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NuGet.LibraryModel;

namespace NuGet.ProjectModel
{
    public class ProjectSpec : ProjectSpecBase
    {
        private readonly IDictionary<NuGetFramework, IList<LibraryDependency>> _dependencies;
        private readonly IDictionary<NuGetFramework, IDictionary<string, string>> _properties;
        private readonly HashSet<NuGetFramework> _frameworks;

        public ProjectSpec(
            string uniqueName,
            string msbuildProjectPath,
            IEnumerable<NuGetFramework> targetFrameworks,
            IDictionary<NuGetFramework, IList<LibraryDependency>> dependencies,
            IDictionary<NuGetFramework, IDictionary<string, string>> properties)
            : base(uniqueName, msbuildProjectPath)
        {
            if (dependencies == null)
            {
                throw new ArgumentNullException(nameof(dependencies));
            }

            if (properties == null)
            {
                throw new ArgumentNullException(nameof(properties));
            }

            if (targetFrameworks == null)
            {
                throw new ArgumentNullException(nameof(targetFrameworks));
            }

            _dependencies = dependencies;
            _properties = properties;
            _frameworks = new HashSet<NuGetFramework>(targetFrameworks);
        }

        /// <summary>
        /// Retrieve msbuild properties for the given framework.
        /// </summary>
        public IDictionary<string, string> GetProperties(NuGetFramework framework)
        {
            if (!_frameworks.Contains(framework))
            {
                throw new ArgumentOutOfRangeException(nameof(framework));
            }

            IDictionary<string, string> result;
            if (!_properties.TryGetValue(framework, out result))
            {
                result = new Dictionary<string, string>();
            }

            return result;
        }

        /// <summary>
        /// Retrieve msbuild property for the given framework.
        /// </summary>
        public string GetProperty(NuGetFramework framework, string name)
        {
            string val;
            if (GetProperties(framework).TryGetValue(name, out val)
                && !string.IsNullOrEmpty(val))
            {
                return val;
            }

            return null;
        }

        /// <summary>
        /// Dependencies for the given framework, packages and projects.
        /// </summary>
        public IList<LibraryDependency> GetDependencies(NuGetFramework framework)
        {
            if (!_frameworks.Contains(framework))
            {
                throw new ArgumentOutOfRangeException(nameof(framework));
            }

            IList<LibraryDependency> result;
            if (!_dependencies.TryGetValue(framework, out result))
            {
                result = new List<LibraryDependency>();
            }

            return result;
        }

        /// <summary>
        /// Support target frameworks.
        /// </summary>
        public ISet<NuGetFramework> TargetFrameworks
        {
            get
            {
                return _frameworks;
            }
        }
    }
}
