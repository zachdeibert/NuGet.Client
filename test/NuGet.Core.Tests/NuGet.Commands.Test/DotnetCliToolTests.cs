using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NuGet.Common;
using NuGet.Configuration;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Protocol.Core.Types;
using NuGet.Test.Utility;
using Xunit;

namespace NuGet.Commands.Test
{
    public class DotnetCliToolTests
    {
        [Fact]
        public async Task DotnetCliTool_BasicToolRestore()
        {
            // Arrange
            using (var pathContext = new SimpleTestPathContext())
            {
                var logger = new TestLogger();
                var dgFile = new DependencyGraphSpec();

                var spec = new PackageSpec();

                dgFile.AddProject();

                // Act
                var result = await CommandsTestUtility.RunSingleRestore(dgFile, pathContext, logger);

                // Assert
                Assert.True(result.Success, "Failed: " + string.Join(Environment.NewLine, logger.Messages));
            }
        }
    }
}
