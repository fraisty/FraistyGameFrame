#if UNITY_EDITOR
namespace FRFrame.Editor
{
    public sealed class MainPatchPackageBuildStep : IFRFrameBuildStep
    {
        private readonly string _oldFileManifestPath;

        public string Name => "Build Main Patch Package";
        public bool Enabled { get; }

        public MainPatchPackageBuildStep(string oldFileManifestPath, bool enabled = true)
        {
            _oldFileManifestPath = oldFileManifestPath;
            Enabled = enabled;
        }

        public FRFrameBuildStepResult Execute(FRFrameBuildContext context)
        {
            MainPackageBuildUtility.BuildPatchPackage(context, _oldFileManifestPath);
            return FRFrameBuildStepResult.Passed();
        }
    }
}
#endif
