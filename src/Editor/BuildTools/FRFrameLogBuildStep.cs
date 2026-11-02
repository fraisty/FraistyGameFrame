#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;

namespace FRFrame.Editor
{
    public sealed class FRFrameLogBuildStep : IFRFrameBuildStep
    {
        public string Name => "Sync Log Define Symbols";
        public bool Enabled { get; }

        public FRFrameLogBuildStep(bool enabled = true)
        {
            Enabled = enabled;
        }

        public FRFrameBuildStepResult Execute(FRFrameBuildContext context)
        {
            FRFrameBuildProfile profile = context.BuildProfile;
            if (profile == null)
            {
                return FRFrameBuildStepResult.Failed("Build profile is null.");
            }

            NamedBuildTarget namedBuildTarget = NamedBuildTarget.FromBuildTargetGroup(profile.buildTargetGroup);
            HashSet<string> defines = FRLogScriptingDefineSymbols.ReadDefines(namedBuildTarget);
            if (profile.logDefineMode == FRBuildLogDefineMode.Custom)
            {
                defines.Remove(FRLogDefineSymbols.File);
                if (ShouldEnableFileLog(profile))
                {
                    defines.Add(FRLogDefineSymbols.File);
                }
            }
            else
            {
                FRLogScriptingDefineSymbols.RemoveLogDefines(defines);
                foreach (string define in GetLogDefines(profile))
                {
                    defines.Add(define);
                }
            }

            string result = string.Join(";", defines.OrderBy(value => value));
            PlayerSettings.SetScriptingDefineSymbols(namedBuildTarget, result);
            context.Logger.Info($"[LogBuild] Define mode: {profile.logDefineMode}, defines: {result}");
            context.Logger.Info($"[LogBuild] Development level: {profile.developmentMinimumLogLevel}, release level: {profile.releaseMinimumLogLevel}, file dev/release: {profile.enableFileLogInDevelopment}/{profile.enableFileLogInRelease}");
            return FRFrameBuildStepResult.Passed();
        }

        private static IEnumerable<string> GetLogDefines(FRFrameBuildProfile profile)
        {
            FRBuildLogDefineMode mode = profile.logDefineMode == FRBuildLogDefineMode.FollowBuildMode ? GetBuildModeDefineMode(profile) : profile.logDefineMode;
            switch (mode)
            {
                case FRBuildLogDefineMode.All:
                    yield return FRLogDefineSymbols.EnableLog;
                    break;
                case FRBuildLogDefineMode.InfoAndAbove:
                    yield return FRLogDefineSymbols.InfoAndAbove;
                    break;
                case FRBuildLogDefineMode.WarningAndAbove:
                    yield return FRLogDefineSymbols.WarningAndAbove;
                    break;
                case FRBuildLogDefineMode.ErrorAndAbove:
                    yield return FRLogDefineSymbols.ErrorAndAbove;
                    break;
                case FRBuildLogDefineMode.Custom:
                    foreach (string define in GetCustomLevelDefines(profile))
                    {
                        yield return define;
                    }
                    break;
            }

            if (ShouldEnableFileLog(profile))
            {
                yield return FRLogDefineSymbols.File;
            }
        }

        private static FRBuildLogDefineMode GetBuildModeDefineMode(FRFrameBuildProfile profile)
        {
            if (profile.buildMode == FRFrameBuildMode.DevelopmentBuild)
            {
                return FRBuildLogDefineMode.InfoAndAbove;
            }

            if (profile.buildMode == FRFrameBuildMode.ReleaseBuild)
            {
                return FRBuildLogDefineMode.WarningAndAbove;
            }

            return EditorUserBuildSettings.development ? FRBuildLogDefineMode.InfoAndAbove : FRBuildLogDefineMode.WarningAndAbove;
        }

        private static IEnumerable<string> GetCustomLevelDefines(FRFrameBuildProfile profile)
        {
            FRLogLevel minimumLevel = EditorUserBuildSettings.development ? profile.developmentMinimumLogLevel : profile.releaseMinimumLogLevel;
            if (minimumLevel <= FRLogLevel.Debug)
            {
                yield return FRLogDefineSymbols.DebugAndAbove;
            }
            else if (minimumLevel <= FRLogLevel.Info)
            {
                yield return FRLogDefineSymbols.InfoAndAbove;
            }
            else if (minimumLevel <= FRLogLevel.Warning)
            {
                yield return FRLogDefineSymbols.WarningAndAbove;
            }
            else if (minimumLevel <= FRLogLevel.Error)
            {
                yield return FRLogDefineSymbols.ErrorAndAbove;
            }
            else
            {
                yield return FRLogDefineSymbols.FatalAndAbove;
            }
        }

        private static bool ShouldEnableFileLog(FRFrameBuildProfile profile)
        {
            return EditorUserBuildSettings.development ? profile.enableFileLogInDevelopment : profile.enableFileLogInRelease;
        }
    }
}
#endif
