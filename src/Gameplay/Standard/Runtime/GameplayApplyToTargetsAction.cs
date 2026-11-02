using System;
using FRFrame.Gameplay.Extensions;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>First-party effect action for every resolved target. Uses each target's sole EffectContainer.
    /// Optional activation cleanup is registered as exact leases, including foreign-target effects;
    /// it never asks an Ability to own a foreign container's handle or scans by Definition.</summary>
    public sealed class GameplayApplyToTargetsAction : IGameplaySkillAction, IGameplaySkillContextAction, IGameplaySkillGuardedContextAction
    {
        public GameplayEffectDefinition Effect { get; }
        public bool OwnedByAbility { get; }
        public GameplayApplyToTargetsAction(GameplayEffectDefinition effect, bool ownedByAbility = false)
        {
            Effect = effect ?? throw new ArgumentNullException(nameof(effect)); OwnedByAbility = ownedByAbility;
            if (ownedByAbility && effect.Stacking != null) throw new ArgumentException("Ability-owned target effects must be unstacked; Area uses contribution leases for shared stacks.");
        }
        public GameplaySkillActionResult Execute(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host) => ExecuteWithContext(context, triggerEvent, host).Result;
        public GameplaySkillActionExecution ExecuteWithContext(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host) => ExecuteGuarded(context, triggerEvent, host, null);
        public GameplaySkillActionExecution ExecuteGuarded(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host, Func<bool> canContinue)
        {
            if (!(host is GameplayStandardActor actor) || actor.IsDisposed) throw new InvalidOperationException("Standard target effects require a standard world actor.");
            GameplayAbilityHandle owner = default;
            if (OwnedByAbility && Effect.DurationPolicy != GameplayEffectDurationPolicy.Instant)
            {
                if (!GameplayStandardActivation.TryFindGrant(actor, context, out owner))
                    return new GameplaySkillActionExecution(GameplaySkillActionResult.Rejected, context);
            }
            var ids = context.TargetData.Targets.Count > 0 ? context.TargetData.Targets :
                context.TargetData.HasPrimaryTarget ? new[] { context.TargetData.PrimaryTargetId } : Array.Empty<GameplaySourceId>();
            foreach (var id in ids)
            {
                if (canContinue != null && !canContinue()) return new GameplaySkillActionExecution(GameplaySkillActionResult.Rejected, context);
                if (!actor.World.TryGetActor(id, out var target)) continue;
                var targeted = context.WithTarget(new GameplayTargetData(id, target.Position, null, new[] { id }));
                var result = actor.ApplyEffect(targeted, Effect);
                if (!result.Succeeded) return new GameplaySkillActionExecution(GameplaySkillActionResult.Rejected, context);
                if (owner.IsValid && GameplayEffectLease.TryCapture(target.Effects, result, out var lease))
                {
                    var owned = new GameplayOwnedTargetEffectLease(actor.Abilities, owner, target.Effects, lease);
                    if (!actor.Abilities.TryOwnLifetime(owner, owned))
                    {
                        owned.Dispose(); return new GameplaySkillActionExecution(GameplaySkillActionResult.Rejected, context);
                    }
                }
            }
            return new GameplaySkillActionExecution(GameplaySkillActionResult.Succeeded, context);
        }
    }
}
