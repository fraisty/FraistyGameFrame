using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace FRFrame
{
    public static class YooAssetPackagesJsonLoader
    {
        public static string PersistentPath => Path.Combine(BootstrapConfigLoader.PersistentRoot, BootstrapFileNames.YooAssetPackagesFile);

        public static async UniTask<YooAssetPackagesConfig> LoadAsync()
        {
            string json = await BootstrapConfigLoader.LoadTextAsync(BootstrapFileNames.YooAssetPackagesFile);
            if (string.IsNullOrEmpty(json))
            {
                FRLog.Error($"Load YooAssetPackagesConfig json failed : {PersistentPath}");
                return null;
            }

            YooAssetPackagesConfig config = ScriptableObject.CreateInstance<YooAssetPackagesConfig>();
            JsonUtility.FromJsonOverwrite(json, config);

            return config;
        }

        public static void Save(YooAssetPackagesConfig config)
        {
            if (config == null)
            {
                return;
            }

            BootstrapConfigLoader.SaveText(BootstrapFileNames.YooAssetPackagesFile, JsonUtility.ToJson(config, true));
        }
    }
}
