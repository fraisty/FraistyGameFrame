namespace FRFrame
{
    public sealed class PackageDownloadProgress
    {
        public string PackageName;
        public int TotalDownloadCount;
        public int CurrentDownloadCount;
        public long TotalDownloadBytes;
        public long CurrentDownloadBytes;
        public float Progress;
    }
}
