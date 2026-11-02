#if UNITY_EDITOR
namespace FRFrame.Editor
{
    public sealed class BootstrapConfigExportBuildStep : IFRFrameBuildStep
    {
        public string Name => "Export Bootstrap Config";
        public bool Enabled { get; }

        public BootstrapConfigExportBuildStep(bool enabled = true)
        {
            Enabled = enabled;
        }

        public FRFrameBuildStepResult Execute(FRFrameBuildContext context)
        {
            BootstrapConfigExporter.Export(context);
            return FRFrameBuildStepResult.Passed();
        }
    }
}
#endif
