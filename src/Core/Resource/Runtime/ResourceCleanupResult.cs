namespace FRFrame
{
    public sealed class ResourceCleanupResult
    {
        public int RemovedAssetCount;
        public int ClearedPoolBucketCount;
        public bool CalledUnloadUnusedAssets;
    }
}
