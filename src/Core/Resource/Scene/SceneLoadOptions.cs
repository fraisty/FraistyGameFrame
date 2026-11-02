using System.Threading;
using UnityEngine.SceneManagement;

namespace FRFrame
{
    public sealed class SceneLoadOptions
    {
        public string PackageName;
        public LoadSceneMode LoadSceneMode = LoadSceneMode.Single;
        public bool ActivateOnLoad = true;
        public bool IsMainScene = true;
        public ResourceResidency Residency = ResourceResidency.Normal;
        public bool CleanupNormalAssetsAfterLoad = true;
        public bool CleanupSceneScopedAssetsAfterLoad = true;
        public CancellationToken CancellationToken;
    }
}
