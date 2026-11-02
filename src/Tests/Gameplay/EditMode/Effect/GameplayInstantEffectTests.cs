using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Effect
{
    public sealed class GameplayInstantEffectTests
    {
        private static readonly GameplayTag Key = new GameplayTag("Data.Effect.Test.Amount");
        private static readonly GameplayAttributeId Attack = new GameplayAttributeId("Test.Attack");

        [Test]
        public void SetByCaller_IsImmutableFiniteAndExactKey()
        {
            var input = new List<KeyValuePair<GameplayTag, float>> { Pair(Key, 4f) };
            var values = new GameplaySetByCallerValues(input);
            input[0] = Pair(Key, 99f);
            Assert.That(values.Count, Is.EqualTo(1));
            Assert.That(values.GetRequiredValue(Key), Is.EqualTo(4f));
            Assert.That(values.TryGetValue(new GameplayTag("Data.Effect.Test"), out _), Is.False);
            Assert.Throws<KeyNotFoundException>(() => values.GetRequiredValue(new GameplayTag("Missing")));
            Assert.Throws<ArgumentException>(() => new GameplaySetByCallerValues(new[] { Pair(default, 1f) }));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameplaySetByCallerValues(new[] { Pair(Key, float.NaN) }));
            Assert.Throws<ArgumentException>(() => new GameplaySetByCallerValues(new[] { Pair(Key, 1f), Pair(Key, 2f) }));
            Assert.That(new GameplaySetByCallerValues(Array.Empty<KeyValuePair<GameplayTag, float>>()).Count, Is.Zero);
        }

        [Test]
        public void Instant_ExecutesInOrderWithoutHandleEntryOrSourceAllocation()
        {
            var first = new Capture();
            var second = new Capture();
            var definition = Instant(first, second);
            Assert.That(definition.Duration, Is.Zero);
            var allocator = new GameplaySourceIdAllocator();
            var target = new Target(allocator.Allocate());
            var container = new GameplayEffectContainer(target.Attributes, allocator, target.Tags);
            var context = new GameplayContext(allocator.Allocate(), new GameplayTargetData(target.TargetId));
            var application = new GameplayEffectApplicationContext(context, target,
                new GameplaySetByCallerValues(new[] { Pair(Key, 4f) }));
            Assert.Throws<InvalidOperationException>(() => container.Apply(definition));
            var result = container.Apply(definition, application);
            Assert.That(result.HasActiveHandle, Is.False);
            Assert.That(result.ActiveHandle.IsValid, Is.False);
            Assert.That(container.IsActive(result.ActiveHandle), Is.False);
            Assert.That(container.TryGetRemainingDuration(result.ActiveHandle, out _), Is.False);
            Assert.That(container.TryGetPeriodRemaining(result.ActiveHandle, out _), Is.False);
            Assert.That(first.Calls, Is.EqualTo(1));
            Assert.That(second.Calls, Is.EqualTo(1));
            Assert.That(first.Last, Is.SameAs(application));
            Assert.That(allocator.Allocate().Value, Is.EqualTo(3), "Instant must not allocate an active source.");
            container.Advance(100f);
            Assert.That(first.Calls, Is.EqualTo(1));
        }

        [Test]
        public void DefinitionAndApplication_ValidatePolicyAndTarget()
        {
            Assert.Throws<ArgumentException>(() => new GameplayEffectDefinition(
                GameplayEffectDurationPolicy.Instant, 5f, Array.Empty<GameplayEffectAttributeModifierDefinition>(),
                Array.Empty<GameplayTag>(), Array.Empty<IGameplayEffectExecution>()));
            Assert.Throws<ArgumentException>(() => new GameplayEffectDefinition(
                GameplayEffectDurationPolicy.Instant, 0f,
                new[] { new GameplayEffectAttributeModifierDefinition(Attack, GameplayModifierOperation.Add, 1f) },
                Array.Empty<GameplayTag>(), new IGameplayEffectExecution[] { new Capture() }));
            Assert.Throws<ArgumentException>(() => new GameplayEffectDefinition(
                GameplayEffectDurationPolicy.Duration, 1f, Array.Empty<GameplayEffectAttributeModifierDefinition>(),
                Array.Empty<GameplayTag>(), new IGameplayEffectExecution[] { new Capture() }));
            var ids = new GameplaySourceIdAllocator();
            var one = new Target(ids.Allocate());
            var other = new Target(ids.Allocate());
            var context = new GameplayContext(ids.Allocate(), new GameplayTargetData(one.TargetId));
            var values = new GameplaySetByCallerValues(Array.Empty<KeyValuePair<GameplayTag, float>>());
            Assert.Throws<ArgumentException>(() => new GameplayEffectApplicationContext(context, other, values));
        }

        [Test]
        public void InstantFailure_IsFailFastWithoutRollbackOrReplay()
        {
            var first = new Capture();
            var second = new Capture { Throw = true };
            var third = new Capture();
            var ids = new GameplaySourceIdAllocator();
            var target = new Target(ids.Allocate());
            var context = new GameplayContext(ids.Allocate(), new GameplayTargetData(target.TargetId));
            var application = new GameplayEffectApplicationContext(context, target,
                new GameplaySetByCallerValues(Array.Empty<KeyValuePair<GameplayTag, float>>()));
            var container = new GameplayEffectContainer(target.Attributes, ids, target.Tags);
            Assert.Throws<InvalidOperationException>(() => container.Apply(Instant(first, second, third), application));
            Assert.That(first.Calls, Is.EqualTo(1));
            Assert.That(second.Calls, Is.EqualTo(1));
            Assert.That(third.Calls, Is.Zero);
            Assert.That(ids.Allocate().Value, Is.EqualTo(3));
        }

        [Test]
        public void PeriodicDefinitionAndLegacyApply_ValidateBeforeSourceAllocation()
        {
            var execution = new Capture();
            var emptyModifiers = Array.Empty<GameplayEffectAttributeModifierDefinition>();
            var emptyTags = Array.Empty<GameplayTag>();
            Assert.That(new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 2f,
                emptyModifiers).Period, Is.Zero);
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameplayEffectDefinition(
                GameplayEffectDurationPolicy.Duration, 2f, float.NaN, emptyModifiers, emptyTags,
                new IGameplayEffectExecution[] { execution }));
            Assert.Throws<ArgumentException>(() => new GameplayEffectDefinition(
                GameplayEffectDurationPolicy.Instant, 0f, 1f, emptyModifiers, emptyTags,
                new IGameplayEffectExecution[] { execution }));
            Assert.Throws<ArgumentException>(() => new GameplayEffectDefinition(
                GameplayEffectDurationPolicy.Duration, 2f, 1f, emptyModifiers, emptyTags,
                Array.Empty<IGameplayEffectExecution>()));
            Assert.Throws<ArgumentException>(() => new GameplayEffectDefinition(
                GameplayEffectDurationPolicy.Infinite, 0f, emptyModifiers, emptyTags,
                new IGameplayEffectExecution[] { execution }));
            var ids = new GameplaySourceIdAllocator();
            var target = new Target(ids.Allocate());
            target.Attributes.Register(Attack, 10f);
            var container = new GameplayEffectContainer(target.Attributes, ids, target.Tags);
            var tag = new GameplayTag("State.LegacyRejected");
            var periodic = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 3f, 1f,
                new[] { new GameplayEffectAttributeModifierDefinition(Attack, GameplayModifierOperation.Add, 5f) },
                new[] { tag }, new IGameplayEffectExecution[] { execution });
            Assert.Throws<InvalidOperationException>(() => container.Apply(periodic));
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(10f));
            Assert.That(target.Tags.HasTagExact(tag), Is.False);
            Assert.That(ids.Allocate().Value, Is.EqualTo(2));
        }

        [Test]
        public void PeriodicDuration_ExecutesAtFinalBoundaryThenExpiresAndRemoveStopsTicks()
        {
            var ids = new GameplaySourceIdAllocator();
            var target = new Target(ids.Allocate());
            var application = Application(ids, target, 4f);
            var container = new GameplayEffectContainer(target.Attributes, ids, target.Tags);
            var execution = new Capture();
            var result = container.Apply(Periodic(3f, 1f, execution), application);
            Assert.That(result.HasActiveHandle, Is.True);
            Assert.That(container.TryGetPeriodRemaining(result.ActiveHandle, out float period), Is.True);
            Assert.That(period, Is.EqualTo(1f));
            container.Advance(.5f);
            Assert.That(execution.Calls, Is.Zero);
            container.Advance(.5f);
            Assert.That(execution.Calls, Is.EqualTo(1));
            Assert.That(execution.Last, Is.SameAs(application));
            container.Advance(2f);
            Assert.That(execution.Calls, Is.EqualTo(3));
            Assert.That(container.IsActive(result.ActiveHandle), Is.False);
            Assert.That(container.TryGetPeriodRemaining(result.ActiveHandle, out period), Is.False);
            Assert.That(period, Is.Zero);
            var shortExecution = new Capture();
            var shortResult = container.Apply(Periodic(2.5f, 1f, shortExecution), application);
            container.Advance(2.5f);
            Assert.That(shortExecution.Calls, Is.EqualTo(2));
            Assert.That(container.IsActive(shortResult.ActiveHandle), Is.False);
            var removed = container.Apply(Periodic(3f, 1f, shortExecution), application);
            Assert.That(container.Remove(removed.ActiveHandle), Is.True);
            container.Advance(4f);
            Assert.That(shortExecution.Calls, Is.EqualTo(2));
        }

        [Test]
        public void InfinitePeriodic_LargeDeltaAndReapplicationKeepIndependentSnapshots()
        {
            var ids = new GameplaySourceIdAllocator();
            var target = new Target(ids.Allocate());
            var firstApplication = Application(ids, target, 4f);
            var secondApplication = Application(ids, target, 7f);
            var container = new GameplayEffectContainer(target.Attributes, ids, target.Tags);
            var execution = new Capture();
            var definition = Periodic(0f, 1f, execution, GameplayEffectDurationPolicy.Infinite);
            var first = container.Apply(definition, firstApplication);
            container.Advance(3.4f);
            Assert.That(execution.Calls, Is.EqualTo(3));
            Assert.That(container.TryGetPeriodRemaining(first.ActiveHandle, out float remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(.6f).Within(.00001f));
            var second = container.Apply(definition, secondApplication);
            container.Advance(.6f);
            Assert.That(execution.Calls, Is.EqualTo(4));
            Assert.That(execution.Last, Is.SameAs(firstApplication));
            Assert.That(execution.Last.SetByCaller.GetRequiredValue(Key), Is.EqualTo(4f));
            Assert.That(container.TryGetPeriodRemaining(second.ActiveHandle, out remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(.4f).Within(.00001f));
            container.Advance(.4f);
            Assert.That(execution.Calls, Is.EqualTo(5));
            Assert.That(execution.Last, Is.SameAs(secondApplication));
            Assert.That(ids.Allocate().Value, Is.EqualTo(6), "Only two applications allocate Effect sources.");
        }

        [Test]
        public void PeriodicFailure_CommitsScheduleAndFinalTickCleansUp()
        {
            var ids = new GameplaySourceIdAllocator();
            var target = new Target(ids.Allocate());
            var application = Application(ids, target, 1f);
            var container = new GameplayEffectContainer(target.Attributes, ids, target.Tags);
            var execution = new Capture { Throw = true };
            var result = container.Apply(Periodic(2f, 1f, execution), application);
            Assert.Throws<InvalidOperationException>(() => container.Advance(1f));
            Assert.That(container.TryGetPeriodRemaining(result.ActiveHandle, out float remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(1f));
            Assert.Throws<InvalidOperationException>(() => container.Advance(1f));
            Assert.That(execution.Calls, Is.EqualTo(2));
            Assert.That(container.IsActive(result.ActiveHandle), Is.False);
        }

        [Test]
        public void PeriodicEntry_OwnsModifierTagAndExecutionUntilExpiry()
        {
            var ids = new GameplaySourceIdAllocator();
            var target = new Target(ids.Allocate());
            target.Attributes.Register(Attack, 10f);
            var tag = new GameplayTag("State.PeriodicTest");
            var execution = new Capture();
            var definition = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 2f, 1f,
                new[] { new GameplayEffectAttributeModifierDefinition(Attack, GameplayModifierOperation.Add, 5f) },
                new[] { tag }, new IGameplayEffectExecution[] { execution });
            var container = new GameplayEffectContainer(target.Attributes, ids, target.Tags);
            var result = container.Apply(definition, Application(ids, target, 3f));
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(15f));
            Assert.That(target.Tags.HasTagExact(tag), Is.True);
            container.Advance(2f);
            Assert.That(execution.Calls, Is.EqualTo(2));
            Assert.That(container.IsActive(result.ActiveHandle), Is.False);
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(10f));
            Assert.That(target.Tags.HasTagExact(tag), Is.False);
        }

        private static GameplayEffectDefinition Periodic(float duration, float period,
            IGameplayEffectExecution execution,
            GameplayEffectDurationPolicy policy = GameplayEffectDurationPolicy.Duration) =>
            new GameplayEffectDefinition(policy, duration, period,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), Array.Empty<GameplayTag>(),
                new[] { execution });

        private static GameplayEffectApplicationContext Application(GameplaySourceIdAllocator ids,
            Target target, float value) =>
            new GameplayEffectApplicationContext(
                new GameplayContext(ids.Allocate(), new GameplayTargetData(target.TargetId)), target,
                new GameplaySetByCallerValues(new[] { Pair(Key, value) }));

        private static GameplayEffectDefinition Instant(params IGameplayEffectExecution[] executions) =>
            new GameplayEffectDefinition(GameplayEffectDurationPolicy.Instant, 99f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), Array.Empty<GameplayTag>(), executions);

        private static KeyValuePair<GameplayTag, float> Pair(GameplayTag key, float value) =>
            new KeyValuePair<GameplayTag, float>(key, value);

        private sealed class Capture : IGameplayEffectExecution
        {
            public int Calls;
            public bool Throw;
            public GameplayEffectApplicationContext Last;
            public void Execute(GameplayEffectApplicationContext application)
            {
                Calls++;
                Last = application;
                if (Throw) throw new InvalidOperationException("test failure");
            }
        }

        private sealed class Target : IGameplayEffectExecutionTarget
        {
            public GameplaySourceId TargetId { get; }
            public AttributeSet Attributes { get; } = new AttributeSet();
            public GameplayTagContainer Tags { get; } = new GameplayTagContainer();
            public Target(GameplaySourceId id) { TargetId = id; }
        }
    }
}
