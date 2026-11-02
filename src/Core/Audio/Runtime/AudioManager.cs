using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.SceneManagement;
using Object = UnityEngine.Object;

namespace FRFrame
{
    public sealed class AudioManager : IManagerBase
    {
        private const string DefaultConfigPath = "FRFrameAudioConfig";
        private readonly Dictionary<AudioType, AudioCategory> _categories = new Dictionary<AudioType, AudioCategory>();
        private readonly Dictionary<int, AudioAgent> _agentsById = new Dictionary<int, AudioAgent>();
        private readonly Dictionary<ResourceKey, ResourceHandle<AudioClip>> _preloadedClips = new Dictionary<ResourceKey, ResourceHandle<AudioClip>>();
        private readonly Dictionary<ResourceKey, UniTask<ResourceHandle<AudioClip>>> _preloadTasks = new Dictionary<ResourceKey, UniTask<ResourceHandle<AudioClip>>>();
        private readonly HashSet<int> _loadingIds = new HashSet<int>();
        private readonly Dictionary<string, AudioScope> _scopes = new Dictionary<string, AudioScope>();
        private int _serialSeed;
        private int _musicRequestVersion;
        private Transform _audioRoot;
        private AudioMixer _mixer;
        private float _masterVolume = 1f;
        private bool _masterMute;
        private int _stealCount;
        private int _playFailureCount;
        private bool _destroyed;
        private bool _initialized;

        public override int Priority => 15;
        public AudioEvents Events { get; } = new AudioEvents();
        public AudioMixer Mixer => _mixer;

        public float MasterVolume
        {
            get => _masterVolume;
            set
            {
                _masterVolume = Mathf.Clamp01(value);
                ApplyMasterVolume();
            }
        }

        public bool MasterMute
        {
            get => _masterMute;
            set
            {
                _masterMute = value;
                ApplyMasterVolume();
            }
        }

        public override async UniTask InitializeAsync()
        {
            if (_initialized || _destroyed)
                return;

            await FRFM.WaitReady<ResourceManager>();
            if (_destroyed)
                return;

            InitializeDirectPlayback();
        }

        /// <summary>Initializes the existing categories/agents for already loaded AudioClips without
        /// awaiting the asset loader. Permanent Gameplay Labs and direct-reference presentation backends
        /// use this path; location-based asynchronous playback still requires ResourceManager readiness.</summary>
        public void InitializeDirectPlayback(AudioModuleConfig config = null)
        {
            if (_initialized || _destroyed) return;
            BuildAudioRoot();
            config = config != null ? config : Resources.Load<AudioModuleConfig>(DefaultConfigPath);
            bool ownsConfig = config == null;
            if (config == null)
                config = ScriptableObject.CreateInstance<AudioModuleConfig>();

            _mixer = config.Mixer;
            _masterVolume = config.MasterVolume;
            _masterMute = config.MasterMute;
            BuildCategories(config);
            ApplyMasterVolume();
            SceneManager.activeSceneChanged += OnActiveSceneChanged;
            _initialized = true;
            if (ownsConfig) { if (Application.isPlaying) UnityEngine.Object.Destroy(config); else UnityEngine.Object.DestroyImmediate(config); }
            FRLog.Info("AudioManager initialized.");
        }

        public override void Update(float deltaTime, float unscaledDeltaTime)
        {
            foreach (AudioCategory category in _categories.Values)
                category.Update(deltaTime, unscaledDeltaTime);
        }

        public override void Destroy()
        {
            if (_destroyed)
                return;

            _destroyed = true;
            _initialized = false;
            SceneManager.activeSceneChanged -= OnActiveSceneChanged;
            _musicRequestVersion++;

            foreach (AudioScope scope in new List<AudioScope>(_scopes.Values))
                scope.Dispose();
            _scopes.Clear();

            StopAll(null, 0f, AudioStopReason.ManagerDestroyed);
            ReleaseAllPreloaded();

            foreach (AudioCategory category in _categories.Values)
                category.Destroy();
            _categories.Clear();
            _agentsById.Clear();
            _loadingIds.Clear();
            _preloadTasks.Clear();

            if (_audioRoot != null)
            {
                Object.Destroy(_audioRoot.gameObject);
                _audioRoot = null;
            }

            FRLog.Info("AudioManager destroyed.");
        }

        public AudioScope CreateScope(string name, string packageName = null)
        {
            if (_destroyed)
                return null;

            if (string.IsNullOrEmpty(name))
            {
                FRLog.Error("CreateAudioScope failed, name is null or empty.");
                return null;
            }

            if (_scopes.TryGetValue(name, out AudioScope scope) && !scope.IsDisposed)
            {
                if (!string.Equals(scope.PackageName, packageName, StringComparison.Ordinal))
                    FRLog.Warning($"CreateAudioScope got existing scope with different package. name:{name}, current:{scope.PackageName}, requested:{packageName}");
                return scope;
            }

            scope = new AudioScope(this, name, packageName);
            _scopes[name] = scope;
            return scope;
        }

