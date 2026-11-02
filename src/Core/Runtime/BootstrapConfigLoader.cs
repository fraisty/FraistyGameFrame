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

        public static async UniTask<string> LoadTextAsync(string fileName)
        {
            string persistentPath = Path.Combine(PersistentRoot, fileName);
            if (File.Exists(persistentPath))
            {
                return await UniTask.RunOnThreadPool(() => File.ReadAllText(persistentPath));
            }

            string legacyBootstrapPath = Path.Combine(LegacyPersistentRoot, fileName);
            if (File.Exists(legacyBootstrapPath))
            {
                return await UniTask.RunOnThreadPool(() => File.ReadAllText(legacyBootstrapPath));
            }

            string streamingPath = Path.Combine(Application.streamingAssetsPath, BootstrapFileNames.DirectoryName, fileName);
            return await ReadStreamingAssetsTextAsync(streamingPath, true);
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

        private static async UniTask<string> ReadStreamingAssetsTextAsync(string path, bool logMissing)
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            using UnityWebRequest request = UnityWebRequest.Get(path);
            await request.SendWebRequest().ToUniTask();

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (logMissing)
                {
                    FRLog.Error($"Read StreamingAssets failed : {path}, error : {request.error}");
                }
                return null;
            }

            return request.downloadHandler.text;
#elif UNITY_WEBGL && !UNITY_EDITOR
            using UnityWebRequest request = UnityWebRequest.Get(path);
            await request.SendWebRequest().ToUniTask();

            if (request.result != UnityWebRequest.Result.Success)
            {
                if (logMissing)
                {
                    FRLog.Error($"Read StreamingAssets failed : {path}, error : {request.error}");
                }
                return null;
            }

            return request.downloadHandler.text;
#else
            if (!File.Exists(path))
            {
                if (logMissing)
                {
                    FRLog.Error($"StreamingAssets file not found : {path}");
                }
                return null;
            }

            return await UniTask.RunOnThreadPool(() => File.ReadAllText(path));
#endif
        }
    }
}
