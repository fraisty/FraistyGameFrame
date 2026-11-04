#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using HybridCLR;
using UnityEditor;
using UnityEngine;
using Unity.SharpZipLib.Utils;

namespace FRFrame.Editor
{
    public static class GamePackageBuildUtility
    {
        public const string GameManifestFileName = "game_manifest.json";
        public const string FileManifestFileName = "file_manifest.json";
        public const string PatchManifestFileName = "patch_manifest.json";
        private const string YooAssetPipelineOutputDirectoryName = "OutputCache";

        public static string BuildFullPackage(FRFrameBuildContext context)
        {
            GameBuildProfile profile = RequireGameProfile(context);
            GenerateHybridCLRIfNeeded(context, profile);
            string profileAssetPath = AssetDatabase.GetAssetPath(profile);
            string previousVersion = profile.packageVersion;
            profile.packageVersion = GamePackageVersion.Parse(profile.packageVersion).NextFull().ToString();

            return ExecuteWithVersionRollback(profile, previousVersion, context.Logger, () =>
            {
                string stageRoot = GetStageRoot(context, profile, profile.packageVersion);
                RecreateDirectory(stageRoot);

                string yooAssetSource = BuildAndGetYooAssetPackageDirectory(context, profile);
                CopyYooAssetPackage(yooAssetSource, Path.Combine(stageRoot, "YooAsset"), context.Logger);
                CopyRuntimeFiles(profile, Path.Combine(stageRoot, "RuntimeFiles"), context.Logger);
                CopyGameHotUpdateAssembly(context, profile, Path.Combine(stageRoot, "HotDll"));

                GamePackageBuildManifest manifest = CreateGameManifest(profile);
                manifest.files = CreateFileManifest(stageRoot);
                WriteJson(Path.Combine(stageRoot, GameManifestFileName), manifest);
                WriteJson(Path.Combine(stageRoot, FileManifestFileName), new GamePackageFileManifest { files = manifest.files });

                string outputDirectory = GetGameOutputDirectory(context, profile);
                Directory.CreateDirectory(outputDirectory);
                if (profile.clearHistoryFullPackages)
                {
                    DeleteHistoryFullPackageFiles(outputDirectory, profile, context.Logger);
                }

                string zipPath = Path.Combine(outputDirectory, ResolveFullPackageName(profile));
                DeleteFileIfExists(zipPath);
                ZipUtility.CompressFolderToZip(zipPath, string.Empty, stageRoot);
                WriteFullPackageRecord(context, profile, stageRoot, zipPath, manifest.files.Count);
                CleanupGeneratedYooAssetOutput(profile, yooAssetSource, context.Logger);
                context.Logger.Info($"[GameFullPackage] Output: {zipPath}");
                PersistPackageVersion(profileAssetPath, profile.packageVersion);
                AssetDatabase.Refresh();
                return zipPath;
            });
        }

