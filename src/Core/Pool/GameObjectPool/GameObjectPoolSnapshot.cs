namespace FRFrame
{
    public sealed class GameObjectPoolBucketSnapshot
    {
        public ResourceKey Key;
        public string PackageName;
        public string Location;
        public int InactiveCount;
        public int MaxInactiveCount;
        public bool RootExists;
        public string Warning;
    }

    public sealed class GameObjectPoolPackageSnapshot
    {
        public string PackageName;
        public int BucketCount;
        public int InactiveCount;
    }
}
