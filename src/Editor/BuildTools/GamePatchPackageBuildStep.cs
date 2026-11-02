#if UNITY_EDITOR
namespace FRFrame.Editor
{
    public sealed class GamePatchPackageBuildStep : IFRFrameBuildStep
    {
        private readonly string _oldFileManifestPath;

        public string Name => "Build Game Patch Package";
        public bool Enabled { get; }

        public GamePatchPackageBuildStep(string oldFileManifestPath, bool enabled = true)
        {
            _oldFileManifestPath = oldFileManifestPath;
            Enabled = enabled;
        }

        public FRFrameBuildStepResult Execute(FRFrameBuildContext context)
        {
            GamePackageBuildUtility.BuildPatchPackage(context, _oldFileManifestPath);
            return FRFrameBuildStepResult.Passed();
        }
    }
}
#endif
