using FRFrame;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using Unity.SharpZipLib.Utils;
using YooAsset;
using YooAsset.Editor;

public static class YooAssetPackageHelper
{
    private const string BuiltinCatalogFileName = "BuiltinCatalog.bytes";

    [MenuItem("FRFrame/Build/打包压缩YooAsset Package")]
    public static void PackYooAssetPackage()
    {
        ExecutePackYooAssetPackage();
    }

    public static void ExecuteBuildAndPackYooAssetPackage(FRFrame.Editor.FRFrameBuildContext context)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        List<FRFrame.Editor.GameBuildProfile> profiles = GetBuildGameProfiles(context).ToList();
        if (profiles.Count == 0)
        {
            context.Logger.Info("[YooAsset] 未选择游戏配置，跳过非默认游戏包构建。");
            return;
        }

        foreach (FRFrame.Editor.GameBuildProfile profile in profiles)
        {
            string packageName = GetYooAssetPackageName(profile);
            if (string.IsNullOrEmpty(packageName))
            {
                throw new InvalidOperationException($"游戏配置 {profile.name} 未填写 YooAsset 包名或运行时包名。");
            }

            if (packageName == "DefaultPackage")
            {
                context.Logger.Info("[YooAsset] DefaultPackage 由主包流程处理，跳过游戏包构建。");
                continue;
            }

            ExecuteBuildYooAssetPackage(packageName, profile.packageVersion, context.BuildTarget, context.Logger);
            ExecutePackYooAssetPackage(packageName, profile.packageVersion, context.Logger);
        }
    }

    public static string ExecuteBuildYooAssetPackage(FRFrame.Editor.FRFrameBuildContext context, FRFrame.Editor.GameBuildProfile profile)
    {
        if (context == null)
        {
            throw new ArgumentNullException(nameof(context));
        }

        if (profile == null)
        {
            throw new ArgumentNullException(nameof(profile));
        }

        string packageName = GetYooAssetPackageName(profile);
        if (string.IsNullOrEmpty(packageName))
        {
            throw new InvalidOperationException($"游戏配置 {profile.name} 未填写 YooAsset 包名或运行时包名。");
        }

        string packageVersion = string.IsNullOrEmpty(profile.packageVersion)
            ? DateTime.Now.ToString("yyyy-MM-dd-HHmmss")
            : profile.packageVersion;
        string outputRoot = ResolveBuildOutputRoot(profile.yooAssetBuildOutputRoot);
        string buildinRoot = ResolveBuildinFileRoot(profile.yooAssetBuildinFileRoot);
        string versionOutputPath = Path.Combine(outputRoot, YooAssetConfigs.GetPlatformName(), packageName, packageVersion);
        if (Directory.Exists(versionOutputPath))
        {
            if (!profile.yooAssetOverwriteOutput)
            {
                throw new InvalidOperationException($"YooAsset package output already exists: {versionOutputPath}");
            }

            context.Logger.Warning($"[YooAsset] Delete existing package version output: {versionOutputPath}");
            Directory.Delete(versionOutputPath, true);
        }

        IBuildPipeline pipeline;
        BuildParameters buildParameters;
        switch (profile.yooAssetBuildPipeline)
        {
            case EBuildPipeline.LegacyBuildPipeline:
                LegacyBuildParameters legacyParameters = new LegacyBuildParameters();
                pipeline = new LegacyBuildPipeline();
                buildParameters = legacyParameters;
                legacyParameters.CompressOption = profile.yooAssetCompressOption;
                legacyParameters.StripUnityVersion = profile.yooAssetStripUnityVersion;
                legacyParameters.DisableWriteTypeTree = profile.yooAssetDisableWriteTypeTree;
                break;
            case EBuildPipeline.ScriptableBuildPipeline:
                ScriptableBuildParameters scriptableParameters = new ScriptableBuildParameters();
                pipeline = new FRFrame.Editor.FRFrameScriptableBuildPipeline();
                buildParameters = scriptableParameters;
                scriptableParameters.CompressOption = profile.yooAssetCompressOption;
                scriptableParameters.StripUnityVersion = profile.yooAssetStripUnityVersion;
                scriptableParameters.DisableWriteTypeTree = profile.yooAssetDisableWriteTypeTree;
                scriptableParameters.BuiltinShadersBundleName = profile.yooAssetBuiltinShadersBundleName;
                scriptableParameters.MonoScriptsBundleName = profile.yooAssetMonoScriptsBundleName;
                break;
            default:
                throw new NotSupportedException($"当前游戏整包不支持 YooAsset 构建管线: {profile.yooAssetBuildPipeline}");
        }

        buildParameters.BuildOutputRoot = outputRoot;
        buildParameters.BundledFileRoot = buildinRoot;
        buildParameters.BuildPipeline = profile.yooAssetBuildPipeline.ToString();
        buildParameters.BuildBundleType = (int)EBundleType.AssetBundle;
        buildParameters.BuildTarget = context.BuildTarget;
        buildParameters.PackageName = packageName;
        buildParameters.PackageVersion = packageVersion;
        buildParameters.PackageNote = profile.yooAssetPackageNote;
        buildParameters.ClearBuildCacheFiles = profile.yooAssetClearBuildCacheFiles;
        buildParameters.UseAssetDependencyDB = profile.yooAssetUseAssetDependencyDB;
        buildParameters.EnableSharePackRule = profile.yooAssetEnableSharePackRule;
        buildParameters.SingleReferencedPackAlone = profile.yooAssetSingleReferencedPackAlone;
        buildParameters.VerifyBuildingResult = profile.yooAssetVerifyBuildingResult;
        buildParameters.FileNameStyle = profile.yooAssetFileNameStyle;
        buildParameters.BundledCopyOption = profile.yooAssetBuildinFileCopyOption;
        buildParameters.BundledCopyParams = profile.yooAssetBuildinFileCopyParams;
        buildParameters.BundleEncryptor = new EncryptionNone();
        buildParameters.ManifestEncryptor = new ManifestEncryptorNone();
        buildParameters.ManifestDecryptor = new ManifestDecryptorNone();

        context.Logger.Info($"[YooAsset] Build package: {packageName}, version: {packageVersion}, target: {context.BuildTarget}, pipeline: {profile.yooAssetBuildPipeline}");
        context.Logger.Info($"[YooAsset] OutputRoot: {outputRoot}, BuildinRoot: {buildinRoot}, Compress: {profile.yooAssetCompressOption}, FileName: {profile.yooAssetFileNameStyle}");
        BuildResult result = pipeline.Run(buildParameters, true);
        if (!result.Success)
        {
            throw new InvalidOperationException($"YooAsset build failed: {packageName}, task: {result.FailedTask}, error: {result.ErrorInfo}\n{result.ErrorStack}");
        }

        EnsureBuiltinCatalog(packageName, result.OutputPackageDirectory, context.Logger);
        context.Logger.Info($"[YooAsset] Build output: {result.OutputPackageDirectory}");
        AssetDatabase.Refresh();
        return result.OutputPackageDirectory;
    }

    public static string ExecuteBuildYooAssetPackage(string packageName, string packageVersion, BuildTarget buildTarget, FRFrame.Editor.FRFrameBuildLogger logger = null)
    {
        if (string.IsNullOrEmpty(packageName))
        {
            throw new InvalidOperationException("YooAsset package name is empty.");
        }

        if (string.IsNullOrEmpty(packageVersion))
        {
            packageVersion = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
        }

        string versionOutputPath = GetPackageVersionOutputPath(packageName, packageVersion);
        if (Directory.Exists(versionOutputPath))
        {
            logger?.Warning($"[YooAsset] Delete existing package version output: {versionOutputPath}");
            Directory.Delete(versionOutputPath, true);
        }

        ScriptableBuildParameters buildParameters = new ScriptableBuildParameters
        {
            BuildOutputRoot = BundleBuilderHelper.GetDefaultBuildOutputRoot(),
            BundledFileRoot = BundleBuilderHelper.GetStreamingAssetsRoot(),
            BuildPipeline = nameof(ScriptableBuildPipeline),
            BuildBundleType = (int)EBundleType.AssetBundle,
            BuildTarget = buildTarget,
            PackageName = packageName,
            PackageVersion = packageVersion,
            VerifyBuildingResult = true,
            EnableSharePackRule = true,
            BundledCopyParams = string.Empty,
            CompressOption = ECompressOption.LZ4,
            ClearBuildCacheFiles = false,
            BuiltinShadersBundleName = string.Empty,
            MonoScriptsBundleName = string.Empty,
            DisableWriteTypeTree = false
        };

        logger?.Info($"[YooAsset] Build package: {packageName}, version: {packageVersion}, target: {buildTarget}");
        IBuildPipeline pipeline = new FRFrame.Editor.FRFrameScriptableBuildPipeline();
        BuildResult result = pipeline.Run(buildParameters, true);
        if (!result.Success)
        {
            throw new InvalidOperationException($"YooAsset build failed: {packageName}, task: {result.FailedTask}, error: {result.ErrorInfo}\n{result.ErrorStack}");
        }

        EnsureBuiltinCatalog(packageName, result.OutputPackageDirectory, logger);
        logger?.Info($"[YooAsset] Build output: {result.OutputPackageDirectory}");
        AssetDatabase.Refresh();
        return result.OutputPackageDirectory;
    }

    public static void ExecutePackYooAssetPackage()
    {
        string yooAssetBuildRootPath =
            $"{BundleBuilderHelper.GetDefaultBuildOutputRoot()}/{YooAssetConfigs.GetPlatformName()}";
        Debug.Log(yooAssetBuildRootPath);

        foreach (BundleCollectorPackage package in BundleCollectorSettingData.Setting.Packages)
        {
            string packagePath = $"{yooAssetBuildRootPath}/{package.PackageName}";
            Debug.Log(packagePath);

            if (package.PackageName == "DefaultPackage")
            {
                continue;
            }

            ExecutePackYooAssetPackage(package.PackageName);
        }
    }

    public static string ExecutePackYooAssetPackage(string packageName, FRFrame.Editor.FRFrameBuildLogger logger = null)
    {
        return ExecutePackYooAssetPackage(packageName, null, logger);
    }

    public static string ExecutePackYooAssetPackage(string packageName, string packageVersion, FRFrame.Editor.FRFrameBuildLogger logger = null)
    {
        if (string.IsNullOrEmpty(packageName))
        {
            throw new InvalidOperationException("YooAsset package name is empty.");
        }

        string yooAssetBuildRootPath = Path.Combine(BundleBuilderHelper.GetDefaultBuildOutputRoot(), YooAssetConfigs.GetPlatformName());
        string folderPath = string.IsNullOrEmpty(packageVersion)
            ? GetLatestRealVersionDirectory(packageName)
            : GetPackageVersionDirectory(packageName, packageVersion);
        EnsureBuiltinCatalog(packageName, folderPath, logger);
        string outPathName = Path.Combine(yooAssetBuildRootPath, packageName + ".zip");
        if (File.Exists(outPathName))
        {
            File.Delete(outPathName);
        }

        ZipUtility.CompressFolderToZip(outPathName, string.Empty, folderPath);
        logger?.Info($"[YooAsset] Package zip: {folderPath} -> {outPathName}");
        Debug.Log($"压缩成功： from {folderPath} to {outPathName}");
        return outPathName;
    }

    /// <summary>
    /// YooAsset 3.x 的 BuiltinFileSystem 必须通过 BuiltinCatalog.bytes 建立文件索引。
    /// 外置离线包使用完整构建输出作为自定义内置根目录，因此需要为该目录单独生成 Catalog。
    /// </summary>
    public static void EnsureBuiltinCatalog(string packageName, string packageDirectory, FRFrame.Editor.FRFrameBuildLogger logger = null)
    {
        if (string.IsNullOrEmpty(packageName))
        {
            throw new ArgumentException("YooAsset package name is empty.", nameof(packageName));
        }

        if (string.IsNullOrEmpty(packageDirectory) || !Directory.Exists(packageDirectory))
        {
            throw new DirectoryNotFoundException($"YooAsset package directory not found: {packageDirectory}");
        }

        Type helperType = typeof(YooAssets).Assembly.GetType("YooAsset.BuiltinCatalogHelper", throwOnError: true);
        MethodInfo createMethod = helperType.GetMethod(
            "CreateFile",
            BindingFlags.Public | BindingFlags.Static,
            binder: null,
            types: new[] { typeof(IManifestDecryptor), typeof(string), typeof(string) },
            modifiers: null);
        if (createMethod == null)
        {
            throw new MissingMethodException(helperType.FullName, "CreateFile");
        }

        try
        {
            object result = createMethod.Invoke(null, new object[] { null, packageName, packageDirectory });
            if (!(result is bool succeeded) || !succeeded)
            {
                throw new InvalidOperationException($"Failed to create YooAsset builtin catalog: {packageName}");
            }
        }
        catch (TargetInvocationException exception) when (exception.InnerException != null)
        {
            throw new InvalidOperationException(
                $"Failed to create YooAsset builtin catalog: {packageName}",
                exception.InnerException);
        }

        string catalogPath = Path.Combine(packageDirectory, BuiltinCatalogFileName);
        if (!File.Exists(catalogPath) || new FileInfo(catalogPath).Length == 0)
        {
            throw new FileNotFoundException("YooAsset builtin catalog was not generated.", catalogPath);
        }

        logger?.Info($"[YooAsset] Builtin catalog generated: {catalogPath}");
    }

    [MenuItem("FRFrame/Build/生成YooAssetConfig到StreamAsset")]
    public static void CreateYooAssetConfigJson()
    {
        ExecuteCreateYooAssetConfigJson();
    }

    public static void ExecuteCreateYooAssetConfigJson()
    {
        Debug.LogWarning("旧版 FRFMYooAssetPackages 导出入口已停用，请使用 FRFrame 构建窗口导出 Bootstrap 启动配置。");
    }

    private static IEnumerable<FRFrame.Editor.GameBuildProfile> GetBuildGameProfiles(FRFrame.Editor.FRFrameBuildContext context)
    {
        if (context.GameProfile != null)
        {
            yield return context.GameProfile;
            yield break;
        }

        if (context.BuildProfile?.selectedGameProfiles == null)
        {
            yield break;
        }

        foreach (FRFrame.Editor.GameBuildProfile profile in context.BuildProfile.selectedGameProfiles)
        {
            if (profile != null)
            {
                yield return profile;
            }
        }
    }

    private static string GetYooAssetPackageName(FRFrame.Editor.GameBuildProfile profile)
    {
        if (profile == null)
        {
            return string.Empty;
        }

        return string.IsNullOrEmpty(profile.yooAssetPackageName) ? profile.packageName : profile.yooAssetPackageName;
    }

    public static string GetPackageVersionDirectory(string packageName, string packageVersion)
    {
        if (string.IsNullOrEmpty(packageName))
        {
            throw new InvalidOperationException("YooAsset package name is empty.");
        }

        if (string.IsNullOrEmpty(packageVersion))
        {
            return GetLatestRealVersionDirectory(packageName);
        }

        string versionPath = GetPackageVersionOutputPath(packageName, packageVersion);
        if (!Directory.Exists(versionPath) || IsSimulateDirectory(versionPath))
        {
            throw new DirectoryNotFoundException($"YooAsset package version output not found: {versionPath}");
        }

        return versionPath;
    }

    public static string GetLatestRealVersionDirectory(string packageName)
    {
        string packagePath = GetPackageOutputRoot(packageName);
        if (!Directory.Exists(packagePath))
        {
            throw new DirectoryNotFoundException($"YooAsset package output not found: {packagePath}");
        }

        string latest = Directory.GetDirectories(packagePath)
            .Where(dir => !IsSimulateDirectory(dir))
            .OrderByDescending(Directory.GetLastWriteTimeUtc)
            .FirstOrDefault();

        if (string.IsNullOrEmpty(latest))
        {
            throw new DirectoryNotFoundException($"YooAsset package version output not found: {packagePath}");
        }

        return latest;
    }

    private static string GetPackageOutputRoot(string packageName)
    {
        if (string.IsNullOrEmpty(packageName))
        {
            throw new InvalidOperationException("YooAsset package name is empty.");
        }

        return Path.Combine(BundleBuilderHelper.GetDefaultBuildOutputRoot(), YooAssetConfigs.GetPlatformName(), packageName);
    }

    private static string ResolveBuildOutputRoot(string outputRoot)
    {
        if (string.IsNullOrEmpty(outputRoot))
        {
            return BundleBuilderHelper.GetDefaultBuildOutputRoot();
        }

        return Path.IsPathRooted(outputRoot)
            ? Path.GetFullPath(outputRoot).Replace('\\', '/')
            : Path.GetFullPath(Path.Combine(Application.dataPath, "..", outputRoot)).Replace('\\', '/');
    }

    private static string ResolveBuildinFileRoot(string buildinFileRoot)
    {
        if (string.IsNullOrEmpty(buildinFileRoot))
        {
            return BundleBuilderHelper.GetStreamingAssetsRoot();
        }

        return Path.IsPathRooted(buildinFileRoot)
            ? Path.GetFullPath(buildinFileRoot).Replace('\\', '/')
            : Path.GetFullPath(Path.Combine(Application.dataPath, "..", buildinFileRoot)).Replace('\\', '/');
    }

    private static string GetPackageVersionOutputPath(string packageName, string packageVersion)
    {
        return Path.Combine(GetPackageOutputRoot(packageName), packageVersion);
    }

    private static bool IsSimulateDirectory(string path)
    {
        return string.Equals(Path.GetFileName(path), "Simulate", StringComparison.OrdinalIgnoreCase);
    }
}
