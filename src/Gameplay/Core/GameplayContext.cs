using System;

namespace FRFrame.Gameplay
{
    /// <summary>Read-only identity and target snapshot for one gameplay execution.</summary>
    public sealed class GameplayContext
    {
        public GameplaySourceId InstigatorId { get; }
        /// <summary>Optional actual rule source; an absent source means the instigator is effective.</summary>
        public GameplaySourceId SourceId { get; }
        public GameplaySourceId EffectiveSourceId => SourceId.IsValid ? SourceId : InstigatorId;
        /// <summary>Original cast/source identity; deriving a child SourceId never rewrites this provenance.</summary>
        public GameplaySourceId OriginSourceId { get; }
        /// <summary>Identity of this instigator's execution/cast. Child sources preserve it even
        /// when a summoned actor explicitly inherits a different birth OriginSourceId.</summary>
        public GameplaySourceId ActivationSourceId { get; }
        public GameplayTargetData TargetData { get; }
        public GameplayContextValues Values { get; }
        /// <summary>Captured grant level for this execution; child target/source contexts preserve it.</summary>
        public int AbilityLevel { get; }

        /// <summary>Derives a new target snapshot, preserving instigator, source and values.</summary>
        public GameplayContext WithTarget(GameplayTargetData target) => new GameplayContext(InstigatorId, target, SourceId, Values, AbilityLevel, OriginSourceId, ActivationSourceId);
        /// <summary>Derives a child source. Default source retains the instigator fallback.</summary>
        public GameplayContext WithSource(GameplaySourceId source) => new GameplayContext(InstigatorId, TargetData, source, Values, AbilityLevel, OriginSourceId, ActivationSourceId);
        /// <summary>Explicit spawn-causality binding. Ordinary child WithSource/WithTarget calls
        /// retain the chosen origin; this never changes the actual instigator or current cast source.</summary>
        public GameplayContext WithOriginSource(GameplaySourceId origin)
        {if(!origin.IsValid)throw new ArgumentException("Origin must be valid.",nameof(origin));return new GameplayContext(InstigatorId,TargetData,SourceId,Values,AbilityLevel,origin,ActivationSourceId);}
        /// <summary>Replaces the immutable scalar snapshot; null uses Empty as in the constructor.</summary>
        public GameplayContext WithValues(GameplayContextValues values) => new GameplayContext(InstigatorId, TargetData, SourceId, values, AbilityLevel, OriginSourceId,ActivationSourceId);
        /// <summary>Captures a level without mutating this execution or a shared Definition.</summary>
        public GameplayContext WithAbilityLevel(int level) => level == AbilityLevel ? this :
            new GameplayContext(InstigatorId, TargetData, SourceId, Values, level, OriginSourceId,ActivationSourceId);

        public GameplayContext(GameplaySourceId instigatorId, GameplayTargetData targetData,
            GameplaySourceId sourceId = default, GameplayContextValues values = null, int abilityLevel = 1)
            : this(instigatorId, targetData, sourceId, values, abilityLevel, sourceId.IsValid ? sourceId : instigatorId,sourceId.IsValid ? sourceId : instigatorId) { }

        private GameplayContext(GameplaySourceId instigatorId, GameplayTargetData targetData,
            GameplaySourceId sourceId, GameplayContextValues values, int abilityLevel, GameplaySourceId originSourceId,GameplaySourceId activationSourceId)
        {
            if (!instigatorId.IsValid)
                throw new ArgumentException("Instigator ID must be valid.", nameof(instigatorId));
            InstigatorId = instigatorId;
            TargetData = targetData ?? throw new ArgumentNullException(nameof(targetData));
            SourceId = sourceId;
            Values = values ?? GameplayContextValues.Empty;
            if (abilityLevel < 1) throw new ArgumentOutOfRangeException(nameof(abilityLevel));
            AbilityLevel = abilityLevel;
            OriginSourceId = originSourceId;
            ActivationSourceId = activationSourceId;
        }
    }
}
