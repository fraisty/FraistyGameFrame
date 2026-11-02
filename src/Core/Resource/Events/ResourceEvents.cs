using System;

namespace FRFrame
{
    public sealed class ResourceEvents
    {
        public event Action<ResourceEventArgs> AssetLoaded;
        public event Action<ResourceEventArgs> AssetLoadFailed;
        public event Action<ResourceEventArgs> AssetReleased;
        public event Action<ResourceEventArgs> SceneLoaded;
        public event Action<ResourceEventArgs> SceneUnloaded;
        public event Action<ResourceEventArgs> InstanceRecycled;
        public event Action<ResourceEventArgs> PoolObjectCreated;
        public event Action<ResourceEventArgs> PoolObjectReleased;

        public event Action<AssetLoadEventArgs> AssetLoadedV2;
        public event Action<AssetLoadEventArgs> AssetLoadFailedV2;
        public event Action<AssetReleaseEventArgs> AssetReleasedV2;
        public event Action<SceneLoadEventArgs> SceneLoadedV2;
        public event Action<SceneLoadEventArgs> SceneUnloadedV2;
        public event Action<ResourcePoolEventArgs> InstanceRecycledV2;
        public event Action<ResourcePoolEventArgs> PoolObjectCreatedV2;
        public event Action<ResourcePoolEventArgs> PoolObjectReleasedV2;
        public event Action<PackageDownloadProgress> PackageDownloadProgress;
        public event Action<PackageDownloadResult> PackageDownloadFinished;

        internal void RaiseAssetLoaded(ResourceEventArgs args)
        {
            AssetLoaded?.Invoke(args);
            AssetLoadedV2?.Invoke(new AssetLoadEventArgs { Key = args.Key, Location = args.Location, PackageName = args.PackageName, Error = args.Error, AssetObject = args.AssetObject });
        }

        internal void RaiseAssetLoadFailed(ResourceEventArgs args)
        {
            AssetLoadFailed?.Invoke(args);
            AssetLoadFailedV2?.Invoke(new AssetLoadEventArgs { Key = args.Key, Location = args.Location, PackageName = args.PackageName, Error = args.Error, AssetObject = args.AssetObject });
        }

        internal void RaiseAssetReleased(ResourceEventArgs args)
        {
            AssetReleased?.Invoke(args);
            AssetReleasedV2?.Invoke(new AssetReleaseEventArgs { Key = args.Key, Location = args.Location, PackageName = args.PackageName });
        }

        internal void RaiseSceneLoaded(ResourceEventArgs args)
        {
            SceneLoaded?.Invoke(args);
            SceneLoadedV2?.Invoke(new SceneLoadEventArgs { Key = args.Key, Location = args.Location, PackageName = args.PackageName, Error = args.Error });
        }

        internal void RaiseSceneUnloaded(ResourceEventArgs args)
        {
            SceneUnloaded?.Invoke(args);
            SceneUnloadedV2?.Invoke(new SceneLoadEventArgs { Key = args.Key, Location = args.Location, PackageName = args.PackageName, Error = args.Error });
        }

        internal void RaiseInstanceRecycled(ResourceEventArgs args)
        {
            InstanceRecycled?.Invoke(args);
            InstanceRecycledV2?.Invoke(ToPoolEventArgs(args));
        }

        internal void RaisePoolObjectCreated(ResourceEventArgs args)
        {
            PoolObjectCreated?.Invoke(args);
            PoolObjectCreatedV2?.Invoke(ToPoolEventArgs(args));
        }

        internal void RaisePoolObjectReleased(ResourceEventArgs args)
        {
            PoolObjectReleased?.Invoke(args);
            PoolObjectReleasedV2?.Invoke(ToPoolEventArgs(args));
        }

        internal void RaisePackageDownloadProgress(PackageDownloadProgress progress) => PackageDownloadProgress?.Invoke(progress);
        internal void RaisePackageDownloadFinished(PackageDownloadResult result) => PackageDownloadFinished?.Invoke(result);

        private static ResourcePoolEventArgs ToPoolEventArgs(ResourceEventArgs args)
        {
            return new ResourcePoolEventArgs { Key = args.Key, Location = args.Location, PackageName = args.PackageName, Instance = args.Instance };
        }
    }
}
