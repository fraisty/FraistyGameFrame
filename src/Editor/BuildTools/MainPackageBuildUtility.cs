#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using Unity.SharpZipLib.Utils;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Editor
{
    public static class MainPackageBuildUtility
    {
        public const string MainManifestFileName = "main_manifest.json";
        public const string FileManifestFileName = "file_manifest.json";
        public const string PatchManifestFileName = "main_patch_manifest.json";
        public const string PublishManifestFileName = "publish_manifest.json";

        public static string BuildPublishPackage(FRFrameBuildContext context)
        {
            FRFrameBuildProfile profile = RequireBuildProfile(context);
            if (string.IsNullOrEmpty(profile.publishMainPackageZipPath) || !File.Exists(profile.publishMainPackageZipPath))
            {
                throw new FileNotFoundException("发布聚合包需要先选择主包 zip。", profile.publishMainPackageZipPath);
            }

            string version = GamePackageVersion.Parse(profile.packageVersion).ToString();
            string stageRoot = Path.Combine(GetPublishOutputDirectory(context, profile), "Stage", version);
            RecreateDirectory(stageRoot);

            string mainZipName = Path.GetFileName(profile.publishMainPackageZipPath);
            string mainZipStagePath = Path.Combine(stageRoot, "MainPackage", mainZipName);
            Directory.CreateDirectory(Path.GetDirectoryName(mainZipStagePath));
            File.Copy(profile.publishMainPackageZipPath, mainZipStagePath, true);

            PublishPackageBuildManifest manifest = new PublishPackageBuildManifest
            {
                version = version,
                buildTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                mainPackageZip = $"MainPackage/{mainZipName}"
            };

            string gamesRoot = Path.Combine(stageRoot, "Games");
            Directory.CreateDirectory(gamesRoot);
            foreach (string gameZipPath in GetValidGameZipPaths(profile))
            {
                string gameZipName = Path.GetFileName(gameZipPath);
                File.Copy(gameZipPath, Path.Combine(gamesRoot, gameZipName), true);
                manifest.gamePackageZips.Add($"Games/{gameZipName}");
            }

            WriteJson(Path.Combine(stageRoot, PublishManifestFileName), manifest);
            string outputDirectory = GetPublishOutputDirectory(context, profile);
            Directory.CreateDirectory(outputDirectory);
            string zipPath = Path.Combine(outputDirectory, profile.publishPackageNamePattern.Replace("{Version}", version));
            DeleteFileIfExists(zipPath);
            ZipUtility.CompressFolderToZip(zipPath, string.Empty, stageRoot);
            context.Logger.Info($"[PublishPackage] Output: {zipPath}");
            AssetDatabase.Refresh();
            return zipPath;
        }

        public static string FindLatestMainPackageZip(FRFrameBuildContext context)
        {
            return FindLatestZip(Path.Combine(context.OutputRoot, "MainPackage"), "MainPackage_*.zip");
        }

        public static List<string> FindLatestGamePackageZips(FRFrameBuildContext context)
        {
            List<string> result = new List<string>();
            if (context.BuildProfile?.selectedGameProfiles == null)
            {
                return result;
            }

            foreach (GameBuildProfile gameProfile in context.BuildProfile.selectedGameProfiles)
            {
                if (gameProfile == null)
                {
                    continue;
                }

                string outputRoot = string.IsNullOrEmpty(gameProfile.outputRoot) ? context.OutputRoot : gameProfile.outputRoot;
                string gameDirectoryName = string.IsNullOrEmpty(gameProfile.gameId) ? GetYooAssetPackageName(gameProfile) : gameProfile.gameId;
                string zipPath = FindLatestZip(Path.Combine(outputRoot, gameDirectoryName), "*.zip");
                if (!string.IsNullOrEmpty(zipPath))
                {
                    result.Add(zipPath);
                }
            }

            return result;
        }

        private static List<string> GetValidGameZipPaths(FRFrameBuildProfile profile)
        {
            profile.publishGamePackageZipPaths ??= new List<string>();
            return profile.publishGamePackageZipPaths
                .Where(path => !string.IsNullOrEmpty(path) && File.Exists(path))
                .ToList();
        }

        private static string FindLatestZip(string root, string pattern)
        {
            if (string.IsNullOrEmpty(root) || !Directory.Exists(root))
            {
                return string.Empty;
            }

            string[] files = Directory.GetFiles(root, pattern, SearchOption.TopDirectoryOnly);
            if (files.Length == 0)
            {
                files = Directory.GetFiles(root, "*.zip", SearchOption.TopDirectoryOnly);
            }

            return files.OrderByDescending(File.GetLastWriteTimeUtc).FirstOrDefault() ?? string.Empty;
        }

        public static string BuildFullPackage(FRFrameBuildContext context)
        {
            FRFrameBuildProfile profile = RequireBuildProfile(context);
            profile.packageVersion = GamePackageVersion.Parse(profile.packageVersion).NextFull().ToString();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            string stageRoot = GetStageRoot(context, profile.packageVersion);
            RecreateDirectory(stageRoot);
            BuildStage(context, stageRoot);

            MainPackageBuildManifest manifest = CreateMainManifest(profile);
            manifest.files = CreateFileManifest(stageRoot);
            WriteJson(Path.Combine(stageRoot, MainManifestFileName), manifest);
            WriteJson(Path.Combine(stageRoot, FileManifestFileName), new MainPackageFileManifest { files = manifest.files });

            string outputDirectory = GetMainOutputDirectory(context);
            Directory.CreateDirectory(outputDirectory);
            if (profile.clearHistoryMainFullPackages)
            {
                DeleteHistoryFullPackageFiles(outputDirectory, profile, context.Logger);
            }

            string zipPath = Path.Combine(outputDirectory, ResolveMainFullPackageName(profile));
            DeleteFileIfExists(zipPath);
            ZipUtility.CompressFolderToZip(zipPath, string.Empty, stageRoot);
            WriteFullPackageRecord(context, stageRoot, zipPath, manifest.files.Count);
            context.Logger.Info($"[MainFullPackage] Output: {zipPath}");
            AssetDatabase.Refresh();
            return zipPath;
        }

        public static string BuildPatchPackage(FRFrameBuildContext context, string oldFileManifestPath)
        {
            if (string.IsNullOrEmpty(oldFileManifestPath) || !File.Exists(oldFileManifestPath))
            {
                throw new FileNotFoundException("旧主包 file_manifest.json 不存在。", oldFileManifestPath);
            }

            FRFrameBuildProfile profile = RequireBuildProfile(context);
            string fromVersion = GetVersionFromManifestPath(oldFileManifestPath);
            GamePackageVersion oldVersion = GamePackageVersion.Parse(fromVersion);
            GamePackageVersion nextVersion = GamePackageVersion.Parse(profile.packageVersion).NextPatch();
            if (!oldVersion.IsSameMajor(nextVersion))
            {
                throw new InvalidOperationException($"主包大版本已变化，不能构建补丁包：旧版本 {fromVersion}，当前版本 {nextVersion}。请构建主包整包。");
            }

            profile.packageVersion = nextVersion.ToString();
            EditorUtility.SetDirty(profile);
            AssetDatabase.SaveAssets();

            string fullStageRoot = GetStageRoot(context, profile.packageVersion);
            RecreateDirectory(fullStageRoot);
            BuildStage(context, fullStageRoot);

            MainPackageBuildManifest manifest = CreateMainManifest(profile);
            manifest.files = CreateFileManifest(fullStageRoot);
            WriteJson(Path.Combine(fullStageRoot, MainManifestFileName), manifest);
            WriteJson(Path.Combine(fullStageRoot, FileManifestFileName), new MainPackageFileManifest { files = manifest.files });

            MainPackageFileManifest oldManifest = JsonUtility.FromJson<MainPackageFileManifest>(File.ReadAllText(oldFileManifestPath));
            List<MainPackageFileRecord> oldFiles = oldManifest?.files ?? new List<MainPackageFileRecord>();
            List<MainPackageFileRecord> newFiles = CreateFileManifest(fullStageRoot);
            Dictionary<string, MainPackageFileRecord> oldFileMap = oldFiles.ToDictionary(file => file.path);
            Dictionary<string, MainPackageFileRecord> newFileMap = newFiles.ToDictionary(file => file.path);
            List<MainPackageFileRecord> changedFiles = newFiles
                .Where(file => !oldFileMap.TryGetValue(file.path, out MainPackageFileRecord oldFile) || oldFile.hash != file.hash || oldFile.size != file.size)
                .ToList();
            List<string> deletedFiles = oldFileMap.Keys.Where(path => !newFileMap.ContainsKey(path)).OrderBy(path => path).ToList();

            string patchRoot = GetPatchStageRoot(context, profile.packageVersion);
            RecreateDirectory(patchRoot);
            foreach (MainPackageFileRecord file in changedFiles)
            {
                string source = Path.Combine(fullStageRoot, file.path);
                string destination = Path.Combine(patchRoot, file.path);
                Directory.CreateDirectory(Path.GetDirectoryName(destination));
                File.Copy(source, destination, true);
            }

            MainPatchBuildManifest patchManifest = new MainPatchBuildManifest
            {
                fromVersion = fromVersion,
                toVersion = profile.packageVersion,
                buildTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                changedFiles = changedFiles,
                deletedFiles = deletedFiles
            };
            WriteJson(Path.Combine(patchRoot, PatchManifestFileName), patchManifest);

            string outputDirectory = GetMainOutputDirectory(context);
            Directory.CreateDirectory(outputDirectory);
            string zipPath = Path.Combine(outputDirectory, ResolveMainPatchPackageName(profile, fromVersion));
            DeleteFileIfExists(zipPath);
            ZipUtility.CompressFolderToZip(zipPath, string.Empty, patchRoot);
            WritePatchPackageRecord(context, patchRoot, zipPath, oldFileManifestPath, patchManifest, newFiles.Count);
            context.Logger.Info($"[MainPatchPackage] Output: {zipPath}");
            AssetDatabase.Refresh();
            return zipPath;
        }

        public static string FindDefaultOldFileManifest(FRFrameBuildContext context)
        {
            FRFrameBuildProfile profile = RequireBuildProfile(context);
            string currentVersion = profile.packageVersion;
            string currentManifest = Path.Combine(GetStageRoot(context, currentVersion), FileManifestFileName);
            if (File.Exists(currentManifest))
            {
                return currentManifest;
            }

            GamePackageVersion version = GamePackageVersion.Parse(currentVersion);
            string stageRoot = Path.Combine(GetMainOutputDirectory(context), "Stage");
            if (!Directory.Exists(stageRoot))
            {
                return string.Empty;
            }

            return Directory.GetFiles(stageRoot, FileManifestFileName, SearchOption.AllDirectories)
                .Where(path => IsValidOldManifest(path, version))
                .OrderByDescending(path => GetManifestVersion(path).full)
                .ThenByDescending(path => GetManifestVersion(path).patch)
                .ThenByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault() ?? string.Empty;
        }

        private static bool IsValidOldManifest(string path, GamePackageVersion currentVersion)
        {
            if (string.IsNullOrEmpty(path))
            {
                return false;
            }

            GamePackageVersion manifestVersion = GetManifestVersion(path);
            return manifestVersion.IsSameMajor(currentVersion);
        }

        private static GamePackageVersion GetManifestVersion(string path)
        {
            string versionDirectory = new DirectoryInfo(Path.GetDirectoryName(path)).Name;
            return GamePackageVersion.TryParse(versionDirectory, out GamePackageVersion version) ? version : GamePackageVersion.Default;
        }

        public static string GetStageRoot(FRFrameBuildContext context, string version)
        {
            return Path.Combine(GetMainOutputDirectory(context), "Stage", version);
        }

        private static void BuildStage(FRFrameBuildContext context, string stageRoot)
        {
            BootstrapConfigExporter.Export(context);
            CopyDirectory("Assets/StreamingAssets/HotFix", Path.Combine(stageRoot, "HotFix"), context.Logger, true);
            CopyDirectory(Path.Combine(Application.streamingAssetsPath, BootstrapFileNames.DirectoryName), Path.Combine(stageRoot, BootstrapFileNames.DirectoryName), context.Logger, false);
        }

        private static MainPackageBuildManifest CreateMainManifest(FRFrameBuildProfile profile)
        {
            return new MainPackageBuildManifest
            {
                version = profile.packageVersion,
                buildTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss")
            };
        }

        private static List<MainPackageFileRecord> CreateFileManifest(string root)
        {
            return Directory.GetFiles(root, "*", SearchOption.AllDirectories)
                .Where(path => Path.GetFileName(path) != FileManifestFileName && !IsMetaFile(path))
                .Select(path => new MainPackageFileRecord
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

        private static bool IsMetaFile(string path)
        {
            return Path.GetExtension(path).Equals(".meta", StringComparison.OrdinalIgnoreCase);
        }

        private static void CopyDirectory(string source, string destination, FRFrameBuildLogger logger, bool required)
        {
            if (!Directory.Exists(source))
            {
                if (required)
                {
                    throw new DirectoryNotFoundException(source);
                }

                logger.Warning($"[MainPackage] 目录不存在，跳过：{source}");
                return;
            }

            Directory.CreateDirectory(destination);
            foreach (string directory in Directory.GetDirectories(source, "*", SearchOption.AllDirectories))
            {
                Directory.CreateDirectory(directory.Replace(source, destination));
            }

            foreach (string file in Directory.GetFiles(source, "*", SearchOption.AllDirectories))
            {
                if (IsMetaFile(file))
                {
                    continue;
                }

                string destinationFile = file.Replace(source, destination);
                Directory.CreateDirectory(Path.GetDirectoryName(destinationFile));
                File.Copy(file, destinationFile, true);
            }

            logger.Info($"[MainPackage] 目录已复制：{source} -> {destination}");
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

        private static void DeleteHistoryFullPackageFiles(string outputDirectory, FRFrameBuildProfile profile, FRFrameBuildLogger logger)
        {
            string prefix = ResolveMainFullPackageName(profile).Split(new[] { profile.packageVersion }, StringSplitOptions.None)[0];
            foreach (string file in Directory.GetFiles(outputDirectory, "*.zip", SearchOption.TopDirectoryOnly))
            {
                string fileName = Path.GetFileName(file);
                if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    File.Delete(file);
                    logger.Info($"[MainFullPackage] 历史整包已删除：{file}");
                }
            }
        }

        private static void WriteJson(string path, object value)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, JsonUtility.ToJson(value, true));
        }

        private static void WriteFullPackageRecord(FRFrameBuildContext context, string stageRoot, string zipPath, int fileCount)
        {
            MainPackageBuildRecord record = CreateBaseRecord(context.BuildProfile, "Full", stageRoot, zipPath);
            record.version = context.BuildProfile.packageVersion;
            record.toVersion = context.BuildProfile.packageVersion;
            record.totalFileCount = fileCount;
            WriteBuildRecord(context, record);
            WriteJson(Path.Combine(stageRoot, "build_info.json"), record);
        }

        private static void WritePatchPackageRecord(FRFrameBuildContext context, string patchRoot, string zipPath, string oldFileManifestPath, MainPatchBuildManifest patchManifest, int totalFileCount)
        {
            MainPackageBuildRecord record = CreateBaseRecord(context.BuildProfile, "Patch", patchRoot, zipPath);
            record.version = context.BuildProfile.packageVersion;
            record.fromVersion = patchManifest.fromVersion;
            record.toVersion = patchManifest.toVersion;
            record.oldFileManifestPath = oldFileManifestPath;
            record.totalFileCount = totalFileCount;
            record.changedFileCount = patchManifest.changedFiles.Count;
            record.deletedFileCount = patchManifest.deletedFiles.Count;
            WriteBuildRecord(context, record);
            WriteJson(Path.Combine(patchRoot, "build_info.json"), record);
        }

        private static MainPackageBuildRecord CreateBaseRecord(FRFrameBuildProfile profile, string buildType, string stageRoot, string zipPath)
        {
            return new MainPackageBuildRecord
            {
                buildType = buildType,
                buildTime = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"),
                zipPath = zipPath,
                stageRoot = stageRoot,
                version = profile.packageVersion
            };
        }

        private static void WriteBuildRecord(FRFrameBuildContext context, MainPackageBuildRecord record)
        {
            string historyPath = Path.Combine(context.OutputRoot, "BuildRecords", "MainPackage", "build_history.json");
            MainPackageBuildHistory history = File.Exists(historyPath)
                ? JsonUtility.FromJson<MainPackageBuildHistory>(File.ReadAllText(historyPath))
                : new MainPackageBuildHistory();
            history ??= new MainPackageBuildHistory();
            history.records ??= new List<MainPackageBuildRecord>();
            history.records.Add(record);
            WriteJson(historyPath, history);
        }

        private static string GetMainOutputDirectory(FRFrameBuildContext context)
        {
            return Path.Combine(context.OutputRoot, "MainPackage");
        }

        private static string GetPatchStageRoot(FRFrameBuildContext context, string version)
        {
            return Path.Combine(GetMainOutputDirectory(context), "PatchStage", version);
        }

        private static string GetPublishOutputDirectory(FRFrameBuildContext context, FRFrameBuildProfile profile)
        {
            string outputRoot = string.IsNullOrEmpty(profile.publishOutputRoot) ? context.OutputRoot : profile.publishOutputRoot;
            return Path.Combine(outputRoot, "PublishPackage");
        }

        private static string GetYooAssetPackageName(GameBuildProfile profile)
        {
            return string.IsNullOrEmpty(profile.yooAssetPackageName) ? profile.packageName : profile.yooAssetPackageName;
        }

        private static string ResolveMainFullPackageName(FRFrameBuildProfile profile)
        {
            return profile.mainFullPackageNamePattern.Replace("{Version}", profile.packageVersion);
        }

        private static string ResolveMainPatchPackageName(FRFrameBuildProfile profile, string fromVersion)
        {
            return profile.mainPatchPackageNamePattern
                .Replace("{FromVersion}", fromVersion)
                .Replace("{ToVersion}", profile.packageVersion);
        }

        private static string GetVersionFromManifestPath(string manifestPath)
        {
            string directoryPath = Path.GetDirectoryName(manifestPath);
            string mainManifestPath = Path.Combine(directoryPath, MainManifestFileName);
            if (File.Exists(mainManifestPath))
            {
                MainPackageBuildManifest manifest = JsonUtility.FromJson<MainPackageBuildManifest>(File.ReadAllText(mainManifestPath));
                if (manifest != null && GamePackageVersion.TryParse(manifest.version, out _))
                {
                    return manifest.version;
                }
            }

            return new DirectoryInfo(directoryPath).Name;
        }

        private static FRFrameBuildProfile RequireBuildProfile(FRFrameBuildContext context)
        {
            if (context.BuildProfile == null)
            {
                throw new InvalidOperationException("FRFrame Build Profile is required.");
            }

            return context.BuildProfile;
        }
    }
}
#endif
