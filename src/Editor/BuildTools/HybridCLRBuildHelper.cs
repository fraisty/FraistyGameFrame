#if UNITY_EDITOR
using HybridCLR;
using HybridCLR.Commands;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;

public static class HybridCLRBuildHelper
{
    // =========================
    // StreamingAssets 输出目录
    // =========================
    private const string StreamingRoot = "Assets/StreamingAssets/HotFix";
    private const string StreamingAOTFolder = "AOTMeta";
    private const string StreamingHotFolder = "HotDll";
    private const string VersionFile = "version.txt";
    private const string AOTListFile = "aot_list.txt";
    private const string HotUpdateListFile = "hot_update_list.txt";

    // =========================
    // GameRes 输出目录
    // =========================
    private const string GameResRoot = "Assets/GameRes/Common/DllBytes";
    private const string GameResHotFixFolder = "HotFix";

    /// <summary>
    /// 版本相关
    /// </summary>
    private static string _currentStreamVersion = "1";
    private static SimpleConfigManager _configManager = new SimpleConfigManager();

    private sealed class CopyStats
    {
        public int AOTExpected;
        public int AOTCopied;
        public int AOTMissing;

        public int HotExpected;
        public int HotCopied;
        public int HotMissing;
    }

    // ============================================================
    // 菜单：生成 HybridCLR 所需 DLL
    // ============================================================
    [MenuItem("FRFrame/HotFix/Generate HybridCLR All")]
    public static void GenerateHybridCLRAll()
    {
        try
        {
            ExecuteGenerateAllOnly();
        }
        catch (Exception e)
        {
            Debug.LogError($"[HotFix] GenerateHybridCLRAll failed:\n{e}");
        }
    }

    public static void ExecuteGenerateAllOnly()
    {
        ExecuteGenerateAllOnly(EditorUserBuildSettings.activeBuildTarget);
    }

    public static void ExecuteGenerateAllOnly(BuildTarget target)
    {
        EnsureActiveBuildTarget(target);
        AssetDatabase.Refresh();
        Debug.Log($"[HotFix] ===== HybridCLR Generate All Start ({target}) =====");
        PrebuildCommand.GenerateAll();
        AssetDatabase.Refresh();
        ReleaseBuildMemory();
        Debug.Log($"[HotFix] ===== HybridCLR Generate All Done ({target}) =====");
    }

    public static void ExecuteCompileDllOnly(BuildTarget target)
    {
        EnsureActiveBuildTarget(target);
        AssetDatabase.Refresh();
        Debug.Log($"[HotFix] ===== HybridCLR Compile HotUpdate DLL Start ({target}) =====");
        CompileDllCommand.CompileDll(target);
        AssetDatabase.Refresh();
        ReleaseBuildMemory();
        Debug.Log($"[HotFix] ===== HybridCLR Compile HotUpdate DLL Done ({target}) =====");
    }

    public static void ExecuteGenerateAndCopyToStreamingAssetsOnly()
    {
        ExecuteGenerateAllOnly();
        ExecuteCopyToStreamingAssetsOnly();
    }

    public static void ExecuteGenerateAndCopyToStreamingAssetsOnly(BuildTarget target)
    {
        ExecuteGenerateAllOnly(target);
        ExecuteCopyToStreamingAssetsOnly(target);
    }

    public static void ExecuteGenerateAndCopyMainHotFixToStreamingAssetsOnly(BuildTarget target, IEnumerable<string> excludedAssemblies)
    {
        ExecuteGenerateAllOnly(target);
        ExecuteCopyMainHotFixToStreamingAssetsOnly(target, excludedAssemblies);
    }

    public static void ExecuteGenerateAndCopyMainHotFixToStreamingAssetsAndGameRes(BuildTarget target, IEnumerable<string> excludedAssemblies)
    {
        ExecuteGenerateAllOnly(target);
        ExecuteCopyMainHotFixToStreamingAssetsAndGameRes(target, excludedAssemblies);
    }

