using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Ability
{
    /// <summary>Guards clock edits, no-spend preparation and exclusive cleanup; visible skill behavior lives in the Lab.</summary>
    public sealed class GameplayAbilityLifecycleTests
    {
        private static readonly GameplayTag CdTag = new GameplayTag("Cooldown.Test.Lifecycle");
        private static readonly GameplayTag Block = new GameplayTag("State.Test.Interrupted");
        private static readonly GameplayTag Identity = new GameplayTag("Ability.Test.Scope");
        private static GameplayEffectDefinition Effect(float duration = 8, GameplayEffectStackingDefinition stack = null) =>
            new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, duration, 0,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), new[] { CdTag }, Array.Empty<IGameplayEffectExecution>(), stack);
        private static GameplayAbilityDefinition Definition(GameplayEffectDefinition cd = null, GameplayAbilityChargeDefinition charge = null, IGameplayAbilityCost cost = null) =>
            new GameplayAbilityDefinition(Array.Empty<GameplayTag>(), new[] { Block }, new[] { Identity }, cd, charge,
                abilityTags: new[] { Identity }, ongoingBlockedTags: new[] { Block }, cost: cost);
        private sealed class Host : IGameplayAbilityHost, IGameplaySkillHost
        {
            public GameplaySourceId OwnerId { get; }
            public GameplaySourceId TargetId => OwnerId;
            public AttributeSet Attributes { get; } = new AttributeSet();
            public GameplayTagContainer Tags { get; } = new GameplayTagContainer();
            public GameplayEffectContainer Effects { get; }
            public GameplayAbilityContainer Abilities { get; }
            public GameplaySourceIdAllocator Ids { get; } = new GameplaySourceIdAllocator();
            public int Resource = 10;
            public Host() { OwnerId = Ids.Allocate(); Effects = new GameplayEffectContainer(Attributes, Ids, Tags); Abilities = new GameplayAbilityContainer(this, Ids); }
            public GameplayContext Context => new GameplayContext(OwnerId, new GameplayTargetData(OwnerId));
            public GameplayEffectApplicationResult Apply(GameplayEffectDefinition definition) => Effects.Apply(definition,
                new GameplayEffectApplicationContext(Context, this, GameplaySetByCallerValues.Empty));
        }
        private sealed class Cost : IGameplayAbilityCost
        {
            public bool Reject;
            public int Commits;
            public bool CanPay(IGameplayAbilityHost host) => ((Host)host).Resource >= 2;
            public bool TryCommit(IGameplayAbilityHost host)
            { if (Reject || !CanPay(host)) return false; ((Host)host).Resource -= 2; Commits++; return true; }
        }
        private sealed class Lifetime : IDisposable
        {
            public int Calls;
            public Action Cleanup;
            public void Dispose() { Calls++; Cleanup?.Invoke(); }
        }

        [Test]
        public void CooldownEditsOnlyOwnEffectAndUsesEffectClock()
        {
            var h = new Host(); var definition = Effect(); var a = h.Abilities.Grant(Definition(cd: definition));
            Assert.That(h.Abilities.TryActivate(a), Is.True); h.Abilities.End(a);
            var external = h.Effects.Apply(definition); h.Effects.Advance(1);
            Assert.That(h.Abilities.TryAdjustCooldownRemaining(a, -2), Is.True);
            Assert.That(h.Abilities.GetStatus(a).CooldownRemaining, Is.EqualTo(5));
            h.Effects.TryGetRemainingDuration(external, out float other); Assert.That(other, Is.EqualTo(7));
            h.Abilities.TryAdjustCooldownRemaining(a, 6); Assert.That(h.Abilities.GetStatus(a).CooldownRemaining, Is.EqualTo(11));
            h.Abilities.TrySetCooldownRemaining(a, 3); h.Effects.Advance(3);
            Assert.That(h.Abilities.GetStatus(a).CooldownRemaining, Is.Zero);
            Assert.That(h.Abilities.GetStatus(a).BlockReason, Is.EqualTo(GameplayAbilityBlockReason.Cooldown));
            h.Abilities.TryResetCooldown(a); Assert.That(h.Abilities.GetStatus(a).CooldownRemaining, Is.EqualTo(8));
            h.Abilities.TryAdjustCooldownRemaining(a, -20); Assert.That(h.Abilities.GetStatus(a).CooldownRemaining, Is.Zero);
            Assert.That(h.Effects.IsActive(external), Is.True);
            Assert.That(h.Effects.LastRemoval.Cause, Is.EqualTo(GameplayEffectRemovalCause.Explicit));
            h.Effects.Remove(external); h.Abilities.TryAdjustCooldownRemaining(a, 2); h.Effects.Advance(2);
            Assert.That(h.Abilities.CanActivate(a), Is.True);
            var keyed=new GameplaySetByCallerFloatValue(new GameplayTag("Data.Test.CD"));
            var dynamicCooldown=new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration,1,0,Array.Empty<GameplayEffectAttributeModifierDefinition>(),new[]{CdTag},Array.Empty<IGameplayEffectExecution>(),null,
                durationValue:new GameplayBinaryFloatValue(GameplayBinaryFloatOperation.Add,new GameplayAbilityLevelFloatValue(),keyed));
            var dynamicDefinition=new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),dynamicCooldown,maxLevel:10);
            var b=h.Abilities.Grant(dynamicDefinition,5);
            Assert.That(h.Abilities.TryResetCooldown(b),Is.False,"Value-driven restart requires a previous successful cast capture.");
            var input=h.Context.WithValues(GameplayContextValues.Empty.WithValue(new GameplayTag("Data.Test.CD"),2));
            Assert.That(h.Abilities.TryActivate(b,input),Is.True);h.Abilities.End(b);
            Assert.That(h.Abilities.GetStatus(b).CooldownDuration,Is.EqualTo(7));
            h.Effects.Advance(7);h.Abilities.TrySetLevel(b,10);
            Assert.That(h.Abilities.TryResetCooldown(b),Is.True);Assert.That(h.Abilities.GetStatus(b).CooldownRemaining,Is.EqualTo(7),"Restart preserves last cast timing, never re-evaluates against a later grant level.");
            h.Abilities.TrySetCooldownRemaining(b,0);
            Assert.That(h.Abilities.TryActivate(b,input),Is.True);h.Abilities.End(b);
            Assert.That(h.Abilities.GetStatus(b).CooldownDuration,Is.EqualTo(12));
            Assert.That(h.Abilities.TryAdjustCooldownRemaining(b,-3),Is.True);Assert.That(h.Abilities.GetStatus(b).CooldownRemaining,Is.EqualTo(9));
        }
        [Test]
        public void ChargeEditsPreservePhaseAndFullClearsSchedule()
        {
            var h = new Host(); var a = h.Abilities.Grant(Definition(charge: new GameplayAbilityChargeDefinition(3,3)));
            h.Abilities.TrySetCharges(a, 0); h.Abilities.Advance(1);
            h.Abilities.TryAdjustCharges(a, 1); Assert.That(h.Abilities.GetStatus(a).RechargeRemaining, Is.EqualTo(2));
            Assert.That(h.Abilities.TryAdjustCharges(a, 3), Is.False);
            h.Abilities.TryAdjustCharges(a, -1); h.Abilities.Advance(2);
            Assert.That(h.Abilities.GetStatus(a).CurrentCharges, Is.EqualTo(1));
            h.Abilities.TrySetCharges(a, 3); h.Abilities.Advance(20); h.Abilities.TryAdjustCharges(a, -1);
            Assert.That(h.Abilities.GetStatus(a).RechargeRemaining, Is.EqualTo(3));
            for (int i=0; i<90; i++) h.Abilities.Advance(1f/30);
            Assert.That(h.Abilities.GetStatus(a).CurrentCharges, Is.EqualTo(3));
        }
        [Test]
        public void PreparingCancelDoesNotSpendAndCommitIsOnce()
        {
            var h=new Host(); var cost=new Cost(); var a=h.Abilities.Grant(Definition(charge:new GameplayAbilityChargeDefinition(3,3),cost:cost));
            Assert.That(h.Abilities.TryBeginActivation(a,h.Context),Is.True);
            Assert.That(h.Abilities.IsActive(a),Is.False); Assert.That(h.Tags.HasTag(Identity),Is.False);
            Assert.That(h.Abilities.GetStatus(a).CurrentCharges,Is.EqualTo(3)); Assert.That(h.Resource,Is.EqualTo(10));
            Assert.That(h.Abilities.End(a),Is.False); h.Abilities.Cancel(a);
            Assert.That(h.Resource,Is.EqualTo(10));
            var context=h.Context; h.Abilities.TryBeginActivation(a,context);
            Assert.That(h.Abilities.TryCommitActivation(a),Is.True); Assert.That(h.Abilities.TryCommitActivation(a),Is.False);
            Assert.That(cost.Commits,Is.EqualTo(1)); Assert.That(h.Resource,Is.EqualTo(8));
            Assert.That(h.Abilities.GetStatus(a).CurrentCharges,Is.EqualTo(2));
            Assert.That(h.Abilities.GetGrantedEntriesSnapshot()[0].ActiveContext,Is.SameAs(context));
        }
        [TestCase("CostGate")][TestCase("BlockTag")][TestCase("Payment")]
        public void CommitRejectionCleansPreparationWithoutPaymentResidue(string failure)
        {
            var h=new Host(); var cost=new Cost(); var a=h.Abilities.Grant(Definition(cd:Effect(),cost:cost));
            h.Abilities.TryBeginActivation(a,h.Context); var child=new Lifetime(); h.Abilities.TryOwnLifetime(a,child);
            if(failure=="CostGate") h.Resource=0; else if(failure=="BlockTag") h.Tags.AddTag(Block,h.OwnerId); else cost.Reject=true;
            int resource=h.Resource;
            Assert.That(h.Abilities.TryCommitActivation(a),Is.False); Assert.That(h.Resource,Is.EqualTo(resource));
            Assert.That(cost.Commits,Is.Zero); Assert.That(h.Effects.GetActiveEntriesSnapshot(),Is.Empty);
            Assert.That(h.Tags.HasTag(Identity),Is.False); Assert.That(child.Calls,Is.EqualTo(1));
            Assert.That(h.Abilities.LastTermination.Reason,Is.EqualTo(GameplayAbilityTerminationReason.CommitRejected));
        }
        [TestCase(GameplayAbilityTerminationReason.Ended)] [TestCase(GameplayAbilityTerminationReason.Cancelled)]
        [TestCase(GameplayAbilityTerminationReason.Removed)] [TestCase(GameplayAbilityTerminationReason.OngoingBlocked)]
        [TestCase(GameplayAbilityTerminationReason.CancelledByAbility)]
        public void TerminalCleanupDetachesAndAttemptsEveryResourceDespiteExceptions(GameplayAbilityTerminationReason reason)
        {
            var h=new Host(); var a=h.Abilities.Grant(Definition()); h.Abilities.TryActivate(a,h.Context);
            var sibling=h.Abilities.Grant(Definition()); h.Abilities.TryBeginActivation(sibling);
            var first=new Lifetime { Cleanup=()=> { Assert.That(h.Abilities.IsEngaged(a),Is.False); throw new InvalidOperationException("cleanup"); } };
            var second=new Lifetime(); var released=new Lifetime();
            h.Abilities.TryOwnLifetime(a,first); Assert.That(h.Abilities.TryOwnLifetime(a,first),Is.True);
            Assert.That(h.Abilities.TryOwnLifetime(sibling,first),Is.False);
            h.Abilities.TryOwnLifetime(a,second); h.Abilities.TryOwnLifetime(a,released); h.Abilities.ReleaseOwnedLifetime(a,released);
            int notices=0; h.Abilities.Terminated += o=> { if(o.Handle==a) { notices++; throw new InvalidOperationException("observer"); } };
            h.Abilities.Terminated += o=> { if(o.Handle==a) notices++; };
            Action terminate=()=> { if(reason==GameplayAbilityTerminationReason.Ended) h.Abilities.End(a);
                else if(reason==GameplayAbilityTerminationReason.Cancelled) h.Abilities.Cancel(a);
                else if(reason==GameplayAbilityTerminationReason.Removed) h.Abilities.Remove(a);
                else if(reason==GameplayAbilityTerminationReason.OngoingBlocked) { h.Tags.AddTag(Block,h.OwnerId); h.Abilities.Advance(0); }
                else { var canceller=h.Abilities.Grant(new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),cancelAbilitiesWithTags:new[]{Identity})); h.Abilities.TryActivate(canceller); } };
            Assert.Throws<AggregateException>(()=>terminate()); Assert.That(first.Calls,Is.EqualTo(1)); Assert.That(second.Calls,Is.EqualTo(1));
            Assert.That(released.Calls,Is.Zero); Assert.That(notices,Is.EqualTo(2)); Assert.That(h.Tags.HasTag(Identity),Is.False);
            Assert.That(h.Abilities.LastTermination.Reason,Is.EqualTo(reason)); Assert.That(h.Abilities.Cancel(a),Is.False);
        }
        [Test]
        public void ScopeOwnsSkillSubscriptionAndSequenceLeaseWithoutPolling()
        {
            var h=new Host(); var a=h.Abilities.Grant(Definition()); h.Abilities.TryActivate(a);
            var events=new GameplayEventDispatcher(); var hit=new GameplayTag("Event.Test.Hit"); var cues=new List<GameplayCueRequest>();
            var skill=new GameplaySkillRuntime(new GameplaySkillDefinition(new GameplaySkillEventTriggerDefinition(hit),Array.Empty<IGameplaySkillCondition>(),
                new GameplaySkillSelfTarget(),new IGameplaySkillAction[]{new GameplaySkillApplyEffectAction(new GameplayEffectDefinition(GameplayEffectDurationPolicy.Infinite,0,Array.Empty<GameplayEffectAttributeModifierDefinition>()))},false),h,events);
            var sequences=new GameplaySequenceContainer(); var definition=new GameplaySequenceDefinition(3,new[]{new GameplaySequenceCueMarkerDefinition(2,hit)});
            var sequence=sequences.Start(definition,h.Context,h); var lease=sequences.CreateCancellationLease(sequence);
            h.Abilities.TryOwnLifetime(a,skill); h.Abilities.TryOwnLifetime(a,lease);
            events.Publish(new GameplayEventData(hit,h.OwnerId,h.OwnerId,0)); Assert.That(skill.ActivationCount,Is.EqualTo(1));
            h.Abilities.Cancel(a); lease.Dispose(); events.Publish(new GameplayEventData(hit,h.OwnerId,h.OwnerId,0));
            sequences.Advance(3); sequences.DrainCues(cues);
            Assert.That(skill.IsDisposed,Is.True); Assert.That(skill.ActivationCount,Is.EqualTo(1)); Assert.That(cues,Is.Empty);
            var completed=sequences.Start(definition,h.Context,h); sequences.Advance(3); sequences.CreateCancellationLease(completed).Dispose();
            Assert.That(sequences.ActiveCount,Is.Zero);
        }
        [Test]
        public void MergedLayersHaveExclusiveGrantOwnershipAndCleanupPreservesOtherContribution()
        {
            var h=new Host(); var a=h.Abilities.Grant(Definition()); var b=h.Abilities.Grant(Definition()); h.Abilities.TryActivate(a); h.Abilities.TryActivate(b);
            var stack=new GameplayEffectStackingDefinition(new GameplayTag("Stack.Test.Owned"),GameplayEffectStackAggregation.AggregateByTarget,3,
                GameplayEffectStackDurationRefreshPolicy.NeverRefresh,GameplayEffectStackPeriodResetPolicy.NeverReset,GameplayEffectStackOverflowPolicy.Reject);
            var definition=Effect(20,stack); var first=h.Apply(definition); var second=h.Apply(definition);
            Assert.That(h.Abilities.TryOwnEffectLayer(a,first),Is.True); Assert.That(h.Abilities.TryOwnEffectLayer(a,first),Is.True);
            Assert.That(h.Abilities.TryOwnEffectLayer(b,first),Is.False); Assert.That(h.Abilities.TryOwnEffect(b,first),Is.False);
            Assert.That(h.Abilities.TryOwnEffectLayer(b,second),Is.True);
            h.Abilities.Cancel(a); h.Effects.TryGetStackCount(second.ActiveHandle,out int count); Assert.That(count,Is.EqualTo(1));
            h.Abilities.Remove(b); Assert.That(h.Effects.IsActive(second.ActiveHandle),Is.False);
            Assert.That(h.Effects.LastRemoval.Cause,Is.EqualTo(GameplayEffectRemovalCause.OwnerCleanup));
            h.Abilities.TryActivate(a); var whole=h.Apply(definition); Assert.That(h.Abilities.TryOwnEffect(a,whole),Is.True);
            var c=h.Abilities.Grant(Definition()); h.Abilities.TryActivate(c); var merged=h.Apply(definition);
            Assert.That(h.Abilities.TryOwnEffectLayer(c,merged),Is.False); h.Abilities.Cancel(a);
        }
    }
}