        public static string BuildPatchPackage(FRFrameBuildContext context, string oldFileManifestPath)
        {
            if (string.IsNullOrEmpty(oldFileManifestPath) || !File.Exists(oldFileManifestPath))
            {
                throw new FileNotFoundException("Old file_manifest.json not found.", oldFileManifestPath);
            }

            GameBuildProfile profile = RequireGameProfile(context);
            GenerateHybridCLRIfNeeded(context, profile);
            string profileAssetPath = AssetDatabase.GetAssetPath(profile);
            string fromVersion = GetVersionFromManifestPath(oldFileManifestPath);
            GamePackageVersion oldVersion = GamePackageVersion.Parse(fromVersion);
            GamePackageVersion nextVersion = GamePackageVersion.Parse(profile.packageVersion).NextPatch();
            if (!oldVersion.IsSameMajor(nextVersion))
            {
                throw new InvalidOperationException($"游戏大版本已变化，不能构建补丁包：旧版本 {fromVersion}，当前版本 {nextVersion}。请构建当前游戏整包。");
            }

            string previousVersion = profile.packageVersion;
            profile.packageVersion = nextVersion.ToString();

            return ExecuteWithVersionRollback(profile, previousVersion, context.Logger, () =>
            {
                string fullStageRoot = GetStageRoot(context, profile, profile.packageVersion);
                RecreateDirectory(fullStageRoot);
                string yooAssetSource = BuildAndGetYooAssetPackageDirectory(context, profile);
                CopyYooAssetPackage(yooAssetSource, Path.Combine(fullStageRoot, "YooAsset"), context.Logger);
                CopyRuntimeFiles(profile, Path.Combine(fullStageRoot, "RuntimeFiles"), context.Logger);
                CopyGameHotUpdateAssembly(context, profile, Path.Combine(fullStageRoot, "HotDll"));

                GamePackageBuildManifest manifest = CreateGameManifest(profile);
                manifest.files = CreateFileManifest(fullStageRoot);
                WriteJson(Path.Combine(fullStageRoot, GameManifestFileName), manifest);
                WriteJson(Path.Combine(fullStageRoot, FileManifestFileName), new GamePackageFileManifest { files = manifest.files });

                GamePackageFileManifest oldManifest = JsonUtility.FromJson<GamePackageFileManifest>(File.ReadAllText(oldFileManifestPath));
                List<GamePackageFileRecord> newFiles = CreateFileManifest(fullStageRoot);
                Dictionary<string, GamePackageFileRecord> oldFiles = oldManifest.files.ToDictionary(file => file.path);
                Dictionary<string, GamePackageFileRecord> newFileMap = newFiles.ToDictionary(file => file.path);

                List<GamePackageFileRecord> changedFiles = newFiles
                    .Where(file => !oldFiles.TryGetValue(file.path, out GamePackageFileRecord oldFile) || oldFile.hash != file.hash || oldFile.size != file.size)
                    .ToList();
                List<string> deletedFiles = oldFiles.Keys.Where(path => !newFileMap.ContainsKey(path)).OrderBy(path => path).ToList();

                string patchRoot = GetPatchStageRoot(context, profile);
                RecreateDirectory(patchRoot);
                foreach (GamePackageFileRecord file in changedFiles)
                {
                    string source = Path.Combine(fullStageRoot, file.path);
                    string destination = Path.Combine(patchRoot, file.path);
                    Directory.CreateDirectory(Path.GetDirectoryName(destination));
                    File.Copy(source, destination, true);
                }

                GamePatchBuildManifest patchManifest = new GamePatchBuildManifest
                {
                    gameId = profile.gameId,
                    fromVersion = fromVersion,
                    toVersion = profile.packageVersion,
                    buildTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                    changedFiles = changedFiles,
                    deletedFiles = deletedFiles
                };
                WriteJson(Path.Combine(patchRoot, PatchManifestFileName), patchManifest);

                string outputDirectory = GetGameOutputDirectory(context, profile);
                Directory.CreateDirectory(outputDirectory);
                string zipPath = Path.Combine(outputDirectory, ResolvePatchPackageName(profile, patchManifest.fromVersion));
                DeleteFileIfExists(zipPath);
                ZipUtility.CompressFolderToZip(zipPath, string.Empty, patchRoot);
                WritePatchPackageRecord(context, profile, patchRoot, zipPath, oldFileManifestPath, patchManifest, newFiles.Count);
                CleanupGeneratedYooAssetOutput(profile, yooAssetSource, context.Logger);
                context.Logger.Info($"[GamePatchPackage] Output: {zipPath}");
                PersistPackageVersion(profileAssetPath, profile.packageVersion);
                AssetDatabase.Refresh();
                return zipPath;
            });
        }

        private static T ExecuteWithVersionRollback<T>(GameBuildProfile profile, string previousVersion, FRFrameBuildLogger logger, Func<T> buildAction)
        {
            try
            {
                return buildAction();
            }
            catch
            {
                profile.packageVersion = previousVersion;
                logger.Warning($"[GamePackage] Build failed; restored package version to {previousVersion}.");
                throw;
            }
        }

        private static void PersistPackageVersion(string profileAssetPath, string packageVersion)
        {
            GameBuildProfile persistedProfile = AssetDatabase.LoadAssetAtPath<GameBuildProfile>(profileAssetPath);
            if (persistedProfile == null)
            {
                throw new InvalidOperationException($"Failed to reload game build profile for version persistence: {profileAssetPath}");
            }

            persistedProfile.packageVersion = packageVersion;
            EditorUtility.SetDirty(persistedProfile);
            AssetDatabase.SaveAssets();
        }

