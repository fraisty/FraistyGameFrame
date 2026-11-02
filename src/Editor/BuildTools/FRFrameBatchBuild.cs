#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using UnityEngine.Rendering;

namespace FRFrame.Editor
{
    public static class FRFrameBatchBuild
    {
        private const string DefaultBuildProfilePath = "Assets/Settings/FRFrameBuildProfile.asset";

        public static void BuildGameFullPackage()
        {
            if (!Application.isBatchMode)
            {
                throw new InvalidOperationException("FRFrameBatchBuild.BuildGameFullPackage must run in Unity batch mode.");
            }

            int exitCode = 0;
            try
            {
                ExecuteGameFullPackage();
            }
            catch (Exception exception)
            {
                exitCode = 1;
                Debug.LogException(exception);
            }
            finally
            {
                AssetDatabase.SaveAssets();
            }

            EditorApplication.Exit(exitCode);
        }

        private static void ExecuteGameFullPackage()
        {
            string buildProfilePath = NormalizeAssetPath(GetArgumentValue("-frBuildProfile") ?? DefaultBuildProfilePath);
            FRFrameBuildProfile buildProfile = LoadAsset<FRFrameBuildProfile>(buildProfilePath, "FRFrame build profile");
            GameBuildProfile gameProfile = ResolveGameProfile(buildProfile);
            FRFrameBuildLogger logger = new FRFrameBuildLogger();
            string outputRoot = string.IsNullOrEmpty(buildProfile.outputRoot) ? "BuildOutput" : buildProfile.outputRoot;

            logger.Info($"[BatchBuild] Project: {Path.GetFullPath(Path.Combine(Application.dataPath, ".."))}");
            logger.Info($"[BatchBuild] Build profile: {buildProfilePath}");
            logger.Info($"[BatchBuild] Game profile: {AssetDatabase.GetAssetPath(gameProfile)}");
            logger.Info($"[BatchBuild] BatchMode={Application.isBatchMode}, GraphicsDevice={SystemInfo.graphicsDeviceType}, ActiveTarget={EditorUserBuildSettings.activeBuildTarget}");
            if (SystemInfo.graphicsDeviceType != GraphicsDeviceType.Null)
            {
                logger.Warning("[BatchBuild] Graphics device is active. Add -nographics to isolate texture GPU upload memory.");
            }

            FRFrameBuildContext context = new FRFrameBuildContext(
                buildProfile,
                gameProfile,
                buildProfile.buildTarget,
                outputRoot,
                logger);
            FRFrameBuildPipeline pipeline = new FRFrameBuildPipeline()
                .Add(new FRFramePrepareBuildSettingsStep())
                .Add(new GameFullPackageBuildStep());

            FRFrameBuildStepResult result = pipeline.Execute(context);
            if (!result.Success)
            {
                throw new BuildFailedException(result.Error);
            }

            logger.Info($"[BatchBuild] Game full package completed in {result.ElapsedSeconds:F2}s.");
        }

        private static GameBuildProfile ResolveGameProfile(FRFrameBuildProfile buildProfile)
        {
            string gameProfilePath = GetArgumentValue("-frGameProfile");
            if (!string.IsNullOrWhiteSpace(gameProfilePath))
            {
                return LoadAsset<GameBuildProfile>(NormalizeAssetPath(gameProfilePath), "game build profile");
            }

            GameBuildProfile[] profiles = buildProfile.selectedGameProfiles?
                .Where(profile => profile != null)
                .ToArray() ?? Array.Empty<GameBuildProfile>();
            string gameId = GetArgumentValue("-frGameId");
            if (!string.IsNullOrWhiteSpace(gameId))
            {
                GameBuildProfile matchedProfile = profiles.FirstOrDefault(profile =>
                    string.Equals(profile.gameId, gameId, StringComparison.OrdinalIgnoreCase));
                if (matchedProfile == null)
                {
                    throw new BuildFailedException($"Game profile not found in selectedGameProfiles: {gameId}");
                }

                return matchedProfile;
            }

            if (profiles.Length != 1)
            {
                throw new BuildFailedException(
                    $"Build profile contains {profiles.Length} selected game profiles. " +
                    "Pass -frGameProfile <asset path> or -frGameId <game id>.");
            }

            return profiles[0];
        }

        private static T LoadAsset<T>(string assetPath, string label) where T : UnityEngine.Object
        {
            T asset = AssetDatabase.LoadAssetAtPath<T>(assetPath);
            if (asset == null)
            {
                throw new BuildFailedException($"Unable to load {label}: {assetPath}");
            }

            return asset;
        }

        private static string NormalizeAssetPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return string.Empty;
            }

            string normalizedPath = path.Trim().Replace('\\', '/');
            if (!Path.IsPathRooted(normalizedPath))
            {
                return normalizedPath;
            }

            string projectRoot = Path.GetFullPath(Path.Combine(Application.dataPath, "..")).Replace('\\', '/').TrimEnd('/');
            string fullPath = Path.GetFullPath(normalizedPath).Replace('\\', '/');
            if (!fullPath.StartsWith(projectRoot + "/", StringComparison.OrdinalIgnoreCase))
            {
                throw new BuildFailedException($"Asset path is outside the project: {path}");
            }

            return fullPath.Substring(projectRoot.Length + 1);
        }

        private static string GetArgumentValue(string argumentName)
        {
            string[] arguments = Environment.GetCommandLineArgs();
            for (int i = 0; i < arguments.Length; i++)
            {
                string argument = arguments[i];
                if (argument.StartsWith(argumentName + "=", StringComparison.OrdinalIgnoreCase))
                {
                    return argument.Substring(argumentName.Length + 1).Trim('"');
                }

                if (string.Equals(argument, argumentName, StringComparison.OrdinalIgnoreCase) && i + 1 < arguments.Length)
                {
                    return arguments[i + 1].Trim('"');
                }
            }

            return null;
        }
    }
}
#endif
