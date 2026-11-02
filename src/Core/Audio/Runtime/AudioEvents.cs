using System;

namespace FRFrame
{
    public sealed class AudioEvents
    {
        public event Action<AudioHandle> PlayStarted;
        public event Action<AudioHandle, AudioStopReason> PlayStopped;
        public event Action<ResourceKey, string> LoadFailed;
        public event Action<AudioType, float> VolumeChanged;
        public event Action<AudioType, bool> MuteChanged;

        internal void RaisePlayStarted(AudioHandle handle)
        {
            PlayStarted?.Invoke(handle);
        }

        internal void RaisePlayStopped(AudioHandle handle, AudioStopReason reason)
        {
            PlayStopped?.Invoke(handle, reason);
        }

        internal void RaiseLoadFailed(ResourceKey key, string error)
        {
            LoadFailed?.Invoke(key, error);
        }

        internal void RaiseVolumeChanged(AudioType type, float volume)
        {
            VolumeChanged?.Invoke(type, volume);
        }

        internal void RaiseMuteChanged(AudioType type, bool mute)
        {
            MuteChanged?.Invoke(type, mute);
        }
    }
}
