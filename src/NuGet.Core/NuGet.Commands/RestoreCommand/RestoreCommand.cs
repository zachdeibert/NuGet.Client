// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using NuGet.Client;
using NuGet.Common;
using NuGet.ContentModel;
using NuGet.DependencyResolver;
using NuGet.Frameworks;
using NuGet.LibraryModel;
using NuGet.Packaging;
using NuGet.Packaging.Core;
using NuGet.ProjectModel;
using NuGet.Protocol;
using NuGet.Repositories;
using NuGet.RuntimeModel;
using NuGet.Versioning;

namespace NuGet.Commands
{
    public class RestoreCommand
    {
        private readonly ILogger _logger;
        private readonly RestoreRequest _request;

        private bool _success = true;

        private readonly Dictionary<NuGetFramework, RuntimeGraph> _runtimeGraphCache = new Dictionary<NuGetFramework, RuntimeGraph>();
        private readonly ConcurrentDictionary<PackageIdentity, RuntimeGraph> _runtimeGraphCacheByPackage
            = new ConcurrentDictionary<PackageIdentity, RuntimeGraph>(PackageIdentity.Comparer);
        private readonly Dictionary<RestoreTargetGraph, Dictionary<string, LibraryIncludeFlags>> _includeFlagGraphs
            = new Dictionary<RestoreTargetGraph, Dictionary<string, LibraryIncludeFlags>>();

        public RestoreCommand(RestoreRequest request)
        {
            if (request == null)
            {
                throw new ArgumentNullException(nameof(request));
            }

            // Validate the lock file version requested
            if (request.LockFileVersion < 1 || request.LockFileVersion > LockFileFormat.Version)
            {
                Debug.Fail($"Lock file version {_request.LockFileVersion} is not supported.");
                throw new ArgumentOutOfRangeException(nameof(_request.LockFileVersion));
            }

            _logger = request.Log;
            _request = request;
        }

        public Task<RestoreResult> ExecuteAsync()
        {
            return ExecuteAsync(CancellationToken.None);
        }

        public async Task<RestoreResult> ExecuteAsync(CancellationToken token)
        {
            _logger.LogMinimal(string.Format(CultureInfo.CurrentCulture, Strings.Log_RestoringPackages, _request.Project.FilePath));

            var projectLockFilePath = string.IsNullOrEmpty(_request.LockFilePath) ?
                Path.Combine(_request.Project.BaseDirectory, LockFileFormat.LockFileName) :
                _request.LockFilePath;

            string projectHash = string.Empty;

            if (_request.ExistingLockFile != null && _request.ExistingLockFile.Success)
            {
                var allDependencies = GetAllProjectDependencies();

                var isFloatingRange =
                    allDependencies.SelectMany(dep => dep.Value).Any(dep => dep.VersionRange.IsFloating);

                if (!isFloatingRange)
                {
                    projectHash = GenerateShaHash(_request.Project, allDependencies.ToJson());
                    if (string.Equals(_request.ExistingLockFile.Sha1, projectHash, StringComparison.OrdinalIgnoreCase))
                    {
                        if (!IsAnyPackageMissing(_request.ExistingLockFile))
                        {
                            // Only execute tool restore if the request lock file version is 2 or greater.
                            // Tools did not exist prior to v2 lock files.
                            var toolRestoreResults2 = Enumerable.Empty<ToolRestoreResult>();
                            if (_request.LockFileVersion >= 2)
                            {
                                toolRestoreResults2 = await ExecuteToolRestoresAsync(
                                    _request.DependencyProviders.GlobalPackages,
                                    _request.DependencyProviders.FallbackPackageFolders,
                                    token);
                            }

                            return new RestoreResult(
                                true,
                                _request.ExistingLockFile,
                                projectLockFilePath,
                                new MSBuildRestoreResult(_request.Project.Name, _request.Project.BaseDirectory, true),
                                toolRestoreResults2);
                        }
                    }
                }
            }

            // Local package folders (non-sources)
            var localRepositories = new List<NuGetv3LocalRepository>();
            localRepositories.Add(_request.DependencyProviders.GlobalPackages);
            localRepositories.AddRange(_request.DependencyProviders.FallbackPackageFolders);

            var contextForProject = CreateRemoteWalkContext(_request);

            var graphs = await ExecuteRestoreAsync(
                _request.DependencyProviders.GlobalPackages,
                _request.DependencyProviders.FallbackPackageFolders,
                contextForProject,
                token);

            // Only execute tool restore if the request lock file version is 2 or greater.
            // Tools did not exist prior to v2 lock files.
            var toolRestoreResults = Enumerable.Empty<ToolRestoreResult>();
            if (_request.LockFileVersion >= 2)
            {
                toolRestoreResults = await ExecuteToolRestoresAsync(
                                    _request.DependencyProviders.GlobalPackages,
                                    _request.DependencyProviders.FallbackPackageFolders,
                                    token);
            }

            // generate hash if not generated already
            if (string.IsNullOrEmpty(projectHash))
            {
                var allDependencies = GetAllProjectDependencies();
                projectHash = GenerateShaHash(_request.Project, allDependencies.ToJson());
            }

            var lockFile = BuildLockFile(
                _request.ExistingLockFile,
                _request.Project,
                graphs,
                localRepositories,
                contextForProject,
                toolRestoreResults,
                projectHash,
                true);

            if (!ValidateRestoreGraphs(graphs, _logger))
            {
                _success = false;
            }

            var checkResults = VerifyCompatibility(
                _request.Project,
                _includeFlagGraphs,
                localRepositories,
                lockFile,
                graphs,
                _logger);

            if (checkResults.Any(r => !r.Success))
            {
                _success = false;
            }

            // update success attribute of lockfile
            lockFile.Success = _success;

            // Generate Targets/Props files
            var msbuild = RestoreMSBuildFiles(_request.Project, graphs, localRepositories, contextForProject);

            // If the request is for a v1 lock file then downgrade it and remove all v2 properties
            if (_request.LockFileVersion == 1)
            {
                DowngradeLockFileToV1(lockFile);
            }

            return new RestoreResult(
                _success,
                graphs,
                checkResults,
                lockFile,
                _request.ExistingLockFile,
                projectLockFilePath,
                msbuild,
                toolRestoreResults);
        }

