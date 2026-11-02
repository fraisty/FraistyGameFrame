#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.Build;

namespace FRFrame.Editor
{
    public sealed class FRFramePrepareBuildSettingsStep : IFRFrameBuildStep
    {
        public string Name => "Prepare Build Settings";
        public bool Enabled { get; }

        public FRFramePrepareBuildSettingsStep(bool enabled = true)
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

            BuildTargetGroup targetGroup = BuildPipeline.GetBuildTargetGroup(profile.buildTarget);
            if (targetGroup == BuildTargetGroup.Unknown)
            {
                return FRFrameBuildStepResult.Failed($"Cannot resolve build target group for {profile.buildTarget}.");
            }

            if (profile.buildTargetGroup != targetGroup)
            {
                context.Logger.Warning($"[PrepareBuild] Profile target group {profile.buildTargetGroup} does not match {profile.buildTarget}; using {targetGroup}.");
            }

            if (profile.switchBuildTarget && EditorUserBuildSettings.activeBuildTarget != profile.buildTarget)
            {
                context.Logger.Info($"[PrepareBuild] Switch target: {EditorUserBuildSettings.activeBuildTarget} -> {profile.buildTarget}");
                if (!EditorUserBuildSettings.SwitchActiveBuildTarget(targetGroup, profile.buildTarget))
                {
                    return FRFrameBuildStepResult.Failed($"Switch build target failed: {profile.buildTarget}");
                }
            }
            else if (EditorUserBuildSettings.activeBuildTarget != profile.buildTarget)
            {
                return FRFrameBuildStepResult.Failed($"Active build target is {EditorUserBuildSettings.activeBuildTarget}, but profile target is {profile.buildTarget}. Enable auto switch target or switch platform manually.");
            }

            if (!string.IsNullOrWhiteSpace(profile.scriptingDefineSymbols))
            {
                PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.FromBuildTargetGroup(targetGroup), profile.scriptingDefineSymbols);
                context.Logger.Info($"[PrepareBuild] Scripting defines: {profile.scriptingDefineSymbols}");
            }

            AssetDatabase.Refresh();
            return FRFrameBuildStepResult.Passed();
        }
    }
}
#endif
