using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Effect
{
    /// <summary>Guards exact contribution ownership, boundary ordering and callback snapshot semantics, which visual QA cannot reliably prove.</summary>
    public sealed class GameplayEffectCapabilityTests
    {
        private static readonly GameplayTag DurationKey = new GameplayTag("Data.Test.Duration"), PeriodKey = new GameplayTag("Data.Test.Period");
        private static readonly GameplayAttributeId Stat = new GameplayAttributeId("Test.Stat");
        private sealed class Target : IGameplayEffectExecutionTarget
        {
            public GameplaySourceId TargetId { get; }
            public AttributeSet Attributes { get; } = new AttributeSet();
            public GameplayTagContainer Tags { get; } = new GameplayTagContainer();
            public Target(GameplaySourceId id) { TargetId = id; Attributes.Register(Stat, 100); }
        }
        private sealed class Capture : IGameplayEffectExecution
        { public readonly List<GameplayEffectApplicationContext> Calls = new List<GameplayEffectApplicationContext>(); public void Execute(GameplayEffectApplicationContext app) => Calls.Add(app); }
        private sealed class Overflow : IGameplayEffectOverflowExecution
        { public Action<GameplayEffectOverflowContext> Action; public void Execute(GameplayEffectOverflowContext context) => Action(context); }
        private sealed class Completion : IGameplayEffectCompletionExecution
        { public int Count; public void Execute(GameplayEffectCompletionContext context) => Count++; }
        private static GameplayEffectApplicationContext App(Target target, GameplaySourceId source, float duration = 3, float period = 1) =>
            new GameplayEffectApplicationContext(new GameplayContext(source, new GameplayTargetData(target.TargetId)), target,
                new GameplaySetByCallerValues(new[] { new KeyValuePair<GameplayTag,float>(DurationKey,duration), new KeyValuePair<GameplayTag,float>(PeriodKey,period) }));
        private static GameplayEffectDefinition Definition(Capture capture = null, GameplayEffectStackingDefinition stacking = null, Completion completion = null,
            GameplayEffectPeriodicStartPolicy start = GameplayEffectPeriodicStartPolicy.AfterFirstPeriod, bool dynamic = true) =>
            new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 3, capture == null ? 0 : 1,
                new[] { new GameplayEffectAttributeModifierDefinition(Stat, GameplayModifierOperation.Add, 10) }, new[] { new GameplayTag("State.Test.Active") },
                capture == null ? Array.Empty<IGameplayEffectExecution>() : new IGameplayEffectExecution[] { capture }, stacking,
                completionExecutions: completion == null ? null : new IGameplayEffectCompletionExecution[] { completion }, effectTags: new[] { new GameplayTag("Effect.Debuff.Poison") },
                periodicStart: start, durationValue: dynamic ? new GameplaySetByCallerFloatValue(DurationKey) : null,
                periodValue: dynamic && capture != null ? new GameplaySetByCallerFloatValue(PeriodKey) : null);
        private static GameplayEffectStackingDefinition Stack(GameplayEffectStackLifetimePolicy lifetime = GameplayEffectStackLifetimePolicy.IndependentLayerLifetime,
            GameplayEffectStackOverflowPolicy overflow = GameplayEffectStackOverflowPolicy.Reject, IGameplayEffectOverflowExecution rule = null,
            GameplayEffectStackPeriodResetPolicy reset = GameplayEffectStackPeriodResetPolicy.NeverReset) =>
            new GameplayEffectStackingDefinition(new GameplayTag("Stack.Test"), GameplayEffectStackAggregation.AggregateByTarget, 3,
                GameplayEffectStackDurationRefreshPolicy.NeverRefresh, reset, overflow, lifetime, rule == null ? null : new[] { rule });

        [Test]
        public void Cleanse_SnapshotsClassificationAndObservesDispelWithoutForgingCompletion()
        {
            var ids = new GameplaySourceIdAllocator(); var target = new Target(ids.Allocate()); var c = new GameplayEffectContainer(target.Attributes, ids, target.Tags);
            var completion = new Completion(); var d = Definition(completion: completion);
            var a = c.Apply(d, App(target, ids.Allocate())); var b = c.Apply(d, App(target, ids.Allocate()));
            Assert.That(target.Tags.HasTag(new GameplayTag("Effect.Debuff")), Is.False);
            var order = new List<GameplayEffectHandle>(); GameplayEffectHandle created = default;
            c.Removed += o => { order.Add(o.Handle); Assert.That(o.Cause, Is.EqualTo(GameplayEffectRemovalCause.Dispel)); if (o.Handle == a.ActiveHandle) created = c.Apply(d, App(target, ids.Allocate())).ActiveHandle; };
            var query = new GameplayEffectQuery(new GameplayTagQuery(new[] { new GameplayTag("Effect.Debuff") }, Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>()));
            Assert.That(c.RemoveMatching(query), Is.EqualTo(2)); CollectionAssert.AreEqual(new[] { a.ActiveHandle,b.ActiveHandle },order);
            Assert.That(c.IsActive(created), Is.True); Assert.That(completion.Count, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => c.Remove(created, GameplayEffectRemovalCause.NaturalExpiry));
        }
        [Test]
        public void TimingSnapshot_RefreshResetUsesIncomingAndNeverPoliciesPreserveSchedule()
        {
            var ids = new GameplaySourceIdAllocator(); var target = new Target(ids.Allocate()); var c = new GameplayEffectContainer(target.Attributes,ids,target.Tags);
            var capture = new Capture(); var d = Definition(capture);
            Assert.Throws<InvalidOperationException>(() => c.Apply(d));
            var a = c.Apply(d,App(target,ids.Allocate(),3,1)); var b = c.Apply(d,App(target,ids.Allocate(),6,2));
            var snapshots = c.GetActiveEntriesSnapshot(); Assert.That(snapshots[0].AppliedDuration, Is.EqualTo(3)); Assert.That(snapshots[1].AppliedPeriod, Is.EqualTo(2));
            c.Advance(1); Assert.That(capture.Calls.Count, Is.EqualTo(1));
            c.Remove(a.ActiveHandle); c.Remove(b.ActiveHandle);
            var refresh = new GameplayEffectStackingDefinition(new GameplayTag("Stack.Refresh"),GameplayEffectStackAggregation.AggregateByTarget,3,
                GameplayEffectStackDurationRefreshPolicy.RefreshOnSuccessfulApplication,GameplayEffectStackPeriodResetPolicy.ResetOnSuccessfulApplication,GameplayEffectStackOverflowPolicy.RefreshExistingAtLimit);
            d = Definition(capture, refresh); a = c.Apply(d,App(target,ids.Allocate(),3,1)); c.Advance(.5f); c.Apply(d,App(target,ids.Allocate(),6,2));
            snapshots = c.GetActiveEntriesSnapshot(); Assert.That(snapshots[0].Remaining, Is.EqualTo(6)); Assert.That(snapshots[0].PeriodRemaining, Is.EqualTo(2));
            c.Remove(a.ActiveHandle); d = Definition(capture,Stack(GameplayEffectStackLifetimePolicy.SharedEntryLifetime));
            a = c.Apply(d,App(target,ids.Allocate(),3,1)); c.Advance(.5f); c.Apply(d,App(target,ids.Allocate(),6,2));
            snapshots = c.GetActiveEntriesSnapshot(); Assert.That(snapshots[0].AppliedDuration,Is.EqualTo(3)); Assert.That(snapshots[0].AppliedPeriod,Is.EqualTo(1));
            var legacy = Definition(dynamic:false); c.Remove(a.ActiveHandle); var h = c.Apply(legacy); c.Advance(3); Assert.That(c.IsActive(h),Is.False);
        }
        [Test]
        public void ImmediateAndIndependentExpiry_PreserveFinalPeriodAndSurvivingContext()
        {
            var ids = new GameplaySourceIdAllocator(); var target = new Target(ids.Allocate()); var c = new GameplayEffectContainer(target.Attributes,ids,target.Tags);
            var capture = new Capture(); var completion = new Completion(); var d = Definition(capture,Stack(),completion,GameplayEffectPeriodicStartPolicy.ExecuteOnApplication);
            var a = c.Apply(d,App(target,ids.Allocate(),3)); c.Advance(1);
            var source = ids.Allocate(); var b = c.Apply(d,App(target,source,1));
            Assert.That(capture.Calls.Count,Is.EqualTo(3)); Assert.That(capture.Calls[2].StackCount,Is.EqualTo(2));
            c.Advance(1); Assert.That(capture.Calls[3].StackCount,Is.EqualTo(2));
            Assert.That(target.Attributes.GetCurrentValue(Stat),Is.EqualTo(110)); Assert.That(c.RemoveLayer(b.LayerHandle),Is.False);
            c.Advance(1); Assert.That(capture.Calls[4].StackCount,Is.EqualTo(1)); Assert.That(capture.Calls[4].Context.EffectiveSourceId,Is.EqualTo(capture.Calls[0].Context.EffectiveSourceId));
            Assert.That(c.IsActive(a.ActiveHandle),Is.False); Assert.That(completion.Count,Is.EqualTo(1)); Assert.That(c.LastRemoval.Cause,Is.EqualTo(GameplayEffectRemovalCause.NaturalExpiry));
        }
        [Test]
        public void LayerRemoval_IsContainerScopedAndRetractsOnlyItsExactModifiers()
        {
            var ids = new GameplaySourceIdAllocator(); var target = new Target(ids.Allocate()); var c = new GameplayEffectContainer(target.Attributes,ids,target.Tags);
            var otherTarget = new Target(ids.Allocate()); var other = new GameplayEffectContainer(otherTarget.Attributes,ids,otherTarget.Tags);
            var d = Definition(stacking:Stack()); var a = c.Apply(d,App(target,ids.Allocate())); var b = c.Apply(d,App(target,ids.Allocate(),8));
            Assert.That(a.LayerHandle,Is.Not.EqualTo(b.LayerHandle)); Assert.That(other.RemoveLayer(a.LayerHandle),Is.False);
            Assert.That(c.RemoveLayer(a.LayerHandle),Is.True); Assert.That(c.RemoveLayer(a.LayerHandle),Is.False);
            Assert.That(target.Attributes.GetCurrentValue(Stat),Is.EqualTo(110)); Assert.That(target.Tags.HasTag(new GameplayTag("State.Test.Active")),Is.True);
            c.RemoveLayer(b.LayerHandle); Assert.That(target.Attributes.GetCurrentValue(Stat),Is.EqualTo(100)); Assert.That(target.Tags.HasTag(new GameplayTag("State.Test.Active")),Is.False);
        }
        [Test]
        public void Overflow_CommitsAuthoredOrderAndCreatesNoPhantomLayer()
        {
            var ids = new GameplaySourceIdAllocator(); var target = new Target(ids.Allocate()); var c = new GameplayEffectContainer(target.Attributes,ids,target.Tags);
            var next = Definition(dynamic:false); var rule = new Overflow { Action = ctx => { Assert.That(ctx.StackCount,Is.EqualTo(3)); ctx.Apply(next); ctx.RemoveCurrent(); } };
            var d = Definition(stacking:Stack(overflow:GameplayEffectStackOverflowPolicy.ExecuteOverflow,rule:rule));
            for (int i=0;i<3;i++) c.Apply(d,App(target,ids.Allocate()));
            var result = c.Apply(d,App(target,ids.Allocate())); Assert.That(result.Outcome,Is.EqualTo(GameplayEffectApplicationOutcome.OverflowExecuted));
            Assert.That(result.Succeeded,Is.True); Assert.That(result.LayerHandle.IsValid,Is.False); Assert.That(result.HasActiveHandle,Is.False);
            Assert.That(c.LastRemoval.Cause,Is.EqualTo(GameplayEffectRemovalCause.Overflow)); Assert.That(c.GetActiveEntriesSnapshot().Count,Is.EqualTo(1));
            Assert.That(target.Attributes.GetCurrentValue(Stat),Is.EqualTo(110));
        }
    }
}