        private Dictionary<string, LibraryRange[]> GetAllProjectDependencies()
        {
            var projectDependencies = new Dictionary<string, LibraryRange[]>();

            var dependencies = _request.Project.Dependencies.Select(lib => lib.LibraryRange).ToArray();
            projectDependencies.Add(_request.Project.Name, dependencies);

            // External references
            var updatedExternalProjects = new List<ExternalProjectReference>(_request.ExternalProjects);

            if (_request.ExternalProjects.Count > 0)
            {
                // There should be at most one match in the external projects.
                var rootProjectMatches = _request.ExternalProjects.Where(proj =>
                        string.Equals(
                            _request.Project.Name,
                            proj.PackageSpecProjectName,
                            StringComparison.OrdinalIgnoreCase))
                    .ToList();

                var rootProject = rootProjectMatches.SingleOrDefault();

                if (rootProject != null)
                {
                    // Replace the project spec with the passed in package spec,
                    // for installs which are done in memory first this will be
                    // different from the one on disk
                    updatedExternalProjects.RemoveAll(project =>
                            project.UniqueName.Equals(rootProject.UniqueName, StringComparison.Ordinal));

                    var updatedReference = new ExternalProjectReference(
                        rootProject.UniqueName,
                        _request.Project,
                        rootProject.MSBuildProjectPath,
                        rootProject.ExternalProjectReferences);

                    updatedExternalProjects.Add(updatedReference);

                }
            }

            // the external project provider is specific to the current restore project
            var projectResolver = new PackageSpecResolver(_request.Project);
            var provider = new PackageSpecReferenceDependencyProvider(projectResolver, updatedExternalProjects, _logger);

            var projectRange = new LibraryRange()
            {
                Name = _request.Project.Name,
                VersionRange = new VersionRange(_request.Project.Version),
                TypeConstraint = LibraryDependencyTarget.Project | LibraryDependencyTarget.ExternalProject
            };

            var library = new Library
            {
                LibraryRange = projectRange,
                Identity = new LibraryIdentity
                {
                    Name = _request.Project.Name,
                    Version = _request.Project.Version,
                    Type = LibraryType.Project,
                },
                Path = _request.Project.FilePath,
                Dependencies = _request.Project.Dependencies,
                Resolved = true
            };

            var runtimeIds = RequestRuntimeUtility.GetRestoreRuntimes(_request);
            var projectFrameworkRuntimePairs = CreateFrameworkRuntimePairs(_request.Project, runtimeIds);

            foreach (var framework in projectFrameworkRuntimePairs)
            {
                ProcessDependencies(library, framework.Framework, provider, projectDependencies);                
            }

            return projectDependencies;
        }

        private void ProcessDependencies(Library library, NuGetFramework framework, PackageSpecReferenceDependencyProvider provider, Dictionary<string, LibraryRange[]> projectRefs)
        {
            foreach (var dependency in library.Dependencies)
            {
                if (provider.SupportsType(dependency.LibraryRange.TypeConstraint))
                {
                    var rootPath = GetRootPathForParentProject(library.Path);
                    var match = provider.GetLibrary(dependency.LibraryRange, framework, rootPath);
                    if (match != null && !projectRefs.ContainsKey(match.LibraryRange.Name))
                    {
                        projectRefs.Add(match.LibraryRange.Name, match.Dependencies.Select(lib => lib.LibraryRange).ToArray());
                        ProcessDependencies(match, framework, provider, projectRefs);
                    }
                }
            }
        }

