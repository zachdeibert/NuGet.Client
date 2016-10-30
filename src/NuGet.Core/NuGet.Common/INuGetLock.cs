// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Threading;
using System.Threading.Tasks;

namespace NuGet.Common
{
    /// <summary>
    /// Basic get/release interface for an Async lock.
    /// </summary>
    public interface INuGetLock : IDisposable
    {
        /// <summary>
        /// Get lock
        /// </summary>
        /// <param name="token"></param>
        /// <returns></returns>
        Task AcquireAsync(CancellationToken token);

        /// <summary>
        /// Release lock.
        /// </summary>
        Task ReleaseAsync();

        /// <summary>
        /// Lock id
        /// </summary>
        string Id { get; }
    }
}
