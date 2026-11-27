using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Networking;

namespace FRFrame
{
    public static class BootstrapConfigLoader
    {
        public static string PersistentRoot => PathExtension.GetMainPackageBootstrapRootPath();
        public static string LegacyPersistentRoot => Path.Combine(PathExtension.GetPersistentDataPath(), BootstrapFileNames.DirectoryName);
        public static string PersistentManifestPath => Path.Combine(PersistentRoot, BootstrapFileNames.ManifestFile);

        public static UniTask<string> LoadTextAsync(string fileName)
        {
            string persistentPath = Path.Combine(PersistentRoot, fileName);
            if (File.Exists(persistentPath))
            {
                return UniTask.FromResult(File.ReadAllText(persistentPath));
            }

            string legacyBootstrapPath = Path.Combine(LegacyPersistentRoot, fileName);
            if (File.Exists(legacyBootstrapPath))
            {
                return UniTask.FromResult(File.ReadAllText(legacyBootstrapPath));
            }

            string streamingPath = Path.Combine(Application.streamingAssetsPath, BootstrapFileNames.DirectoryName, fileName);
#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_WEBGL)
            return ReadStreamingAssetsTextAsync(streamingPath);
#else
            if (!File.Exists(streamingPath))
            {
                FRLog.Error($"StreamingAssets file not found : {streamingPath}");
                return UniTask.FromResult<string>(null);
            }

            return UniTask.FromResult(File.ReadAllText(streamingPath));
#endif
        }

        public static void SaveText(string fileName, string json)
        {
            Directory.CreateDirectory(PersistentRoot);
            File.WriteAllText(Path.Combine(PersistentRoot, fileName), json);
        }

        public static async UniTask<UIBootstrapConfig> LoadUIBootstrapAsync()
        {
            string json = await LoadTextAsync(BootstrapFileNames.UIBootstrapFile);
            if (string.IsNullOrEmpty(json))
            {
                return null;
            }

            return JsonUtility.FromJson<UIBootstrapConfig>(json);
        }

#if !UNITY_EDITOR && (UNITY_ANDROID || UNITY_WEBGL)
        private static async UniTask<string> ReadStreamingAssetsTextAsync(string path)
        {
            using UnityWebRequest request = UnityWebRequest.Get(path);
            await request.SendWebRequest().ToUniTask();

            if (request.result != UnityWebRequest.Result.Success)
            {
                FRLog.Error($"Read StreamingAssets failed : {path}, error : {request.error}");
                return null;
            }

            return request.downloadHandler.text;
        }
#endif
    }
}