        /// <summary>
        /// Returns root directory of the parent project.
        /// This will be null if the reference is from a non-project type.
        /// </summary>
        private string GetRootPathForParentProject(string path)
        {
            if (!string.IsNullOrEmpty(path))
            {
                var projectJsonPath = new FileInfo(path);

                // For files in the root of the drive this will be null
                if (projectJsonPath.Directory.Parent == null)
                {
                    return projectJsonPath.Directory.FullName;
                }
                else
                {
                    return projectJsonPath.Directory.Parent.FullName;
                }
            }

            return null;
        }

        private bool IsAnyPackageMissing(LockFile lockFile)
        {
            var packageTypeLibraries =
                lockFile.Libraries.Where(
                    l => l.Type != LibraryType.Project && l.Type != LibraryType.ExternalProject).ToArray();

            foreach (var library in packageTypeLibraries)
            {
                var packagePathResolver = new VersionFolderPathResolver(_request.PackagesDirectory);
                var hashPath = packagePathResolver.GetHashPath(library.Name, library.Version);

                if (!File.Exists(hashPath))
                {
                    return true;
                }

                var sha512 = File.ReadAllText(hashPath);

                if (library.Sha512 != sha512)
                {
                    // A package has changed
                    return true;
                }
            }
            return false;
        }

        private LockFile BuildLockFile(
            LockFile existingLockFile,
            PackageSpec project,
            IEnumerable<RestoreTargetGraph> graphs,
            IReadOnlyList<NuGetv3LocalRepository> localRepositories,
            RemoteWalkContext contextForProject,
            IEnumerable<ToolRestoreResult> toolRestoreResults,
            string projectHash,
            bool success)
        {
            // Build the lock file
            var lockFile = new LockFileBuilder(_request.LockFileVersion, _logger, _includeFlagGraphs)
                    .CreateLockFile(
                        existingLockFile,
                        project,
                        graphs,
                        localRepositories,
                        contextForProject,
                        toolRestoreResults,
                        projectHash,
                        success);

            return lockFile;
        }

        public string GenerateShaHash(PackageSpec project, string projDependenciesJson)
        {
            var items = new List<string>();
            
            // local providers
            foreach (var provider in _request.DependencyProviders.LocalProviders)
            {
                
                items.Add(provider.RepositorySource);
            }

            // remote providers
            foreach (var provider in _request.DependencyProviders.RemoteProviders)
            {
                items.Add(provider.RepositorySource);
            }

            // project tools
            if (project.Tools != null && project.Tools.Count > 0)
            {
                items.Add(project.Tools.ToJson());
            }

            // project target frameworks
            if (project.TargetFrameworks != null && project.TargetFrameworks.Count > 0)
            {
                items.Add(project.TargetFrameworks.ToJson());
            }

            // project dependencies along with project ref dependencies
            items.Add(projDependenciesJson);

            var value = items.ToJson();
            string hashValue;
            using (var sha = SHA1.Create())
            {
                /* string b64 = ByteArrayToString(Encoding.ASCII.GetBytes(value));
                 var b64Bytes = Encoding.ASCII.GetBytes(b64);
                 var result = sha.ComputeHash(b64Bytes);
                 hashValue = BitConverter.ToString(result).Replace("-", "").ToLower();*/

                hashValue = Convert.ToBase64String(sha.ComputeHash(Encoding.UTF8.GetBytes(value)));
            }

            return hashValue;
        }

        public static string ByteArrayToString(byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
            {
                hex.AppendFormat("{0:x2}", b);
            }
            return hex.ToString().ToLower();
        }

