#if UNITY_EDITOR
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace FRFrame.Editor
{
    public enum FRFrameGamePackagePlayMode
    {
        EditorSimulateMode,
        OfflinePlayMode,
        HostPlayMode,
        WebPlayMode
    }

    [CreateAssetMenu(menuName = "FRFrame/Build/Game Build Profile", fileName = "GameBuildProfile")]
    public sealed class GameBuildProfile : ScriptableObject
    {
        public string gameId = string.Empty;
        public string displayName = string.Empty;
        public string packageName = string.Empty;
        public string packageVersion = "1.0.0";
        public string gameConfigLocation = string.Empty;
        public GamePackageConfig gameConfigAsset;
        public string runtimeFilesRoot = string.Empty;
        public string hotUpdateAssemblyName = string.Empty;
        public bool includeHotUpdateAssembly = true;
        public string yooAssetPackageName = string.Empty;
        public FRFrameGamePackagePlayMode packagePlayMode = FRFrameGamePackagePlayMode.OfflinePlayMode;
        public bool buildYooAsset = true;
        public EBuildPipeline yooAssetBuildPipeline = EBuildPipeline.ScriptableBuildPipeline;
        public ECompressOption yooAssetCompressOption = ECompressOption.LZ4;
        public EFileNameStyle yooAssetFileNameStyle = EFileNameStyle.HashName;
        public EBundledCopyOption yooAssetBuildinFileCopyOption = EBundledCopyOption.None;
        public string yooAssetBuildOutputRoot = string.Empty;
        public string yooAssetBuildinFileRoot = string.Empty;
        public string yooAssetBuildinFileCopyParams = string.Empty;
        public bool yooAssetEnableSharePackRule = true;
        public bool yooAssetSingleReferencedPackAlone = true;
        public bool yooAssetVerifyBuildingResult = true;
        public bool yooAssetClearBuildCacheFiles = false;
        public bool yooAssetUseAssetDependencyDB = false;
        public bool yooAssetStripUnityVersion = false;
        public bool yooAssetDisableWriteTypeTree = false;
        public bool yooAssetOverwriteOutput = true;
        public bool yooAssetClearBuildOutputAfterPackage = true;
        public string yooAssetBuiltinShadersBundleName = string.Empty;
        public string yooAssetMonoScriptsBundleName = string.Empty;
        public string yooAssetPackageNote = string.Empty;
        public string outputRoot = "BuildOutput/Games";
        public bool clearHistoryFullPackages = false;
        public string fullPackageNamePattern = "{GameId}_full_{Version}.zip";
        public string patchPackageNamePattern = "{GameId}_patch_{FromVersion}_to_{ToVersion}.zip";
    }
}
#endif
