using System;
using FRFrame.Gameplay.Extensions;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Push-based release of a foreign-target effect cleanup relation. Target expiry/despawn
    /// removes the Ability's stale relation; Ability End removes only this exact target effect.</summary>
    internal sealed class GameplayOwnedTargetEffectLease : IDisposable
    {
        private readonly GameplayAbilityContainer _abilities;
        private readonly GameplayAbilityHandle _ability;
        private readonly GameplayEffectContainer _effects;
        private readonly GameplayEffectLease _lease;
        private bool _disposed;
        public GameplayOwnedTargetEffectLease(GameplayAbilityContainer abilities, GameplayAbilityHandle ability,
            GameplayEffectContainer effects, GameplayEffectLease lease)
        { _abilities = abilities; _ability = ability; _effects = effects; _lease = lease; _effects.Removed += Removed; }
        private void Removed(GameplayEffectRemovalObservation observation) { if (observation.Handle == _lease.Handle) Dispose(); }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true; _effects.Removed -= Removed;
            _abilities.ReleaseOwnedLifetime(_ability, this); _lease.Dispose();
        }
    }
    internal sealed class GameplayOwnedSequenceLease : IDisposable
    {
        private readonly GameplayStandardActor _actor;
        private readonly GameplayAbilityHandle _ability;
        private readonly GameplaySequenceHandle _sequence;
        private bool _disposed;
        public GameplayOwnedSequenceLease(GameplayStandardActor actor, GameplayAbilityHandle ability, GameplaySequenceHandle sequence)
        { _actor = actor; _ability = ability; _sequence = sequence; _actor.Sequences.Terminated += Ended; }
        private void Ended(GameplaySequenceTermination observation) { if (observation.Handle.Equals(_sequence)) Dispose(); }
        public void Dispose()
        {
            if (_disposed) return; _disposed = true; _actor.Sequences.Terminated -= Ended;
            _actor.Abilities.ReleaseOwnedLifetime(_ability, this); _actor.Sequences.Cancel(_sequence);
        }
    }
    public static class GameplayStandardActivation
    {
        /// <summary>Resolves only this actor's active grant using the captured activation identity,
        /// including summoned actors whose birth origin belongs to the parent's cast.</summary>
        public static bool TryFindGrant(GameplayStandardActor actor, GameplayContext context, out GameplayAbilityHandle handle)
        {
            handle = default;
            if (actor.IsDisposed || context.InstigatorId != actor.Id) return false;
            foreach (var grant in actor.Abilities.GetGrantedEntriesSnapshot())
                if (grant.IsActive && actor.Abilities.TryCreateExecutionContext(grant.Handle, new GameplayTargetData(actor.Id), out var cast) &&
                    cast.ActivationSourceId == context.ActivationSourceId) { handle = grant.Handle; return true; }
            return false;
        }
    }
    /// <summary>Starts the existing Sequence clock and attaches its cancellation/cleanup relation to the cast.</summary>
    public sealed class GameplayOwnedSequenceAction : IGameplaySkillAction
    {
        public GameplaySequenceDefinition Sequence { get; }
        public GameplayOwnedSequenceAction(GameplaySequenceDefinition sequence) => Sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));
        public GameplaySkillActionResult Execute(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host)
        {
            if (!(host is GameplayStandardActor actor) || !GameplayStandardActivation.TryFindGrant(actor, context, out var grant))
                return GameplaySkillActionResult.Rejected;
            var handle = actor.Sequences.StartSkill(Sequence, context, triggerEvent, actor);
            if (!actor.Sequences.IsActive(handle)) return GameplaySkillActionResult.Succeeded;
            var lease = new GameplayOwnedSequenceLease(actor, grant, handle);
            if (actor.Abilities.TryOwnLifetime(grant, lease)) return GameplaySkillActionResult.Succeeded;
            lease.Dispose(); return GameplaySkillActionResult.Rejected;
        }
    }
    public sealed class GameplayEndAbilityAction : IGameplaySkillAction
    {
        public bool Cancel { get; }
        public GameplayEndAbilityAction(bool cancel = false) => Cancel = cancel;
        public GameplaySkillActionResult Execute(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host)
        {
            if (!(host is GameplayStandardActor actor) || !GameplayStandardActivation.TryFindGrant(actor, context, out var grant))
                return GameplaySkillActionResult.Rejected;
            return (Cancel ? actor.Abilities.Cancel(grant) : actor.Abilities.End(grant)) ? GameplaySkillActionResult.Succeeded : GameplaySkillActionResult.Rejected;
        }
    }
}