        private static bool ValidateRestoreGraphs(IEnumerable<RestoreTargetGraph> graphs, ILogger logger)
        {
            foreach (var g in graphs)
            {
                foreach (var cycle in g.AnalyzeResult.Cycles)
                {
                    logger.LogError(Strings.Log_CycleDetected + $" {Environment.NewLine}  {cycle.GetPath()}.");
                    return false;
                }

                foreach (var versionConflict in g.AnalyzeResult.VersionConflicts)
                {
                    logger.LogError(
                        string.Format(
                            CultureInfo.CurrentCulture,
                            Strings.Log_VersionConflict,
                            versionConflict.Selected.Key.Name)
                        + $" {Environment.NewLine} {versionConflict.Selected.GetPath()} {Environment.NewLine} {versionConflict.Conflicting.GetPath()}.");
                    return false;
                }

                foreach (var downgrade in g.AnalyzeResult.Downgrades)
                {
                    var downgraded = downgrade.DowngradedFrom;
                    var downgradedBy = downgrade.DowngradedTo;

                    // Not all dependencies have a min version, if one does not exist use 0.0.0
                    var fromVersion = downgraded.Key.VersionRange.MinVersion ?? new NuGetVersion(0, 0, 0);
                    var toVersion = downgradedBy.Key.VersionRange.MinVersion ?? new NuGetVersion(0, 0, 0);

                    logger.LogWarning(
                        string.Format(
                            CultureInfo.CurrentCulture,
                            Strings.Log_DowngradeWarning,
                            downgraded.Key.Name,
                            fromVersion,
                            toVersion)
                        + $" {Environment.NewLine} {downgraded.GetPath()} {Environment.NewLine} {downgradedBy.GetPath()}");
                }
            }

            return true;
        }

        private static IList<CompatibilityCheckResult> VerifyCompatibility(
            PackageSpec project,
            Dictionary<RestoreTargetGraph, Dictionary<string, LibraryIncludeFlags>> includeFlagGraphs,
            IReadOnlyList<NuGetv3LocalRepository> localRepositories,
            LockFile lockFile,
            IEnumerable<RestoreTargetGraph> graphs,
            ILogger logger)
        {
            // Scan every graph for compatibility, as long as there were no unresolved packages
            var checkResults = new List<CompatibilityCheckResult>();
            if (graphs.All(g => !g.Unresolved.Any()))
            {
                var checker = new CompatibilityChecker(localRepositories, lockFile, logger);
                foreach (var graph in graphs)
                {
                    logger.LogVerbose(string.Format(CultureInfo.CurrentCulture, Strings.Log_CheckingCompatibility, graph.Name));

                    var includeFlags = IncludeFlagUtils.FlattenDependencyTypes(includeFlagGraphs, project, graph);

                    var res = checker.Check(graph, includeFlags);
                    checkResults.Add(res);
                    if (res.Success)
                    {
                        logger.LogVerbose(string.Format(CultureInfo.CurrentCulture, Strings.Log_PackagesAndProjectsAreCompatible, graph.Name));
                    }
                    else
                    {
                        // Get error counts on a project vs package basis
                        var projectCount = res.Issues.Count(issue => issue.Type == CompatibilityIssueType.ProjectIncompatible);
                        var packageCount = res.Issues.Count(issue => issue.Type != CompatibilityIssueType.ProjectIncompatible);

                        // Log a summary with compatibility error counts
                        if (projectCount > 0)
                        {
                            logger.LogError(
                                string.Format(CultureInfo.CurrentCulture,
                                    Strings.Log_ProjectsIncompatible,
                                    graph.Name));

                            logger.LogDebug($"Incompatible projects: {projectCount}");
                        }

                        if (packageCount > 0)
                        {
                            logger.LogError(
                                string.Format(CultureInfo.CurrentCulture,
                                    Strings.Log_PackagesIncompatible,
                                    graph.Name));

                            logger.LogDebug($"Incompatible packages: {packageCount}");
                        }
                    }
                }
            }

            return checkResults;
        }

