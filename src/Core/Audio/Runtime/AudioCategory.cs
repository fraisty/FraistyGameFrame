using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Audio;

namespace FRFrame
{
    internal sealed class AudioCategory
    {
        private readonly AudioManager _manager;
        private readonly List<AudioAgent> _agents = new List<AudioAgent>();
        private readonly string _volumeParameter;
        private readonly AudioMixer _mixer;
        private bool _paused;
        private bool _mute;
        private float _volume = 1f;

        public AudioType Type { get; }
        public string Name { get; }
        public bool AllowSteal { get; }
        public bool AvoidStealSamePriority { get; }
        public IReadOnlyList<AudioAgent> Agents => _agents;
        public bool Paused => _paused;

        public bool Mute
        {
            get => _mute;
            set
            {
                _mute = value;
                ApplyMixerVolume();
            }
        }

        public float Volume
        {
            get => _volume;
            set
            {
                _volume = Mathf.Clamp01(value);
                ApplyMixerVolume();
            }
        }

        public AudioCategory(AudioManager manager, AudioGroupConfig config, Transform parent, AudioMixer mixer)
        {
            _manager = manager;
            _mixer = mixer;
            Type = config.Type;
            Name = string.IsNullOrEmpty(config.MixerGroupName) ? config.Type.ToString() : config.MixerGroupName;
            _volumeParameter = string.IsNullOrEmpty(config.VolumeParameter) ? $"{config.Type}Volume" : config.VolumeParameter;
            _volume = Mathf.Clamp01(config.Volume);
            _mute = config.Mute;
            AllowSteal = config.AllowSteal;
            AvoidStealSamePriority = config.AvoidStealSamePriority;

            Transform categoryRoot = new GameObject(Name).transform;
            categoryRoot.SetParent(parent, false);
            AudioMixerGroup mixerGroup = FindMixerGroup(mixer, Name);
            int count = Mathf.Max(1, config.AgentCount);
            for (int i = 0; i < count; i++)
                _agents.Add(new AudioAgent(manager, this, categoryRoot, i, config, mixerGroup));

            ApplyMixerVolume();
        }

        public AudioAgent SelectAgent(AudioPlayOptions options)
        {
            for (int i = 0; i < _agents.Count; i++)
            {
                if (_agents[i].IsIdle)
                    return _agents[i];
            }

            if (!AllowSteal)
                return null;

            AudioAgent selected = null;
            for (int i = 0; i < _agents.Count; i++)
            {
                AudioAgent agent = _agents[i];
                if (agent.Priority < options.Priority)
                {
                    if (selected == null || agent.Priority < selected.Priority || agent.Elapsed > selected.Elapsed)
                        selected = agent;
                }
                else if (!AvoidStealSamePriority && agent.Priority == options.Priority)
                {
                    if (selected == null || agent.Elapsed > selected.Elapsed)
                        selected = agent;
                }
            }

            return selected;
        }

        public void Update(float deltaTime, float unscaledDeltaTime)
        {
            for (int i = 0; i < _agents.Count; i++)
                _agents[i].Update(deltaTime, unscaledDeltaTime);
        }

        public void StopAll(float fadeOut, AudioStopReason reason)
        {
            for (int i = 0; i < _agents.Count; i++)
                _agents[i].Stop(fadeOut, reason);
        }

        public void StopNonPersistent()
        {
            for (int i = 0; i < _agents.Count; i++)
            {
                if (!_agents[i].PersistentAcrossScene)
                    _agents[i].Stop(0f, AudioStopReason.SceneChanged);
            }
        }

        public void PauseAll()
        {
            _paused = true;
            for (int i = 0; i < _agents.Count; i++)
                _agents[i].Pause();
        }

        public void ResumeAll()
        {
            _paused = false;
            for (int i = 0; i < _agents.Count; i++)
                _agents[i].Resume();
        }

        public AudioAgent FindAgent(int id)
        {
            for (int i = 0; i < _agents.Count; i++)
            {
                if (_agents[i].Id == id)
                    return _agents[i];
            }

            return null;
        }

        public AudioCategorySnapshot CreateSnapshot()
        {
            var snapshot = new AudioCategorySnapshot
            {
                Type = Type,
                Name = Name,
                Volume = Volume,
                Mute = Mute,
                Paused = Paused,
                AgentCount = _agents.Count
            };

            for (int i = 0; i < _agents.Count; i++)
            {
                AudioAgentSnapshot agent = _agents[i].CreateSnapshot();
                snapshot.Agents.Add(agent);
                switch (agent.State)
                {
                    case AudioAgentState.Idle:
                        snapshot.IdleCount++;
                        break;
                    case AudioAgentState.Loading:
                        snapshot.LoadingCount++;
                        break;
                    default:
                        snapshot.PlayingCount++;
                        break;
                }
            }

            return snapshot;
        }

        public void Destroy()
        {
            for (int i = 0; i < _agents.Count; i++)
                _agents[i].Destroy();
            _agents.Clear();
        }

        private void ApplyMixerVolume()
        {
            if (_mixer == null || string.IsNullOrEmpty(_volumeParameter))
                return;
            _mixer.SetFloat(_volumeParameter, AudioManager.LinearToDb(_mute ? 0f : _volume));
        }

        private static AudioMixerGroup FindMixerGroup(AudioMixer mixer, string name)
        {
            if (mixer == null)
                return null;

            AudioMixerGroup[] groups = mixer.FindMatchingGroups(name);
            if (groups != null && groups.Length > 0)
                return groups[0];

            groups = mixer.FindMatchingGroups("Master");
            return groups != null && groups.Length > 0 ? groups[0] : null;
        }
    }
}