        internal void RemoveScope(AudioScope scope)
        {
            if (scope != null && _scopes.TryGetValue(scope.Name, out AudioScope current) && current == scope)
                _scopes.Remove(scope.Name);
        }

        public async UniTask<AudioHandle> PlayMusicAsync(string location, float fadeOut = 0.3f, float fadeIn = 0.3f, string packageName = null, CancellationToken cancellationToken = default)
        {
            if (cancellationToken.IsCancellationRequested || _destroyed)
                return default;

            StopAll(AudioType.Music, fadeOut);
            int requestVersion = ++_musicRequestVersion;
            AudioHandle handle = await PlayAsync(location, new AudioPlayOptions
            {
                Type = AudioType.Music,
                PackageName = packageName,
                Loop = true,
                FadeIn = fadeIn,
                FadeOut = fadeOut,
                Priority = int.MaxValue
            }, cancellationToken);

            if (requestVersion != _musicRequestVersion)
            {
                handle.Stop();
                return default;
            }

            return handle;
        }

        public async UniTask<AudioHandle> PlayAmbienceAsync(string location, AudioPlayOptions options = null, CancellationToken cancellationToken = default)
        {
            options = PrepareOptions(options, AudioType.Ambience);
            return await PlayAsync(location, options, cancellationToken);
        }

        public async UniTask<AudioHandle> PlaySoundAsync(string location, AudioPlayOptions options = null, CancellationToken cancellationToken = default)
        {
            options = PrepareOptions(options, AudioType.Sound);
            return await PlayAsync(location, options, cancellationToken);
        }

        public async UniTask<AudioHandle> PlayUISoundAsync(string location, float volume = 1f, string packageName = null, CancellationToken cancellationToken = default)
        {
            return await PlayAsync(location, new AudioPlayOptions
            {
                Type = AudioType.UISound,
                Volume = volume,
                PackageName = packageName
            }, cancellationToken);
        }

        public async UniTask<AudioHandle> PlayVoiceAsync(string location, AudioPlayOptions options = null, CancellationToken cancellationToken = default)
        {
            options = PrepareOptions(options, AudioType.Voice);
            return await PlayAsync(location, options, cancellationToken);
        }

        public async UniTask<AudioHandle> PlayAsync(string location, AudioPlayOptions options, CancellationToken cancellationToken = default)
        {
            options ??= new AudioPlayOptions();
            return await PlayAsync(new ResourceKey(options.PackageName, location), options, cancellationToken);
        }

        public async UniTask<AudioHandle> PlayAsync(ResourceKey key, AudioPlayOptions options, CancellationToken cancellationToken = default)
        {
            options ??= new AudioPlayOptions();
            if (cancellationToken.IsCancellationRequested || _destroyed)
                return default;

            key = FRFM.Resource.NormalizeKey(new ResourceKey(string.IsNullOrEmpty(options.PackageName) ? key.PackageName : options.PackageName, key.Location));
            if (!_categories.TryGetValue(options.Type, out AudioCategory category))
            {
                _playFailureCount++;
                FRLog.Error($"Play audio failed, category not found : {options.Type}");
                return default;
            }

            AudioAgent agent = category.SelectAgent(options);
            if (agent == null)
            {
                _playFailureCount++;
                FRLog.Warning($"Play audio failed, no available agent : {key}");
                return default;
            }

            if (!agent.IsIdle)
            {
                _stealCount++;
                agent.Stop(options.FadeOut > 0f ? options.FadeOut : 0.15f, AudioStopReason.Stolen);
            }

            int id = NextId();
            AudioHandle handle = new AudioHandle(this, id);
            _agentsById[id] = agent;
            _loadingIds.Add(id);
            agent.MarkLoading(id, key, options.Clone());

            ResourceHandle<AudioClip> resourceHandle;
            try
            {
                ResourceLoadOptions loadOptions = new ResourceLoadOptions
                {
                    CancellationToken = cancellationToken
                };
                if (_preloadedClips.TryGetValue(key, out ResourceHandle<AudioClip> preloaded) && preloaded.IsValid)
                {
                    AudioClip cachedClip = await FRFM.Resource.LoadAssetAsync<AudioClip>(key, loadOptions);
                    resourceHandle = new ResourceHandle<AudioClip>(FRFM.Resource, key, cachedClip);
                }
                else
                {
                    resourceHandle = await FRFM.Resource.LoadAssetHandleAsync<AudioClip>(key, loadOptions);
                }
            }
            catch (Exception exception)
            {
                _loadingIds.Remove(id);
                if (_agentsById.TryGetValue(id, out AudioAgent failedAgent) && failedAgent == agent)
                    agent.Stop(0f, AudioStopReason.LoadFailed);
                FRLog.Error($"Play audio load exception : {key}, {exception.Message}");
                Events.RaiseLoadFailed(key, exception.Message);
                return default;
            }

            _loadingIds.Remove(id);
            if (cancellationToken.IsCancellationRequested || _destroyed)
            {
                resourceHandle?.Release();
                if (_agentsById.TryGetValue(id, out AudioAgent canceledAgent) && canceledAgent == agent)
                    agent.Stop(0f, AudioStopReason.ScopeDisposed);
                return default;
            }

            if (!_agentsById.TryGetValue(id, out AudioAgent currentAgent) || currentAgent != agent || agent.Id != id)
            {
                resourceHandle?.Release();
                return default;
            }

            if (resourceHandle == null || !resourceHandle.IsValid)
            {
                _agentsById.Remove(id);
                agent.Stop(0f, AudioStopReason.LoadFailed);
                Events.RaiseLoadFailed(key, "load failed");
                return default;
            }

            agent.PlayLoaded(id, key, resourceHandle, options.Clone());
            Events.RaisePlayStarted(handle);
            return handle;
        }

