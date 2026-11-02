namespace FRFrame
{
    public sealed class PackageDownloadEventArgs
    {
        public string PackageName;
        public PackageDownloadProgress Progress;
        public PackageDownloadResult Result;
    }
}
