#if UNITY_EDITOR
namespace FRFrame.Editor
{
    public sealed class FRFrameBuildStepResult
    {
        public bool Success { get; private set; }
        public string Error { get; private set; }
        public double ElapsedSeconds { get; private set; }

        public static FRFrameBuildStepResult Passed(double elapsedSeconds = 0d)
        {
            return new FRFrameBuildStepResult
            {
                Success = true,
                Error = string.Empty,
                ElapsedSeconds = elapsedSeconds
            };
        }

        public static FRFrameBuildStepResult Failed(string error, double elapsedSeconds = 0d)
        {
            return new FRFrameBuildStepResult
            {
                Success = false,
                Error = error ?? string.Empty,
                ElapsedSeconds = elapsedSeconds
            };
        }
    }
}
#endif
