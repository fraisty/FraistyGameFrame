#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace FRFrame.Editor
{
    public sealed class FRFramePlayerBuildStep : IFRFrameBuildStep
    {
        public string Name => "Build Player";
        public bool Enabled { get; }

        public FRFramePlayerBuildStep(bool enabled = true)
        {
            Enabled = enabled;
        }

        public FRFrameBuildStepResult Execute(FRFrameBuildContext context)
        {
            string[] scenes = EditorBuildSettings.scenes
                .Where(scene => scene.enabled)
                .Select(scene => scene.path)
                .ToArray();
            if (scenes.Length == 0)
            {
                return FRFrameBuildStepResult.Failed("No enabled EditorBuildSettings scenes.");
            }

            string locationPathName = GetPlayerOutputPath(context);
            Directory.CreateDirectory(Path.GetDirectoryName(locationPathName));

            BuildPlayerOptions options = new BuildPlayerOptions
            {
                scenes = scenes,
                locationPathName = locationPathName,
                target = context.BuildTarget,
                targetGroup = context.BuildProfile.buildTargetGroup,
                options = GetBuildOptions(context.BuildProfile)
            };

            context.Logger.Info($"[PlayerBuild] Output: {locationPathName}");
            context.Logger.Info($"[PlayerBuild] Options: {options.options}");
            BuildReport report = BuildPipeline.BuildPlayer(options);
            BuildSummary summary = report.summary;
            WriteBuildReport(context, report, locationPathName);
            context.Logger.Info($"[PlayerBuild] Result: {summary.result}, size: {summary.totalSize}, time: {summary.totalTime}");

            return summary.result == BuildResult.Succeeded
                ? FRFrameBuildStepResult.Passed(summary.totalTime.TotalSeconds)
                : FRFrameBuildStepResult.Failed(summary.result.ToString(), summary.totalTime.TotalSeconds);
        }

        private static BuildOptions GetBuildOptions(FRFrameBuildProfile profile)
        {
            if (profile == null || profile.buildMode == FRFrameBuildMode.FollowEditorBuildSettings)
            {
                return EditorUserBuildSettings.development ? BuildOptions.Development : BuildOptions.None;
            }

            if (profile.buildMode == FRFrameBuildMode.DevelopmentBuild)
            {
                return BuildOptions.Development;
            }

            return BuildOptions.None;
        }

        private static string GetPlayerOutputPath(FRFrameBuildContext context)
        {
            string productName = string.IsNullOrEmpty(context.BuildProfile.playerOutputName) ? PlayerSettings.productName : context.BuildProfile.playerOutputName;
            if (string.IsNullOrEmpty(productName))
            {
                productName = "UnityPlayer";
            }

            string directory = Path.Combine(context.OutputRoot, "Player", context.BuildTarget.ToString());
            switch (context.BuildTarget)
            {
                case BuildTarget.Android:
                    return Path.Combine(directory, productName + ".apk");
                case BuildTarget.StandaloneWindows:
                case BuildTarget.StandaloneWindows64:
                    return Path.Combine(directory, productName + ".exe");
                default:
                    return Path.Combine(directory, productName);
            }
        }

        private static void WriteBuildReport(FRFrameBuildContext context, BuildReport report, string locationPathName)
        {
            BuildSummary summary = report.summary;
            string reportPath = Path.Combine(context.OutputRoot, "BuildRecords", "Player", DateTime.Now.ToString("yyyyMMdd_HHmmss") + "_build_report.json");
            Directory.CreateDirectory(Path.GetDirectoryName(reportPath));
            FRFrameBuildProfile profile = context.BuildProfile;
            string json = JsonUtility.ToJson(new PlayerBuildReportRecord
            {
                target = context.BuildTarget.ToString(),
                outputPath = locationPathName,
                result = summary.result.ToString(),
                totalSize = summary.totalSize,
                totalTime = summary.totalTime.ToString(),
                buildStartedAt = summary.buildStartedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                buildEndedAt = summary.buildEndedAt.ToString("yyyy-MM-dd HH:mm:ss"),
                logDefineMode = profile.logDefineMode.ToString(),
                developmentMinimumLogLevel = profile.developmentMinimumLogLevel.ToString(),
                releaseMinimumLogLevel = profile.releaseMinimumLogLevel.ToString(),
                enableFileLogInDevelopment = profile.enableFileLogInDevelopment,
                enableFileLogInRelease = profile.enableFileLogInRelease
            }, true);
            File.WriteAllText(reportPath, json);
            context.Logger.Info($"[PlayerBuild] Report: {reportPath}");
        }

        [Serializable]
        private sealed class PlayerBuildReportRecord
        {
            public string target;
            public string outputPath;
            public string result;
            public ulong totalSize;
            public string totalTime;
            public string buildStartedAt;
            public string buildEndedAt;
            public string logDefineMode;
            public string developmentMinimumLogLevel;
            public string releaseMinimumLogLevel;
            public bool enableFileLogInDevelopment;
            public bool enableFileLogInRelease;
        }
    }
}
#endif
