using System;
using System.Collections.Generic;

namespace FRFrame
{
    public sealed class ObjectPoolModule
    {
        private readonly Dictionary<string, IObjectPoolBase> _pools = new Dictionary<string, IObjectPoolBase>();

        public IObjectPool<T> CreatePool<T>(
            string name,
            Func<T> factory,
            Action<T> onGet = null,
            Action<T> onRelease = null,
            Action<T> onDestroy = null,
            int maxCount = 0) where T : class
        {
            if (string.IsNullOrEmpty(name))
            {
                FRLog.Error("CreatePool failed, pool name is null or empty.");
                return null;
            }

            if (_pools.TryGetValue(name, out var existing))
            {
                var typedPool = existing as IObjectPool<T>;
                if (typedPool == null)
                {
                    FRLog.Error($"CreatePool failed, pool name already exists with different type : {name}");
                }
                return typedPool;
            }

            var pool = new ObjectPool<T>(name, factory, onGet, onRelease, onDestroy, maxCount);
            _pools[name] = pool;
            return pool;
        }

        public IObjectPool<T> CreatePool<T>(
            ObjectPoolKey<T> key,
            Func<T> factory,
            Action<T> onGet = null,
            Action<T> onRelease = null,
            Action<T> onDestroy = null,
            int maxCount = 0) where T : class
        {
            return CreatePool(key.Name, factory, onGet, onRelease, onDestroy, maxCount);
        }

        public IObjectPool<T> GetPool<T>(string name) where T : class
        {
            if (string.IsNullOrEmpty(name))
                return null;

            if (_pools.TryGetValue(name, out var pool))
            {
                var typedPool = pool as IObjectPool<T>;
                if (typedPool == null)
                {
                    FRLog.Error($"GetPool failed, pool type mismatch : {name}");
                }
                return typedPool;
            }

            return null;
        }

        public IObjectPool<T> GetPool<T>(ObjectPoolKey<T> key) where T : class
        {
            return GetPool<T>(key.Name);
        }

        public IObjectPool<T> GetOrCreatePool<T>(Func<T> factory, int maxCount = 0) where T : class
        {
            return CreatePool(typeof(T).FullName, factory, maxCount: maxCount);
        }

        public bool HasPool(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            return _pools.ContainsKey(name);
        }

        public bool ClearPool(string name, bool remove = true)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            if (!_pools.TryGetValue(name, out var pool))
                return false;

            pool.Clear();

            if (remove)
                _pools.Remove(name);

            return true;
        }

        public bool ClearPool<T>(ObjectPoolKey<T> key, bool remove = true) where T : class
        {
            return ClearPool(key.Name, remove);
        }

        public bool RemovePoolOnly(string name)
        {
            if (string.IsNullOrEmpty(name))
                return false;

            return _pools.Remove(name);
        }

        public void ClearAll()
        {
            foreach (var pair in _pools)
            {
                pair.Value.Clear();
            }

            _pools.Clear();
        }

        public int GetPoolCount()
        {
            return _pools.Count;
        }

        public int GetInactiveCount(string name)
        {
            if (string.IsNullOrEmpty(name))
                return 0;

            if (_pools.TryGetValue(name, out var pool))
                return pool.CountInactive;

            return 0;
        }

        public IReadOnlyList<ObjectPoolSnapshot> GetSnapshots()
        {
            var snapshots = new List<ObjectPoolSnapshot>(_pools.Count);
            foreach (var pair in _pools)
            {
                var pool = pair.Value;
                snapshots.Add(new ObjectPoolSnapshot
                {
                    Name = pool.Name,
                    ObjectTypeName = pool.ObjectTypeName,
                    CountAll = pool.CountAll,
                    CountActive = pool.CountActive,
                    CountInactive = pool.CountInactive,
                    TotalCreated = pool.TotalCreated,
                    TotalAcquired = pool.TotalAcquired,
                    TotalReleased = pool.TotalReleased,
                    HitCount = pool.HitCount,
                    MissCount = pool.MissCount
                });
            }

            return snapshots;
        }
    }
}
