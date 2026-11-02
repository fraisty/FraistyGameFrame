#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Editor
{
    public static class BootstrapConfigExporter
    {
        private const string HotFixDirectory = "Assets/StreamingAssets/HotFix";
        private const string LegacyGameCatalogSeedFile = "game_catalog_seed.json";

        public static string StreamingBootstrapRoot => Path.Combine(Application.streamingAssetsPath, BootstrapFileNames.DirectoryName);

        public static void Export(FRFrameBuildContext context)
        {
            Directory.CreateDirectory(StreamingBootstrapRoot);
            WriteYooAssetPackages(context);
            WriteUIBootstrap(context);
            DeleteLegacyGameCatalogSeed();
            WriteHotfixManifest();
            WriteBootstrapManifest(context);
            AssetDatabase.Refresh();
            context.Logger.Info($"[Bootstrap] 已导出启动配置：{StreamingBootstrapRoot}");
        }

        private static void WriteYooAssetPackages(FRFrameBuildContext context)
        {
            YooAssetPackagesConfig config = ScriptableObject.CreateInstance<YooAssetPackagesConfig>();
            config.packages.Clear();
            config.packages.Add(new PackageConfig
            {
                name = string.IsNullOrEmpty(context.BuildProfile.defaultYooAssetPackageName) ? "DefaultPackage" : context.BuildProfile.defaultYooAssetPackageName,
                playMode = YooAsset.EPlayMode.OfflinePlayMode,
                isDefaultPackage = true
            });

            List<GameBuildProfile> profiles = context.BuildProfile.selectedGameProfiles;
            if (profiles != null)
            {
                for (int i = 0; i < profiles.Count; i++)
                {
                    GameBuildProfile profile = profiles[i];
                    string packageName = profile == null ? string.Empty : GetPackageName(profile);
                    if (string.IsNullOrEmpty(packageName) || packageName == config.packages[0].name)
                    {
                        continue;
                    }

                    config.packages.Add(new PackageConfig
                    {
                        name = packageName,
                        playMode = YooAsset.EPlayMode.OfflinePlayMode,
                        isDefaultPackage = false
                    });
                }
            }

            File.WriteAllText(Path.Combine(StreamingBootstrapRoot, BootstrapFileNames.YooAssetPackagesFile), JsonUtility.ToJson(config, true));
        }

        private static void WriteUIBootstrap(FRFrameBuildContext context)
        {
            UIBootstrapConfig config = new UIBootstrapConfig();
            File.WriteAllText(Path.Combine(StreamingBootstrapRoot, BootstrapFileNames.UIBootstrapFile), JsonUtility.ToJson(config, true));
        }

        private static void DeleteLegacyGameCatalogSeed()
        {
            string path = Path.Combine(StreamingBootstrapRoot, LegacyGameCatalogSeedFile);
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static void WriteHotfixManifest()
        {
            HotfixBootstrapManifest manifest = new HotfixBootstrapManifest
            {
                version = ReadTextIfExists(Path.Combine(HotFixDirectory, "version.txt")),
                buildTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                aotFiles = CreateRecords(Path.Combine(HotFixDirectory, "AOTMeta"), "AOTMeta"),
                hotUpdateFiles = CreateRecords(Path.Combine(HotFixDirectory, "HotDll"), "HotDll")
            };

            File.WriteAllText(Path.Combine(StreamingBootstrapRoot, BootstrapFileNames.HotfixManifestFile), JsonUtility.ToJson(manifest, true));
        }

        private static void WriteBootstrapManifest(FRFrameBuildContext context)
        {
            BootstrapManifest manifest = new BootstrapManifest
            {
                version = context.BuildProfile.packageVersion,
                buildTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };

            File.WriteAllText(Path.Combine(StreamingBootstrapRoot, BootstrapFileNames.ManifestFile), JsonUtility.ToJson(manifest, true));
        }

        private static List<HotfixFileRecord> CreateRecords(string root, string relativeRoot)
        {
            List<HotfixFileRecord> records = new List<HotfixFileRecord>();
            if (!Directory.Exists(root))
            {
                return records;
            }

            string[] files = Directory.GetFiles(root, "*", SearchOption.AllDirectories);
            Array.Sort(files, StringComparer.Ordinal);
            for (int i = 0; i < files.Length; i++)
            {
                string path = files[i];
                records.Add(new HotfixFileRecord
                {
                    path = Path.Combine(relativeRoot, ToRelativePath(root, path)).Replace('\\', '/'),
                    size = new FileInfo(path).Length,
                    hash = ComputeHash(path)
                });
            }

            return records;
        }

        private static string GetPackageName(GameBuildProfile profile)
        {
            return string.IsNullOrEmpty(profile.yooAssetPackageName) ? profile.packageName : profile.yooAssetPackageName;
        }

        private static string ReadTextIfExists(string path)
        {
            return File.Exists(path) ? File.ReadAllText(path).Trim() : string.Empty;
        }

        private static string ComputeHash(string path)
        {
            using (FileStream stream = File.OpenRead(path))
            using (SHA256 sha = SHA256.Create())
            {
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
            }
        }

        private static string ToRelativePath(string root, string path)
        {
            Uri rootUri = new Uri(AppendDirectorySeparatorChar(Path.GetFullPath(root)));
            Uri fileUri = new Uri(Path.GetFullPath(path));
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fileUri).ToString()).Replace('/', Path.DirectorySeparatorChar);
        }

        private static string AppendDirectorySeparatorChar(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString()) ? path : path + Path.DirectorySeparatorChar;
        }
    }
}
#endif
