#if UNITY_EDITOR
using System;

namespace FRFrame.Editor
{
    public sealed class FRFrameActionBuildStep : IFRFrameBuildStep
    {
        private readonly Action<FRFrameBuildContext> _execute;

        public string Name { get; }
        public bool Enabled { get; }

        public FRFrameActionBuildStep(string name, Action<FRFrameBuildContext> execute, bool enabled = true)
        {
            Name = name;
            _execute = execute;
            Enabled = enabled;
        }

        public FRFrameBuildStepResult Execute(FRFrameBuildContext context)
        {
            _execute?.Invoke(context);
            return FRFrameBuildStepResult.Passed();
        }
    }
}
#endif
