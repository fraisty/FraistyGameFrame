using System.Collections.Generic;

namespace FRFrame
{
    public sealed class AudioDiagnosticsSnapshot
    {
        public int PlayingCount;
        public int LoadingCount;
        public int PreloadedClipCount;
        public int StealCount;
        public int PlayFailureCount;
        public List<AudioCategorySnapshot> Categories = new List<AudioCategorySnapshot>();
    }

    public sealed class AudioCategorySnapshot
    {
        public AudioType Type;
        public string Name;
        public float Volume;
        public bool Mute;
        public bool Paused;
        public int AgentCount;
        public int IdleCount;
        public int LoadingCount;
        public int PlayingCount;
        public List<AudioAgentSnapshot> Agents = new List<AudioAgentSnapshot>();
    }

    public sealed class AudioAgentSnapshot
    {
        public int Id;
        public AudioAgentState State;
        public string Location;
        public string PackageName;
        public string ClipName;
        public bool Loop;
        public int Priority;
        public float Volume;
        public float Elapsed;
    }
}
