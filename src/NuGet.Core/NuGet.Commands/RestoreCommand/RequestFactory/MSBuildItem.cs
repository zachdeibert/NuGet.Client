using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace NuGet.Commands
{
    /// <summary>
    /// Internal ITaskItem abstraction
    /// </summary>
    public class MSBuildItem
    {
        public string Identity { get; }

        public IDictionary<string, string> Metadata { get; }

        public MSBuildItem(string identity, IDictionary<string, string> metadata)
        {
            if (identity == null)
            {
                throw new ArgumentNullException(nameof(identity));
            }

            if (metadata == null)
            {
                throw new ArgumentNullException(nameof(metadata));
            }

            Identity = identity;
            Metadata = metadata;
        }
    }
}
