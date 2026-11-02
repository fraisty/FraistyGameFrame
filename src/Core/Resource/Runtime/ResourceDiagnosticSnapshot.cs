using System.Collections.Generic;

namespace FRFrame
{
    public sealed class ResourceDiagnosticSnapshot
    {
        public ResourceStatistics Statistics;
        public List<PackageResourceSnapshot> Packages = new List<PackageResourceSnapshot>();
        public List<AssetEntrySnapshot> Assets = new List<AssetEntrySnapshot>();
        public List<SceneEntrySnapshot> Scenes = new List<SceneEntrySnapshot>();
        public List<string> Warnings = new List<string>();
    }

    public sealed class AssetEntrySnapshot
    {
        public ResourceKey Key;
        public string PackageName;
        public string Location;
        public int RefCount;
        public ResourceResidency Residency;
        public ResourceEntryState State;
        public float LastUseTime;
        public bool HandleValid;
        public bool IsResident;
    }

    public sealed class SceneEntrySnapshot
    {
        public ResourceKey Key;
        public string PackageName;
        public string Location;
        public int RefCount;
        public ResourceResidency Residency;
        public ResourceEntryState State;
        public float LastUseTime;
        public bool HandleValid;
        public bool IsMainScene;
    }

    public sealed class PackageResourceSnapshot
    {
        public string PackageName;
        public int AssetCount;
        public int SceneCount;
        public int LoadingAssetCount;
    }
}
