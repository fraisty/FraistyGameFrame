#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace FRFrame.Editor
{
    [Serializable]
    public sealed class MainPackageBuildManifest
    {
        public string version;
        public string buildTime;
        public List<MainPackageFileRecord> files = new List<MainPackageFileRecord>();
    }

    [Serializable]
    public sealed class MainPackageFileManifest
    {
        public List<MainPackageFileRecord> files = new List<MainPackageFileRecord>();
    }

    [Serializable]
    public sealed class MainPackageFileRecord
    {
        public string path;
        public string hash;
        public long size;
    }

    [Serializable]
    public sealed class MainPatchBuildManifest
    {
        public string fromVersion;
        public string toVersion;
        public string buildTime;
        public List<MainPackageFileRecord> changedFiles = new List<MainPackageFileRecord>();
        public List<string> deletedFiles = new List<string>();
    }

    [Serializable]
    public sealed class PublishPackageBuildManifest
    {
        public string version;
        public string buildTime;
        public string mainPackageZip;
        public List<string> gamePackageZips = new List<string>();
    }

    [Serializable]
    public sealed class MainPackageBuildRecord
    {
        public string buildType;
        public string version;
        public string fromVersion;
        public string toVersion;
        public string buildTime;
        public string zipPath;
        public string stageRoot;
        public string oldFileManifestPath;
        public int totalFileCount;
        public int changedFileCount;
        public int deletedFileCount;
    }

    [Serializable]
    public sealed class MainPackageBuildHistory
    {
        public List<MainPackageBuildRecord> records = new List<MainPackageBuildRecord>();
    }
}
#endif
