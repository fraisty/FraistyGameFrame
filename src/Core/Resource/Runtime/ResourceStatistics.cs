namespace FRFrame
{
    /// <summary>
    /// 资源与池运行统计信息。
    /// 供调试、面板和诊断接口使用。
    /// </summary>
    public sealed class ResourceStatistics
    {
        public int LoadedAssetCount;
        public int LoadedSceneCount;
        public int LoadingAssetCount;
        public int ReferencePoolCachedCount;
        public int ObjectPoolCount;
        public int GameObjectPoolBucketCount;
        public int GameObjectPoolInactiveCount;

        public override string ToString()
        {
            return $"LoadedAssets:{LoadedAssetCount}, " +
                $"LoadedScenes:{LoadedSceneCount}, " +
                $"LoadingAssets:{LoadingAssetCount}, " +
                $"RefPoolCached:{ReferencePoolCachedCount}, " +
                $"ObjectPools:{ObjectPoolCount}, " +
                $"GOPoolBuckets:{GameObjectPoolBucketCount}, " +
                $"GOInactive:{GameObjectPoolInactiveCount}";
        }
    }
}
