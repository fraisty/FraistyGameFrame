#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using YooAsset;
using YooAsset.Editor;

namespace FRFrame.Editor
{
    public sealed class DefaultYooAssetPackageBuildStep : IFRFrameBuildStep
    {
        public string Name => "Build Default YooAsset Package";
        public bool Enabled { get; }

        public DefaultYooAssetPackageBuildStep(bool enabled = true)
        {
            Enabled = enabled;
        }

        public FRFrameBuildStepResult Execute(FRFrameBuildContext context)
        {
            FRFrameBuildProfile profile = context.BuildProfile;
            if (profile == null)
            {
                return FRFrameBuildStepResult.Failed("Build profile is null.");
            }

            string packageVersion = string.IsNullOrEmpty(profile.defaultYooAssetPackageVersion)
                ? profile.packageVersion
                : profile.defaultYooAssetPackageVersion;
            if (string.IsNullOrEmpty(packageVersion))
            {
                packageVersion = DateTime.Now.ToString("yyyy-MM-dd-HHmmss");
            }

            string outputRoot = ResolveBuildOutputRoot(profile.defaultYooAssetBuildOutputRoot);
            string buildinRoot = ResolveBuildinFileRoot(profile.defaultYooAssetBuildinFileRoot);
            string versionOutputPath = Path.Combine(outputRoot, YooAssetConfigs.GetPlatformName(), profile.defaultYooAssetPackageName, packageVersion);
            if (Directory.Exists(versionOutputPath))
            {
                if (!profile.defaultYooAssetOverwriteOutput)
                {
                    return FRFrameBuildStepResult.Failed($"YooAsset package output already exists: {versionOutputPath}");
                }

                context.Logger.Warning($"[YooAsset] Delete existing default package version output: {versionOutputPath}");
                Directory.Delete(versionOutputPath, true);
            }

            IBuildPipeline pipeline;
            BuildParameters buildParameters;
            switch (profile.defaultYooAssetBuildPipeline)
            {
                case EBuildPipeline.LegacyBuildPipeline:
                    LegacyBuildParameters legacyParameters = new LegacyBuildParameters();
                    pipeline = new LegacyBuildPipeline();
                    buildParameters = legacyParameters;
                    legacyParameters.CompressOption = profile.defaultYooAssetCompressOption;
                    legacyParameters.StripUnityVersion = profile.defaultYooAssetStripUnityVersion;
                    legacyParameters.DisableWriteTypeTree = profile.defaultYooAssetDisableWriteTypeTree;
                    break;
                case EBuildPipeline.ScriptableBuildPipeline:
                    ScriptableBuildParameters scriptableParameters = new ScriptableBuildParameters();
                    pipeline = new FRFrameScriptableBuildPipeline();
                    buildParameters = scriptableParameters;
                    scriptableParameters.CompressOption = profile.defaultYooAssetCompressOption;
                    scriptableParameters.StripUnityVersion = profile.defaultYooAssetStripUnityVersion;
                    scriptableParameters.DisableWriteTypeTree = profile.defaultYooAssetDisableWriteTypeTree;
                    scriptableParameters.BuiltinShadersBundleName = profile.defaultYooAssetBuiltinShadersBundleName;
                    scriptableParameters.MonoScriptsBundleName = profile.defaultYooAssetMonoScriptsBundleName;
                    break;
                default:
                    return FRFrameBuildStepResult.Failed($"Unsupported YooAsset build pipeline: {profile.defaultYooAssetBuildPipeline}");
            }

            buildParameters.BuildOutputRoot = outputRoot;
            buildParameters.BundledFileRoot = buildinRoot;
            buildParameters.BuildPipeline = profile.defaultYooAssetBuildPipeline.ToString();
            buildParameters.BuildBundleType = (int)EBundleType.AssetBundle;
            buildParameters.BuildTarget = context.BuildTarget;
            buildParameters.PackageName = profile.defaultYooAssetPackageName;
            buildParameters.PackageVersion = packageVersion;
            buildParameters.PackageNote = profile.defaultYooAssetPackageNote;
            buildParameters.ClearBuildCacheFiles = profile.defaultYooAssetClearBuildCacheFiles;
            buildParameters.UseAssetDependencyDB = profile.defaultYooAssetUseAssetDependencyDB;
            buildParameters.EnableSharePackRule = profile.defaultYooAssetEnableSharePackRule;
            buildParameters.SingleReferencedPackAlone = profile.defaultYooAssetSingleReferencedPackAlone;
            buildParameters.VerifyBuildingResult = profile.defaultYooAssetVerifyBuildingResult;
            buildParameters.FileNameStyle = profile.defaultYooAssetFileNameStyle;
            buildParameters.BundledCopyOption = profile.defaultYooAssetBuildinFileCopyOption;
            buildParameters.BundledCopyParams = profile.defaultYooAssetBuildinFileCopyParams;
            buildParameters.BundleEncryptor = new EncryptionNone();
            buildParameters.ManifestEncryptor = new ManifestEncryptorNone();
            buildParameters.ManifestDecryptor = new ManifestDecryptorNone();

            context.Logger.Info($"[YooAsset] Build default package: {profile.defaultYooAssetPackageName}, version: {packageVersion}, target: {context.BuildTarget}");
            BuildResult result = pipeline.Run(buildParameters, true);
            if (!result.Success)
            {
                return FRFrameBuildStepResult.Failed($"YooAsset build failed: {profile.defaultYooAssetPackageName}, task: {result.FailedTask}, error: {result.ErrorInfo}\n{result.ErrorStack}");
            }

            context.Logger.Info($"[YooAsset] Default package build output: {result.OutputPackageDirectory}");
            AssetDatabase.Refresh();
            return FRFrameBuildStepResult.Passed();
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
    }
}
#endif
