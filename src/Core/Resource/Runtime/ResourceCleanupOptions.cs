namespace FRFrame
{
    public sealed class ResourceCleanupOptions
    {
        public string PackageName;
        public ResourceResidency? Residency;
        public float MinUnusedSeconds = 0f;
        public int MaxRemoveCount = 0;
        public bool IncludeResident = false;
        public bool UnloadUnusedAssets = true;
        public float UnloadUnusedAssetsMinInterval = 5f;
    }
}
