using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Effect
{
    public sealed class GameplayEffectStackingTests
    {
        private static readonly GameplayAttributeId Attack = new GameplayAttributeId("Test.Stack.Attack");
        private static readonly GameplayAttributeId Defense = new GameplayAttributeId("Test.Stack.Defense");
        private static readonly GameplayTag Key = new GameplayTag("Effect.Test.Stack");
        private static readonly GameplayTag Granted = new GameplayTag("State.Test.Stack");
        private static readonly GameplayTag ValueKey = new GameplayTag("Data.Test.Stack.Value");

        [Test]
        public void StackingDefinition_ValidatesAndLegacyApplyFailsBeforeAllocation()
        {
            Assert.Throws<ArgumentException>(() => Stack(default, GameplayEffectStackAggregation.AggregateByTarget));
            Assert.Throws<ArgumentOutOfRangeException>(() => Stack(Key, (GameplayEffectStackAggregation)99));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameplayEffectStackingDefinition(Key,
                GameplayEffectStackAggregation.AggregateByTarget, 0,
                GameplayEffectStackDurationRefreshPolicy.NeverRefresh,
                GameplayEffectStackPeriodResetPolicy.NeverReset, GameplayEffectStackOverflowPolicy.Reject));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameplayEffectStackingDefinition(Key,
                GameplayEffectStackAggregation.AggregateByTarget, 1,
                (GameplayEffectStackDurationRefreshPolicy)99,
                GameplayEffectStackPeriodResetPolicy.NeverReset, GameplayEffectStackOverflowPolicy.Reject));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameplayEffectStackingDefinition(Key,
                GameplayEffectStackAggregation.AggregateByTarget, 1,
                GameplayEffectStackDurationRefreshPolicy.NeverRefresh,
                (GameplayEffectStackPeriodResetPolicy)99, GameplayEffectStackOverflowPolicy.Reject));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameplayEffectStackingDefinition(Key,
                GameplayEffectStackAggregation.AggregateByTarget, 1,
                GameplayEffectStackDurationRefreshPolicy.NeverRefresh,
                GameplayEffectStackPeriodResetPolicy.NeverReset, (GameplayEffectStackOverflowPolicy)99));
            var stacking = Stack(Key, GameplayEffectStackAggregation.AggregateByTarget);
            Assert.Throws<ArgumentException>(() => new GameplayEffectDefinition(
                GameplayEffectDurationPolicy.Instant, 0f, 0f, Array.Empty<GameplayEffectAttributeModifierDefinition>(),
                Array.Empty<GameplayTag>(), new IGameplayEffectExecution[] { new Capture() }, stacking));

            var ids = new GameplaySourceIdAllocator();
            var target = new Target(ids.Allocate());
            var container = new GameplayEffectContainer(target.Attributes, ids, target.Tags);
            var definition = Definition(stacking);
            Assert.Throws<InvalidOperationException>(() => container.Apply(definition));
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(14f));
            Assert.That(target.Tags.HasTagExact(Granted), Is.False);
            Assert.That(ids.Allocate().Value, Is.EqualTo(2));
            var ordinary = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 1f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>());
            var normal = container.Apply(ordinary, Application(ids.Allocate(), target, 0f));
            Assert.That(normal.Outcome, Is.EqualTo(GameplayEffectApplicationOutcome.NewActiveEffect));
            Assert.That(normal.StackCount, Is.EqualTo(1));
            Assert.That(container.TryGetStackCount(normal.ActiveHandle, out int count), Is.True);
            Assert.That(count, Is.EqualTo(1));
            container.Remove(normal.ActiveHandle);
            Assert.That(container.TryGetStackCount(normal.ActiveHandle, out count), Is.False);
            Assert.That(count, Is.Zero);
        }

        [Test]
        public void AggregateBySource_OneHandleOneSourceAndRefreshAtLimit()
        {
            var ids = new GameplaySourceIdAllocator();
            var target = new Target(ids.Allocate());
            var sourceA = ids.Allocate();
            var sourceB = ids.Allocate();
            var container = new GameplayEffectContainer(target.Attributes, ids, target.Tags);
            var definition = Definition(Stack(Key, GameplayEffectStackAggregation.AggregateBySource));
            var a1 = container.Apply(definition, Application(sourceA, target, 1f));
            Assert.That(a1.Outcome, Is.EqualTo(GameplayEffectApplicationOutcome.NewActiveEffect));
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(16f));
            container.Advance(5f);
            Assert.That(container.TryGetRemainingDuration(a1.ActiveHandle, out float remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(2f));
            var a2 = container.Apply(definition, Application(sourceA, target, 2f));
            Assert.That(a2.Outcome, Is.EqualTo(GameplayEffectApplicationOutcome.StackIncreased));
            Assert.That(a2.ActiveHandle, Is.EqualTo(a1.ActiveHandle));
            Assert.That(a2.StackCount, Is.EqualTo(2));
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(18f));
            Assert.That(container.TryGetRemainingDuration(a1.ActiveHandle, out remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(7f));
            var b1 = container.Apply(definition, Application(sourceB, target, 3f));
            Assert.That(b1.ActiveHandle, Is.Not.EqualTo(a1.ActiveHandle));
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(20f));
            var a3 = container.Apply(definition, Application(sourceA, target, 4f));
            Assert.That(a3.StackCount, Is.EqualTo(3));
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(22f));
            container.Advance(3f);
            var cap = container.Apply(definition, Application(sourceA, target, 5f));
            Assert.That(cap.Outcome, Is.EqualTo(GameplayEffectApplicationOutcome.RefreshedAtLimit));
            Assert.That(cap.Succeeded, Is.True);
            Assert.That(cap.ActiveHandle, Is.EqualTo(a1.ActiveHandle));
            Assert.That(cap.StackCount, Is.EqualTo(3));
            Assert.That(container.TryGetRemainingDuration(a1.ActiveHandle, out remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(7f));
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(22f));
            Assert.That(target.Tags.HasTagExact(Granted), Is.True);
            Assert.That(ids.Allocate().Value, Is.EqualTo(6), "Only two aggregate entries allocate Effect sources.");
            Assert.That(container.Remove(a1.ActiveHandle), Is.True);
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(16f));
            Assert.That(target.Tags.HasTagExact(Granted), Is.True);
            container.Advance(4f);
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(14f));
            Assert.That(target.Tags.HasTagExact(Granted), Is.False);
        }

        [Test]
        public void AggregateByTarget_CollisionAndRejectPreserveExistingState()
        {
            var ids = new GameplaySourceIdAllocator();
            var target = new Target(ids.Allocate());
            var sourceA = ids.Allocate();
            var sourceB = ids.Allocate();
            var container = new GameplayEffectContainer(target.Attributes, ids, target.Tags);
            var capture = new Capture();
            var stacking = Stack(Key, GameplayEffectStackAggregation.AggregateByTarget,
                max: 2, duration: GameplayEffectStackDurationRefreshPolicy.NeverRefresh,
                period: GameplayEffectStackPeriodResetPolicy.NeverReset,
                overflow: GameplayEffectStackOverflowPolicy.Reject);
            var definition = Definition(stacking, 2f, capture);
            var firstApplication = Application(sourceA, target, 1f);
            var secondApplication = Application(sourceB, target, 2f);
            var rejectedApplication = Application(sourceB, target, 3f);
            var first = container.Apply(definition, firstApplication);
            container.Advance(.5f);
            Assert.Throws<InvalidOperationException>(() => container.Apply(
                Definition(stacking, 2f, capture), secondApplication));
            var second = container.Apply(definition, secondApplication);
            Assert.That(second.ActiveHandle, Is.EqualTo(first.ActiveHandle));
            Assert.That(second.StackCount, Is.EqualTo(2));
            Assert.That(container.TryGetRemainingDuration(first.ActiveHandle, out float duration), Is.True);
            Assert.That(duration, Is.EqualTo(6.5f));
            Assert.That(container.TryGetPeriodRemaining(first.ActiveHandle, out float period), Is.True);
            Assert.That(period, Is.EqualTo(1.5f));
            var rejected = container.Apply(definition, rejectedApplication);
            Assert.That(rejected.Outcome, Is.EqualTo(GameplayEffectApplicationOutcome.RejectedAtLimit));
            Assert.That(rejected.Succeeded, Is.False);
            Assert.That(rejected.ActiveHandle, Is.EqualTo(first.ActiveHandle));
            Assert.That(rejected.StackCount, Is.EqualTo(2));
            Assert.That(container.TryGetRemainingDuration(first.ActiveHandle, out duration), Is.True);
            Assert.That(duration, Is.EqualTo(6.5f));
            container.Advance(1.5f);
            Assert.That(capture.Calls, Is.EqualTo(1), "A stacked periodic Entry executes once per period.");
            Assert.That(capture.Last.Context, Is.SameAs(secondApplication.Context));
            Assert.That(capture.Last.StackCount, Is.EqualTo(2));
            Assert.That(capture.Last.SetByCaller.GetRequiredValue(ValueKey), Is.EqualTo(2f));
            Assert.That(capture.Last.Context.EffectiveSourceId, Is.EqualTo(sourceB));
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(18f));
        }

        [Test]
        public void PeriodResetAndLayerFailure_AreIndependentOfDurationAndOldContributions()
        {
            var ids = new GameplaySourceIdAllocator();
            var target = new Target(ids.Allocate());
            var source = ids.Allocate();
            var container = new GameplayEffectContainer(target.Attributes, ids, target.Tags);
            var capture = new Capture();
            var reset = Definition(Stack(Key, GameplayEffectStackAggregation.AggregateByTarget,
                period: GameplayEffectStackPeriodResetPolicy.ResetOnSuccessfulApplication), 2f, capture);
            var first = container.Apply(reset, Application(source, target, 1f));
            container.Advance(1.5f);
            var next = container.Apply(reset, Application(source, target, 2f));
            Assert.That(next.ActiveHandle, Is.EqualTo(first.ActiveHandle));
            Assert.That(container.TryGetPeriodRemaining(first.ActiveHandle, out float period), Is.True);
            Assert.That(period, Is.EqualTo(2f));
            container.Advance(.5f);
            Assert.That(capture.Calls, Is.Zero);
            container.Remove(first.ActiveHandle);

            target.Attributes.Register(Defense, 1f);
            var failing = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 7f, 2f,
                new[]
                {
                    new GameplayEffectAttributeModifierDefinition(Attack, GameplayModifierOperation.Add, 2f),
                    new GameplayEffectAttributeModifierDefinition(Defense, GameplayModifierOperation.Add,
                        float.MaxValue * .6f)
                }, new[] { Granted }, new IGameplayEffectExecution[] { capture },
                Stack(new GameplayTag("Effect.Test.Rollback"), GameplayEffectStackAggregation.AggregateByTarget,
                    period: GameplayEffectStackPeriodResetPolicy.NeverReset));
            var original = Application(source, target, 4f);
            var active = container.Apply(failing, original);
            container.Advance(1.5f);
            Assert.Throws<OverflowException>(() => container.Apply(failing,
                Application(source, target, 9f)));
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(16f));
            Assert.That(target.Tags.HasTagExact(Granted), Is.True);
            Assert.That(container.TryGetStackCount(active.ActiveHandle, out int count), Is.True);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(container.TryGetRemainingDuration(active.ActiveHandle, out float duration), Is.True);
            Assert.That(duration, Is.EqualTo(5.5f));
            Assert.That(container.TryGetPeriodRemaining(active.ActiveHandle, out period), Is.True);
            Assert.That(period, Is.EqualTo(.5f));
            container.Advance(.5f);
            Assert.That(capture.Last.Context, Is.SameAs(original.Context));
            Assert.That(capture.Last.SetByCaller.GetRequiredValue(ValueKey), Is.EqualTo(4f));
            Assert.That(container.Remove(active.ActiveHandle), Is.True);
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(14f));
            Assert.That(target.Tags.HasTagExact(Granted), Is.False);
        }

        [Test]
        public void RefreshAtLimit_ReplacesPeriodicApplicationWithoutMultiplyingExecution()
        {
            var ids = new GameplaySourceIdAllocator();
            var target = new Target(ids.Allocate());
            var sourceA = ids.Allocate();
            var sourceB = ids.Allocate();
            var capture = new Capture();
            var definition = Definition(Stack(Key, GameplayEffectStackAggregation.AggregateByTarget,
                max: 1, period: GameplayEffectStackPeriodResetPolicy.NeverReset), 2f, capture);
            var container = new GameplayEffectContainer(target.Attributes, ids, target.Tags);
            var first = container.Apply(definition, Application(sourceA, target, 1f));
            container.Advance(1f);
            var latest = Application(sourceB, target, 9f);
            var refreshed = container.Apply(definition, latest);
            Assert.That(refreshed.Outcome, Is.EqualTo(GameplayEffectApplicationOutcome.RefreshedAtLimit));
            Assert.That(refreshed.ActiveHandle, Is.EqualTo(first.ActiveHandle));
            Assert.That(refreshed.StackCount, Is.EqualTo(1));
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(16f));
            Assert.That(container.TryGetPeriodRemaining(first.ActiveHandle, out float period), Is.True);
            Assert.That(period, Is.EqualTo(1f));
            container.Advance(1f);
            Assert.That(capture.Calls, Is.EqualTo(1));
            Assert.That(capture.Last.Context, Is.SameAs(latest.Context));
            Assert.That(capture.Last.Context.EffectiveSourceId, Is.EqualTo(sourceB));
            Assert.That(capture.Last.SetByCaller.GetRequiredValue(ValueKey), Is.EqualTo(9f));
        }

        private static GameplayEffectStackingDefinition Stack(GameplayTag key,
            GameplayEffectStackAggregation aggregation, int max = 3,
            GameplayEffectStackDurationRefreshPolicy duration =
                GameplayEffectStackDurationRefreshPolicy.RefreshOnSuccessfulApplication,
            GameplayEffectStackPeriodResetPolicy period = GameplayEffectStackPeriodResetPolicy.NeverReset,
            GameplayEffectStackOverflowPolicy overflow = GameplayEffectStackOverflowPolicy.RefreshExistingAtLimit) =>
            new GameplayEffectStackingDefinition(key, aggregation, max, duration, period, overflow);

        private static GameplayEffectDefinition Definition(GameplayEffectStackingDefinition stacking,
            float period = 0f, IGameplayEffectExecution execution = null) =>
            new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 7f, period,
                new[] { new GameplayEffectAttributeModifierDefinition(Attack, GameplayModifierOperation.Add, 2f) },
                new[] { Granted }, execution == null ? Array.Empty<IGameplayEffectExecution>() :
                    new[] { execution }, stacking);

        private static GameplayEffectApplicationContext Application(GameplaySourceId source,
            Target target, float value) =>
            new GameplayEffectApplicationContext(
                new GameplayContext(source, new GameplayTargetData(target.TargetId)), target,
                new GameplaySetByCallerValues(new[]
                {
                    new KeyValuePair<GameplayTag, float>(ValueKey, value)
                }));

        private sealed class Target : IGameplayEffectExecutionTarget
        {
            public GameplaySourceId TargetId { get; }
            public AttributeSet Attributes { get; } = new AttributeSet();
            public GameplayTagContainer Tags { get; } = new GameplayTagContainer();

            public Target(GameplaySourceId targetId)
            {
                TargetId = targetId;
                Attributes.Register(Attack, 14f);
            }
        }

        private sealed class Capture : IGameplayEffectExecution
        {
            public int Calls;
            public GameplayEffectApplicationContext Last;
            public void Execute(GameplayEffectApplicationContext application)
            {
                Calls++;
                Last = application;
            }
        }
    }
}
