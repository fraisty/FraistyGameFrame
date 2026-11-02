#if UNITY_EDITOR
namespace FRFrame.Editor
{
    public interface IFRFrameBuildStep
    {
        string Name { get; }
        bool Enabled { get; }
        FRFrameBuildStepResult Execute(FRFrameBuildContext context);
    }
}
#endif
