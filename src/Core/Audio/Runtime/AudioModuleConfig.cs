using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace FRFrame
{
    [CreateAssetMenu(menuName = "FRFrame/Audio/Audio Module Config")]
    public sealed class AudioModuleConfig : ScriptableObject
    {
        public AudioMixer Mixer;
        [Range(0f, 1f)] public float MasterVolume = 1f;
        public bool MasterMute;
        public List<AudioGroupConfig> Groups = new List<AudioGroupConfig>
        {
            new AudioGroupConfig { Type = AudioType.Music, MixerGroupName = "Music", VolumeParameter = "MusicVolume", AgentCount = 2 },
            new AudioGroupConfig { Type = AudioType.Ambience, MixerGroupName = "Ambience", VolumeParameter = "AmbienceVolume", AgentCount = 4 },
            new AudioGroupConfig { Type = AudioType.Sound, MixerGroupName = "Sound", VolumeParameter = "SoundVolume", AgentCount = 16 },
            new AudioGroupConfig { Type = AudioType.UISound, MixerGroupName = "UISound", VolumeParameter = "UISoundVolume", AgentCount = 8 },
            new AudioGroupConfig { Type = AudioType.Voice, MixerGroupName = "Voice", VolumeParameter = "VoiceVolume", AgentCount = 4 }
        };
    }
}
