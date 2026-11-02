using System.Threading;

namespace FRFrame
{
    public sealed class ResourceLoadOptions
    {
        public string PackageName;
        public ResourceResidency Residency = ResourceResidency.Normal;
        public bool TrackReference = true;
        public bool AllowCache = true;
        public bool AllowGameObjectPool = true;
        public CancellationToken CancellationToken;
        public bool ReleaseOnCanceled = true;
        public bool WarnIfSyncLoadMissCache = true;

        public static ResourceLoadOptions Default => new ResourceLoadOptions();
    }
}
