using UnityEngine;

namespace FRFrame.Live2D
{
    public class Live2DSimpleAudioEventHandler : MonoBehaviour, ILive2DAudioEventHandler
    {
        public AudioSource audioSource;
        public string audioPackageName;
        public bool loadAudioKeyFromFRFrameResource = true;
        [Range(0f, 1f)] public float masterVolume = 1f;
        [Range(0f, 1f)] public float sfxVolume = 1f;
        public bool logResourceKeyOnly = true;

        private void Awake()
        {
            if (audioSource == null) audioSource = GetComponent<AudioSource>();
            if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
        }

        public void PlayAudio(Live2DAudioEventData data)
        {
            var clip = data.FallbackClip;
            if (clip == null && loadAudioKeyFromFRFrameResource && !string.IsNullOrEmpty(data.AudioKey) && FRFM.Resource != null && FRFM.Resource.IsReady)
                clip = FRFM.Resource.LoadAsset<AudioClip>(data.AudioKey, string.IsNullOrEmpty(audioPackageName) ? null : audioPackageName);

            if (clip != null)
            {
                audioSource.pitch = data.Pitch <= 0f ? 1f : data.Pitch;
                audioSource.PlayOneShot(clip, data.Volume * masterVolume * sfxVolume);
                return;
            }

            if (logResourceKeyOnly && !string.IsNullOrEmpty(data.AudioKey))
                Debug.Log($"[Live2D] Audio key requested: {data.AudioKey}", this);
        }
    }
}