    public static void ExecuteCopyMainHotFixToStreamingAssetsOnly(BuildTarget target, IEnumerable<string> excludedAssemblies)
    {
        CopyStats stats = BuildStreamingHotFix(target, excludedAssemblies);
        PrintStats("Copy Main HotFix To StreamingAssets Only", stats);
        AssetDatabase.Refresh();
        Debug.Log("[HotFix] ===== Copy Main HotFix To StreamingAssets Only Done =====");
    }

    public static void ExecuteCopyMainHotFixToStreamingAssetsAndGameRes(BuildTarget target, IEnumerable<string> excludedAssemblies)
    {
        CopyStats stats = BuildStreamingHotFix(target, excludedAssemblies);
        PrintStats("Copy Main HotFix To StreamingAssets And GameRes", stats);
        SyncStreamingToGameRes();
        AssetDatabase.Refresh();
        Debug.Log("[HotFix] ===== Copy Main HotFix To StreamingAssets And GameRes Done =====");
    }

    public static void ExecuteGenerateAndCopyToStreamingAssetsAndGameRes()
    {
        ExecuteGenerateAllOnly();
        ExecuteCopyToStreamingAssetsAndGameRes();
    }

    public static void ExecuteGenerateAndCopyToStreamingAssetsAndGameRes(BuildTarget target)
    {
        ExecuteGenerateAllOnly(target);
        ExecuteCopyToStreamingAssetsAndGameRes(target);
    }

    [MenuItem("FRFrame/HotFix/Copy HybridCLR To StreamingAssets Only")]
    public static void CopyToStreamingAssetsOnly()
    {
        try
        {
            ExecuteCopyToStreamingAssetsOnly();
        }
        catch (Exception e)
        {
            Debug.LogError($"[HotFix] CopyToStreamingAssetsOnly failed:\n{e}");
        }
    }

    public static void ExecuteCopyToStreamingAssetsOnly()
    {
        ExecuteCopyToStreamingAssetsOnly(EditorUserBuildSettings.activeBuildTarget);
    }

    public static void ExecuteCopyToStreamingAssetsOnly(BuildTarget target)
    {
        CopyStats stats = BuildStreamingHotFix(target);
        PrintStats("Copy To StreamingAssets Only", stats);
        AssetDatabase.Refresh();
        Debug.Log("[HotFix] ===== Copy To StreamingAssets Only Done =====");
    }

    // ============================================================
    // 菜单：只同步 StreamingAssets/HotFix -> GameRes
    // ============================================================
    [MenuItem("FRFrame/HotFix/Sync Streaming HotFix To GameRes Only")]
    public static void SyncStreamingHotFixToGameResOnly()
    {
        try
        {
            ExecuteSyncStreamingHotFixToGameResOnly();
        }
        catch (Exception e)
        {
            Debug.LogError($"[HotFix] SyncStreamingHotFixToGameResOnly failed:\n{e}");
        }
    }

    public static void ExecuteSyncStreamingHotFixToGameResOnly()
    {
        SyncStreamingToGameRes();
        AssetDatabase.Refresh();
        Debug.Log("[HotFix] ===== Sync Streaming HotFix To GameRes Only Done =====");
    }

    // ============================================================
    // 菜单：先生成 StreamingAssets，再同步到 GameRes
    // ============================================================
    [MenuItem("FRFrame/HotFix/Copy HybridCLR To StreamingAssets And GameRes")]
    public static void CopyToStreamingAssetsAndGameRes()
    {
        try
        {
            ExecuteCopyToStreamingAssetsAndGameRes();
        }
        catch (Exception e)
        {
            Debug.LogError($"[HotFix] CopyToStreamingAssetsAndGameRes failed:\n{e}");
        }
    }

    public static void ExecuteCopyToStreamingAssetsAndGameRes()
    {
        ExecuteCopyToStreamingAssetsAndGameRes(EditorUserBuildSettings.activeBuildTarget);
    }

