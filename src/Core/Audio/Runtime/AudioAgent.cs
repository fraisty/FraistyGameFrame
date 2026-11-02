using UnityEngine;
using UnityEngine.Audio;

namespace FRFrame
{
    internal sealed class AudioAgent
    {
        private readonly AudioManager _manager;
        private readonly AudioCategory _category;
        private readonly Transform _transform;
        private readonly AudioSource _source;
        private ResourceHandle<AudioClip> _resourceHandle;
        private AudioPlayOptions _options;
        private bool _hasResourceHandle;
        private bool _hasFollowTarget;
        private bool _stopReported;
        private float _baseVolume = 1f;
        private float _fadeTimer;
        private float _fadeDuration;
        private float _fadeStartVolume;
        private float _fadeTargetVolume;
        private Vector3 _lastFollowPosition;

        public int Id { get; private set; }
        public AudioAgentState State { get; private set; } = AudioAgentState.Idle;
        public ResourceKey Key { get; private set; }
        public float Elapsed { get; private set; }
        public int Priority => _options != null ? _options.Priority : 0;
        public bool IsIdle => State == AudioAgentState.Idle;
        public bool IsPlayingLike => State == AudioAgentState.Playing || State == AudioAgentState.FadingIn || State == AudioAgentState.FadingOut || State == AudioAgentState.Paused;
        public bool PersistentAcrossScene => _options != null && _options.PersistentAcrossScene;

        public AudioAgent(AudioManager manager, AudioCategory category, Transform parent, int index, AudioGroupConfig config, AudioMixerGroup mixerGroup)
        {
            _manager = manager;
            _category = category;
            GameObject go = new GameObject($"AudioAgent_{category.Type}_{index}");
            go.transform.SetParent(parent, false);
            _transform = go.transform;
            _source = go.AddComponent<AudioSource>();
            _source.playOnAwake = false;
            _source.outputAudioMixerGroup = mixerGroup;
            ApplyGroupConfig(config);
        }

        public void ApplyGroupConfig(AudioGroupConfig config)
        {
            _source.rolloffMode = config.RolloffMode;
            _source.minDistance = config.MinDistance;
            _source.maxDistance = config.MaxDistance;
        }

        public void PlayLoaded(int id, ResourceKey key, ResourceHandle<AudioClip> resourceHandle, AudioPlayOptions options)
        {
            ClearCurrent(false, AudioStopReason.Stopped);
            _hasResourceHandle = true;
            _resourceHandle = resourceHandle;
            PlayInternal(id, key, resourceHandle.Asset, options);
        }

        public void PlayClip(int id, AudioClip clip, AudioPlayOptions options)
        {
            ClearCurrent(false, AudioStopReason.Stopped);
            _hasResourceHandle = false;
            _resourceHandle = null;
            PlayInternal(id, default, clip, options);
        }

        public void MarkLoading(int id, ResourceKey key, AudioPlayOptions options)
        {
            ClearCurrent(false, AudioStopReason.Stopped);
            Id = id;
            Key = key;
            _options = options;
            Elapsed = 0f;
            _stopReported = false;
            State = AudioAgentState.Loading;
        }

        public void Stop(float fadeOut, AudioStopReason reason)
        {
            if (State == AudioAgentState.Idle)
                return;

            if (State == AudioAgentState.Loading)
            {
                ClearCurrent(true, reason);
                return;
            }

            if (fadeOut > 0f && _source.isPlaying)
            {
                BeginFade(_source.volume, 0f, fadeOut, AudioAgentState.FadingOut);
                State = AudioAgentState.FadingOut;
                return;
            }

            ClearCurrent(true, reason);
        }

        public void Pause()
        {
            if (!IsPlayingLike || State == AudioAgentState.Paused)
                return;
            _source.Pause();
            State = AudioAgentState.Paused;
        }

        public void Resume()
        {
            if (State != AudioAgentState.Paused)
                return;
            _source.UnPause();
            State = AudioAgentState.Playing;
        }

        public void SetVolume(float volume)
        {
            _baseVolume = Mathf.Clamp01(volume);
            if (State != AudioAgentState.FadingIn && State != AudioAgentState.FadingOut)
                _source.volume = _baseVolume;
        }

        public void Update(float deltaTime, float unscaledDeltaTime)
        {
            if (State == AudioAgentState.Idle || State == AudioAgentState.Loading)
                return;

            Elapsed += unscaledDeltaTime;
            UpdateFollowTarget();
            UpdateFade(unscaledDeltaTime);

            if ((State == AudioAgentState.Playing || State == AudioAgentState.FadingIn) && !_source.loop && !_source.isPlaying)
                ClearCurrent(true, AudioStopReason.Finished);
        }

