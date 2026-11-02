namespace FRFrame
{
    public enum PackageRuntimeState
    {
        None,
        Created,
        Initializing,
        Initialized,
        Failed
    }

    public sealed class PackageRuntimeInfo
    {
        public string PackageName;
        public bool IsDefaultPackage;
        public PackageRuntimeState State;
        public string Version;
        public string LastError;
    }
}
