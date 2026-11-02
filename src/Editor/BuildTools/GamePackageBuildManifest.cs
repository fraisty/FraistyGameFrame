#if UNITY_EDITOR
using System;
using System.Collections.Generic;

namespace FRFrame.Editor
{
    [Serializable]
    public sealed class GamePackageBuildManifest
    {
        public string gameId;
        public string displayName;
        public string packageName;
        public string yooAssetPackageName;
        public string version;
        public string gameConfigLocation;
        public string hotUpdateAssemblyName;
        public string buildTime;
        public List<GamePackageFileRecord> files = new List<GamePackageFileRecord>();
    }

    [Serializable]
    public sealed class GamePackageFileRecord
    {
        public string path;
        public string hash;
        public long size;
    }

    [Serializable]
    public sealed class GamePatchBuildManifest
    {
        public string gameId;
        public string fromVersion;
        public string toVersion;
        public string buildTime;
        public List<GamePackageFileRecord> changedFiles = new List<GamePackageFileRecord>();
        public List<string> deletedFiles = new List<string>();
    }

    [Serializable]
    public sealed class GamePackageBuildRecord
    {
        public string gameId;
        public string displayName;
        public string packageName;
        public string yooAssetPackageName;
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
    public sealed class GamePackageBuildHistory
    {
        public List<GamePackageBuildRecord> records = new List<GamePackageBuildRecord>();
    }
}
#endif