    public static void ExecuteCopyToStreamingAssetsAndGameRes(BuildTarget target)
    {
        CopyStats stats = BuildStreamingHotFix(target);
        PrintStats("Copy To StreamingAssets And GameRes", stats);

        SyncStreamingToGameRes();

        AssetDatabase.Refresh();
        Debug.Log("[HotFix] ===== Copy To StreamingAssets And GameRes Done =====");
    }

    // ============================================================
    // 菜单：清理 StreamingAssets HotFix
    // ============================================================
    [MenuItem("FRFrame/HotFix/Clear StreamingAssets HotFix")]
    public static void ClearStreamingAssets()
    {
        ClearDirectory(StreamingRoot);
        AssetDatabase.Refresh();
        Debug.Log("[HotFix] StreamingAssets/HotFix cleared.");
    }

    // ============================================================
    // 菜单：清理 GameRes HotFix
    // ============================================================
    [MenuItem("FRFrame/HotFix/Clear GameRes HotFix")]
    public static void ClearGameResHotFix()
    {
        string gameResHotFixDstDir = Path.Combine(GameResRoot, GameResHotFixFolder);
        ClearDirectory(gameResHotFixDstDir);
        AssetDatabase.Refresh();
        Debug.Log("[HotFix] GameRes/Common/DllBytes/HotFix cleared.");
    }

    // ============================================================
    // 菜单：清理全部
    // ============================================================
    [MenuItem("FRFrame/HotFix/Clear All HotFix Output")]
    public static void ClearAllHotFixOutput()
    {
        ClearDirectory(StreamingRoot);
        ClearDirectory(Path.Combine(GameResRoot, GameResHotFixFolder));
        AssetDatabase.Refresh();
        Debug.Log("[HotFix] All hotfix output cleared.");
    }

