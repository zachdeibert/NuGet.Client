using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using NuGet.Test.Utility;
using Xunit;

namespace NuGet.CommandLine.Test
{
    public class NuGetDeleteCommandTest
    {
        // Tests deleting a package from a source that is a file system directory.
        [Fact]
        public void DeleteCommand_DeleteFromV2FileSystemSource()
        {
            var nugetexe = Util.GetNuGetExePath();

            using (var source = TestFileSystemUtility.CreateRandomTestFolder())
            {
                // Arrange
                var packageFileName = Util.CreateTestPackage("testPackage1", "1.1.0", source);
                Assert.True(File.Exists(packageFileName));

                // Act
                string[] args = new string[] {
                    "delete", "testPackage1", "1.1.0",
                    "-Source", source, "-NonInteractive" };
                var r = CommandRunner.Run(
                    nugetexe,
                    Directory.GetCurrentDirectory(),
                    String.Join(" ", args),
                    waitForExit: true);

                // Assert
                Assert.Equal(0, r.Item1);
                Assert.False(File.Exists(packageFileName));
            }
        }

        [Fact]
        public void DeleteCommand_DeleteReadOnlyFromV2FileSystemSource()
        {
            var nugetexe = Util.GetNuGetExePath();

            using (var source = TestFileSystemUtility.CreateRandomTestFolder())
            {
                // Arrange
                var packageFileName = Util.CreateTestPackage("testPackage1", "1.1.0", source);
                Assert.True(File.Exists(packageFileName));
                File.SetAttributes(packageFileName,
                    File.GetAttributes(packageFileName) | FileAttributes.ReadOnly);
                // Act
                string[] args = new string[] {
                    "delete", "testPackage1", "1.1.0",
                    "-Source", source, "-NonInteractive" };
                var r = CommandRunner.Run(
                    nugetexe,
                    Directory.GetCurrentDirectory(),
                    String.Join(" ", args),
                    waitForExit: true);

                // Assert
                Assert.Equal(0, r.Item1);
                Assert.False(File.Exists(packageFileName));
            }
        }

        [Fact]
        public void DeleteCommand_DeleteFromV3FileSystemSource()
        {
            var nugetexe = Util.GetNuGetExePath();

            using (var source = TestFileSystemUtility.CreateRandomTestFolder())
            {
                //drop dummy artifacts to make it a V3
                var dummyPackageName = "foo";
                var version = "1.0.0";
                var packageFolder = Directory.CreateDirectory(Path.Combine(source.Path, dummyPackageName));
                var packageVersionFolder = Directory.CreateDirectory(Path.Combine(packageFolder.FullName, "1.0.0"));
                File.WriteAllText(Path.Combine(packageVersionFolder.FullName, dummyPackageName + ".nuspec"), "dummy text");
                Assert.True(Directory.Exists(packageVersionFolder.FullName));
                // Act
                string[] args = new string[] {
                    "delete", "foo", version,
                    "-Source", source, "-NonInteractive" };
                var r = CommandRunner.Run(
                    nugetexe,
                    Directory.GetCurrentDirectory(),
                    String.Join(" ", args),
                    waitForExit: true);

                // Assert
                Assert.Equal(0, r.Item1);
                //The specific version folder should be gone.
                Assert.False(Directory.Exists(packageVersionFolder.FullName));
            }
        }

        [Fact]
        public void DeleteCommand_DeleteReadOnlyFileFromV3FileSystemSource()
        {
            var nugetexe = Util.GetNuGetExePath();

            using (var source = TestFileSystemUtility.CreateRandomTestFolder())
            {
                //drop dummy artifacts to make it a V3
                var dummyPackageName = "foo";
                var version = "1.0.0";
                var packageFolder = Directory.CreateDirectory(Path.Combine(source.Path, dummyPackageName));
                var packageVersionFolder = Directory.CreateDirectory(Path.Combine(packageFolder.FullName, "1.0.0"));
                var dummyNuspec = Path.Combine(packageVersionFolder.FullName, dummyPackageName + ".nuspec");
                File.WriteAllText(dummyNuspec, "dummy text");
                File.SetAttributes(dummyNuspec, File.GetAttributes(dummyNuspec) | FileAttributes.ReadOnly);
                // Act
                string[] args = new string[] {
                    "delete", "foo", version,
                    "-Source", source, "-NonInteractive" };
                var r = CommandRunner.Run(
                    nugetexe,
                    Directory.GetCurrentDirectory(),
                    String.Join(" ", args),
                    waitForExit: true);

                // Assert
                Assert.Equal(0, r.Item1);
                Assert.False(Directory.Exists(packageVersionFolder.FullName));
            }
        }

