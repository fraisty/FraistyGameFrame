#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace FRFrame.Editor
{
    public sealed class FRFrameBuildPipeline
    {
        private readonly List<IFRFrameBuildStep> _steps = new List<IFRFrameBuildStep>();

        public IReadOnlyList<IFRFrameBuildStep> Steps => _steps;

        public FRFrameBuildPipeline Add(IFRFrameBuildStep step)
        {
            if (step != null)
            {
                _steps.Add(step);
            }

            return this;
        }

        public FRFrameBuildStepResult Execute(FRFrameBuildContext context)
        {
            if (context == null)
            {
                return FRFrameBuildStepResult.Failed("Build context is null.");
            }

            Stopwatch totalWatch = Stopwatch.StartNew();
            foreach (IFRFrameBuildStep step in _steps)
            {
                if (step == null || !step.Enabled)
                {
                    continue;
                }

                Stopwatch stepWatch = Stopwatch.StartNew();
                context.Logger.Info($"[Build] Start step: {step.Name}");

                FRFrameBuildStepResult result;
                try
                {
                    result = step.Execute(context);
                }
                catch (Exception e)
                {
                    stepWatch.Stop();
                    result = FRFrameBuildStepResult.Failed(e.ToString(), stepWatch.Elapsed.TotalSeconds);
                }

                stepWatch.Stop();
                if (!result.Success)
                {
                    context.Logger.Error($"[Build] Step failed: {step.Name}\n{result.Error}");
                    return FRFrameBuildStepResult.Failed(result.Error, totalWatch.Elapsed.TotalSeconds);
                }

                context.Logger.Info($"[Build] Step done: {step.Name}, {stepWatch.Elapsed.TotalSeconds:F2}s");
            }

            totalWatch.Stop();
            return FRFrameBuildStepResult.Passed(totalWatch.Elapsed.TotalSeconds);
        }
    }
}
#endif
