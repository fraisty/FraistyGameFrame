using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace FRFrame
{
    public sealed class AudioScope : IDisposable
    {
        private readonly AudioManager _audioManager;
        private readonly HashSet<AudioHandle> _handles = new HashSet<AudioHandle>();
        private readonly HashSet<ResourceKey> _preloadedKeys = new HashSet<ResourceKey>();
        private readonly CancellationTokenSource _lifetimeCts = new CancellationTokenSource();

        public string Name { get; }
        public string PackageName { get; }
        public bool IsDisposed { get; private set; }

        internal AudioScope(AudioManager audioManager, string name, string packageName)
        {
            _audioManager = audioManager;
            Name = name;
            PackageName = packageName;
            _audioManager.Events.PlayStopped += OnAudioStopped;
        }

        public async UniTask<AudioHandle> PlayMusicAsync(string location, float fadeOut = 0.3f, float fadeIn = 0.3f)
        {
            AudioHandle handle = await _audioManager.PlayMusicAsync(location, fadeOut, fadeIn, PackageName, LifetimeToken);
            Track(handle);
            return handle;
        }

        public async UniTask<AudioHandle> PlayAmbienceAsync(string location, AudioPlayOptions options = null)
        {
            AudioHandle handle = await _audioManager.PlayAmbienceAsync(location, WithPackage(options), LifetimeToken);
            Track(handle);
            return handle;
        }

        public async UniTask<AudioHandle> PlaySoundAsync(string location, AudioPlayOptions options = null)
        {
            AudioHandle handle = await _audioManager.PlaySoundAsync(location, WithPackage(options), LifetimeToken);
            Track(handle);
            return handle;
        }

        public async UniTask<AudioHandle> PlayUISoundAsync(string location, float volume = 1f)
        {
            AudioHandle handle = await _audioManager.PlayUISoundAsync(location, volume, PackageName, LifetimeToken);
            Track(handle);
            return handle;
        }

        public async UniTask<AudioHandle> PlayVoiceAsync(string location, AudioPlayOptions options = null)
        {
            AudioHandle handle = await _audioManager.PlayVoiceAsync(location, WithPackage(options), LifetimeToken);
            Track(handle);
            return handle;
        }

        public async UniTask<AudioHandle> PlayAsync(string location, AudioPlayOptions options)
        {
            AudioHandle handle = await _audioManager.PlayAsync(location, WithPackage(options), LifetimeToken);
            Track(handle);
            return handle;
        }

        public async UniTask<AudioHandle> PlayAsync(ResourceKey key, AudioPlayOptions options)
        {
            AudioHandle handle = await _audioManager.PlayAsync(key, WithPackage(options), LifetimeToken);
            Track(handle);
            return handle;
        }

        public AudioHandle Play(AudioClip clip, AudioPlayOptions options = null)
        {
            AudioHandle handle = _audioManager.Play(clip, WithPackage(options));
            Track(handle);
            return handle;
        }

        public async UniTask PreloadAsync(string location, AudioType type)
        {
            ResourceKey key = FRFM.Resource.NormalizeKey(new ResourceKey(PackageName, location));
            await _audioManager.PreloadAsync(key, type, LifetimeToken);
            if (!IsDisposed && !_lifetimeCts.IsCancellationRequested)
                _preloadedKeys.Add(key);
        }

        public async UniTask PreloadAsync(ResourceKey key, AudioType type)
        {
            key = FRFM.Resource.NormalizeKey(key);
            await _audioManager.PreloadAsync(key, type, LifetimeToken);
            if (!IsDisposed && !_lifetimeCts.IsCancellationRequested)
                _preloadedKeys.Add(key);
        }

        public void StopAll(float fadeOut = 0f)
        {
            foreach (AudioHandle handle in new List<AudioHandle>(_handles))
                handle.Stop(fadeOut);
            _handles.Clear();
        }

        public void Dispose()
        {
            if (IsDisposed)
                return;

            IsDisposed = true;
            _lifetimeCts.Cancel();
            _audioManager.Events.PlayStopped -= OnAudioStopped;
            foreach (AudioHandle handle in new List<AudioHandle>(_handles))
                handle.Stop();
            _handles.Clear();

            foreach (ResourceKey key in _preloadedKeys)
                _audioManager.ReleasePreloaded(key);
            _preloadedKeys.Clear();
            _audioManager.RemoveScope(this);
            _lifetimeCts.Dispose();
        }

        private AudioPlayOptions WithPackage(AudioPlayOptions options)
        {
            options ??= new AudioPlayOptions();
            if (string.IsNullOrEmpty(options.PackageName))
                options.PackageName = PackageName;
            return options;
        }

        private CancellationToken LifetimeToken => IsDisposed
            ? new CancellationToken(true)
            : _lifetimeCts.Token;

        private void Track(AudioHandle handle)
        {
            if (handle.Id <= 0)
                return;

            if (IsDisposed || _lifetimeCts.IsCancellationRequested)
            {
                handle.Stop();
                return;
            }

            _handles.Add(handle);
        }

        private void OnAudioStopped(AudioHandle handle, AudioStopReason reason)
        {
            _handles.Remove(handle);
        }
    }
}