        public AudioHandle Play(AudioClip clip, AudioPlayOptions options = null)
        {
            if (_destroyed || clip == null)
                return default;

            options ??= new AudioPlayOptions();
            if (!_categories.TryGetValue(options.Type, out AudioCategory category))
            {
                _playFailureCount++;
                FRLog.Error($"Play audio clip failed, category not found : {options.Type}");
                return default;
            }

            AudioAgent agent = category.SelectAgent(options);
            if (agent == null)
            {
                _playFailureCount++;
                FRLog.Warning($"Play audio clip failed, no available agent : {clip.name}");
                return default;
            }

            if (!agent.IsIdle)
            {
                _stealCount++;
                agent.Stop(options.FadeOut > 0f ? options.FadeOut : 0.15f, AudioStopReason.Stolen);
            }

            int id = NextId();
            var handle = new AudioHandle(this, id);
            _agentsById[id] = agent;
            agent.PlayClip(id, clip, options.Clone());
            Events.RaisePlayStarted(handle);
            return handle;
        }

        public bool Stop(AudioHandle handle, float fadeOut = 0f)
        {
            if (!_agentsById.TryGetValue(handle.Id, out AudioAgent agent))
                return false;
            agent.Stop(fadeOut, AudioStopReason.Stopped);
            return true;
        }

        public void StopAll(AudioType? type = null, float fadeOut = 0f)
        {
            if (!type.HasValue || type.Value == AudioType.Music)
                _musicRequestVersion++;
            StopAll(type, fadeOut, AudioStopReason.Stopped);
        }

        public void Pause(AudioHandle handle)
        {
            if (_agentsById.TryGetValue(handle.Id, out AudioAgent agent))
                agent.Pause();
        }

        public void Resume(AudioHandle handle)
        {
            if (_agentsById.TryGetValue(handle.Id, out AudioAgent agent))
                agent.Resume();
        }

        public void PauseAll(AudioType? type = null)
        {
            foreach (var pair in _categories)
            {
                if (!type.HasValue || pair.Key == type.Value)
                    pair.Value.PauseAll();
            }
        }

        public void ResumeAll(AudioType? type = null)
        {
            foreach (var pair in _categories)
            {
                if (!type.HasValue || pair.Key == type.Value)
                    pair.Value.ResumeAll();
            }
        }

        public bool IsHandleValid(AudioHandle handle)
        {
            return _agentsById.ContainsKey(handle.Id);
        }

        public void SetHandleVolume(AudioHandle handle, float volume)
        {
            if (_agentsById.TryGetValue(handle.Id, out AudioAgent agent))
                agent.SetVolume(volume);
        }

        public float GetVolume(AudioType type)
        {
            return _categories.TryGetValue(type, out AudioCategory category) ? category.Volume : 0f;
        }

        public void SetVolume(AudioType type, float volume)
        {
            if (!_categories.TryGetValue(type, out AudioCategory category))
                return;
            category.Volume = volume;
            Events.RaiseVolumeChanged(type, category.Volume);
        }

        public bool GetMute(AudioType type)
        {
            return _categories.TryGetValue(type, out AudioCategory category) && category.Mute;
        }

        public void SetMute(AudioType type, bool mute)
        {
            if (!_categories.TryGetValue(type, out AudioCategory category))
                return;
            category.Mute = mute;
            Events.RaiseMuteChanged(type, mute);
        }

        public async UniTask PreloadAsync(string location, AudioType type, string packageName = null, CancellationToken cancellationToken = default)
        {
            await PreloadAsync(new ResourceKey(packageName, location), type, cancellationToken);
        }