        public static string GetStageRoot(FRFrameBuildContext context, GameBuildProfile profile, string version)
        {
            return Path.Combine(GetGameOutputDirectory(context, profile), "Stage", version);
        }

        private static GamePackageBuildManifest CreateGameManifest(GameBuildProfile profile)
        {
            return new GamePackageBuildManifest
            {
                gameId = profile.gameId,
                displayName = profile.displayName,
                packageName = profile.packageName,
                yooAssetPackageName = GetYooAssetPackageName(profile),
                version = profile.packageVersion,
                gameConfigLocation = ResolveGameConfigLocation(profile),
                hotUpdateAssemblyName = GetGameHotUpdateAssemblyFileName(profile),
                buildTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        private static string ResolveGameConfigLocation(GameBuildProfile profile)
        {
            if (!string.IsNullOrEmpty(profile.gameConfigLocation))
            {
                if (profile.gameConfigAsset == null)
                {
                    return profile.gameConfigLocation;
                }

                ValidateGameConfigAsset(profile.gameConfigAsset, profile);
                return profile.gameConfigLocation;
            }

            if (profile.gameConfigAsset == null)
            {
                throw new InvalidOperationException($"游戏运行时配置未设置：{profile.gameId}");
            }

            string assetPath = AssetDatabase.GetAssetPath(profile.gameConfigAsset);
            if (string.IsNullOrEmpty(assetPath))
            {
                throw new InvalidOperationException($"游戏运行时配置不是有效资产：{profile.gameId}");
            }

            ValidateGameConfigAsset(profile.gameConfigAsset, profile);
            return assetPath;
        }

        private static void ValidateGameConfigAsset(GamePackageConfig config, GameBuildProfile profile)
        {
            if (string.IsNullOrEmpty(config.entryScene))
            {
                throw new InvalidOperationException($"游戏运行时配置缺少入口场景：{profile.gameId}");
            }

            if (config.uiConfigGroup == null)
            {
                throw new InvalidOperationException($"游戏运行时配置缺少 UI 配置组：{profile.gameId}");
            }

            if (config.icon == null)
            {
                throw new InvalidOperationException($"游戏运行时配置缺少图标：{profile.gameId}");
            }

            if (config.backgroundVideo == null)
            {
                throw new InvalidOperationException($"游戏运行时配置缺少背景视频：{profile.gameId}");
            }

            ValidateAssetInPackage(config, config.uiConfigGroup, profile, "UI 配置组");
            ValidateAssetInPackage(config, config.icon, profile, "图标");
            ValidateAssetInPackage(config, config.backgroundVideo, profile, "背景视频");
        }

        private static void ValidateAssetInPackage(GamePackageConfig config, UnityEngine.Object asset, GameBuildProfile profile, string label)
        {
            string configPath = AssetDatabase.GetAssetPath(config).Replace('\\', '/');
            string assetPath = AssetDatabase.GetAssetPath(asset).Replace('\\', '/');
            string packageName = GetYooAssetPackageName(profile);
            if (!IsUnderGameRes(configPath, packageName) || !IsUnderGameRes(assetPath, packageName))
            {
                throw new InvalidOperationException($"游戏运行时配置的{label}必须位于 Assets/GameRes/{packageName}/ 下：{assetPath}");
            }
        }

        private static bool IsUnderGameRes(string assetPath, string packageName)
        {
            return !string.IsNullOrEmpty(assetPath)
                && !string.IsNullOrEmpty(packageName)
                && assetPath.StartsWith($"Assets/GameRes/{packageName}/", StringComparison.OrdinalIgnoreCase);
        }

        private static string BuildAndGetYooAssetPackageDirectory(FRFrameBuildContext context, GameBuildProfile profile)
        {
            string packageName = GetYooAssetPackageName(profile);
            string packageDirectory;
            if (!profile.buildYooAsset)
            {
                packageDirectory = YooAssetPackageHelper.GetPackageVersionDirectory(packageName, profile.packageVersion);
                YooAssetPackageHelper.EnsureBuiltinCatalog(packageName, packageDirectory, context.Logger);
            }
            else
            {
                packageDirectory = YooAssetPackageHelper.ExecuteBuildYooAssetPackage(context, profile);
            }

            if (string.IsNullOrEmpty(packageDirectory) || !Directory.Exists(packageDirectory))
            {
                throw new DirectoryNotFoundException($"YooAsset build output not found: {packageDirectory}");
            }

            return packageDirectory;
        }

        private static void CleanupGeneratedYooAssetOutput(GameBuildProfile profile, string packageDirectory, FRFrameBuildLogger logger)
        {
            if (profile == null || !profile.buildYooAsset || !profile.yooAssetClearBuildOutputAfterPackage || string.IsNullOrEmpty(packageDirectory))
            {
                return;
            }

            string fullPackageDirectory = Path.GetFullPath(packageDirectory);
            if (!Directory.Exists(fullPackageDirectory))
            {
                return;
            }

            string packageVersionName = Path.GetFileName(fullPackageDirectory);
            string packageNameDirectory = Path.GetDirectoryName(fullPackageDirectory);
            string platformDirectory = packageNameDirectory == null ? null : Path.GetDirectoryName(packageNameDirectory);
            if (string.IsNullOrEmpty(packageVersionName)
                || string.IsNullOrEmpty(packageNameDirectory)
                || string.IsNullOrEmpty(platformDirectory)
                || !string.Equals(Path.GetFileName(packageNameDirectory), GetYooAssetPackageName(profile), StringComparison.OrdinalIgnoreCase)
                || !string.Equals(Path.GetFileName(platformDirectory), YooAssetConfigs.GetPlatformName(), StringComparison.OrdinalIgnoreCase))
            {
                logger.Warning($"[GamePackage] Skip YooAsset output cleanup because path is not a package version directory: {fullPackageDirectory}");
                return;
            }

            Directory.Delete(fullPackageDirectory, true);
            logger.Info($"[GamePackage] Cleaned YooAsset version output: {fullPackageDirectory}");

            string pipelineOutputDirectory = Path.Combine(packageNameDirectory, YooAssetPipelineOutputDirectoryName);
            if (Directory.Exists(pipelineOutputDirectory))
            {
                Directory.Delete(pipelineOutputDirectory, true);
                logger.Info($"[GamePackage] Cleaned YooAsset pipeline output: {pipelineOutputDirectory}");
            }
        }

        private static void CopyYooAssetPackage(string source, string destinationRoot, FRFrameBuildLogger logger)
        {
            CopyDirectory(source, destinationRoot);
            logger.Info($"[GamePackage] YooAsset package copied: {source}");
        }

        private static void CopyRuntimeFiles(GameBuildProfile profile, string destinationRoot, FRFrameBuildLogger logger)
        {
            if (!string.IsNullOrEmpty(profile.runtimeFilesRoot))
            {
                string source = profile.runtimeFilesRoot;
                if (!Path.IsPathRooted(source))
                {
                    source = Path.GetFullPath(source);
                }

                if (Directory.Exists(source))
                {
                    CopyDirectory(source, destinationRoot);
                    logger.Info($"[GamePackage] RuntimeFiles copied: {source}");
                }
                else
                {
                    logger.Warning($"[GamePackage] RuntimeFiles root not found: {source}");
                }
            }
        }

        private static void GenerateHybridCLRIfNeeded(FRFrameBuildContext context, GameBuildProfile profile)
        {
            if (!profile.includeHotUpdateAssembly)
            {
                return;
            }

            context.Logger.Info($"[GamePackage] 编译最新 HybridCLR 热更 DLL: {context.BuildTarget}");
            HybridCLRBuildHelper.ExecuteCompileDllOnly(context.BuildTarget);
        }

        private static void CopyGameHotUpdateAssembly(FRFrameBuildContext context, GameBuildProfile profile, string destinationRoot)
        {
            if (!profile.includeHotUpdateAssembly)
            {
                return;
            }

            string assemblyBytesFileName = GetGameHotUpdateAssemblyFileName(profile);
            if (string.IsNullOrEmpty(assemblyBytesFileName))
            {
                return;
            }

            string assemblyDllFileName = assemblyBytesFileName.Substring(0, assemblyBytesFileName.Length - ".bytes".Length);
            string hotDllRoot = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(context.BuildTarget);
            string source = Path.Combine(hotDllRoot, assemblyDllFileName);
            if (!File.Exists(source))
            {
                throw new FileNotFoundException($"游戏热更 DLL 不存在，请先生成 HybridCLR：{source}", source);
            }

            Directory.CreateDirectory(destinationRoot);
            string destination = Path.Combine(destinationRoot, assemblyBytesFileName);
            File.Copy(source, destination, true);
            context.Logger.Info($"[GamePackage] Game hot update dll copied: {source} -> {destination}");
        }

        private static string GetGameHotUpdateAssemblyFileName(GameBuildProfile profile)
        {
            if (!profile.includeHotUpdateAssembly)
            {
                return string.Empty;
            }

            string assemblyName = string.IsNullOrEmpty(profile.hotUpdateAssemblyName) ? profile.gameId : profile.hotUpdateAssemblyName;
            return string.IsNullOrEmpty(assemblyName) ? string.Empty : EnsureDllExtension(assemblyName) + ".bytes";
        }

        private static string EnsureDllExtension(string assemblyName)
        {
            return assemblyName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? assemblyName : assemblyName + ".dll";
        }

        private static List<GamePackageFileRecord> CreateFileManifest(string root)
        {
            return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path) != FileManifestFileName)
                .Select(path => new GamePackageFileRecord
                {
                    path = ToRelativePath(root, path),
                    hash = ComputeHash(path),
                    size = new FileInfo(path).Length
                })
                .OrderBy(file => file.path)
                .ToList();
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
            return Uri.UnescapeDataString(rootUri.MakeRelativeUri(fileUri).ToString()).Replace('/', Path.DirectorySeparatorChar).Replace('\\', '/');
        }

