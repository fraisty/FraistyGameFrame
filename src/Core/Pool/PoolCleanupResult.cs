namespace FRFrame
{
    public sealed class PoolCleanupResult
    {
        public int ReferenceRemovedCount;
        public int ObjectPoolClearedCount;
        public int GameObjectBucketClearedCount;
        public int GameObjectDestroyedCount;
    }
}
