#if UNITY_EDITOR
namespace FRFrame.Editor
{
    public sealed class YooAssetPackageZipBuildStep : IFRFrameBuildStep
    {
        public string Name => "Build And Pack YooAsset Packages";
        public bool Enabled { get; }

        public YooAssetPackageZipBuildStep(bool enabled = true)
        {
            Enabled = enabled;
        }

        public FRFrameBuildStepResult Execute(FRFrameBuildContext context)
        {
            YooAssetPackageHelper.ExecuteBuildAndPackYooAssetPackage(context);
            return FRFrameBuildStepResult.Passed();
        }
    }
}
#endif
