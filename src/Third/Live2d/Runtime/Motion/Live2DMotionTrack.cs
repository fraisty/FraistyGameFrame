using System.Collections.Generic;
using UnityEngine;

namespace FRFrame.Live2D
{
    public sealed class Live2DMotionTrack
    {
        private readonly List<Live2DEvaluatedParameter> _evaluated = new List<Live2DEvaluatedParameter>(128);
        private int _eventCursor;
        private float _elapsed;
        private float _fadeInElapsed;
        private float _stopElapsed;
        private float _stopFadeOutDuration;
        private bool _stopping;

        public ILive2DPlayableClip Clip { get; }
        public bool IsStopping => _stopping;
        public string ClipId => Clip != null ? Clip.ClipId : string.Empty;

        public Live2DMotionTrack(ILive2DPlayableClip clip)
        {
            Clip = clip;
            _stopFadeOutDuration = clip != null ? clip.FadeOutTime : 0.25f;
        }

        public void Update(float dt, Live2DLayerId layerId, List<Live2DPlayableEvent> eventBuffer)
        {
            if (Clip == null) return;
            float previousTime = GetPlaybackTime();
            _elapsed += dt;
            _fadeInElapsed += dt;
            if (_stopping) _stopElapsed += dt;
            float currentTime = GetPlaybackTime();
            DispatchEvents(previousTime, currentTime, layerId, eventBuffer);
        }

        public void BeginStop(float fadeOut = -1f)
        {
            _stopping = true;
            _stopElapsed = 0f;
            _stopFadeOutDuration = fadeOut >= 0f ? fadeOut : (Clip != null ? Clip.FadeOutTime : 0.25f);
        }

        public bool IsFullyStopped()
        {
            if (!_stopping) return false;
            return _stopFadeOutDuration <= 0f || _stopElapsed >= _stopFadeOutDuration;
        }

        public bool IsNaturallyFinished()
        {
            if (Clip == null) return true;
            if (Clip.Loop) return false;
            return _elapsed >= Clip.Duration;
        }

        public float GetPlaybackTime()
        {
            if (Clip == null || Clip.Duration <= 0f) return 0f;
            return Clip.Loop ? Mathf.Repeat(_elapsed, Clip.Duration) : Mathf.Min(_elapsed, Clip.Duration);
        }

        public void Evaluate(float defaultFadeIn, float defaultFadeOut, List<Live2DEvaluatedParameter> results)
        {
            if (Clip == null) return;
            _evaluated.Clear();
            Clip.Evaluate(GetPlaybackTime(), _evaluated);
            float weight = GetWeight(defaultFadeIn, defaultFadeOut);
            if (weight <= 0.0001f) return;
            foreach (var e in _evaluated)
                results.Add(new Live2DEvaluatedParameter(e.Id, e.TargetType, e.Value, weight * e.Weight, e.BlendMode));
        }

        private float GetWeight(float defaultFadeIn, float defaultFadeOut)
        {
            if (Clip == null) return 0f;
            float weight = 1f;
            float fadeIn = Clip.FadeInTime > 0f ? Clip.FadeInTime : defaultFadeIn;
            if (fadeIn > 0f) weight *= Mathf.Clamp01(_fadeInElapsed / fadeIn);

            if (_stopping)
            {
                float fadeOut = _stopFadeOutDuration > 0f ? _stopFadeOutDuration : defaultFadeOut;
                weight *= fadeOut > 0f ? Mathf.Clamp01(1f - _stopElapsed / fadeOut) : 0f;
            }
            else if (!Clip.Loop)
            {
                float remain = Mathf.Max(0f, Clip.Duration - _elapsed);
                float fadeOut = Clip.FadeOutTime > 0f ? Clip.FadeOutTime : defaultFadeOut;
                if (fadeOut > 0f && remain < fadeOut) weight *= Mathf.Clamp01(remain / fadeOut);
            }

            return Mathf.Clamp01(weight);
        }

        private void DispatchEvents(float previousTime, float currentTime, Live2DLayerId layerId, List<Live2DPlayableEvent> eventBuffer)
        {
            var events = Clip.CustomEvents;
            if (events == null || events.Count == 0) return;

            if (Clip.Loop && currentTime < previousTime) _eventCursor = 0;

            while (_eventCursor < events.Count)
            {
                var e = events[_eventCursor];
                if (e == null || e.time > currentTime) break;
                if (e.time >= previousTime || Mathf.Approximately(previousTime, currentTime))
                    eventBuffer.Add(new Live2DPlayableEvent(layerId, ClipId, e));
                _eventCursor++;
            }
        }
    }
}
