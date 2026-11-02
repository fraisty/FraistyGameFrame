using System.Collections.Generic;

namespace FRFrame.Live2D
{
    public sealed class Live2DMotionLayer
    {
        public Live2DLayerId LayerId { get; }
        public Live2DMotionTrack Current { get; private set; }
        public Live2DMotionTrack Previous { get; private set; }
        public string CurrentClipId => Current != null ? Current.ClipId : string.Empty;

        public Live2DMotionLayer(Live2DLayerId layerId)
        {
            LayerId = layerId;
        }

        public void Play(ILive2DPlayableClip clip)
        {
            if (Current != null)
            {
                Previous = Current;
                Previous.BeginStop();
            }
            Current = new Live2DMotionTrack(clip);
        }

        public void Stop(float fadeOut)
        {
            Current?.BeginStop(fadeOut);
        }

        public void Clear()
        {
            Current = null;
            Previous = null;
        }

        public void Update(float dt, List<Live2DPlayableEvent> eventBuffer)
        {
            Previous?.Update(dt, LayerId, eventBuffer);
            if (Previous != null && Previous.IsFullyStopped()) Previous = null;
            Current?.Update(dt, LayerId, eventBuffer);
            if (Current != null && Current.IsFullyStopped()) Current = null;
        }

        public bool IsCurrentNaturallyFinished()
        {
            return Current != null && Current.IsNaturallyFinished();
        }

        public void Evaluate(float defaultFadeIn, float defaultFadeOut, List<Live2DEvaluatedParameter> results)
        {
            Previous?.Evaluate(defaultFadeIn, defaultFadeOut, results);
            Current?.Evaluate(defaultFadeIn, defaultFadeOut, results);
        }
    }
}
