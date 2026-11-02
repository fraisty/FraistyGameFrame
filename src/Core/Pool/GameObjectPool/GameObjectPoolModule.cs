using System.Collections.Generic;
using UnityEngine;

namespace FRFrame
{
    public sealed class GameObjectPoolModule
    {
        private readonly Dictionary<ResourceKey, GameObjectPoolBucket> _buckets = new Dictionary<ResourceKey, GameObjectPoolBucket>();
        private readonly Dictionary<ResourceKey, GameObjectPoolOptions> _options = new Dictionary<ResourceKey, GameObjectPoolOptions>();
        private Transform _root;
        private int _defaultMaxInactiveCount;

        public bool EnableDuplicateReleaseCheck { get; set; } = true;

        public bool ContainsBucket(ResourceKey key) => _buckets.ContainsKey(key);
        public void SetRoot(Transform root) => _root = root;
        public void SetDefaultMaxInactiveCount(int count) => _defaultMaxInactiveCount = Mathf.Max(0, count);
        public void SetOptions(ResourceKey key, GameObjectPoolOptions options) => _options[key] = options ?? new GameObjectPoolOptions();
        public GameObjectPoolOptions GetOptions(ResourceKey key) => _options.TryGetValue(key, out var options) ? options : null;

        public int GetMaxInactiveCount(ResourceKey key)
        {
            var options = GetOptions(key);
            if (options == null || options.UseGlobalDefaultMaxInactiveCount)
                return _defaultMaxInactiveCount;
            return Mathf.Max(0, options.MaxInactiveCount);
        }

        public GameObjectPoolBucket GetOrCreateBucket(ResourceKey key, string location)
        {
            if (_buckets.TryGetValue(key, out var bucket))
                return bucket;

            bucket = new GameObjectPoolBucket { ResourceKey = key, Location = location };
            if (_root != null)
            {
                var go = new GameObject($"Pool_{location}");
                go.transform.SetParent(_root, false);
                bucket.Root = go.transform;
            }

            _buckets.Add(key, bucket);
            return bucket;
        }

        public GameObject TryGet(ResourceKey key)
        {
            if (!_buckets.TryGetValue(key, out var bucket))
                return null;

            while (bucket.InactiveStack.Count > 0)
            {
                var obj = bucket.InactiveStack.Pop();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                bucket.InactiveSet.Remove(obj);
#endif
                if (obj == null)
                    continue;

                if (obj.transform.parent == bucket.Root)
                    obj.transform.SetParent(null, false);

                obj.SetActive(true);
                InvokeSpawn(obj);
                return obj;
            }

            return null;
        }

        /// <summary>Spawns an already loaded prefab through the existing bucket/lifecycle protocol.
        /// Direct-reference Gameplay VFX/spatial sources use this path without duplicating a pool or
        /// skipping IPoolSpawnHandler on the first instance.</summary>
        public GameObject GetOrCreate(ResourceKey key, string location, GameObject prefab)
        {
            if (prefab == null) throw new System.ArgumentNullException(nameof(prefab));
            var instance = TryGet(key);
            if (instance != null) return instance;
            GetOrCreateBucket(key, location);
            instance = Object.Instantiate(prefab);
            try { InvokeSpawn(instance); return instance; }
            catch { DestroyPooledObject(instance); throw; }
        }

        public void Release(ResourceKey key, string location, GameObject instance)
        {
            if (instance == null) return;

            var bucket = GetOrCreateBucket(key, location);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (EnableDuplicateReleaseCheck && bucket.InactiveSet.Contains(instance))
            {
                FRLog.Warning($"GameObjectPool duplicate release ignored : {key}");
                return;
            }
#endif

            int maxInactiveCount = GetMaxInactiveCount(key);
            if (maxInactiveCount > 0 && bucket.InactiveStack.Count >= maxInactiveCount)
            {
                DestroyPooledObject(instance);
                return;
            }

            InvokeRecycle(instance);
            ApplyReleaseTransform(key, instance.transform);
            instance.SetActive(false);

            if (bucket.Root != null)
                instance.transform.SetParent(bucket.Root, false);

            bucket.InactiveStack.Push(instance);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bucket.InactiveSet.Add(instance);
#endif
        }

        public int Clear(ResourceKey key)
        {
            if (!_buckets.TryGetValue(key, out var bucket)) return 0;
            int destroyed = ClearBucketObjects(bucket);
            if (bucket.Root != null)
                if(Application.isPlaying)Object.Destroy(bucket.Root.gameObject);else Object.DestroyImmediate(bucket.Root.gameObject);
            _buckets.Remove(key);
            _options.Remove(key);
            return destroyed;
        }

        public int ClearPackage(string packageName)
        {
            int destroyed = 0;
            var pendingRemove = new List<ResourceKey>();
            foreach (var pair in _buckets)
            {
                if (pair.Key.PackageName == packageName)
                    pendingRemove.Add(pair.Key);
            }

            for (int i = 0; i < pendingRemove.Count; i++)
                destroyed += Clear(pendingRemove[i]);
            return destroyed;
        }

