using System;

namespace FRFrame.Gameplay
{
    /// <summary>One immutable moment inside a Sequence. Time uses the host's rule-time seconds.</summary>
    public abstract class GameplaySequenceMarkerDefinition
    {
        public float Time { get; }

        protected GameplaySequenceMarkerDefinition(float time)
        {
            if (!float.IsFinite(time) || time < 0f) throw new ArgumentOutOfRangeException(nameof(time));
            Time = time;
        }
    }

    /// <summary>Applies an existing Effect at this moment; its later lifetime belongs only to EffectContainer.</summary>
    public sealed class GameplaySequenceApplyEffectMarkerDefinition : GameplaySequenceMarkerDefinition
    {
        public GameplayEffectDefinition Effect { get; }

        public GameplaySequenceApplyEffectMarkerDefinition(float time, GameplayEffectDefinition effect) : base(time) =>
            Effect = effect ?? throw new ArgumentNullException(nameof(effect));
    }

    /// <summary>Emits a presentation request without holding a prefab or animation reference.</summary>
    public sealed class GameplaySequenceCueMarkerDefinition : GameplaySequenceMarkerDefinition
    {
        public GameplayTag CueTag { get; }
        public float? Magnitude { get; }
        public float? Duration { get; }

        public GameplaySequenceCueMarkerDefinition(float time, GameplayTag cueTag, float? magnitude = null,
            float? duration = null) : base(time)
        {
            if (!cueTag.IsValid) throw new ArgumentException("Cue tag must be valid.", nameof(cueTag));
            if (magnitude.HasValue && !float.IsFinite(magnitude.Value)) throw new ArgumentOutOfRangeException(nameof(magnitude));
            if (duration.HasValue && (!float.IsFinite(duration.Value) || duration.Value <= 0f))
                throw new ArgumentOutOfRangeException(nameof(duration));
            CueTag = cueTag;
            Magnitude = magnitude;
            Duration = duration;
        }
    }
}
