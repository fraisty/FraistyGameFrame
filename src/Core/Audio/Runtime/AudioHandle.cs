using System;
using UnityEngine;

namespace FRFrame
{
    public readonly struct AudioHandle : IEquatable<AudioHandle>
    {
        private readonly AudioManager _manager;

        public int Id { get; }
        public bool IsValid => _manager != null && Id > 0 && _manager.IsHandleValid(this);

        internal AudioHandle(AudioManager manager, int id)
        {
            _manager = manager;
            Id = id;
        }

        public void Stop(float fadeOut = 0f)
        {
            _manager?.Stop(this, fadeOut);
        }

        public void Pause()
        {
            _manager?.Pause(this);
        }

        public void Resume()
        {
            _manager?.Resume(this);
        }

        public void SetVolume(float volume)
        {
            _manager?.SetHandleVolume(this, volume);
        }

        public bool Equals(AudioHandle other)
        {
            return Id == other.Id;
        }

        public override bool Equals(object obj)
        {
            return obj is AudioHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return Id;
        }
    }
}