        private async Task<IEnumerable<ToolRestoreResult>> ExecuteToolRestoresAsync(
            NuGetv3LocalRepository userPackageFolder,
            IReadOnlyList<NuGetv3LocalRepository> fallbackPackageFolders,
            CancellationToken token)
        {
            var toolPathResolver = new ToolPathResolver(_request.PackagesDirectory);
            var results = new List<ToolRestoreResult>();

            var localRepositories = new List<NuGetv3LocalRepository>();
            localRepositories.Add(userPackageFolder);
            localRepositories.AddRange(fallbackPackageFolders);

            foreach (var tool in _request.Project.Tools)
            {
                _logger.LogMinimal(string.Format(
                    CultureInfo.CurrentCulture,
                    Strings.Log_RestoringToolPackages,
                    tool.LibraryRange.Name,
                    _request.Project.FilePath));

                // Build the fallback framework (which uses the "imports").
                var framework = LockFile.ToolFramework;
                if (tool.Imports.Any())
                {
                    framework = new FallbackFramework(framework, tool.Imports);
                }

                // Build a package spec in memory to execute the tool restore as if it were
                // its own project. For now, we always restore for a null runtime and a single
                // constant framework.
                var toolPackageSpec = new PackageSpec(new JObject())
                {
                    Name = Guid.NewGuid().ToString(), // make sure this package never collides with a dependency
                    Dependencies = new List<LibraryDependency>(),
                    Tools = new List<ToolDependency>(),
                    TargetFrameworks =
                    {
                        new TargetFrameworkInformation
                        {
                            FrameworkName = framework,
                            Dependencies = new List<LibraryDependency>
                            {
                                new LibraryDependency
                                {
                                    LibraryRange = tool.LibraryRange
                                }
                            }
                        }
                    }
                };

                // Try to find the existing lock file. Since the existing lock file is pathed under
                // a folder that includes the resolved tool's version, this is a bit of a chicken
                // and egg problem. That is, we need to run the restore operation in order to resolve
                // a tool version, but we need the tool version to find the existing project.lock.json
                // file which is required before executing the restore! Fortunately, this is solved by
                // looking at the tool's consuming project's lock file to see if the tool has been
                // restored before.
                LockFile existingToolLockFile = null;
                string toolHashValue = string.Empty;
                if (_request.ExistingLockFile != null)
                {
                    var existingTarget = _request
                        .ExistingLockFile
                        .Tools
                        .Where(t => t.RuntimeIdentifier == null)
                        .Where(t => t.TargetFramework.Equals(LockFile.ToolFramework))
                        .FirstOrDefault();

                    var existingLibrary = existingTarget?.Libraries
                        .Where(l => StringComparer.OrdinalIgnoreCase.Equals(l.Name, tool.LibraryRange.Name))
                        .Where(l => tool.LibraryRange.VersionRange.Satisfies(l.Version))
                        .FirstOrDefault();

                    if (existingLibrary != null)
                    {
                        var existingLockFilePath = toolPathResolver.GetLockFilePath(
                            existingLibrary.Name,
                            existingLibrary.Version,
                            existingTarget.TargetFramework);

                        existingToolLockFile = LockFileUtilities.GetLockFile(existingLockFilePath, _logger);

                        var isFloatingRange = tool.LibraryRange.VersionRange.IsFloating ||
                                                  existingLibrary.Dependencies.Any(dep => dep.VersionRange.IsFloating);

                        var sortedDependencies = existingLibrary.Dependencies.OrderBy(dep => dep.Id);

                        toolHashValue = GenerateShaHash(toolPackageSpec, sortedDependencies.ToJson());

                        if (existingToolLockFile != null && !isFloatingRange)
                        {                            
                            if (string.Equals(existingToolLockFile.Sha1, toolHashValue, StringComparison.Ordinal))
                            {
                                if (!IsAnyPackageMissing(existingToolLockFile))
                                {
                                    results.Add(new ToolRestoreResult(
                                        tool.LibraryRange.Name,
                                        true,
                                        existingTarget,
                                        existingLibrary,
                                        existingLockFilePath,
                                        existingToolLockFile,
                                        existingToolLockFile));

                                    // skip rest of the code and move to next tool
                                    continue;
                                }
                            }
                        }
                    }
                }

                // Execute the restore.
                var toolSuccess = true; // success for this individual tool restore
                var runtimeIds = new HashSet<string>();
                var projectFrameworkRuntimePairs = CreateFrameworkRuntimePairs(toolPackageSpec, runtimeIds);
                var allInstalledPackages = new HashSet<LibraryIdentity>();
                var contextForTool = CreateRemoteWalkContext(_request);
                var walker = new RemoteDependencyWalker(contextForTool);
                var projectRestoreRequest = new ProjectRestoreRequest(
                    _request,
                    toolPackageSpec,
                    existingToolLockFile,
                    new Dictionary<NuGetFramework, RuntimeGraph>(),
                    _runtimeGraphCacheByPackage);
                var projectRestoreCommand = new ProjectRestoreCommand(_logger, projectRestoreRequest);
                var result = await projectRestoreCommand.TryRestore(
                    tool.LibraryRange,
                    projectFrameworkRuntimePairs,
                    allInstalledPackages,
                    userPackageFolder,
                    fallbackPackageFolders,
                    walker,
                    contextForTool,
                    writeToLockFile: true,
                    forceRuntimeGraphCreation: false,
                    token: token);

                var graphs = result.Item2;
                if (!result.Item1)
                {
                    toolSuccess = false;
                    _success = false;
                }                

                // Create the lock file (in memory).
                var toolLockFile = BuildLockFile(
                    existingToolLockFile,
                    toolPackageSpec,
                    graphs,
                    localRepositories,
                    contextForTool,
                    Enumerable.Empty<ToolRestoreResult>(),
                    toolHashValue,
                    true);

                // Build the path based off of the resolved tool. For now, we assume there is only
                // ever one target.
                var target = toolLockFile.Targets.Single();
                var fileTargetLibrary = target
                    .Libraries
                    .FirstOrDefault(l => StringComparer.OrdinalIgnoreCase.Equals(tool.LibraryRange.Name, l.Name));
                string toolLockFilePath = null;
                if (fileTargetLibrary != null)
                {
                    toolLockFilePath = toolPathResolver.GetLockFilePath(
                        fileTargetLibrary.Name,
                        fileTargetLibrary.Version,
                        target.TargetFramework);

                    //update hash value if required
                    if (string.IsNullOrEmpty(toolHashValue))
                    {
                        var sortedDependencies = fileTargetLibrary.Dependencies.OrderBy(dep => dep.Id);
                        toolLockFile.Sha1 = GenerateShaHash(toolPackageSpec, sortedDependencies.ToJson());
                    }
                }

                // Validate the results.
                if (!ValidateRestoreGraphs(graphs, _logger))
                {
                    toolSuccess = false;
                    _success = false;
                }
                
                var checkResults = VerifyCompatibility(
                    toolPackageSpec,
                    new Dictionary<RestoreTargetGraph, Dictionary<string, LibraryIncludeFlags>>(),
                    localRepositories,
                    toolLockFile,
                    graphs,
                    _logger);

                if (checkResults.Any(r => !r.Success))
                {
                    toolSuccess = false;
                    _success = false;
                }

                // update tool success value in tool lock file
                toolLockFile.Success = toolSuccess;

                results.Add(new ToolRestoreResult(
                    tool.LibraryRange.Name,
                    toolSuccess,
                    target,
                    fileTargetLibrary,
                    toolLockFilePath,
                    toolLockFile,
                    existingToolLockFile));
            }

            return results;
        }