    // ============================================================
    // 生成 StreamingAssets/HotFix
    // ============================================================
    private static CopyStats BuildStreamingHotFix(BuildTarget target, IEnumerable<string> excludedAssemblies = null)
    {
        string platform = GetPlatformName(target);

        string aotSrcDir = SettingsUtil.GetAssembliesPostIl2CppStripDir(target);
        string hotSrcDir = SettingsUtil.GetHotUpdateDllsOutputDirByTarget(target);

        string streamingAOTDstDir = Path.Combine(StreamingRoot, StreamingAOTFolder);
        string streamingHotDstDir = Path.Combine(StreamingRoot, StreamingHotFolder);

        if (!Directory.Exists(aotSrcDir))
        {
            throw new DirectoryNotFoundException(
                $"[HotFix] AOT source dir not found: {aotSrcDir}\n请先执行 HybridCLR → Generate → All");
        }

        if (!Directory.Exists(hotSrcDir))
        {
            throw new DirectoryNotFoundException(
                $"[HotFix] HotUpdate source dir not found: {hotSrcDir}\n请先执行 HybridCLR → Generate → All");
        }

        // HybridCLR 9 no longer stores the supplementary AOT list in its settings.
        List<string> aotAssemblyNames = new List<string>
        {
            "DOTween",
            "System.Core",
            "UniTask",
            "Unity.InputSystem",
            "UnityEngine.CoreModule",
            "UnityEngine.JSONSerializeModule",
            "YooAsset",
            "mscorlib",
            "System",
        };

        List<string> hotUpdateDlls = SettingsUtil.HotUpdateAssemblyFilesExcludePreserved;
        if (hotUpdateDlls == null || hotUpdateDlls.Count == 0)
        {
            throw new Exception("[HotFix] SettingsUtil.HotUpdateAssemblyFilesExcludePreserved 为空！");
        }

        HashSet<string> excludedAssemblyNames = CreateAssemblyNameSet(excludedAssemblies);
        hotUpdateDlls = hotUpdateDlls
            .Where(dll => !excludedAssemblyNames.Contains(Path.GetFileNameWithoutExtension(EnsureDllExtension(dll))))
            .ToList();
        if (hotUpdateDlls.Count == 0)
        {
            throw new Exception("[HotFix] 主包 HotUpdate 列表为空，请确认是否把所有 DLL 都配置成游戏包 DLL。");
        }

        Debug.Log($"[HotFix] Platform = {platform}");
        Debug.Log($"[HotFix] AOT source = {aotSrcDir}");
        Debug.Log($"[HotFix] HotUpdate source = {hotSrcDir}");

        EnsureDirectoryExists("Assets/StreamingAssets");
        GetCurrentStreamVersion(StreamingRoot);

        ClearDirectory(StreamingRoot);
        Directory.CreateDirectory(streamingAOTDstDir);
        Directory.CreateDirectory(streamingHotDstDir);

        CopyStats stats = new CopyStats
        {
            AOTExpected = CountValidEntries(aotAssemblyNames),
            HotExpected = CountValidEntries(hotUpdateDlls)
        };

        // =========================
        // AOT
        // =========================
        List<string> copiedAOTNames = new List<string>();

        foreach (string asmName in aotAssemblyNames)
        {
            if (string.IsNullOrWhiteSpace(asmName))
                continue;

            string dllFileName = EnsureDllExtension(asmName);
            string src = Path.Combine(aotSrcDir, dllFileName);

            if (!File.Exists(src))
            {
                stats.AOTMissing++;
                Debug.LogWarning($"[HotFix] AOT file not found, skip: {src}");
                continue;
            }

            string bytesFileName = dllFileName + ".bytes";
            string dst = Path.Combine(streamingAOTDstDir, bytesFileName);

            File.Copy(src, dst, true);
            copiedAOTNames.Add(bytesFileName);
            stats.AOTCopied++;

            Debug.Log($"[HotFix] AOT Copy: {src} -> {dst}");
        }

        string aotListPath = Path.Combine(streamingAOTDstDir, AOTListFile);
        File.WriteAllText(aotListPath, string.Join("\n", copiedAOTNames));
        Debug.Log($"[HotFix] AOT list written: {aotListPath}");

        // =========================
        // HotUpdate
        // =========================
        List<string> copiedHotUpdateNames = new List<string>();

        foreach (string dll in hotUpdateDlls)
        {
            if (string.IsNullOrWhiteSpace(dll))
                continue;

            string dllFileName = EnsureDllExtension(dll);
            string src = Path.Combine(hotSrcDir, dllFileName);

            if (!File.Exists(src))
            {
                stats.HotMissing++;
                Debug.LogWarning($"[HotFix] HotUpdate dll not found, skip: {src}");
                continue;
            }

            string bytesFileName = dllFileName + ".bytes";
            string dst = Path.Combine(streamingHotDstDir, bytesFileName);

            File.Copy(src, dst, true);
            copiedHotUpdateNames.Add(bytesFileName);
            stats.HotCopied++;

            Debug.Log($"[HotFix] HotUpdate Copy: {src} -> {dst}");
        }

        string hotUpdateListPath = Path.Combine(streamingHotDstDir, HotUpdateListFile);
        File.WriteAllText(hotUpdateListPath, string.Join("\n", copiedHotUpdateNames));
        Debug.Log($"[HotFix] HotUpdate list written: {hotUpdateListPath}");

        if (stats.AOTMissing > 0 || stats.HotMissing > 0 || stats.AOTCopied != stats.AOTExpected || stats.HotCopied != stats.HotExpected)
        {
            throw new Exception(
                "[HotFix] HybridCLR copy output incomplete.\n" +
                $"AOT Expected={stats.AOTExpected}, Copied={stats.AOTCopied}, Missing={stats.AOTMissing}\n" +
                $"HotUpdate Expected={stats.HotExpected}, Copied={stats.HotCopied}, Missing={stats.HotMissing}");
        }

        string streamingVersionPath = Path.Combine(StreamingRoot, VersionFile);
        NormalizeStreamVersionText(streamingVersionPath);
        
        return stats;
    }

