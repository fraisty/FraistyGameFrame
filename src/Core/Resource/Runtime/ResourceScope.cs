using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FRFrame
{
    public sealed class ResourceScope : System.IDisposable
    {
        private readonly ResourceManager _resourceManager;
        private readonly Dictionary<ResourceKey, int> _references = new Dictionary<ResourceKey, int>();
        private readonly Dictionary<ResourceKey, ResourceRawFileHandle> _rawFiles = new Dictionary<ResourceKey, ResourceRawFileHandle>();

        public string Name { get; }
        public string PackageName { get; }
        public ResourceScopeState State { get; private set; } = ResourceScopeState.Active;
        public IReadOnlyList<ResourceKey> Keys => new List<ResourceKey>(_references.Keys);

        internal ResourceScope(ResourceManager resourceManager, string name, string packageName = null)
        {
            _resourceManager = resourceManager;
            Name = name;
            PackageName = packageName;
        }

        public async UniTask<T> LoadAssetAsync<T>(string location, ResourceLoadOptions options = null) where T : Object
        {
            return await LoadAssetAsync<T>(CreateKey(location), options);
        }

        public async UniTask<T> LoadAssetAsync<T>(ResourceKey key, ResourceLoadOptions options = null) where T : Object
        {
            if (!EnsureActive())
                return null;

            key = _resourceManager.NormalizeKey(key);
            T asset = await _resourceManager.LoadAssetAsync<T>(key, options);
            if (asset != null)
                Retain(key);
            return asset;
        }

        public async UniTask<ResourceHandle<T>> LoadAssetHandleAsync<T>(string location, ResourceLoadOptions options = null) where T : Object
        {
            return await LoadAssetHandleAsync<T>(CreateKey(location), options);
        }

        public async UniTask<ResourceHandle<T>> LoadAssetHandleAsync<T>(ResourceKey key, ResourceLoadOptions options = null) where T : Object
        {
            if (!EnsureActive())
                return null;

            key = _resourceManager.NormalizeKey(key);
            var handle = await _resourceManager.LoadAssetHandleAsync<T>(key, options);
            if (handle != null && handle.IsValid)
                Retain(key);
            return handle;
        }

        public UniTask<string> LoadRawFilePathAsync(string location)
        {
            return LoadRawFilePathAsync(CreateKey(location));
        }

        public async UniTask<string> LoadRawFilePathAsync(ResourceKey key)
        {
            if (!EnsureActive())
                return null;

            key = _resourceManager.NormalizeKey(key);
            if (_rawFiles.TryGetValue(key, out ResourceRawFileHandle cached) && cached.IsValid)
                return cached.FilePath;

            ResourceRawFileHandle loaded = await _resourceManager.LoadRawFileHandleAsync(key);
            if (loaded == null || !loaded.IsValid)
                return null;

            if (State != ResourceScopeState.Active)
            {
                loaded.Release();
                return null;
            }

            // 并发请求只保留一个由 scope 持有的原生文件句柄。
            if (_rawFiles.TryGetValue(key, out cached) && cached.IsValid)
            {
                loaded.Release();
                return cached.FilePath;
            }

            _rawFiles[key] = loaded;
            return loaded.FilePath;
        }

        public async UniTask<GameObject> InstantiateAsync(string location, Transform parent = null, ResourceLoadOptions options = null)
        {
            return await InstantiateAsync(CreateKey(location), parent, options);
        }

        public async UniTask<GameObject> InstantiateAsync(ResourceKey key, Transform parent = null, ResourceLoadOptions options = null)
        {
            if (!EnsureActive())
                return null;

            key = _resourceManager.NormalizeKey(key);
            GameObject instance = await _resourceManager.InstantiateAsync(key, parent, options);
            if (instance != null)
                Retain(key);
            return instance;
        }

        public void Release(ResourceKey key)
        {
            key = _resourceManager.NormalizeKey(key);
            if (!_references.TryGetValue(key, out int count))
                return;

            _resourceManager.ReleaseAssetReference(key);
            count--;
            if (count <= 0)
                _references.Remove(key);
            else
                _references[key] = count;
        }

        public void ReleaseRawFile(string location)
        {
            ReleaseRawFile(CreateKey(location));
        }

        public void ReleaseRawFile(ResourceKey key)
        {
            key = _resourceManager.NormalizeKey(key);
            if (!_rawFiles.TryGetValue(key, out ResourceRawFileHandle handle))
                return;

            handle.Release();
            _rawFiles.Remove(key);
        }

        public void ReleaseAll()
        {
            if (State == ResourceScopeState.Released)
                return;

            State = ResourceScopeState.Releasing;
            foreach (var pair in new Dictionary<ResourceKey, int>(_references))
            {
                for (int i = 0; i < pair.Value; i++)
                    _resourceManager.ReleaseAssetReference(pair.Key);
            }
            _references.Clear();
            foreach (ResourceRawFileHandle handle in _rawFiles.Values)
                handle.Release();
            _rawFiles.Clear();
            State = ResourceScopeState.Released;
        }

        public void Dispose()
        {
            ReleaseAll();
        }

        private void Retain(ResourceKey key)
        {
            _references.TryGetValue(key, out int count);
            _references[key] = count + 1;
        }

        private ResourceKey CreateKey(string location)
        {
            return _resourceManager.NormalizeKey(new ResourceKey(PackageName, location));
        }

        private bool EnsureActive()
        {
            if (State == ResourceScopeState.Active)
                return true;
            FRLog.Error($"ResourceScope is released : {Name}");
            return false;
        }
    }
}
