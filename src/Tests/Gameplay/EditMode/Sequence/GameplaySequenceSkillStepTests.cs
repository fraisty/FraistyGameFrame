using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Sequence
{
    public sealed class GameplaySequenceSkillStepTests
    {
        private static readonly GameplayTag Signal = new GameplayTag("Event.Test.Cast");
        private static readonly GameplayTag Key = new GameplayTag("Value.Test.Power");
        private static readonly GameplayAttributeId Attack = new GameplayAttributeId("Test.Attack");

        [Test]
        public void MarkerReadsCurrentWorldAndPreservesActivationIdentityValuesAndRealEvent()
        {
            var host = new Host();
            var oldTarget = new GameplayTargetData(host.OwnerId);
            var source = host.Ids.Allocate();
            var values = new GameplayContextValues(new[] { new KeyValuePair<GameplayTag, float>(Key, 42f) });
            var activation = new GameplayContext(host.OwnerId, oldTarget, source, values);
            var trigger = new GameplayEventData(Signal, source, host.OwnerId, 9f);
            GameplayContext executed = null;
            GameplayEventData observed = default;
            var target = new Target((h, e) => { observed = e; return new GameplayTargetData(((Host)h).WorldTarget); });
            var action = new ActionNode((c, e, h) => { executed = c; observed = e; return GameplaySkillActionResult.Succeeded; });
            var definition = Schedule(.5f, target, action);
            var play = new GameplaySkillPlaySequenceAction(definition);
            play.Execute(activation, trigger, host);
            host.WorldTarget = host.Ids.Allocate(); // World/facing resolver inputs change after Start.
            host.Sequences.Advance(.49f);
            Assert.That(target.Calls, Is.Zero);
            host.Sequences.Advance(.01f);
            Assert.That(executed.TargetData.PrimaryTargetId, Is.EqualTo(host.WorldTarget));
            Assert.That(executed, Is.Not.SameAs(activation));
            Assert.That(executed.InstigatorId, Is.EqualTo(activation.InstigatorId));
            Assert.That(executed.SourceId, Is.EqualTo(source));
            Assert.That(executed.EffectiveSourceId, Is.EqualTo(source));
            Assert.That(executed.Values, Is.SameAs(values));
            Assert.That(activation.TargetData, Is.SameAs(oldTarget));
            Assert.That(observed.EventTag, Is.EqualTo(Signal));
            Assert.That(observed.InstigatorId, Is.EqualTo(trigger.InstigatorId));
            Assert.That(observed.TargetId, Is.EqualTo(trigger.TargetId));
            Assert.That(observed.Magnitude, Is.EqualTo(9f));
            Assert.That(host.Sequences.ActiveCount, Is.Zero);
        }

        [Test]
        public void ConditionsEvaluateAtMarkerAndNullTargetSkipsOnlyItsStep()
        {
            var host = new Host();
            bool allowed = false;
            int actions = 0;
            var target = new Target((h, e) => null);
            var action = new ActionNode((c, e, h) => { actions++; return GameplaySkillActionResult.Succeeded; });
            var condition = new Condition((h, e) => allowed && e.EventTag == Signal);
            var definition = new GameplaySequenceDefinition(1f, new GameplaySequenceMarkerDefinition[] {
                new GameplaySequenceSkillStepMarkerDefinition(.5f, new[] { condition }, target, new[] { action }),
                new GameplaySequenceCueMarkerDefinition(1f, new GameplayTag("Cue.Test.End")) });
            host.Sequences.StartSkill(definition, host.Context(), host.Event(), host);
            allowed = true;
            host.Sequences.Advance(.5f);
            Assert.That(condition.Calls, Is.EqualTo(1));
            Assert.That(target.Calls, Is.EqualTo(1));
            Assert.That(actions, Is.Zero);
            Assert.That(host.Sequences.ActiveCount, Is.EqualTo(1));
            host.Sequences.Advance(.5f);
            var cues = new List<GameplayCueRequest>(); host.Sequences.DrainCues(cues);
            Assert.That(cues.Count, Is.EqualTo(1));

            host.Sequences.StartSkill(definition, host.Context(), host.Event(), host);
            allowed = false;
            host.Sequences.Advance(.5f);
            Assert.That(condition.Calls, Is.EqualTo(2));
            Assert.That(target.Calls, Is.EqualTo(1));
        }

        [Test]
        public void CancelAndClearBeforeMarkerPreventTargetAndActionWithoutRemovingAppliedEffects()
        {
            var host = new Host();
            var target = new Target((h, e) => new GameplayTargetData(h.OwnerId));
            var effect = Boost();
            var definition = new GameplaySequenceDefinition(1f, new GameplaySequenceMarkerDefinition[] {
                new GameplaySequenceSkillStepMarkerDefinition(0f, Array.Empty<IGameplaySkillCondition>(),
                    new GameplaySkillSelfTarget(), new[] { new GameplaySkillApplyEffectAction(effect) }),
                new GameplaySequenceSkillStepMarkerDefinition(1f, Array.Empty<IGameplaySkillCondition>(), target,
                    new[] { new ActionNode((c, e, h) => throw new AssertionException("Cancelled Action ran")) }) });
            var handle = host.Sequences.StartSkill(definition, host.Context(), host.Event(), host);
            Assert.That(host.Attributes.GetCurrentValue(Attack), Is.EqualTo(13f));
            Assert.That(host.Sequences.Cancel(handle), Is.True);
            host.Sequences.StartSkill(Schedule(1f, target,
                new ActionNode((c, e, h) => throw new AssertionException("Cleared Action ran"))), host.Context(), host.Event(), host);
            host.Sequences.Clear(); host.Sequences.Advance(2f);
            Assert.That(target.Calls, Is.Zero);
            Assert.That(host.Attributes.GetCurrentValue(Attack), Is.EqualTo(13f));
            host.Effects.Advance(5f);
            Assert.That(host.Attributes.GetCurrentValue(Attack), Is.EqualTo(10f));
        }

        [Test]
        public void RejectionStopsOrderedActionsButNotLaterMarkersOrAlreadyAppliedEffects()
        {
            var host = new Host();
            var order = new List<int>();
            var definition = new GameplaySequenceDefinition(1f, new GameplaySequenceMarkerDefinition[] {
                new GameplaySequenceSkillStepMarkerDefinition(.5f, Array.Empty<IGameplaySkillCondition>(),
                    new GameplaySkillSelfTarget(), new IGameplaySkillAction[] {
                        new ActionNode((c,e,h) => { order.Add(1); return GameplaySkillActionResult.Succeeded; }),
                        new GameplaySkillApplyEffectAction(Boost()),
                        new ActionNode((c,e,h) => { order.Add(2); return GameplaySkillActionResult.Rejected; }),
                        new ActionNode((c,e,h) => { order.Add(3); return GameplaySkillActionResult.Succeeded; }) }),
                new GameplaySequenceSkillStepMarkerDefinition(1f, Array.Empty<IGameplaySkillCondition>(),
                    new GameplaySkillSelfTarget(), new[] {
                        new ActionNode((c,e,h) => { order.Add(4); return GameplaySkillActionResult.Succeeded; }) }) });
            host.Sequences.StartSkill(definition, host.Context(), host.Event(), host);
            host.Sequences.Advance(1f);
            Assert.That(order, Is.EqualTo(new[] { 1, 2, 4 }));
            Assert.That(host.Attributes.GetCurrentValue(Attack), Is.EqualTo(13f));
        }

        [Test]
        public void EqualTimeMixedMarkersKeepAuthoredOrderAndIndependentTargetSnapshots()
        {
            var host = new Host();
            var cue = new GameplayTag("Cue.Test.Before");
            var definition = new GameplaySequenceDefinition(.5f, new GameplaySequenceMarkerDefinition[] {
                new GameplaySequenceCueMarkerDefinition(.5f, cue),
                new GameplaySequenceSkillStepMarkerDefinition(.5f, Array.Empty<IGameplaySkillCondition>(),
                    new GameplaySkillSelfTarget(), new[] { new ActionNode((c,e,h) => {
                        var cues = new List<GameplayCueRequest>(); host.Sequences.DrainCues(cues);
                        Assert.That(cues[0].CueTag, Is.EqualTo(cue));
                        Assert.That(host.Attributes.GetCurrentValue(Attack), Is.EqualTo(10f));
                        return GameplaySkillActionResult.Succeeded; }) }),
                new GameplaySequenceApplyEffectMarkerDefinition(.5f, Boost()),
                new GameplaySequenceSkillStepMarkerDefinition(.5f, Array.Empty<IGameplaySkillCondition>(),
                    new GameplaySkillSelfTarget(), new[] { new ActionNode((c,e,h) => {
                        Assert.That(host.Attributes.GetCurrentValue(Attack), Is.EqualTo(13f));
                        return GameplaySkillActionResult.Succeeded; }) }) });
            host.Sequences.StartSkill(definition, host.Context(), host.Event(), host);
            host.Sequences.Advance(.5f);
        }

        [Test]
        public void SkillStepWithoutRealEventFailsBeforeAddingEntryOrExecutingZeroMarkers()
        {
            var host = new Host();
            var target = new Target((h,e) => new GameplayTargetData(h.OwnerId));
            var definition = Schedule(0f, target, new ActionNode((c,e,h) => GameplaySkillActionResult.Succeeded));
            Assert.Throws<InvalidOperationException>(() => host.Sequences.Start(definition, host.Context(), host));
            Assert.Throws<ArgumentException>(() => host.Sequences.StartSkill(definition, host.Context(), default, host));
            Assert.That(host.Sequences.ActiveCount, Is.Zero);
            Assert.That(target.Calls, Is.Zero);
            host.Sequences.StartSkill(definition, host.Context(), host.Event(), host);
            Assert.That(target.Calls, Is.EqualTo(1));
        }

        [Test]
        public void ActionsCanCancelClearAndStartWithoutExecutingCancelledWorkOrConsumingPastDelta()
        {
            var host = new Host();
            var log = new List<int>();
            var child = Schedule(.1f, new GameplaySkillSelfTarget(), new ActionNode((c,e,h) => {
                log.Add(2); return GameplaySkillActionResult.Succeeded; }));
            var parent = new GameplaySequenceDefinition(.5f, new GameplaySequenceMarkerDefinition[] {
                new GameplaySequenceSkillStepMarkerDefinition(.5f, Array.Empty<IGameplaySkillCondition>(),
                    new GameplaySkillSelfTarget(), new[] { new ActionNode((c,e,h) => {
                        log.Add(1); host.Sequences.Clear(); host.Sequences.StartSkill(child, c, e, h);
                        return GameplaySkillActionResult.Succeeded; }) }),
                new GameplaySequenceCueMarkerDefinition(.5f, new GameplayTag("Cue.Test.Cancelled")) });
            host.Sequences.StartSkill(parent, host.Context(), host.Event(), host);
            host.Sequences.StartSkill(child, host.Context(), host.Event(), host);
            host.Sequences.Advance(.5f);
            Assert.That(log, Is.EqualTo(new[] { 1 }));
            var cues = new List<GameplayCueRequest>(); host.Sequences.DrainCues(cues);
            Assert.That(cues, Is.Empty);
            host.Sequences.Advance(.1f);
            Assert.That(log, Is.EqualTo(new[] { 1, 2 }));
        }

        private static GameplaySequenceDefinition Schedule(float time, IGameplaySkillTarget target, IGameplaySkillAction action) =>
            new GameplaySequenceDefinition(time, new[] { new GameplaySequenceSkillStepMarkerDefinition(time,
                Array.Empty<IGameplaySkillCondition>(), target, new[] { action }) });
        private static GameplayEffectDefinition Boost() => new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 5f,
            new[] { new GameplayEffectAttributeModifierDefinition(Attack, GameplayModifierOperation.Add, 3f) });

        private sealed class Host : IGameplaySkillHost, IGameplaySequenceHost
        {
            public GameplaySourceIdAllocator Ids { get; } = new GameplaySourceIdAllocator();
            public GameplaySourceId OwnerId { get; }
            public GameplaySourceId WorldTarget;
            public GameplaySourceId TargetId => OwnerId;
            public AttributeSet Attributes { get; } = new AttributeSet();
            public GameplayTagContainer Tags { get; } = new GameplayTagContainer();
            public GameplayEffectContainer Effects { get; }
            public GameplaySequenceContainer Sequences { get; } = new GameplaySequenceContainer();
            public Host() { OwnerId = Ids.Allocate(); WorldTarget = OwnerId; Attributes.Register(Attack, 10f);
                Effects = new GameplayEffectContainer(Attributes, Ids, Tags); }
            public GameplayContext Context() => new GameplayContext(OwnerId, new GameplayTargetData(OwnerId));
            public GameplayEventData Event() => new GameplayEventData(Signal, OwnerId, OwnerId, 3f);
        }
        private sealed class Target : IGameplaySkillTarget
        {
            private readonly Func<IGameplaySkillHost, GameplayEventData, GameplayTargetData> _resolve;
            public int Calls;
            public Target(Func<IGameplaySkillHost, GameplayEventData, GameplayTargetData> resolve) { _resolve = resolve; }
            public GameplayTargetData Resolve(IGameplaySkillHost host, GameplayEventData e) { Calls++; return _resolve(host, e); }
        }
        private sealed class Condition : IGameplaySkillCondition
        {
            private readonly Func<IGameplaySkillHost, GameplayEventData, bool> _evaluate;
            public int Calls;
            public Condition(Func<IGameplaySkillHost, GameplayEventData, bool> evaluate) { _evaluate = evaluate; }
            public bool Evaluate(IGameplaySkillHost host, GameplayEventData e) { Calls++; return _evaluate(host, e); }
        }
        private sealed class ActionNode : IGameplaySkillAction
        {
            private readonly Func<GameplayContext, GameplayEventData, IGameplaySkillHost, GameplaySkillActionResult> _execute;
            public ActionNode(Func<GameplayContext, GameplayEventData, IGameplaySkillHost, GameplaySkillActionResult> execute) { _execute = execute; }
            public GameplaySkillActionResult Execute(GameplayContext c, GameplayEventData e, IGameplaySkillHost h) => _execute(c,e,h);
        }
    }
}