        private static string AppendDirectorySeparatorChar(string path)
        {
            return path.EndsWith(Path.DirectorySeparatorChar.ToString()) ? path : path + Path.DirectorySeparatorChar;
        }

        private static void CopyDirectory(string source, string destination)
        {
            Directory.CreateDirectory(destination);
            foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(directory.Replace(source, destination));
            }

            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                string destinationFile = file.Replace(source, destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile));
                File.Copy(file, destinationFile, true);
            }
        }

        private static void RecreateDirectory(string path)
        {
            if (Directory.Exists(path))
            {
                Directory.Delete(path, true);
            }

            Directory.CreateDirectory(path);
        }

        private static void DeleteFileIfExists(string path)
        {
            if (File.Exists(path))
            {
                File.Delete(path);
            }
        }

        private static void DeleteHistoryFullPackageFiles(string outputDirectory, GameBuildProfile profile, FRFrameBuildLogger logger)
        {
            if (!Directory.Exists(outputDirectory))
            {
                return;
            }

            string prefix = ResolveFullPackageName(profile).Split(new[] { profile.packageVersion }, StringSplitOptions.None)[0];
            foreach (string file in Directory.GetFiles(outputDirectory, "*.zip", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                    logger.Info($"[GameFullPackage] History full package deleted: {file}");
                }
            }
        }

        private static void WriteJson(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(value, true));
        }

        private static void WriteFullPackageRecord(FRFrameBuildContext context, GameBuildProfile profile, string stageRoot, string zipPath, int fileCount)
        {
            GamePackageBuildRecord record = CreateBaseRecord(profile, "Full", stageRoot, zipPath);
            record.version = profile.packageVersion;
            record.toVersion = profile.packageVersion;
            record.totalFileCount = fileCount;
            WriteBuildRecord(context, profile, record);
            WriteJson(Path.Combine(stageRoot, "build_info.json"), record);
        }

        private static void WritePatchPackageRecord(
            FRFrameBuildContext context,
            GameBuildProfile profile,
            string patchRoot,
            string zipPath,
            string oldFileManifestPath,
            GamePatchBuildManifest patchManifest,
            int totalFileCount)
        {
            GamePackageBuildRecord record = CreateBaseRecord(profile, "Patch", patchRoot, zipPath);
            record.version = profile.packageVersion;
            record.fromVersion = patchManifest.fromVersion;
            record.toVersion = patchManifest.toVersion;
            record.oldFileManifestPath = oldFileManifestPath;
            record.totalFileCount = totalFileCount;
            record.changedFileCount = patchManifest.changedFiles.Count;
            record.deletedFileCount = patchManifest.deletedFiles.Count;
            WriteBuildRecord(context, profile, record);
            WriteJson(Path.Combine(patchRoot, "build_info.json"), record);
        }

        private static GamePackageBuildRecord CreateBaseRecord(GameBuildProfile profile, string buildType, string stageRoot, string zipPath)
        {
            return new GamePackageBuildRecord
            {
                gameId = profile.gameId,
                displayName = profile.displayName,
                packageName = profile.packageName,
                yooAssetPackageName = GetYooAssetPackageName(profile),
                buildType = buildType,
                buildTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                zipPath = zipPath,
                stageRoot = stageRoot
            };
        }

        private static void WriteBuildRecord(FRFrameBuildContext context, GameBuildProfile profile, GamePackageBuildRecord record)
        {
            string historyPath = Path.Combine(context.OutputRoot, "BuildRecords", "Games", string.IsNullOrEmpty(profile.gameId) ? GetYooAssetPackageName(profile) : profile.gameId, "build_history.json");
            GamePackageBuildHistory history = File.Exists(historyPath)
                ? JsonUtility.FromJson<GamePackageBuildHistory>(File.ReadAllText(historyPath))
                : new GamePackageBuildHistory();
            history ??= new GamePackageBuildHistory();
            history.records ??= new List<GamePackageBuildRecord>();
            history.records.Add(record);
            WriteJson(historyPath, history);
        }

        private static string GetGameOutputDirectory(FRFrameBuildContext context, GameBuildProfile profile)
        {
            string outputRoot = string.IsNullOrEmpty(profile.outputRoot) ? context.OutputRoot : profile.outputRoot;
            return Path.Combine(outputRoot, string.IsNullOrEmpty(profile.gameId) ? GetYooAssetPackageName(profile) : profile.gameId);
        }

        private static string GetPatchStageRoot(FRFrameBuildContext context, GameBuildProfile profile)
        {
            return Path.Combine(GetGameOutputDirectory(context, profile), "PatchStage", profile.packageVersion);
        }

        private static string ResolveFullPackageName(GameBuildProfile profile)
        {
            return profile.fullPackageNamePattern
                .Replace("{GameId}", profile.gameId)
                .Replace("{Version}", profile.packageVersion);
        }

        private static string ResolvePatchPackageName(GameBuildProfile profile, string fromVersion)
        {
            return profile.patchPackageNamePattern
                .Replace("{GameId}", profile.gameId)
                .Replace("{FromVersion}", fromVersion)
                .Replace("{ToVersion}", profile.packageVersion);
        }

        private static string GetYooAssetPackageName(GameBuildProfile profile)
        {
            if (!string.IsNullOrEmpty(profile.yooAssetPackageName))
            {
                return profile.yooAssetPackageName;
            }

            return profile.packageName;
        }

        private static string GetVersionFromManifestPath(string manifestPath)
        {
            string directoryPath = Path.GetDirectoryName(manifestPath);
            string gameManifestPath = Path.Combine(directoryPath, GameManifestFileName);
            if (File.Exists(gameManifestPath))
            {
                GamePackageBuildManifest manifest = JsonUtility.FromJson<GamePackageBuildManifest>(File.ReadAllText(gameManifestPath));
                if (manifest != null && GamePackageVersion.TryParse(manifest.version, out _))
                {
                    return manifest.version;
                }
            }

            DirectoryInfo directory = new DirectoryInfo(directoryPath);
            return directory.Name;
        }

        private static GameBuildProfile RequireGameProfile(FRFrameBuildContext context)
        {
            if (context.GameProfile == null)
            {
                throw new InvalidOperationException("Game Build Profile is required.");
            }

            if (string.IsNullOrEmpty(GetYooAssetPackageName(context.GameProfile)))
            {
                throw new InvalidOperationException("Game Build Profile packageName or yooAssetPackageName is required.");
            }

            return context.GameProfile;
        }

        [Serializable]
        private sealed class GamePackageFileManifest
        {
            public List<GamePackageFileRecord> files = new List<GamePackageFileRecord>();
        }
    }
}
#endif
