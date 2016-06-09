using System;
using System.Net.Http;
using NuGet.Common;

namespace NuGet.Protocol.Core.Types
{
    public class NuGetHttpHeaderUtility
    {
        public const string ApiKeyHeader = "X-NuGet-ApiKey";
        public const string WarningHeader = "NuGet-Warning";

        public static void LogServerWarning(ILogger log, HttpResponseMessage response)
        {
            if(log == null)
            {
                throw new ArgumentNullException(nameof(log));
            }

            if(response == null)
            {
                throw new ArgumentNullException(nameof(response));
            }

            if (response.Headers.Contains(WarningHeader))
            {
                foreach (var warning in response.Headers.GetValues(WarningHeader))
                {
                    log.LogWarning(warning);
                }
            }
        }
    }
}
