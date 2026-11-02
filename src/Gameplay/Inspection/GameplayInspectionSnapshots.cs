using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FRFrame.Gameplay
{
    /// <summary>A detached attribute value and its source-aware modifier contributions.</summary>
    public sealed class GameplayAttributeEntrySnapshot
    {
        public GameplayAttributeId Id { get; }
        public float BaseValue { get; }
        public float CurrentValue { get; }
        public IReadOnlyList<GameplayAttributeModifier> Modifiers { get; }

        public GameplayAttributeEntrySnapshot(GameplayAttributeId id, float baseValue, float currentValue,
            IEnumerable<GameplayAttributeModifier> modifiers)
        {
            Id = id;
            BaseValue = baseValue;
            CurrentValue = currentValue;
            Modifiers = new ReadOnlyCollection<GameplayAttributeModifier>(new List<GameplayAttributeModifier>(modifiers));
        }
    }

    /// <summary>One exact tag and a detached, ordered copy of its contributing sources.</summary>
    public sealed class GameplayTagContributionSnapshot
    {
        public GameplayTag ExactTag { get; }
        public IReadOnlyList<GameplaySourceId> SourceIds { get; }

        public GameplayTagContributionSnapshot(GameplayTag exactTag, IEnumerable<GameplaySourceId> sourceIds)
        {
            ExactTag = exactTag;
            SourceIds = new ReadOnlyCollection<GameplaySourceId>(new List<GameplaySourceId>(sourceIds));
        }
    }

    /// <summary>Detached timing and ownership state for an active persistent Effect.</summary>
    public readonly struct GameplayActiveEffectSnapshot
    {
        public GameplayEffectHandle Handle { get; }
        public GameplayEffectDefinition Definition { get; }
        public GameplaySourceId SourceId { get; }
        public GameplayContext ApplicationContext { get; }
        public int StackCount { get; }
        public float? Remaining { get; }
        public float? PeriodRemaining { get; }
        public float AppliedDuration { get; }
        public float AppliedPeriod { get; }
        public IReadOnlyList<GameplayEffectLayerSnapshot> Layers { get; }

        public GameplayActiveEffectSnapshot(GameplayEffectHandle handle, GameplayEffectDefinition definition,
            GameplaySourceId sourceId, GameplayContext applicationContext, int stackCount,
            float? remaining, float? periodRemaining, float appliedDuration = 0f, float appliedPeriod = 0f,
            IEnumerable<GameplayEffectLayerSnapshot> layers = null)
        {
            Handle = handle;
            Definition = definition;
            SourceId = sourceId;
            ApplicationContext = applicationContext;
            StackCount = stackCount;
            Remaining = remaining;
            PeriodRemaining = periodRemaining;
            AppliedDuration = appliedDuration;
            AppliedPeriod = appliedPeriod;
            Layers = new List<GameplayEffectLayerSnapshot>(layers ?? Array.Empty<GameplayEffectLayerSnapshot>()).AsReadOnly();
        }
    }

    /// <summary>Detached grant state; Status is read from the authoritative Ability container.</summary>
    public readonly struct GameplayGrantedAbilitySnapshot
    {
        public GameplayAbilityHandle Handle { get; }
        public GameplayAbilityDefinition Definition { get; }
        public GameplayAbilityStatus Status { get; }
        public bool IsActive { get; }
        public GameplayContext ActiveContext { get; }
        /// <summary>ActiveContext also carries the immutable Begin context while Preparing.</summary>
        public GameplayAbilityPhase Phase => Status.Phase;
        public int OwnedEffectsCount { get; }
        public int OwnedLayersCount { get; }
        public int OwnedLifetimesCount { get; }
        /// <summary>Latest termination of this grant; null before its first terminated scope.</summary>
        public GameplayAbilityTerminationObservation LastTermination { get; }
        public int Level { get; }

        public GameplayGrantedAbilitySnapshot(GameplayAbilityHandle handle, GameplayAbilityDefinition definition,
            GameplayAbilityStatus status, bool isActive, GameplayContext activeContext,
            int ownedEffectsCount = 0, int ownedLayersCount = 0, int ownedLifetimesCount = 0, GameplayAbilityTerminationObservation lastTermination = null,
            int level = 1)
        {
            Handle = handle;
            Definition = definition;
            Status = status;
            IsActive = isActive;
            ActiveContext = activeContext;
            OwnedEffectsCount = ownedEffectsCount;
            OwnedLayersCount = ownedLayersCount;
            OwnedLifetimesCount = ownedLifetimesCount;
            LastTermination = lastTermination;
            Level = level;
        }
    }

    /// <summary>Detached progress of one active Sequence; Effect lifetime is not owned here.</summary>
    public readonly struct GameplayActiveSequenceSnapshot
    {
        public GameplaySequenceHandle Handle { get; }
        public GameplaySequenceDefinition Definition { get; }
        public GameplayContext Context { get; }
        public float Elapsed { get; }
        public int NextMarkerIndex { get; }
        public float? NextMarkerTime { get; }

        public GameplayActiveSequenceSnapshot(GameplaySequenceHandle handle, GameplaySequenceDefinition definition,
            GameplayContext context, float elapsed, int nextMarkerIndex, float? nextMarkerTime)
        {
            Handle = handle;
            Definition = definition;
            Context = context;
            Elapsed = elapsed;
            NextMarkerIndex = nextMarkerIndex;
            NextMarkerTime = nextMarkerTime;
        }
    }

    /// <summary>Publish order and immutable event value captured by an opt-in Dispatcher history.</summary>
    public readonly struct GameplayEventHistoryEntry
    {
        public long Sequence { get; }
        public GameplayEventData Data { get; }

        public GameplayEventHistoryEntry(long sequence, GameplayEventData data)
        {
            Sequence = sequence;
            Data = data;
        }
    }
}
