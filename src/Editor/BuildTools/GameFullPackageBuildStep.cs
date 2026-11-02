#if UNITY_EDITOR
namespace FRFrame.Editor
{
    public sealed class GameFullPackageBuildStep : IFRFrameBuildStep
    {
        public string Name => "Build Game Full Package";
        public bool Enabled { get; }

        public GameFullPackageBuildStep(bool enabled = true)
        {
            Enabled = enabled;
        }

        public FRFrameBuildStepResult Execute(FRFrameBuildContext context)
        {
            GamePackageBuildUtility.BuildFullPackage(context);
            return FRFrameBuildStepResult.Passed();
        }
    }
}
#endif
