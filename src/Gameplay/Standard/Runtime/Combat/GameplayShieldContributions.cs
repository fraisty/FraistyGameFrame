using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Extensions;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Determines whether a cast starts a new contribution or updates its existing group.</summary>
    public enum GameplayShieldReapplicationPolicy
    {
        IndependentContributions = 0,
        RefreshSingleCapacityAndDuration,
        AccumulateSingleCapacityAndExtendDuration
    }

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
        internal GameplayShieldContribution Add(GameplayContext context, GameplayEffectLease lease, float amount,
            GameplayEffectDefinition group, float before, float after)
        {
            if(_disposed)throw new ObjectDisposedException(nameof(GameplayShieldContributions));
            WriteBalance(after);
            Reconcile(before);
            var entry = new GameplayShieldContribution(this, context, lease, amount, group);
            _entries.Add(entry);return entry;
        }

        internal bool TryReapply(GameplayEffectDefinition group, GameplayEffectApplicationContext application,
            bool ownedByAbility, bool extend, float amount, float before, out GameplayShieldContribution exhausted, out float after)
        {
            exhausted = null;
            after = before;
            var context = application.Context;
            var entry = _entries.Find(candidate => ReferenceEquals(candidate.Group, group) && candidate.Lease.IsActive &&
                candidate.Context.InstigatorId == context.InstigatorId &&
                (!ownedByAbility || candidate.Context.ActivationSourceId == context.ActivationSourceId));
            if (entry == null) return false;

            // Project reconciliation before committing either the clock or resource. Only the matching
            // entry's FIFO position is needed to calculate how much of an external debit reached it.
            double debit = UnaccountedDebit(before);
            foreach (var candidate in _entries)
            {
                if (candidate == entry) break;
                debit = Math.Max(0, debit - candidate.Remaining);
            }
            float remaining = entry.Remaining - (float)Math.Min(debit, entry.Remaining);
            if (remaining <= 0)
            {
                exhausted = entry;
                return false;
            }

            float capacity = extend ? AddCapacity(remaining, amount) : amount;
            float balance = AddCapacity(before, (double)capacity - remaining);
            float duration = GameplayValueEvaluator.Evaluate(group.DurationValue, application);
            if (!_target.Effects.TryUpdateUnstackedDuration(entry.EffectHandle, duration, extend, () => WriteBalance(balance)))
                return false;
            Reconcile(before);
            entry.Remaining = capacity;
            after = balance;
            return true;
        }

        // Balance and capacity are floats, while the delta must be computed without losing another group's share.
        internal static float AddCapacity(float before, double delta)
        {
            double total = before + delta;
            float result = (float)total;
            if (!float.IsFinite(result) || total > float.MaxValue || (delta != 0 && result == before))
                throw new ArithmeticException("The requested Shield balance cannot be represented.");
            return result;
        }
        private void WriteBalance(float value) =>
            _target.Resources.SetBalancesAtomic(new Dictionary<GameplayAttributeId,float>{{_target.CombatAttributes.Shield,value}});
        internal float Balance()
        {
            if(_target.CombatAttributes==null||!_target.CombatAttributes.Shield.IsValid||!_target.Resources.TryGetBalance(_target.CombatAttributes.Shield,out float balance))
                throw new InvalidOperationException("Shield mapping is unavailable.");
            return balance;
        }
        private double UnaccountedDebit(float actual)
        {
            double total=0;foreach(var entry in _entries)total+=entry.Remaining;
            return Math.Max(0,total-Math.Max(0,actual));
        }
        private void Reconcile(float actual)
        {
            double removed=UnaccountedDebit(actual);
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
                    var removed = Remove(entry, true);
                    if (broken != null) callbacks += () => broken(hit);
                    callbacks += removed;
                }
                return callbacks;
            };
        }
        private void OnRemoved(GameplayEffectRemovalObservation observation)
        {
            var entry = _entries.Find(candidate => candidate.EffectHandle == observation.Handle);
            if (entry == null) return;
            var expired = observation.Cause == GameplayEffectRemovalCause.NaturalExpiry ? entry.Expired : null;
            var removed = Remove(entry, false);
            expired?.Invoke();
            removed?.Invoke();
        }
        internal void RemoveOnDeath()
        {
            foreach (var entry in _entries.ToArray())
                Remove(entry, true)?.Invoke();
        }
        // Return the completion so combat can dispatch it after the committed break/expiry branch.
        internal Action Remove(GameplayShieldContribution entry,bool removeEffect)
        {
            if(entry.IsDisposed)return null;
            var removed = entry.Removed;
            if (entry.Remaining > 0)
            {
                float before = Balance();
                Reconcile(before);
                if (entry.Remaining > 0)
                    _target.Resources.SetBalancesAtomic(new Dictionary<GameplayAttributeId,float>{{_target.CombatAttributes.Shield,Math.Max(0,before-entry.Remaining)}});
            }
            _entries.Remove(entry);entry.MarkRemoved();
            if(removeEffect)entry.Lease.Dispose();
            return removed;
        }
        public void Dispose()
        {
            if(_disposed)return;_disposed=true;_target.Effects.Removed-=OnRemoved;
            foreach(var entry in _entries.ToArray())Remove(entry,true)?.Invoke();
        }
    }
    /// <summary>Exact remaining consumable capacity. Context preserves root/level; lease supplies the
    /// existing Effect lifetime. Cancel/expiry never remove another shield's remaining capacity.</summary>
    public sealed class GameplayShieldContribution : IDisposable
    {
        private readonly GameplayShieldContributions _owner;
        internal GameplayEffectLease Lease {get;}
        internal GameplayEffectDefinition Group {get;}
        public GameplayContext Context {get;}
        public GameplayEffectHandle EffectHandle=>Lease.Handle;
        public float Remaining {get;internal set;}
        public bool IsDisposed {get;private set;}
        internal Action ReleaseBinding;
        internal Action<GameplayContext> Broken;
        internal Action Expired;
        internal Action<float> Absorbed;
        internal Action Removed;
        internal GameplayShieldContribution(GameplayShieldContributions owner, GameplayContext context, GameplayEffectLease lease,
            float amount, GameplayEffectDefinition group)
        {_owner=owner;Context=context;Lease=lease;Remaining=amount;Group=group;}
        internal void MarkRemoved(){IsDisposed=true;Remaining=0;Broken=null;Expired=null;Absorbed=null;Removed=null;ReleaseBinding?.Invoke();ReleaseBinding=null;}
        public void Dispose()=>_owner.Remove(this,true)?.Invoke();
    }
    /// <summary>Grants or reapplies a timed shield. The target EffectContainer owns its clock;
    /// aggregate groups preserve the original application context and FIFO position.</summary>
    public sealed class GameplayGrantTimedShieldAction : IGameplaySkillAction
    {
        private readonly IGameplayFloatValue _amount;
        private readonly GameplayEffectDefinition _lifetime;
        private readonly bool _ownedByAbility;
        private readonly GameplayShieldReapplicationPolicy _reapplicationPolicy;
        private readonly IReadOnlyList<IGameplaySkillAction> _onBroken, _onExpired, _onAbsorbed, _onRemoved;
        public GameplayGrantTimedShieldAction(IGameplayFloatValue amount,IGameplayFloatValue duration,bool ownedByAbility=false,
            IEnumerable<IGameplaySkillAction> onBroken = null, IEnumerable<IGameplaySkillAction> onExpired = null,
            IGameplayEffectPresentationDefinition presentation = null, IEnumerable<IGameplaySkillAction> onAbsorbed = null,
            IEnumerable<IGameplaySkillAction> onRemoved = null,
            GameplayShieldReapplicationPolicy reapplicationPolicy = GameplayShieldReapplicationPolicy.IndependentContributions)
        {
            if (!Enum.IsDefined(typeof(GameplayShieldReapplicationPolicy), reapplicationPolicy))
                throw new ArgumentOutOfRangeException(nameof(reapplicationPolicy));
            _amount=amount??throw new ArgumentNullException(nameof(amount));
            _lifetime=new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration,1,0,Array.Empty<GameplayEffectAttributeModifierDefinition>(),Array.Empty<GameplayTag>(),Array.Empty<IGameplayEffectExecution>(),null,
                effectTags:new[]{new GameplayTag("Effect.Standard.Shield")},durationValue:duration??throw new ArgumentNullException(nameof(duration)),
                presentation:presentation);
            _ownedByAbility=ownedByAbility;
            _reapplicationPolicy = reapplicationPolicy;
            _onBroken = GameplaySpatialMath.Actions(onBroken);
            _onExpired = GameplaySpatialMath.Actions(onExpired);
            _onAbsorbed = GameplaySpatialMath.Actions(onAbsorbed);
            _onRemoved = GameplaySpatialMath.Actions(onRemoved);
        }
        public GameplaySkillActionResult Execute(GameplayContext context,GameplayEventData triggerEvent,IGameplaySkillHost host)
        {
            if(!(host is GameplayStandardActor actor)||!context.TargetData.HasPrimaryTarget||!actor.World.TryGetActor(context.TargetData.PrimaryTargetId,out var target)||target.CombatAttributes==null)
                return GameplaySkillActionResult.Rejected;
            GameplayAbilityHandle grant=default;
            if(_ownedByAbility&&!GameplayStandardActivation.TryFindGrant(actor,context,out grant))return GameplaySkillActionResult.Rejected;
            var application=new GameplayEffectApplicationContext(context,target,context.Values.AsSetByCallerValues(),actor.Attributes,actor.Id,instigatorTarget:actor);
            float amount=GameplayValueEvaluator.Evaluate(_amount,application);if(amount<0)throw new ArithmeticException("Shield must be nonnegative.");
            bool grouped = _reapplicationPolicy != GameplayShieldReapplicationPolicy.IndependentContributions;
            if (grouped && (!target.IsTargetable || target.World.IsDisposed || amount == 0)) return GameplaySkillActionResult.Rejected;
            float before = target.Shields.Balance();
            GameplayShieldContribution exhausted = null;
            if (grouped)
            {
                bool extend = _reapplicationPolicy == GameplayShieldReapplicationPolicy.AccumulateSingleCapacityAndExtendDuration;
                if (target.Shields.TryReapply(_lifetime, application, _ownedByAbility, extend, amount, before, out exhausted, out float updated))
                {
                    target.ReportCombat(new GameplayCombatOutcome(context, GameplayCombatOutcomeKind.Shield,
                        default, updated - before, 0, before, updated));
                    return GameplaySkillActionResult.Succeeded;
                }
            }
            float after = grouped ? GameplayShieldContributions.AddCapacity(before, amount) : before + amount;
            GameplayShieldContribution contribution = null;
            target.Effects.ApplyUnstackedDuration(_lifetime, application, result =>
            {
                if (!GameplayEffectLease.TryCapture(target.Effects, result, out var lease))
                    throw new InvalidOperationException("A new unstacked Shield Effect must confer an exact lease.");
                contribution = target.Shields.Add(context, lease, amount, grouped ? _lifetime : null, before, after);
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
                        new GameplayEventData(new GameplayTag("Event.Standard.Shield.Broken"), retaliate, 1),
                        segmentStart: target.Position);
                };
                if (_onExpired.Count > 0) contribution.Expired = () =>
                {
                    if (!actor.IsTargetable || !target.IsTargetable) return;
                    actor.RunPresentationCallback(_onExpired, context,
                        new GameplayEventData(new GameplayTag("Event.Standard.Shield.Expired"), context, 1));
                };
            });
            if (contribution == null || contribution.IsDisposed) return GameplaySkillActionResult.Rejected;
            if(_ownedByAbility&&!actor.Abilities.TryOwnLifetime(grant,contribution)){contribution.Dispose();return GameplaySkillActionResult.Rejected;}
            if(_ownedByAbility)contribution.ReleaseBinding=()=>actor.Abilities.ReleaseOwnedLifetime(grant,contribution);
            if (_onRemoved.Count > 0) contribution.Removed = () =>
                {
                    if (actor.IsDisposed || (_ownedByAbility && !GameplayStandardActivation.TryFindGrant(actor, context, out _))) return;
                    actor.RunPresentationCallback(_onRemoved, context,
                        new GameplayEventData(new GameplayTag("Event.Standard.Shield.Removed"), context, 1));
                };
            // An external debit can silently empty the old group. Retire it after registering
            // the replacement so OnRemoved reentry sees the new group, without a fake break.
            exhausted?.Dispose();
            target.ReportCombat(new GameplayCombatOutcome(context,GameplayCombatOutcomeKind.Shield,default,amount,0,before,after));
            return GameplaySkillActionResult.Succeeded;
        }
    }
}
