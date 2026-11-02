using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace FRFrame
{
    public sealed class PoolManager : IManagerBase
    {
        public override int Priority => 1;

        public ReferencePoolModule ReferencePool { get; private set; }
        public ObjectPoolModule ObjectPool { get; private set; }
        public GameObjectPoolModule GameObjectPool { get; private set; }
        public bool EnableDuplicateReleaseCheck
        {
            get => GameObjectPool != null && GameObjectPool.EnableDuplicateReleaseCheck;
            set
            {
                if (GameObjectPool != null)
                    GameObjectPool.EnableDuplicateReleaseCheck = value;
            }
        }

        private readonly Dictionary<string, List<string>> _poolGroups = new Dictionary<string, List<string>>();
        private GameObject _poolRootGo;

        public override UniTask InitializeAsync()
        {
            ReferencePool = new ReferencePoolModule();
            ObjectPool = new ObjectPoolModule();
            GameObjectPool = new GameObjectPoolModule();

            _poolRootGo = new GameObject("[FRFrame_Pools]");
            Object.DontDestroyOnLoad(_poolRootGo);
            GameObjectPool.SetRoot(_poolRootGo.transform);

            FRLog.Info("PoolManager initialized.");
            return UniTask.CompletedTask;
        }

        public override void Destroy()
        {
            GameObjectPool?.ClearAll();
            ReferencePool?.ClearAll();
            ObjectPool?.ClearAll();
            _poolGroups.Clear();

            if (_poolRootGo != null)
            {
                Object.Destroy(_poolRootGo);
                _poolRootGo = null;
            }

            FRLog.Info("PoolManager destroyed.");
        }

        public PoolCleanupResult TrimAll(PoolCleanupOptions options = null)
        {
            options ??= new PoolCleanupOptions();
            var result = new PoolCleanupResult();

            if (options.IncludeReferencePool && ReferencePool != null)
                result.ReferenceRemovedCount = ReferencePool.TrimAll(options.ReferenceKeepPercent);

            if (options.IncludeObjectPool && ObjectPool != null)
            {
                result.ObjectPoolClearedCount = ObjectPool.GetPoolCount();
                ObjectPool.ClearAll();
            }

            if (options.IncludeGameObjectPool && GameObjectPool != null)
            {
                if (!string.IsNullOrEmpty(options.PackageName))
                {
                    result.GameObjectBucketClearedCount = GameObjectPool.GetBucketCount(options.PackageName);
                    result.GameObjectDestroyedCount = GameObjectPool.ClearPackage(options.PackageName);
                }
                else
                {
                    result.GameObjectDestroyedCount = GameObjectPool.Trim(options.GameObjectMaxRemoveCount);
                }
            }

            return result;
        }

        public void ClearPackage(string packageName)
        {
            GameObjectPool?.ClearPackage(packageName);
        }

        public void ClearSceneScopedPools()
        {
            GameObjectPool?.Trim();
        }

        public void RegisterPoolGroup(string groupName, string poolName)
        {
            if (string.IsNullOrEmpty(groupName) || string.IsNullOrEmpty(poolName))
                return;

            if (!_poolGroups.TryGetValue(groupName, out var pools))
            {
                pools = new List<string>();
                _poolGroups.Add(groupName, pools);
            }

            if (!pools.Contains(poolName))
                pools.Add(poolName);
        }

        public bool ClearPoolGroup(string groupName)
        {
            if (string.IsNullOrEmpty(groupName) || !_poolGroups.TryGetValue(groupName, out var pools))
                return false;

            for (int i = 0; i < pools.Count; i++)
                ObjectPool?.ClearPool(pools[i]);

            _poolGroups.Remove(groupName);
            return true;
        }
    }
}
