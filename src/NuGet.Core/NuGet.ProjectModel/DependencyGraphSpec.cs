using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace NuGet.ProjectModel
{
    public class DependencyGraphSpec
    {
        public DependencyGraphSpec(JObject json)
        {
            Json = json;
        }

        public DependencyGraphSpec()
        {
            Json = new JObject();
        }

        /// <summary>
        /// Projects to restore.
        /// </summary>
        public IReadOnlyList<string> Restore { get; }

        /// <summary>
        /// All project specs.
        /// </summary>
        public IReadOnlyList<PackageSpec> Projects { get; }

        /// <summary>
        /// File json.
        /// </summary>
        public JObject Json { get; }

        public ExternalProjectReference GetProject(string projectUniqueName)
        {
            throw new NotImplementedException();
        }

        public void AddRestore(string projectUniqueName)
        {
            throw new NotImplementedException();
        }

        public void AddProject(PackageSpec projectSpec)
        {
            throw new NotImplementedException();
        }

        public static DependencyGraphSpec Load(string path)
        {
            throw new NotImplementedException();
        }

        public static DependencyGraphSpec Load(JObject json)
        {
            throw new NotImplementedException();
        }

        public void Save(string path)
        {
            throw new NotImplementedException();
        }
    }
}