    // ============================================================
    // 同步 StreamingAssets/HotFix -> GameRes/Common/DllBytes/HotFix
    // ============================================================
    private static void SyncStreamingToGameRes()
    {
        string gameResHotFixDstDir = Path.Combine(GameResRoot, GameResHotFixFolder);

        if (!Directory.Exists(StreamingRoot))
        {
            throw new DirectoryNotFoundException(
                $"[HotFix] Streaming source not found: {StreamingRoot}\n请先执行 Copy HybridCLR To StreamingAssets Only");
        }

        string streamingVersionPath = Path.Combine(StreamingRoot, VersionFile);
        string streamingAOTDir = Path.Combine(StreamingRoot, StreamingAOTFolder);
        string streamingHotDir = Path.Combine(StreamingRoot, StreamingHotFolder);

        bool hasVersion = File.Exists(streamingVersionPath);
        bool hasAOTDir = Directory.Exists(streamingAOTDir);
        bool hasHotDir = Directory.Exists(streamingHotDir);

        if (!hasVersion && !hasAOTDir && !hasHotDir)
        {
            throw new Exception(
                $"[HotFix] Streaming source looks invalid: {StreamingRoot}\n" +
                $"缺少 version.txt / AOTMeta / HotDll，请先生成 StreamingAssets 热更目录。");
        }

        EnsureDirectoryExists("Assets/GameRes");
        EnsureDirectoryExists(GameResRoot);

        ClearDirectory(gameResHotFixDstDir);
        CopyDirectoryFiltered(StreamingRoot, gameResHotFixDstDir);
        
        string gameResVersionPath = Path.Combine(gameResHotFixDstDir, VersionFile);
        File.WriteAllText(gameResVersionPath, _currentStreamVersion);

        Debug.Log($"[HotFix] Full HotFix folder copied:");
        Debug.Log($"         {StreamingRoot}");
        Debug.Log($"         -> {gameResHotFixDstDir}");
        Debug.Log($"[HotFix] GameRes version written: {gameResVersionPath} = {_currentStreamVersion}");
    }

    // ============================================================
    // 统计输出
    // ============================================================
    private static void PrintStats(string title, CopyStats stats)
    {
        Debug.Log(
            "[HotFix] ===== Summary =====\n" +
            $"[HotFix] Task: {title}\n" +
            $"[HotFix] AOT      Expected={stats.AOTExpected}, Copied={stats.AOTCopied}, Missing={stats.AOTMissing}\n" +
            $"[HotFix] HotUpdate Expected={stats.HotExpected}, Copied={stats.HotCopied}, Missing={stats.HotMissing}"
        );
    }

    // ============================================================
    // 平台名
    // ============================================================
    private static string GetPlatformName(BuildTarget target)
    {
        switch (target)
        {
            case BuildTarget.Android:
                return "Android";
            case BuildTarget.iOS:
                return "iOS";
            case BuildTarget.StandaloneWindows:
            case BuildTarget.StandaloneWindows64:
                return "StandaloneWindows64";
            case BuildTarget.StandaloneOSX:
                return "StandaloneOSX";
            case BuildTarget.WebGL:
                return "WebGL";
            default:
                return target.ToString();
        }
    }

    private static void EnsureActiveBuildTarget(BuildTarget target)
    {
        if (EditorUserBuildSettings.activeBuildTarget != target)
        {
            throw new InvalidOperationException(
                $"[HotFix] Active build target is {EditorUserBuildSettings.activeBuildTarget}, but requested target is {target}. " +
                "Run Prepare Build Settings before generating HybridCLR output.");
        }
    }

    private static void ReleaseBuildMemory()
    {
        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();
        Debug.Log("[HotFix] Released managed build memory.");
    }

