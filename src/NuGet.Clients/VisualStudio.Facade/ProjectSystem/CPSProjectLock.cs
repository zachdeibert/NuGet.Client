// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using EnvDTE;
using Microsoft.VisualStudio.Shell.Interop;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.ProjectSystem;
using Microsoft.VisualStudio.Shell;
using MsBuildProject = Microsoft.Build.Evaluation.Project;
using Task = System.Threading.Tasks.Task;
using NuGet.Common;
using System.Threading;
#if VS14
using Microsoft.VisualStudio.ProjectSystem.Designers;
#elif VS15
using Microsoft.VisualStudio.ProjectSystem.Properties;
#endif

namespace NuGet.VisualStudio.Facade.ProjectSystem
{
    internal class CPSProjectLock : INuGetLock
    {
        private readonly Project _project;
        private readonly IVsHierarchy _hierarchy;
        private ProjectWriteLockReleaser _lockReleaser;
        private bool _isLocked;
        private readonly string _projectPath;

        public CPSProjectLock(Project project, IVsHierarchy hierarchy, string projectPath)
        {
            _project = project;
            _hierarchy = hierarchy;
            _projectPath = projectPath;
        }

        public async Task AcquireAsync(CancellationToken token)
        {
            await ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Noop if this was already called
                if (!_isLocked)
                {
                    var vsProject = (IVsProject)_hierarchy;
                    UnconfiguredProject unconfiguredProject = ProjectHelper.GetUnconfiguredProject(vsProject);
                    if (unconfiguredProject != null)
                    {
                        var service = unconfiguredProject.ProjectService.Services.ProjectLockService;
                        if (service != null)
                        {
                            _lockReleaser = await service.WriteLockAsync();
                            _isLocked = true;
                        }
                    }
                }
            });
        }

        public async Task ReleaseAsync()
        {
            await ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                // Since all calls happen on the UI thread no additional locking is needed
                if (_isLocked)
                {
                    await _lockReleaser.ReleaseAsync();
                    _isLocked = false;
                    _lockReleaser.Dispose();
                }
            });
        }

        public string Id
        {
            get
            {
                return _projectPath;
            }
        }

        public void Dispose()
        {
            if (_isLocked)
            {
                _isLocked = false;
                _lockReleaser.Dispose();
            }
        }
    }
}
