using System;
using UnityEngine;
using YooAsset;
using Object = UnityEngine.Object;

namespace FRFrame
{
    public sealed class ResourceHandle<T> : IDisposable where T : Object
    {
        private readonly ResourceManager _resourceManager;

        public ResourceKey Key { get; }
        public T Asset { get; }
        public ResourceHandleState State { get; private set; }
        public bool IsValid => State == ResourceHandleState.Valid && Asset != null;

        internal ResourceHandle(ResourceManager resourceManager, ResourceKey key, T asset)
        {
            _resourceManager = resourceManager;
            Key = key;
            Asset = asset;
            State = asset != null ? ResourceHandleState.Valid : ResourceHandleState.None;
        }

        public void Release()
        {
            if (State == ResourceHandleState.Released)
            {
                FRLog.Warning($"ResourceHandle duplicate release ignored : {Key}");
                return;
            }

            if (State != ResourceHandleState.Valid)
                return;

            _resourceManager?.ReleaseAssetReference(Key);
            State = ResourceHandleState.Released;
        }

        public void Dispose()
        {
            Release();
        }
    }

    public sealed class ResourceRawFileHandle : IDisposable
    {
        public ResourceKey Key { get; }
        public string FilePath { get; }
        public ResourceHandleState State { get; private set; }
        public bool IsValid => State == ResourceHandleState.Valid && !string.IsNullOrEmpty(FilePath);

        internal ResourceRawFileHandle(ResourceKey key, string filePath)
        {
            Key = key;
            FilePath = filePath;
            State = !string.IsNullOrEmpty(filePath)
                ? ResourceHandleState.Valid
                : ResourceHandleState.None;
        }

        public void Release()
        {
            if (State == ResourceHandleState.Released)
            {
                FRLog.Warning($"ResourceRawFileHandle duplicate release ignored : {Key}");
                return;
            }

            if (State != ResourceHandleState.Valid)
                return;

            State = ResourceHandleState.Released;
        }

        public void Dispose()
        {
            Release();
        }
    }
}
