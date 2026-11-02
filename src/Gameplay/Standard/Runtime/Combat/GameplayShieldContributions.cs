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
        internal Action PrepareConsumption(float amount,float before)
        {
            Reconcile(before);var updates=new List<(GameplayShieldContribution entry,float remaining)>();
            foreach(var entry in _entries){float take=Math.Min(amount,entry.Remaining);updates.Add((entry,entry.Remaining-take));amount-=take;}
            return ()=>{foreach(var update in updates)if(!update.entry.IsDisposed)update.entry.Remaining=update.remaining;};
        }
        private void OnRemoved(GameplayEffectRemovalObservation observation)
        {foreach(var entry in _entries.ToArray())if(!entry.Lease.IsActive)Remove(entry,false);}
        internal void Remove(GameplayShieldContribution entry,bool removeEffect)
        {
            if(entry.IsDisposed)return;
            float before=Balance();Reconcile(before);
            _target.Resources.SetBalancesAtomic(new Dictionary<GameplayAttributeId,float>{{_target.CombatAttributes.Shield,Math.Max(0,before-entry.Remaining)}});
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
        internal GameplayShieldContribution(GameplayShieldContributions owner,GameplayContext context,GameplayEffectLease lease,float amount)
        {_owner=owner;Context=context;Lease=lease;Remaining=amount;}
        internal void MarkRemoved(){IsDisposed=true;Remaining=0;ReleaseBinding?.Invoke();ReleaseBinding=null;}
        public void Dispose()=>_owner.Remove(this,true);
    }
    /// <summary>Grants a separate timed shield using an actual Effect clock; no time lives in this action.</summary>
    public sealed class GameplayGrantTimedShieldAction : IGameplaySkillAction
    {
        private readonly IGameplayFloatValue _amount;
        private readonly GameplayEffectDefinition _lifetime;
        private readonly bool _ownedByAbility;
        public GameplayGrantTimedShieldAction(IGameplayFloatValue amount,IGameplayFloatValue duration,bool ownedByAbility=false)
        {
            _amount=amount??throw new ArgumentNullException(nameof(amount));
            _lifetime=new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration,1,0,Array.Empty<GameplayEffectAttributeModifierDefinition>(),Array.Empty<GameplayTag>(),Array.Empty<IGameplayEffectExecution>(),null,
                effectTags:new[]{new GameplayTag("Effect.Standard.Shield")},durationValue:duration??throw new ArgumentNullException(nameof(duration)));
            _ownedByAbility=ownedByAbility;
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
