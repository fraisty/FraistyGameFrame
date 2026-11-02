using System;
using System.Threading;

namespace FRFrame
{
    public sealed class PackageDownloadOptions
    {
        public int DownloadingMaxNum = 10;
        public int FailedTryAgain = 3;
        public CancellationToken CancellationToken;
        public Action<PackageDownloadProgress> ProgressCallback;
    }
}
