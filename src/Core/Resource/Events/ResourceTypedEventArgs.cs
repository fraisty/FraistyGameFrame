using UnityEngine;
using Object = UnityEngine.Object;

namespace FRFrame
{
    public sealed class AssetLoadEventArgs
    {
        public ResourceKey Key;
        public string Location;
        public string PackageName;
        public string Error;
        public Object AssetObject;
    }

    public sealed class AssetReleaseEventArgs
    {
        public ResourceKey Key;
        public string Location;
        public string PackageName;
    }

    public sealed class SceneLoadEventArgs
    {
        public ResourceKey Key;
        public string Location;
        public string PackageName;
        public string Error;
    }

    public sealed class ResourcePoolEventArgs
    {
        public ResourceKey Key;
        public string Location;
        public string PackageName;
        public GameObject Instance;
    }
}