        // Same as DeleteCommand_DeleteFromFileSystemSource, except that the directory is specified
        // in unix style.
        [Fact]
        public void DeleteCommand_DeleteFromFileSystemSourceUnixStyle()
        {
            var nugetexe = Util.GetNuGetExePath();

            using (var windowsSource = TestFileSystemUtility.CreateRandomTestFolder())
            {
                string source = ((string)windowsSource).Replace(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

                // Arrange
                var packageFileName = Util.CreateTestPackage("testPackage1", "1.1.0", windowsSource);
                Assert.True(File.Exists(packageFileName));

                // Act
                string[] args = new string[] {
                    "delete",
                    "testPackage1",
                    "1.1.0",
                    "-Source",
                    source,
                    "-NonInteractive" };

                var r = CommandRunner.Run(
                    nugetexe,
                    Directory.GetCurrentDirectory(),
                    $"delete testPackage1 1.1.0 -Source {source} -NonInteractive",
                    waitForExit: true);

                // Assert
                Assert.Equal(0, r.Item1);
                Assert.False(File.Exists(packageFileName));
            }
        }

        [Fact]
        public void DeleteCommand_DeleteFromHttpSource()
        {
            var nugetexe = Util.GetNuGetExePath();

            // Arrange
            using (var server = new MockServer())
            {
                server.Start();
                bool deleteRequestIsCalled = false;

                server.Delete.Add("/nuget/testPackage1/1.1", request =>
                {
                    deleteRequestIsCalled = true;
                    return HttpStatusCode.OK;
                });

                // Act
                string[] args = new string[] {
                    "delete", "testPackage1", "1.1.0",
                    "-Source", server.Uri + "nuget", "-NonInteractive" };

                var r = CommandRunner.Run(
                    nugetexe,
                    Directory.GetCurrentDirectory(),
                    string.Join(" ", args),
                    waitForExit: true);

                // Assert
                Assert.Equal(0, r.Item1);
                Assert.True(deleteRequestIsCalled);
            }
        }

        [Theory, MemberData(nameof(ServerWarningData))]
        public void DeleteCommand_ShowsServerWarnings(string firstServerWarning, string secondServerWarning)
        {
            var serverWarnings = new[] { firstServerWarning, secondServerWarning };
            var nugetexe = Util.GetNuGetExePath();

            // Arrange
            using (var server = new MockServer())
            {
                server.Start();

                server.Delete.Add("/nuget/testPackage1/1.1", request => HttpStatusCode.OK);

                server.AddServerWarnings(serverWarnings);

                // Act
                string[] args = new string[] {
                    "delete", "testPackage1", "1.1.0",
                    "-Source", server.Uri + "nuget", "-NonInteractive" };

                var r = CommandRunner.Run(
                    nugetexe,
                    Directory.GetCurrentDirectory(),
                    string.Join(" ", args),
                    waitForExit: true);

                // Assert
                foreach (var serverWarning in serverWarnings)
                {
                    if (!string.IsNullOrEmpty(serverWarning))
                    {
                        Assert.Contains(serverWarning, r.Item2);
                    }
                }
            }
        }

        public static IEnumerable<string[]> ServerWarningData
        {
            get
            {
                return new[]
                {
                    new string[] { null, null },
                    new string[] { "Single server warning message", null},
                    new string[] { "First of two server warning messages", "Second of two server warning messages"}
                };
            }
        }
    }
}
