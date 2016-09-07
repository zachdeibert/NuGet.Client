using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace NuGet.ProjectModel
{
    public class DependencyGraphSpec
    {
        private readonly SortedSet<string> _restore = new SortedSet<string>(StringComparer.Ordinal);
        private readonly SortedDictionary<string, PackageSpec> _projects = new SortedDictionary<string, PackageSpec>(StringComparer.Ordinal);

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
        public IReadOnlyList<string> Restore
        {
            get
            {
                return _restore.ToList();
            }
        }

        /// <summary>
        /// All project specs.
        /// </summary>
        public IReadOnlyList<PackageSpec> Projects
        {
            get
            {
                return _projects.Values.ToList();
            }
        }

        /// <summary>
        /// File json.
        /// </summary>
        public JObject Json { get; }

        public PackageSpec GetProjectSpec(string projectUniqueName)
        {
            PackageSpec project;
            _projects.TryGetValue(projectUniqueName, out project);

            return project;
        }

        public IDictionary<string, IList<PackageSpec>> GetClosure(string rootUniqueName)
        {
            if (rootUniqueName == null)
            {
                throw new ArgumentNullException(nameof(rootUniqueName));
            }

            var closure = new SortedDictionary<string, IList<PackageSpec>>(StringComparer.Ordinal);

            

            return closure;
        }

        public IDictionary<string, IList<PackageSpec>> GetClosure(PackageSpec rootProject)
        {
            if (rootProject == null)
            {
                throw new ArgumentNullException(nameof(rootProject));
            }

            return GetClosure(rootProject.MSBuildMetadata.ProjectUniqueName);
        }

        public void AddRestore(string projectUniqueName)
        {
            _restore.Add(projectUniqueName);
        }

        public void AddProject(PackageSpec projectSpec)
        {
            // Find the unique name in the spec, otherwise generate a new one.
            var projectUniqueName = projectSpec.MSBuildMetadata?.ProjectUniqueName
                ?? Guid.NewGuid().ToString();

            _projects.Add(projectUniqueName, projectSpec);
        }

        public static DependencyGraphSpec Load(string path)
        {
            var json = ReadJson(path);

            return Load(json);
        }

        public static DependencyGraphSpec Load(JObject json)
        {
            return new DependencyGraphSpec(json);
        }

        public void Save(string path)
        {
            var json = GetJson(spec: this);

            using (var fileStream = new FileStream(path, FileMode.Create))
            using (var textWriter = new StreamWriter(fileStream))
            using (var jsonWriter = new JsonTextWriter(textWriter))
            {
                jsonWriter.Formatting = Formatting.Indented;
                json.WriteTo(jsonWriter);
            }
        }

        private static JObject GetJson(DependencyGraphSpec spec)
        {
            var json = new JObject();

            return json;
        }

        private static JObject ReadJson(string packageSpecPath)
        {
            JObject json;

            using (var stream = new FileStream(packageSpecPath, FileMode.Open, FileAccess.Read, FileShare.Read))
            using (var reader = new JsonTextReader(new StreamReader(stream)))
            {
                try
                {
                    json = JObject.Load(reader);
                }
                catch (JsonReaderException ex)
                {
                    throw FileFormatException.Create(ex, packageSpecPath);
                }
            }

            return json;
        }
    }
}
