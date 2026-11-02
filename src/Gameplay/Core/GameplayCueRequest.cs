using System;

namespace FRFrame.Gameplay
{
    /// <summary>Immutable rule-layer request for presentation; contains no view assets.</summary>
    public sealed class GameplayCueRequest
    {
        public GameplayTag CueTag { get; }
        public GameplayContext Context { get; }
        public float? Magnitude { get; }
        /// <summary>Optional rule-owned timing for a presentation that must meet a later cue.</summary>
        public float? Duration { get; }

        public GameplayCueRequest(GameplayTag cueTag, GameplayContext context, float? magnitude = null,
            float? duration = null)
        {
            if (!cueTag.IsValid) throw new ArgumentException("Cue tag must be valid.", nameof(cueTag));
            if (magnitude.HasValue && !float.IsFinite(magnitude.Value))
                throw new ArgumentOutOfRangeException(nameof(magnitude));
            if (duration.HasValue && (!float.IsFinite(duration.Value) || duration.Value <= 0f))
                throw new ArgumentOutOfRangeException(nameof(duration));
            CueTag = cueTag;
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Magnitude = magnitude;
            Duration = duration;
        }
    }
}
