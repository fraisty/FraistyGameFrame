#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace FRFrame.Editor
{
    public enum FRFrameBuildMode
    {
        FollowEditorBuildSettings,
        DevelopmentBuild,
        ReleaseBuild,
        OfflineFullApk,
        MinimalApk,
        HotFixOnly,
        BootstrapOnly
    }

    public enum FRBuildLogDefineMode
    {
        FollowBuildMode,
        All,
        InfoAndAbove,
        WarningAndAbove,
        ErrorAndAbove,
        DisableAll,
        Custom
    }

    [CreateAssetMenu(menuName = "FRFrame/Build/FRFrame Build Profile", fileName = "FRFrameBuildProfile")]
    public sealed class FRFrameBuildProfile : ScriptableObject
    {
        public BuildTarget buildTarget = BuildTarget.Android;
        public BuildTargetGroup buildTargetGroup = BuildTargetGroup.Android;
        public string outputRoot = "BuildOutput";
        public string packageVersion = "1.0.0";
        public FRFrameBuildMode buildMode = FRFrameBuildMode.FollowEditorBuildSettings;
        public string scriptingDefineSymbols = string.Empty;
        public FRBuildLogDefineMode logDefineMode = FRBuildLogDefineMode.FollowBuildMode;
        public bool enableRuntimeConsoleInDevelopment = true;
        public bool enableFileLogInDevelopment = true;
        public bool enableFileLogInRelease = false;
        public FRLogLevel developmentMinimumLogLevel = FRLogLevel.Debug;
        public FRLogLevel releaseMinimumLogLevel = FRLogLevel.Warning;
        public string playerOutputName = string.Empty;
        public bool switchBuildTarget = true;
        public bool enableHybridCLR = true;
        public bool buildHotUpdateDll = true;
        public bool buildYooAsset = true;
        public bool buildDefaultYooAsset = true;
        public string defaultYooAssetPackageName = "DefaultPackage";
        public string defaultYooAssetPackageVersion = string.Empty;
        public EBuildPipeline defaultYooAssetBuildPipeline = EBuildPipeline.ScriptableBuildPipeline;
        public ECompressOption defaultYooAssetCompressOption = ECompressOption.LZ4;
        public EFileNameStyle defaultYooAssetFileNameStyle = EFileNameStyle.HashName;
        public EBundledCopyOption defaultYooAssetBuildinFileCopyOption = EBundledCopyOption.ClearAndCopyAll;
        public string defaultYooAssetBuildOutputRoot = string.Empty;
        public string defaultYooAssetBuildinFileRoot = string.Empty;
        public string defaultYooAssetBuildinFileCopyParams = string.Empty;
        public bool defaultYooAssetEnableSharePackRule = true;
        public bool defaultYooAssetSingleReferencedPackAlone = true;
        public bool defaultYooAssetVerifyBuildingResult = true;
        public bool defaultYooAssetClearBuildCacheFiles = false;
        public bool defaultYooAssetUseAssetDependencyDB = false;
        public bool defaultYooAssetStripUnityVersion = false;
        public bool defaultYooAssetDisableWriteTypeTree = false;
        public bool defaultYooAssetOverwriteOutput = true;
        public string defaultYooAssetBuiltinShadersBundleName = string.Empty;
        public string defaultYooAssetMonoScriptsBundleName = string.Empty;
        public string defaultYooAssetPackageNote = string.Empty;
        public bool exportBootstrap = true;
        public bool buildPlayer = false;
        public bool clearHistoryMainFullPackages = false;
        public string mainFullPackageNamePattern = "MainPackage_full_{Version}.zip";
        public string mainPatchPackageNamePattern = "MainPackage_patch_{FromVersion}_to_{ToVersion}.zip";
        public string mainOldFileManifestPath = string.Empty;
        public string publishPackageNamePattern = "PublishPackage_{Version}.zip";
        public string publishOutputRoot = string.Empty;
        public string publishMainPackageZipPath = string.Empty;
        public List<string> publishGamePackageZipPaths = new List<string>();
        public List<GameBuildProfile> selectedGameProfiles = new List<GameBuildProfile>();
    }
}
#endif
