using System;
using System.Threading;
using System.Threading.Tasks;
using NuGet.Common;

namespace NuGet.Common
{
    /// <summary>
    /// A lock that does nothing.
    /// </summary>
    public class NullNuGetLock : INuGetLock
    {
        /// <summary>
        /// Static instance
        /// </summary>
        public static NullNuGetLock Instance = new NullNuGetLock();

        public Task AcquireAsync(CancellationToken token)
        {
            return Task.FromResult(false);
        }

        public Task ReleaseAsync()
        {
            return Task.FromResult(false);
        }

        public void Dispose()
        {
            // Do nothing
        }

        public string Id
        {
            get
            {
                return string.Empty;
            }
        }
    }
}