        private async Task<IEnumerable<RestoreTargetGraph>> ExecuteRestoreAsync(
            NuGetv3LocalRepository userPackageFolder,
            IReadOnlyList<NuGetv3LocalRepository> fallbackPackageFolders,
            RemoteWalkContext context,
            CancellationToken token)
        {
            if (_request.Project.TargetFrameworks.Count == 0)
            {
                _logger.LogError(string.Format(CultureInfo.CurrentCulture, Strings.Log_ProjectDoesNotSpecifyTargetFrameworks, _request.Project.Name, _request.Project.FilePath));
                _success = false;
                return Enumerable.Empty<RestoreTargetGraph>();
            }            

            // External references
            var updatedExternalProjects = new List<ExternalProjectReference>(_request.ExternalProjects);

            if (_request.ExternalProjects.Count > 0)
            {
                // There should be at most one match in the external projects.
                var rootProjectMatches = _request.ExternalProjects.Where(proj =>
                     string.Equals(
                         _request.Project.Name,
                         proj.PackageSpecProjectName,
                         StringComparison.OrdinalIgnoreCase))
                     .ToList();

                if (rootProjectMatches.Count > 1)
                {
                    throw new InvalidOperationException($"Ambiguous project name '{_request.Project.Name}'.");
                }

                var rootProject = rootProjectMatches.SingleOrDefault();

                if (rootProject != null)
                {
                    // Replace the project spec with the passed in package spec,
                    // for installs which are done in memory first this will be
                    // different from the one on disk
                    updatedExternalProjects.RemoveAll(project =>
                        project.UniqueName.Equals(rootProject.UniqueName, StringComparison.Ordinal));

                    var updatedReference = new ExternalProjectReference(
                        rootProject.UniqueName,
                        _request.Project,
                        rootProject.MSBuildProjectPath,
                        rootProject.ExternalProjectReferences);

                    updatedExternalProjects.Add(updatedReference);

                    // Determine if the targets and props files should be written out.
                    context.IsMsBuildBased = XProjUtility.IsMSBuildBasedProject(rootProject.MSBuildProjectPath);
                }
                else
                {
                    Debug.Fail("RestoreRequest.ExternaProjects contains references, but does not contain the top level references. Add the project we are restoring for.");
                    throw new InvalidOperationException($"Missing external reference metadata for {_request.Project.Name}");
                }
            }

            // Load repositories

            // the external project provider is specific to the current restore project
            var projectResolver = new PackageSpecResolver(_request.Project);
            context.ProjectLibraryProviders.Add(
                    new PackageSpecReferenceDependencyProvider(projectResolver, updatedExternalProjects, _logger));

            var remoteWalker = new RemoteDependencyWalker(context);

            var projectRange = new LibraryRange()
            {
                Name = _request.Project.Name,
                VersionRange = new VersionRange(_request.Project.Version),
                TypeConstraint = LibraryDependencyTarget.Project | LibraryDependencyTarget.ExternalProject
            };

            // Resolve dependency graphs
            var allInstalledPackages = new HashSet<LibraryIdentity>();
            var allGraphs = new List<RestoreTargetGraph>();
            var runtimeIds = RequestRuntimeUtility.GetRestoreRuntimes(_request);
            var projectFrameworkRuntimePairs = CreateFrameworkRuntimePairs(_request.Project, runtimeIds);
            var hasSupports = _request.Project.RuntimeGraph.Supports.Count > 0;

            var projectRestoreRequest = new ProjectRestoreRequest(
                _request,
                _request.Project,
                _request.ExistingLockFile,
                _runtimeGraphCache,
                _runtimeGraphCacheByPackage);
            var projectRestoreCommand = new ProjectRestoreCommand(_logger, projectRestoreRequest);
            var result = await projectRestoreCommand.TryRestore(
                projectRange,
                projectFrameworkRuntimePairs,
                allInstalledPackages,
                userPackageFolder,
                fallbackPackageFolders,
                remoteWalker,
                context,
                writeToLockFile: true,
                forceRuntimeGraphCreation: hasSupports,
                token: token);

            var success = result.Item1;

            allGraphs.AddRange(result.Item2);

            _success = success;

            // Calculate compatibility profiles to check by merging those defined in the project with any from the command line
            foreach (var profile in _request.Project.RuntimeGraph.Supports)
            {
                var runtimes = result.Item3;

                CompatibilityProfile compatProfile;
                if (profile.Value.RestoreContexts.Any())
                {
                    // Just use the contexts from the project definition
                    compatProfile = profile.Value;
                }
                else if (!runtimes.Supports.TryGetValue(profile.Value.Name, out compatProfile))
                {
                    // No definition of this profile found, so just continue to the next one
                    _logger.LogWarning(string.Format(CultureInfo.CurrentCulture, Strings.Log_UnknownCompatibilityProfile, profile.Key));
                    continue;
                }

                foreach (var pair in compatProfile.RestoreContexts)
                {
                    _logger.LogDebug($" {profile.Value.Name} -> +{pair}");
                    _request.CompatibilityProfiles.Add(pair);
                }
            }

            // Walk additional runtime graphs for supports checks
            if (_success && _request.CompatibilityProfiles.Any())
            {
                var compatibilityResult = await projectRestoreCommand.TryRestore(projectRange,
                                                          _request.CompatibilityProfiles,
                                                          allInstalledPackages,
                                                          userPackageFolder,
                                                          fallbackPackageFolders,
                                                          remoteWalker,
                                                          context,
                                                          writeToLockFile: false,
                                                          forceRuntimeGraphCreation: true,
                                                          token: token);

                _success = compatibilityResult.Item1;

                // TryRestore may contain graphs that are already in allGraphs if the
                // supports section contains the same TxM as the project framework.
                var currentGraphs = new HashSet<KeyValuePair<NuGetFramework, string>>(
                    allGraphs.Select(graph => new KeyValuePair<NuGetFramework, string>(
                        graph.Framework,
                        graph.RuntimeIdentifier))
                    );

                foreach (var graph in compatibilityResult.Item2)
                {
                    var key = new KeyValuePair<NuGetFramework, string>(
                        graph.Framework,
                        graph.RuntimeIdentifier);

                    if (currentGraphs.Add(key))
                    {
                        allGraphs.Add(graph);
                    }
                }
            }

            return allGraphs;
        }