        public int ClearAll()
        {
            int destroyed = 0;
            var keys = new List<ResourceKey>(_buckets.Keys);
            for (int i = 0; i < keys.Count; i++)
                destroyed += Clear(keys[i]);
            _buckets.Clear();
            _options.Clear();
            return destroyed;
        }

        public int Trim(int maxRemoveCount = 0, string packageName = null)
        {
            int destroyed = 0;
            foreach (var pair in _buckets)
            {
                if (!string.IsNullOrEmpty(packageName) && pair.Key.PackageName != packageName)
                    continue;
                var bucket = pair.Value;
                while (bucket.InactiveStack.Count > 0 && (maxRemoveCount <= 0 || destroyed < maxRemoveCount))
                {
                    var obj = bucket.InactiveStack.Pop();
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                    bucket.InactiveSet.Remove(obj);
#endif
                    if (obj != null)
                    {
                        DestroyPooledObject(obj);
                        destroyed++;
                    }
                }
            }
            return destroyed;
        }

        public int GetTotalInactiveCount()
        {
            int count = 0;
            foreach (var pair in _buckets)
                count += pair.Value.CountInactive;
            return count;
        }

        public int GetBucketCount() => _buckets.Count;
        public int GetInactiveCount(ResourceKey key) => _buckets.TryGetValue(key, out var bucket) ? bucket.CountInactive : 0;

        public int GetBucketCount(string packageName)
        {
            int count = 0;
            foreach (var pair in _buckets)
                if (pair.Key.PackageName == packageName) count++;
            return count;
        }

        public int GetInactiveCountByPackage(string packageName)
        {
            int count = 0;
            foreach (var pair in _buckets)
                if (pair.Key.PackageName == packageName) count += pair.Value.CountInactive;
            return count;
        }

        public IReadOnlyList<GameObjectPoolBucketSnapshot> GetBucketSnapshots(string packageName = null)
        {
            var snapshots = new List<GameObjectPoolBucketSnapshot>();
            foreach (var pair in _buckets)
            {
                if (!string.IsNullOrEmpty(packageName) && pair.Key.PackageName != packageName)
                    continue;
                var bucket = pair.Value;
                snapshots.Add(new GameObjectPoolBucketSnapshot
                {
                    Key = pair.Key,
                    PackageName = pair.Key.PackageName,
                    Location = bucket.Location,
                    InactiveCount = bucket.CountInactive,
                    MaxInactiveCount = GetMaxInactiveCount(pair.Key),
                    RootExists = bucket.Root != null,
                    Warning = bucket.Root == null ? "RootMissing" : null
                });
            }
            return snapshots;
        }

        public IReadOnlyList<GameObjectPoolPackageSnapshot> GetPackageSnapshots()
        {
            var map = new Dictionary<string, GameObjectPoolPackageSnapshot>();
            foreach (var pair in _buckets)
            {
                string packageName = pair.Key.PackageName;
                if (!map.TryGetValue(packageName, out var snapshot))
                {
                    snapshot = new GameObjectPoolPackageSnapshot { PackageName = packageName };
                    map.Add(packageName, snapshot);
                }
                snapshot.BucketCount++;
                snapshot.InactiveCount += pair.Value.CountInactive;
            }
            return new List<GameObjectPoolPackageSnapshot>(map.Values);
        }

        private int ClearBucketObjects(GameObjectPoolBucket bucket)
        {
            int destroyed = 0;
            while (bucket.InactiveStack.Count > 0)
            {
                var obj = bucket.InactiveStack.Pop();
                if (obj != null)
                {
                    DestroyPooledObject(obj);
                    destroyed++;
                }
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            bucket.InactiveSet.Clear();
#endif
            return destroyed;
        }

        private void ApplyReleaseTransform(ResourceKey key, Transform target)
        {
            var options = GetOptions(key);
            if (options == null || !options.ResetTransformOnRelease)
                return;
            if (options.ResetLocalPosition) target.localPosition = options.DefaultLocalPosition;
            if (options.ResetLocalRotation) target.localRotation = options.DefaultLocalRotation;
            if (options.ResetLocalScale) target.localScale = options.DefaultLocalScale;
        }

        private void DestroyPooledObject(GameObject obj)
        {
            InvokeDestroy(obj);
            if(Application.isPlaying)Object.Destroy(obj);else Object.DestroyImmediate(obj);
        }

        private static void InvokeSpawn(GameObject obj)
        {
            var handlers = obj.GetComponentsInChildren<IPoolSpawnHandler>(true);
            for (int i = 0; i < handlers.Length; i++) handlers[i].OnSpawnFromPool();
        }

        private static void InvokeRecycle(GameObject obj)
        {
            var handlers = obj.GetComponentsInChildren<IPoolRecycleHandler>(true);
            for (int i = 0; i < handlers.Length; i++) handlers[i].OnRecycleToPool();
        }

        private static void InvokeDestroy(GameObject obj)
        {
            var handlers = obj.GetComponentsInChildren<IPoolDestroyHandler>(true);
            for (int i = 0; i < handlers.Length; i++) handlers[i].OnDestroyFromPool();
        }
    }
}
