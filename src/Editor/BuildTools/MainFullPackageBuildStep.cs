#if UNITY_EDITOR
namespace FRFrame.Editor
{
    public sealed class MainFullPackageBuildStep : IFRFrameBuildStep
    {
        public string Name => "Build Main Full Package";
        public bool Enabled { get; }

        public MainFullPackageBuildStep(bool enabled = true)
        {
            Enabled = enabled;
        }

        public FRFrameBuildStepResult Execute(FRFrameBuildContext context)
        {
            MainPackageBuildUtility.BuildFullPackage(context);
            return FRFrameBuildStepResult.Passed();
        }
    }
}
#endif
