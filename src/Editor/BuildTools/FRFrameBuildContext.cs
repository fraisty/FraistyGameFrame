#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;

namespace FRFrame.Editor
{
    public sealed class FRFrameBuildContext
    {
        public FRFrameBuildProfile BuildProfile { get; }
        public GameBuildProfile GameProfile { get; }
        public BuildTarget BuildTarget { get; }
        public string OutputRoot { get; }
        public FRFrameBuildLogger Logger { get; }
        public Dictionary<string, object> Data { get; } = new Dictionary<string, object>();

        public FRFrameBuildContext(
            FRFrameBuildProfile buildProfile,
            GameBuildProfile gameProfile,
            BuildTarget buildTarget,
            string outputRoot,
            FRFrameBuildLogger logger)
        {
            BuildProfile = buildProfile;
            GameProfile = gameProfile;
            BuildTarget = buildTarget;
            OutputRoot = outputRoot;
            Logger = logger ?? new FRFrameBuildLogger();
        }
    }
}
#endif
