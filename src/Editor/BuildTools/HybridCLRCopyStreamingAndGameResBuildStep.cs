#if UNITY_EDITOR
namespace FRFrame.Editor
{
    public sealed class HybridCLRCopyStreamingAndGameResBuildStep : IFRFrameBuildStep
    {
        public string Name => "Copy HybridCLR To StreamingAssets And GameRes";
        public bool Enabled { get; }

        public HybridCLRCopyStreamingAndGameResBuildStep(bool enabled = true)
        {
            Enabled = enabled;
        }

        public FRFrameBuildStepResult Execute(FRFrameBuildContext context)
        {
            HybridCLRBuildHelper.ExecuteCopyToStreamingAssetsAndGameRes(context.BuildTarget);
            return FRFrameBuildStepResult.Passed();
        }
    }
}
#endif