    // ============================================================
    // 保证 dll 后缀
    // ============================================================
    private static string EnsureDllExtension(string fileName)
    {
        return fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)
            ? fileName
            : fileName + ".dll";
    }

    private static HashSet<string> CreateAssemblyNameSet(IEnumerable<string> assemblies)
    {
        HashSet<string> names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (assemblies == null)
        {
            return names;
        }

        foreach (string assembly in assemblies)
        {
            if (!string.IsNullOrWhiteSpace(assembly))
            {
                names.Add(Path.GetFileNameWithoutExtension(EnsureDllExtension(assembly.Trim())));
            }
        }

        return names;
    }

    // ============================================================
    // 规范化版本文本：必须为可解析整数，读取streampatch的verion，没有用默认值，有就递增1
    // ============================================================
    private static void GetCurrentStreamVersion(string streamingPath)
    {
        if (!Directory.Exists(streamingPath))
        {
            Debug.Log($"{streamingPath} not exist, so use default");
            return;
        }
        
        string streamingVersionPath = Path.Combine(StreamingRoot, VersionFile);
        _configManager.Load(streamingVersionPath);
        string streamingVersion = _configManager.GetValue("GameBoot", "version", _currentStreamVersion);
        
        _currentStreamVersion = streamingVersion;
        Debug.Log($"GetCurrentStreamVersion {_currentStreamVersion}");
    }
    
    private static void NormalizeStreamVersionText(string streamingVersionPath)
    {
        if (!File.Exists(streamingVersionPath))
        {
            Debug.Log($"NormalizeStreamVersionText {streamingVersionPath} not exist, so create");
            File.WriteAllText(streamingVersionPath, string.Empty);
        }
        
        _configManager.Load(streamingVersionPath);
        
        if (!int.TryParse(_currentStreamVersion, out int versionValue))
        {
            Debug.LogError($"NormalizeStreamVersionText {_currentStreamVersion} is not a valid integer version: {_currentStreamVersion}");
            return;
        }
        
        versionValue = versionValue + 1;
        _currentStreamVersion = versionValue.ToString();
        
        _configManager.SetValue("GameBoot", "version", _currentStreamVersion);
        _configManager.Save(streamingVersionPath);
        Debug.Log($"NormalizeStreamVersionText Streaming version written: {streamingVersionPath} = {_currentStreamVersion}");
        AssetDatabase.Refresh();
    }
    
    // ============================================================
    // 统计有效项数量
    // ============================================================
    private static int CountValidEntries(List<string> entries)
    {
        if (entries == null || entries.Count == 0)
            return 0;

        int count = 0;
        foreach (string entry in entries)
        {
            if (!string.IsNullOrWhiteSpace(entry))
                count++;
        }
        return count;
    }

    // ============================================================
    // 确保目录存在
    // ============================================================
    private static void EnsureDirectoryExists(string path)
    {
        if (!Directory.Exists(path))
        {
            Directory.CreateDirectory(path);
        }
    }

    // ============================================================
    // 清理目录和对应 meta
    // ============================================================
    private static void ClearDirectory(string path)
    {
        if (Directory.Exists(path))
        {
            FileUtil.DeleteFileOrDirectory(path);
        }

        string metaPath = path + ".meta";
        if (File.Exists(metaPath))
        {
            FileUtil.DeleteFileOrDirectory(metaPath);
        }
    }

    // ============================================================
    // 递归复制目录，过滤 .meta
    // ============================================================
    private static void CopyDirectoryFiltered(string sourceDir, string destinationDir)
    {
        if (!Directory.Exists(sourceDir))
        {
            throw new DirectoryNotFoundException($"Source directory not found: {sourceDir}");
        }

        Directory.CreateDirectory(destinationDir);

        foreach (string filePath in Directory.GetFiles(sourceDir))
        {
            if (filePath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                continue;

            string fileName = Path.GetFileName(filePath);
            string destFilePath = Path.Combine(destinationDir, fileName);
            File.Copy(filePath, destFilePath, true);
        }

        foreach (string dirPath in Directory.GetDirectories(sourceDir))
        {
            if (dirPath.EndsWith(".meta", StringComparison.OrdinalIgnoreCase))
                continue;

            string dirName = Path.GetFileName(dirPath);
            string destSubDir = Path.Combine(destinationDir, dirName);
            CopyDirectoryFiltered(dirPath, destSubDir);
        }
    }
}
#endif
