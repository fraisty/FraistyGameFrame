#if UNITY_EDITOR
namespace FRFrame.Editor
{
    public sealed class YooAssetConfigExportBuildStep : IFRFrameBuildStep
    {
        public string Name => "Export YooAsset Config To StreamingAssets";
        public bool Enabled { get; }

        public YooAssetConfigExportBuildStep(bool enabled = true)
        {
            Enabled = enabled;
        }

        public FRFrameBuildStepResult Execute(FRFrameBuildContext context)
        {
            YooAssetPackageHelper.ExecuteCreateYooAssetConfigJson();
            return FRFrameBuildStepResult.Passed();
        }
    }
}
#endif
