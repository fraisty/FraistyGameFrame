namespace FRFrame
{
    public sealed class UIPreloadInfo
    {
        public string FormId;
        public UIPreloadMode Mode;
        public bool AssetPreloaded;
        public bool InstanceWarmed;
        public string PackageName;
        public string AssetPath;
        public float LastPreloadTime;
    }
}
