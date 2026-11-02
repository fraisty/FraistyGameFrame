using System;
using UnityEngine;

namespace FRFrame
{
    public sealed class AudioPlayOptions
    {
        public AudioType Type = AudioType.Sound;
        public string PackageName;
        public bool Loop;
        public float Volume = 1f;
        public float FadeIn;
        public float FadeOut;
        public int Priority;
        public float Pitch = 1f;
        public float PanStereo;
        public float SpatialBlend;
        public float MinDistance = 1f;
        public float MaxDistance = 500f;
        public Vector3? Position;
        public Transform FollowTarget;
        [Obsolete("Audio resources are always released when playback stops.")]
        public bool ReleaseOnStop = true;
        public bool PersistentAcrossScene;

        public AudioPlayOptions Clone()
        {
            return (AudioPlayOptions)MemberwiseClone();
        }
    }
}