        public AudioAgentSnapshot CreateSnapshot()
        {
            return new AudioAgentSnapshot
            {
                Id = Id,
                State = State,
                Location = Key.Location,
                PackageName = Key.PackageName,
                ClipName = _source.clip != null ? _source.clip.name : null,
                Loop = _source.loop,
                Priority = Priority,
                Volume = _baseVolume,
                Elapsed = Elapsed
            };
        }

        public void Destroy()
        {
            ClearCurrent(false, AudioStopReason.ManagerDestroyed);
            if (_transform != null)
                Object.Destroy(_transform.gameObject);
        }

        private void PlayInternal(int id, ResourceKey key, AudioClip clip, AudioPlayOptions options)
        {
            Id = id;
            Key = key;
            _options = options ?? new AudioPlayOptions();
            _baseVolume = Mathf.Clamp01(_options.Volume);
            Elapsed = 0f;
            _stopReported = false;

            _source.clip = clip;
            _source.loop = _options.Loop;
            _source.pitch = _options.Pitch;
            _source.panStereo = _options.PanStereo;
            _source.spatialBlend = _options.SpatialBlend;
            _source.minDistance = _options.MinDistance;
            _source.maxDistance = _options.MaxDistance;
            _source.volume = _options.FadeIn > 0f ? 0f : _baseVolume;

            if (_options.FollowTarget != null)
            {
                _hasFollowTarget = true;
                _lastFollowPosition = _options.FollowTarget.position;
                _transform.position = _lastFollowPosition;
            }
            else if (_options.Position.HasValue)
            {
                _transform.position = _options.Position.Value;
            }
            else
            {
                _transform.localPosition = Vector3.zero;
            }

            if (clip == null)
            {
                ClearCurrent(true, AudioStopReason.LoadFailed);
                return;
            }

            _source.Play();
            if (_options.FadeIn > 0f)
                BeginFade(0f, _baseVolume, _options.FadeIn, AudioAgentState.FadingIn);
            else
                State = AudioAgentState.Playing;
        }

        private void UpdateFollowTarget()
        {
            if (_options == null || _options.FollowTarget == null)
            {
                if (_options != null && _hasFollowTarget && _options.Loop && State != AudioAgentState.FadingOut)
                    Stop(_options.FadeOut, AudioStopReason.Stopped);
                return;
            }

            _lastFollowPosition = _options.FollowTarget.position;
            _transform.position = _lastFollowPosition;
        }

        private void BeginFade(float startVolume, float targetVolume, float duration, AudioAgentState state)
        {
            _fadeStartVolume = startVolume;
            _fadeTargetVolume = targetVolume;
            _fadeDuration = Mathf.Max(0.0001f, duration);
            _fadeTimer = 0f;
            State = state;
        }

        private void UpdateFade(float deltaTime)
        {
            if (State != AudioAgentState.FadingIn && State != AudioAgentState.FadingOut)
                return;

            _fadeTimer += deltaTime;
            float t = Mathf.Clamp01(_fadeTimer / _fadeDuration);
            _source.volume = Mathf.Lerp(_fadeStartVolume, _fadeTargetVolume, t);

            if (t < 1f)
                return;

            if (State == AudioAgentState.FadingOut)
                ClearCurrent(true, AudioStopReason.Stopped);
            else
                State = AudioAgentState.Playing;
        }

        private void ClearCurrent(bool reportStop, AudioStopReason reason)
        {
            int oldId = Id;
            bool shouldReport = reportStop && !_stopReported && oldId > 0;
            _stopReported = true;

            if (_source != null)
            {
                _source.Stop();
                _source.clip = null;
            }

            // The playback handle is owned by the agent. Release it on every stop;
            // otherwise ReleaseOnStop=false silently loses the only release path.
            if (_hasResourceHandle)
                _resourceHandle?.Release();

            _hasResourceHandle = false;
            _resourceHandle = null;
            _options = null;
            Key = default;
            Id = 0;
            Elapsed = 0f;
            _fadeTimer = 0f;
            _lastFollowPosition = default;
            _hasFollowTarget = false;
            State = AudioAgentState.Idle;

            if (shouldReport)
                _manager.NotifyAgentStopped(oldId, reason);
        }
    }
}