        public async UniTask PreloadAsync(ResourceKey key, AudioType type, CancellationToken cancellationToken = default)
        {
            key = FRFM.Resource.NormalizeKey(key);
            if (cancellationToken.IsCancellationRequested || _destroyed)
                return;

            if (_preloadedClips.TryGetValue(key, out ResourceHandle<AudioClip> existing) && existing.IsValid)
                return;

            if (!_preloadTasks.TryGetValue(key, out UniTask<ResourceHandle<AudioClip>> preloadTask))
            {
                preloadTask = LoadAndStorePreloadAsync(key);
                _preloadTasks[key] = preloadTask;
            }

            try
            {
                await preloadTask.AttachExternalCancellation(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // The shared preload continues for other callers; this caller is done.
            }
        }

        public void ReleasePreloaded(string location, string packageName = null)
        {
            ReleasePreloaded(new ResourceKey(packageName, location));
        }

        public void ReleasePreloaded(ResourceKey key)
        {
            key = FRFM.Resource.NormalizeKey(key);
            if (!_preloadedClips.TryGetValue(key, out ResourceHandle<AudioClip> handle))
                return;
            handle.Release();
            _preloadedClips.Remove(key);
        }

        public void ReleaseAllPreloaded()
        {
            foreach (ResourceHandle<AudioClip> handle in _preloadedClips.Values)
                handle.Release();
            _preloadedClips.Clear();
        }

        public AudioDiagnosticsSnapshot CreateSnapshot()
        {
            var snapshot = new AudioDiagnosticsSnapshot
            {
                LoadingCount = _loadingIds.Count,
                PreloadedClipCount = _preloadedClips.Count,
                StealCount = _stealCount,
                PlayFailureCount = _playFailureCount
            };

            foreach (AudioCategory category in _categories.Values)
            {
                AudioCategorySnapshot categorySnapshot = category.CreateSnapshot();
                snapshot.Categories.Add(categorySnapshot);
                snapshot.PlayingCount += categorySnapshot.PlayingCount;
            }

            return snapshot;
        }

        internal void NotifyAgentStopped(int id, AudioStopReason reason)
        {
            _agentsById.Remove(id);
            _loadingIds.Remove(id);
            Events.RaisePlayStopped(new AudioHandle(this, id), reason);
        }

        internal static float LinearToDb(float volume)
        {
            return volume <= 0.0001f ? -80f : Mathf.Log10(volume) * 20f;
        }

        private void BuildAudioRoot()
        {
            _audioRoot = FRFrameRoot.Get().GetOrCreateCompoent("Audio");
        }

        private async UniTask<ResourceHandle<AudioClip>> LoadAndStorePreloadAsync(ResourceKey key)
        {
            try
            {
                ResourceHandle<AudioClip> handle = await FRFM.Resource.LoadAssetHandleAsync<AudioClip>(key);
                if (_destroyed || handle == null || !handle.IsValid)
                {
                    handle?.Release();
                    return handle;
                }

                if (_preloadedClips.TryGetValue(key, out ResourceHandle<AudioClip> existing))
                {
                    handle.Release();
                    return existing;
                }

                _preloadedClips[key] = handle;
                return handle;
            }
            finally
            {
                _preloadTasks.Remove(key);
            }
        }

        private void OnActiveSceneChanged(Scene oldScene, Scene newScene)
        {
            foreach (AudioCategory category in _categories.Values)
                category.StopNonPersistent();
        }

        private void BuildCategories(AudioModuleConfig config)
        {
            _categories.Clear();
            List<AudioGroupConfig> groups = config.Groups != null && config.Groups.Count > 0
                ? config.Groups
                : ScriptableObject.CreateInstance<AudioModuleConfig>().Groups;

            for (int i = 0; i < groups.Count; i++)
            {
                AudioGroupConfig group = groups[i];
                _categories[group.Type] = new AudioCategory(this, group, _audioRoot, _mixer);
            }
        }

        private void ApplyMasterVolume()
        {
            if (_mixer != null)
                _mixer.SetFloat("MasterVolume", LinearToDb(_masterMute ? 0f : _masterVolume));
            else
                AudioListener.volume = _masterMute ? 0f : _masterVolume;
        }

        private AudioPlayOptions PrepareOptions(AudioPlayOptions options, AudioType type)
        {
            options ??= new AudioPlayOptions();
            options.Type = type;
            return options;
        }

        private void StopAll(AudioType? type, float fadeOut, AudioStopReason reason)
        {
            foreach (var pair in _categories)
            {
                if (!type.HasValue || pair.Key == type.Value)
                    pair.Value.StopAll(fadeOut, reason);
            }
        }

        private int NextId()
        {
            _serialSeed++;
            if (_serialSeed <= 0)
                _serialSeed = 1;
            return _serialSeed;
        }
    }
}
