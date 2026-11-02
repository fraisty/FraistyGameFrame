#if UNITY_EDITOR
namespace FRFrame.Editor
{
    public sealed class HybridCLRCopyStreamingBuildStep : IFRFrameBuildStep
    {
        public string Name => "Copy HybridCLR To StreamingAssets";
        public bool Enabled { get; }

        public HybridCLRCopyStreamingBuildStep(bool enabled = true)
        {
            Enabled = enabled;
        }

        public FRFrameBuildStepResult Execute(FRFrameBuildContext context)
        {
            HybridCLRBuildHelper.ExecuteCopyToStreamingAssetsOnly(context.BuildTarget);
            return FRFrameBuildStepResult.Passed();
        }
    }
}
#endif
