using System;
using UnityEngine;

namespace FRFrame
{
    [Serializable]
    public sealed class AudioGroupConfig
    {
        public AudioType Type;
        public string MixerGroupName;
        public string VolumeParameter;
        public int AgentCount = 8;
        [Range(0f, 1f)] public float Volume = 1f;
        public bool Mute;
        public bool AllowSteal = true;
        public bool AvoidStealSamePriority;
        public AudioRolloffMode RolloffMode = AudioRolloffMode.Logarithmic;
        public float MinDistance = 1f;
        public float MaxDistance = 500f;
    }
}
