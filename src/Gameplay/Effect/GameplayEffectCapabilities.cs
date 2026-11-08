using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay
{
    /// <summary>Immutable presentation definition supplied by an optional integration assembly.</summary>
    public interface IGameplayEffectPresentationDefinition { }

    /// <summary>Committed layer state for a configured presentation, identified by its exact live handle.</summary>
    public readonly struct GameplayEffectPresentationState
    {
        public GameplayEffectHandle Handle { get; }
        public GameplayEffectDefinition Definition { get; }
        public int StackCount { get; }
        internal GameplayEffectPresentationState(GameplayEffectHandle handle, GameplayEffectDefinition definition, int stackCount)
        { Handle = handle; Definition = definition; StackCount = stackCount; }
    }

    /// <summary>Immediate execution also runs on successful increase/refresh, never rejection or overflow.</summary>
    public enum GameplayEffectPeriodicStartPolicy { AfterFirstPeriod, ExecuteOnApplication }
    /// <summary>Shared keeps the legacy entry clock; independent expires each real contribution on the same owner timeline.</summary>
    public enum GameplayEffectStackLifetimePolicy { SharedEntryLifetime, IndependentLayerLifetime }
    /// <summary>NaturalExpiry can only be produced by the container clock, never by a public removal request.</summary>
    public enum GameplayEffectRemovalCause { NaturalExpiry, Explicit, Dispel, OngoingRequirementMismatch, OwnerCleanup, Overflow }

    /// <summary>Container-local contribution identity; validity does not imply the layer is still alive.</summary>
    public readonly struct GameplayEffectLayerHandle : IEquatable<GameplayEffectLayerHandle>
    {
        public GameplayEffectHandle EffectHandle { get; }
        public long LocalSequence { get; }
        public bool IsValid => EffectHandle.IsValid && LocalSequence > 0;
        internal GameplayEffectLayerHandle(GameplayEffectHandle effect, long sequence)
        { EffectHandle = effect; LocalSequence = sequence; }
        public bool Equals(GameplayEffectLayerHandle other) => EffectHandle == other.EffectHandle && LocalSequence == other.LocalSequence;
        public override bool Equals(object obj) => obj is GameplayEffectLayerHandle other && Equals(other);
        public override int GetHashCode() => (EffectHandle.GetHashCode() * 397) ^ LocalSequence.GetHashCode();
        public static bool operator ==(GameplayEffectLayerHandle a, GameplayEffectLayerHandle b) => a.Equals(b);
        public static bool operator !=(GameplayEffectLayerHandle a, GameplayEffectLayerHandle b) => !a.Equals(b);
    }

    /// <summary>Classification query against the effect itself, never its target's granted state tags.</summary>
    public sealed class GameplayEffectQuery
    {
        public GameplayTagQuery EffectTags { get; }
        public GameplayEffectQuery(GameplayTagQuery effectTags) => EffectTags = effectTags ?? throw new ArgumentNullException(nameof(effectTags));
        internal bool Matches(GameplayEffectDefinition definition)
        {
            foreach (var required in EffectTags.All) if (!Has(definition.EffectTags, required)) return false;
            if (EffectTags.Any.Count > 0)
            {
                bool any = false;
                foreach (var tag in EffectTags.Any) if (Has(definition.EffectTags, tag)) { any = true; break; }
                if (!any) return false;
            }
            foreach (var tag in EffectTags.None) if (Has(definition.EffectTags, tag)) return false;
            return true;
        }
        private static bool Has(IReadOnlyList<GameplayTag> tags, GameplayTag required)
        {
            foreach (var tag in tags) if (tag == required || tag.IsDescendantOf(required)) return true;
            return false;
        }
    }

    /// <summary>Detached notification after all contributions and the entry have been removed.</summary>
    public sealed class GameplayEffectRemovalObservation
    {
        public GameplayEffectDefinition Definition { get; }
        public GameplayEffectHandle Handle { get; }
        public GameplaySourceId SourceId { get; }
        public GameplayEffectApplicationContext ApplicationContext { get; }
        public int StackCount { get; }
        public GameplayEffectRemovalCause Cause { get; }
        internal GameplayEffectRemovalObservation(GameplayEffectDefinition definition, GameplayEffectHandle handle,
            GameplaySourceId source, GameplayEffectApplicationContext application, int count, GameplayEffectRemovalCause cause)
        { Definition = definition; Handle = handle; SourceId = source; ApplicationContext = application; StackCount = count; Cause = cause; }
    }

    /// <summary>Ordered synchronous overflow rule. Completed actions stay committed when a later action throws.</summary>
    public interface IGameplayEffectOverflowExecution { void Execute(GameplayEffectOverflowContext context); }
    /// <summary>Incoming application identity and explicit seams into the same lifecycle owner.</summary>
    public sealed class GameplayEffectOverflowContext
    {
        private readonly GameplayEffectContainer _container;
        public GameplayEffectHandle Handle { get; }
        public GameplayEffectApplicationContext Application { get; }
        public int StackCount { get; }
        internal GameplayEffectOverflowContext(GameplayEffectContainer container, GameplayEffectHandle handle,
            GameplayEffectApplicationContext application, int count)
        { _container = container; Handle = handle; Application = application.WithStackCount(count); StackCount = count; }
        public GameplayEffectApplicationResult Apply(GameplayEffectDefinition next) => _container.Apply(next, Application);
        public bool RemoveCurrent() => _container.Remove(Handle, GameplayEffectRemovalCause.Overflow);
        public bool RemoveLayer(GameplayEffectLayerHandle layer) => layer.EffectHandle == Handle &&
            _container.RemoveLayer(layer, GameplayEffectRemovalCause.Overflow);
    }

    /// <summary>Detached layer identity, original application, and independent remaining lifetime (null for infinite).</summary>
    public readonly struct GameplayEffectLayerSnapshot
    {
        public GameplayEffectLayerHandle Handle { get; }
        public GameplayEffectApplicationContext ApplicationContext { get; }
        public float? Remaining { get; }
        public float AppliedDuration { get; }
        internal GameplayEffectLayerSnapshot(GameplayEffectLayerHandle handle, GameplayEffectApplicationContext application,
            float? remaining, float duration)
        { Handle = handle; ApplicationContext = application; Remaining = remaining; AppliedDuration = duration; }
    }
}
