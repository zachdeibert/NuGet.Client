using System.Net.Http;
using NuGet.Protocol.Core.Types;
using NuGet.Test.Utility;
using Xunit;
using Xunit.Abstractions;

namespace NuGet.Protocol.Core.v3.Tests.Utility
{
    public class NuGetHttpHeaderUtilityTests
    {
        private readonly ITestOutputHelper _output;

        public NuGetHttpHeaderUtilityTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void LogsServerWarningWhenNuGetWarningHeaderPresent()
        {
            var testLogger = new TestLogger();
            var response = new HttpResponseMessage();
            response.Headers.Add(NuGetHttpHeaderUtility.WarningHeader, "test");

            NuGetHttpHeaderUtility.LogServerWarning(testLogger, response);

            Assert.Equal(1, testLogger.Warnings);

            string warning;
            Assert.True(testLogger.Messages.TryDequeue(out warning));
            Assert.Equal("test", warning);
        }
    }
}
