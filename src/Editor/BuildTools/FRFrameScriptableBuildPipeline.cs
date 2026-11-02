using System;
using System.Collections.Generic;
using System.IO;
using YooAsset.Editor;

namespace FRFrame.Editor
{
    /// <summary>
    /// YooAsset ScriptableBuildPipeline with support for video files that must remain raw.
    /// </summary>
    public sealed class FRFrameScriptableBuildPipeline : IBuildPipeline
    {
        public BuildResult Run(BuildParameters buildParameters, bool enableLog)
        {
            if (!(buildParameters is ScriptableBuildParameters))
                throw new ArgumentException($"Invalid build parameter type: '{buildParameters.GetType().Name}'.", nameof(buildParameters));

            var pipeline = new List<IBuildTask>
            {
                new TaskPrepare_SBP(),
                new TaskGetBuildMap_SBP(),
                new TaskBuilding_SBP(),
                new TaskVerifyBuildResult_SBP(),
                // Keep SBP validation intact, then restore raw bytes before encryption and manifest hashing.
                new TaskRestoreRawVideoFiles(),
                new TaskEncryption_SBP(),
                new TaskUpdateBundleInfo_SBP(),
                new TaskCreateManifest_SBP(),
                new TaskCreateReport_SBP(),
                new TaskCreatePackage_SBP(),
                new TaskCopyBundledFiles_SBP(),
                new TaskCreateCatalog_SBP()
            };

            return new BundleBuilder().Run(buildParameters, pipeline, enableLog);
        }

        private sealed class TaskRestoreRawVideoFiles : IBuildTask
        {
            void IBuildTask.Run(BuildContext context)
            {
                BuildMapContext buildMap = context.GetContextObject<BuildMapContext>();
                BuildParametersContext parameters = context.GetContextObject<BuildParametersContext>();
                string pipelineOutputDirectory = parameters.GetPipelineOutputDirectory();

                foreach (BuildBundleInfo bundleInfo in buildMap.Collection)
                {
                    if (bundleInfo.AllPackAssets.Count != 1)
                        continue;

                    string assetPath = bundleInfo.AllPackAssets[0].AssetInfo.AssetPath;
                    string sourceExtension = Path.GetExtension(assetPath);
                    if (!string.Equals(sourceExtension, ".mp4", StringComparison.OrdinalIgnoreCase) ||
                        !string.Equals(Path.GetExtension(bundleInfo.BundleName), sourceExtension, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    string destinationPath = Path.Combine(pipelineOutputDirectory, bundleInfo.BundleName);
                    EditorFileUtility.CopyFile(assetPath, destinationPath, true);
                }
            }
        }
    }
}
