namespace FRFrame
{
    public sealed class PoolCleanupOptions
    {
        public bool IncludeReferencePool = true;
        public bool IncludeObjectPool = true;
        public bool IncludeGameObjectPool = true;
        public string PackageName;
        public float ReferenceKeepPercent = 1f;
        public int GameObjectMaxRemoveCount = 0;
    }
}
