using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Extensions;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Consumable shield ownership over the actual Shield resource. EffectContainer alone
    /// owns expiry; each contribution has an exact unstacked Effect lease. FIFO consumes owned shields
    /// before unowned capacity, and removal subtracts only that contribution's unspent remainder.</summary>
    public sealed class GameplayShieldContributions : IDisposable
    {
        private readonly GameplayStandardActor _target;
        private readonly List<GameplayShieldContribution> _entries=new List<GameplayShieldContribution>();
        private bool _disposed;
        internal GameplayShieldContributions(GameplayStandardActor target)
        {_target=target;target.Effects.Removed+=OnRemoved;}
        public IReadOnlyList<GameplayShieldContribution> Snapshot()=>_entries.ToArray();
        internal GameplayShieldContribution Add(GameplayContext context,GameplayEffectLease lease,float amount)
        {
            if(_disposed)throw new ObjectDisposedException(nameof(GameplayShieldContributions));
            float before=Balance();Reconcile(before);
            var entry=new GameplayShieldContribution(this,context,lease,amount);
            _target.Resources.SetBalancesAtomic(new Dictionary<GameplayAttributeId,float>{{_target.CombatAttributes.Shield,before+amount}});
            _entries.Add(entry);return entry;
        }
        private float Balance()
        {
            if(_target.CombatAttributes==null||!_target.CombatAttributes.Shield.IsValid||!_target.Resources.TryGetBalance(_target.CombatAttributes.Shield,out float balance))
                throw new InvalidOperationException("Shield mapping is unavailable.");
            return balance;
        }
        private void Reconcile(float actual)
        {
            double total=0;foreach(var entry in _entries)total+=entry.Remaining;
            double removed=Math.Max(0,total-Math.Max(0,actual));
            foreach(var entry in _entries){float take=(float)Math.Min(removed,entry.Remaining);entry.Remaining-=take;removed-=take;}
        }
        /// <summary>Prepares ownership allocation before the atomic resource write. Apply the returned
        /// commit only after that write succeeds; resource stores must use the synchronous Core contract.</summary>
        internal Func<Action> PrepareConsumption(float amount,float before, GameplayContext hit)
        {
            Reconcile(before);var updates=new List<(GameplayShieldContribution entry,float remaining,float absorbed)>();
            foreach (var entry in _entries)
            {
                float take = Math.Min(amount, entry.Remaining);
                if (take > 0) updates.Add((entry, entry.Remaining - take, take));
                amount -= take;
                if (amount <= 0) break;
            }
            return () =>
            {
                Action callbacks = null;
                foreach (var update in updates)
                    if (!update.entry.IsDisposed) update.entry.Remaining = update.remaining;
                foreach (var update in updates)
                {
                    var entry = update.entry;
                    if (entry.IsDisposed) continue;
                    if (update.remaining > 0)
                    {
                        var absorbed = entry.Absorbed;
                        if (absorbed != null) callbacks += () => absorbed(update.absorbed);
                        continue;
                    }
                    var broken = entry.Broken;
                    Remove(entry, true);
                    if (broken != null) callbacks += () => broken(hit);
                }
                return callbacks;
            };
        }
        private void OnRemoved(GameplayEffectRemovalObservation observation)
        {
            foreach (var entry in _entries.ToArray())
            {
                if (entry.Lease.IsActive) continue;
                var expired = observation.Handle == entry.EffectHandle && observation.Cause == GameplayEffectRemovalCause.NaturalExpiry
                    ? entry.Expired : null;
                Remove(entry, false);
                expired?.Invoke();
            }
        }
        internal void RemoveOnDeath()
        {
            foreach (var entry in _entries.ToArray())
                Remove(entry, true);
        }
        internal void Remove(GameplayShieldContribution entry,bool removeEffect)
        {
            if(entry.IsDisposed)return;
            if (entry.Remaining > 0)
            {
                float before = Balance();
                Reconcile(before);
                if (entry.Remaining > 0)
                    _target.Resources.SetBalancesAtomic(new Dictionary<GameplayAttributeId,float>{{_target.CombatAttributes.Shield,Math.Max(0,before-entry.Remaining)}});
            }
            _entries.Remove(entry);entry.MarkRemoved();
            if(removeEffect)entry.Lease.Dispose();
        }
        public void Dispose()
        {
            if(_disposed)return;_disposed=true;_target.Effects.Removed-=OnRemoved;
            foreach(var entry in _entries.ToArray())Remove(entry,true);
        }
    }
    /// <summary>Exact remaining consumable capacity. Context preserves root/level; lease supplies the
    /// existing Effect lifetime. Cancel/expiry never remove another shield's remaining capacity.</summary>
    public sealed class GameplayShieldContribution : IDisposable
    {
        private readonly GameplayShieldContributions _owner;
        internal GameplayEffectLease Lease {get;}
        public GameplayContext Context {get;}
        public GameplayEffectHandle EffectHandle=>Lease.Handle;
        public float Remaining {get;internal set;}
        public bool IsDisposed {get;private set;}
        internal Action ReleaseBinding;
        internal Action<GameplayContext> Broken;
        internal Action Expired;
        internal Action<float> Absorbed;
        internal GameplayShieldContribution(GameplayShieldContributions owner,GameplayContext context,GameplayEffectLease lease,float amount)
        {_owner=owner;Context=context;Lease=lease;Remaining=amount;}
        internal void MarkRemoved(){IsDisposed=true;Remaining=0;Broken=null;Expired=null;Absorbed=null;ReleaseBinding?.Invoke();ReleaseBinding=null;}
        public void Dispose()=>_owner.Remove(this,true);
    }
    /// <summary>Grants a separate timed shield using an actual Effect clock; no time lives in this action.</summary>
    public sealed class GameplayGrantTimedShieldAction : IGameplaySkillAction
    {
        private readonly IGameplayFloatValue _amount;
        private readonly GameplayEffectDefinition _lifetime;
        private readonly bool _ownedByAbility;
        private readonly IReadOnlyList<IGameplaySkillAction> _onBroken, _onExpired, _onAbsorbed;
        public GameplayGrantTimedShieldAction(IGameplayFloatValue amount,IGameplayFloatValue duration,bool ownedByAbility=false,
            IEnumerable<IGameplaySkillAction> onBroken = null, IEnumerable<IGameplaySkillAction> onExpired = null,
            IGameplayEffectPresentationDefinition presentation = null, IEnumerable<IGameplaySkillAction> onAbsorbed = null)
        {
            _amount=amount??throw new ArgumentNullException(nameof(amount));
            _lifetime=new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration,1,0,Array.Empty<GameplayEffectAttributeModifierDefinition>(),Array.Empty<GameplayTag>(),Array.Empty<IGameplayEffectExecution>(),null,
                effectTags:new[]{new GameplayTag("Effect.Standard.Shield")},durationValue:duration??throw new ArgumentNullException(nameof(duration)),
                presentation:presentation);
            _ownedByAbility=ownedByAbility;
            _onBroken = GameplaySpatialMath.Actions(onBroken);
            _onExpired = GameplaySpatialMath.Actions(onExpired);
            _onAbsorbed = GameplaySpatialMath.Actions(onAbsorbed);
        }
        public GameplaySkillActionResult Execute(GameplayContext context,GameplayEventData triggerEvent,IGameplaySkillHost host)
        {
            if(!(host is GameplayStandardActor actor)||!context.TargetData.HasPrimaryTarget||!actor.World.TryGetActor(context.TargetData.PrimaryTargetId,out var target)||target.CombatAttributes==null)
                return GameplaySkillActionResult.Rejected;
            GameplayAbilityHandle grant=default;
            if(_ownedByAbility&&!GameplayStandardActivation.TryFindGrant(actor,context,out grant))return GameplaySkillActionResult.Rejected;
            var application=new GameplayEffectApplicationContext(context,target,context.Values.AsSetByCallerValues(),actor.Attributes,actor.Id,instigatorTarget:actor);
            float amount=GameplayValueEvaluator.Evaluate(_amount,application);if(amount<0)throw new ArithmeticException("Shield must be nonnegative.");
            var result=target.Effects.Apply(_lifetime,application);
            if(!GameplayEffectLease.TryCapture(target.Effects,result,out var lease))return GameplaySkillActionResult.Rejected;
            GameplayShieldContribution contribution=null;
            try
            {
                contribution=target.Shields.Add(context,lease,amount);
                if (_onAbsorbed.Count > 0) contribution.Absorbed = absorbed =>
                {
                    if (!actor.IsTargetable || !target.IsTargetable) return;
                    var impact = context.WithTarget(new GameplayTargetData(target.Id, target.Position));
                    actor.RunPresentationCallback(_onAbsorbed, impact,
                        new GameplayEventData(new GameplayTag("Event.Standard.Shield.Absorbed"), impact, absorbed));
                };
                if (_onBroken.Count > 0) contribution.Broken = hit =>
                {
                    if (!actor.IsTargetable || !target.IsTargetable ||
                        !actor.World.TryGetActor(hit.InstigatorId, out var breaker) || !breaker.IsTargetable) return;
                    var retaliate = context.WithTarget(new GameplayTargetData(breaker.Id, breaker.Position));
                    actor.RunPresentationCallback(_onBroken, retaliate,
                        new GameplayEventData(new GameplayTag("Event.Standard.Shield.Broken"), retaliate, 1));
                };
                if (_onExpired.Count > 0) contribution.Expired = () =>
                {
                    if (!actor.IsTargetable || !target.IsTargetable) return;
                    actor.RunPresentationCallback(_onExpired, context,
                        new GameplayEventData(new GameplayTag("Event.Standard.Shield.Expired"), context, 1));
                };
                if(_ownedByAbility&&!actor.Abilities.TryOwnLifetime(grant,contribution)){contribution.Dispose();return GameplaySkillActionResult.Rejected;}
                if(_ownedByAbility)contribution.ReleaseBinding=()=>actor.Abilities.ReleaseOwnedLifetime(grant,contribution);
            }
            catch{if(contribution!=null)contribution.Dispose();else lease.Dispose();throw;}
            target.Resources.TryGetBalance(target.CombatAttributes.Shield,out float after);
            target.ReportCombat(new GameplayCombatOutcome(context,GameplayCombatOutcomeKind.Shield,default,amount,0,after-amount,after));
            return GameplaySkillActionResult.Succeeded;
        }
    }
}