        private static IEnumerable<FrameworkRuntimePair> CreateFrameworkRuntimePairs(
            PackageSpec packageSpec,
            ISet<string> runtimeIds)
        {
            var projectFrameworkRuntimePairs = new List<FrameworkRuntimePair>();
            foreach (var framework in packageSpec.TargetFrameworks)
            {
                // We care about TFM only and null RID for compilation purposes
                projectFrameworkRuntimePairs.Add(new FrameworkRuntimePair(framework.FrameworkName, null));

                foreach (var runtimeId in runtimeIds)
                {
                    projectFrameworkRuntimePairs.Add(new FrameworkRuntimePair(framework.FrameworkName, runtimeId));
                }
            }

            return projectFrameworkRuntimePairs;
        }

        private static RemoteWalkContext CreateRemoteWalkContext(RestoreRequest request)
        {
            var context = new RemoteWalkContext();

            foreach (var provider in request.DependencyProviders.LocalProviders)
            {
                context.LocalLibraryProviders.Add(provider);
            }

            foreach (var provider in request.DependencyProviders.RemoteProviders)
            {
                context.RemoteLibraryProviders.Add(provider);
            }

            return context;
        }

        private MSBuildRestoreResult RestoreMSBuildFiles(PackageSpec project,
            IEnumerable<RestoreTargetGraph> targetGraphs,
            IReadOnlyList<NuGetv3LocalRepository> repositories,
            RemoteWalkContext context)
        {
            // Get the project graph
            var projectFrameworks = project.TargetFrameworks.Select(f => f.FrameworkName).ToList();

            // Non-Msbuild projects should skip targets and treat it as success
            if (!context.IsMsBuildBased)
            {
                return new MSBuildRestoreResult(project.Name, project.BaseDirectory, success: true);
            }

            // Invalid msbuild projects should write out an msbuild error target
            if (projectFrameworks.Count != 1
                || !targetGraphs.Any())
            {
                return new MSBuildRestoreResult(project.Name, project.BaseDirectory, success: false);
            }

            // Gather props and targets to write out
            var graph = targetGraphs
                .Single(g => g.Framework.Equals(projectFrameworks[0]) && string.IsNullOrEmpty(g.RuntimeIdentifier));

            var flattenedFlags = IncludeFlagUtils.FlattenDependencyTypes(_includeFlagGraphs, _request.Project, graph);

            var targets = new List<string>();
            var props = new List<string>();
            foreach (var library in graph.Flattened
                .Distinct()
                .OrderBy(g => g.Data.Match.Library))
            {
                var includeLibrary = true;

                LibraryIncludeFlags libraryFlags;
                if (flattenedFlags.TryGetValue(library.Key.Name, out libraryFlags))
                {
                    includeLibrary = libraryFlags.HasFlag(LibraryIncludeFlags.Build);
                }

                // Skip libraries that do not include build files such as transitive packages
                if (includeLibrary)
                {
                    var packageIdentity = new PackageIdentity(library.Key.Name, library.Key.Version);
                    IList<string> packageFiles;
                    context.PackageFileCache.TryGetValue(packageIdentity, out packageFiles);

                    if (packageFiles != null)
                    {
                        var contentItemCollection = new ContentItemCollection();
                        contentItemCollection.Load(packageFiles);

                        // Find MSBuild thingies
                        var groups = contentItemCollection.FindItemGroups(graph.Conventions.Patterns.MSBuildFiles);

                        // Find the nearest msbuild group, this can include the root level Any group.
                        var buildItems = NuGetFrameworkUtility.GetNearest(
                            groups,
                            graph.Framework,
                            group =>
                                group.Properties[ManagedCodeConventions.PropertyNames.TargetFrameworkMoniker]
                                    as NuGetFramework);

                        if (buildItems != null)
                        {
                            // We need to additionally filter to items that are named "{packageId}.targets" and "{packageId}.props"
                            // Filter by file name here and we'll filter by extension when we add things to the lists.
                            var items = buildItems.Items
                                .Where(item =>
                                    Path.GetFileNameWithoutExtension(item.Path)
                                    .Equals(library.Key.Name, StringComparison.OrdinalIgnoreCase))
                                .ToList();

                            var packageInfo = NuGetv3LocalRepositoryUtility.GetPackage(repositories, library.Key.Name, library.Key.Version);
                            var pathResolver = packageInfo.Repository.PathResolver;

                            targets.AddRange(items
                                .Where(c => Path.GetExtension(c.Path).Equals(".targets", StringComparison.OrdinalIgnoreCase))
                                .Select(c =>
                                    Path.Combine(pathResolver.GetInstallPath(library.Key.Name, library.Key.Version),
                                    c.Path.Replace('/', Path.DirectorySeparatorChar))));

                            props.AddRange(items
                                .Where(c => Path.GetExtension(c.Path).Equals(".props", StringComparison.OrdinalIgnoreCase))
                                .Select(c =>
                                    Path.Combine(pathResolver.GetInstallPath(library.Key.Name, library.Key.Version),
                                    c.Path.Replace('/', Path.DirectorySeparatorChar))));
                        }
                    }
                }
            }

            // Targets files contain a macro for the repository root. If only the user package folder was used
            // allow a replacement. If fallback folders were used the macro cannot be applied.
            // Do not use macros for fallback folders. Use only the first repository which is the user folder.
            var repositoryRoot = repositories.First().RepositoryRoot;

            return new MSBuildRestoreResult(project.Name, project.BaseDirectory, repositoryRoot, props, targets);
        }

        private void DowngradeLockFileToV1(LockFile lockFile)
        {
            // Remove projects from the library section
            var libraryProjects = lockFile.Libraries.Where(lib => lib.Type == LibraryType.Project).ToArray();

            foreach (var library in libraryProjects)
            {
                lockFile.Libraries.Remove(library);
            }

            // Remove projects from the targets section
            foreach (var target in lockFile.Targets)
            {
                var targetProjects = target.Libraries.Where(lib => lib.Type == LibraryType.Project).ToArray();

                foreach (var library in targetProjects)
                {
                    target.Libraries.Remove(library);
                }
            }

            foreach (var library in lockFile.Targets.SelectMany(target => target.Libraries))
            {
                // Null out all target types, these did not exist in v1
                library.Type = null;
            }

            // Remove tools
            lockFile.Tools.Clear();
            lockFile.ProjectFileToolGroups.Clear();
        }
    }
}
