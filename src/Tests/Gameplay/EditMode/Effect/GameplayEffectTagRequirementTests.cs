using System;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Effect
{
    public sealed class GameplayEffectTagRequirementTests
    {
        private static readonly GameplayTag Stun = new GameplayTag("State.Stun");
        private static readonly GameplayTag Ready = new GameplayTag("State.Ready");
        private static readonly GameplayTag Guard = new GameplayTag("State.Guard");
        private static readonly GameplayAttributeId Attack = new GameplayAttributeId("Test.Requirement.Attack");
        private static GameplayTagQuery NoStun => new GameplayTagQuery(
            Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>(), new[] { Stun });

        [Test]
        public void Query_EmptyAllAnyNoneHierarchyAndInvalidInputs()
        {
            var tags = new GameplayTagContainer();
            var ids = new GameplaySourceIdAllocator();
            tags.AddTag(new GameplayTag("State.Ready.Fast"), ids.Allocate());
            tags.AddTag(Guard, ids.Allocate());
            Assert.That(GameplayTagQuery.Empty.Matches(tags), Is.True);
            var input = new[] { Ready };
            var query = new GameplayTagQuery(input, new[] { Guard }, new[] { Stun });
            input[0] = Stun;
            Assert.That(query.All[0], Is.EqualTo(Ready));
            Assert.That(query.Matches(tags), Is.True);
            Assert.That(new GameplayTagQuery(new[] { Stun }, Array.Empty<GameplayTag>(),
                Array.Empty<GameplayTag>()).Matches(tags), Is.False);
            Assert.That(new GameplayTagQuery(Array.Empty<GameplayTag>(), new[] { Stun },
                Array.Empty<GameplayTag>()).Matches(tags), Is.False);
            Assert.That(new GameplayTagQuery(Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>(),
                new[] { Ready }).Matches(tags), Is.False);
            Assert.Throws<ArgumentNullException>(() => new GameplayTagQuery(null,
                Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>()));
            Assert.Throws<ArgumentException>(() => new GameplayTagQuery(new[] { default(GameplayTag) },
                Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>()));
            Assert.Throws<ArgumentNullException>(() => query.Matches(null));
        }

        [Test]
        public void ApplicationRejectsInstantAndStackReapplyBeforeAnyMutation()
        {
            var ids = new GameplaySourceIdAllocator();
            var target = new Target(ids.Allocate());
            var effects = new GameplayEffectContainer(target.Attributes, ids, target.Tags);
            var execution = new CountingExecution();
            var instant = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Instant, 0f, 0f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), Array.Empty<GameplayTag>(),
                new IGameplayEffectExecution[] { execution }, null, NoStun);
            var stacking = new GameplayEffectStackingDefinition(new GameplayTag("Effect.Requirement.Stack"),
                GameplayEffectStackAggregation.AggregateByTarget, 3,
                GameplayEffectStackDurationRefreshPolicy.RefreshOnSuccessfulApplication,
                GameplayEffectStackPeriodResetPolicy.NeverReset,
                GameplayEffectStackOverflowPolicy.RefreshExistingAtLimit);
            var persistent = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 7f, 0f,
                new[] { new GameplayEffectAttributeModifierDefinition(Attack, GameplayModifierOperation.Add, 2f) },
                Array.Empty<GameplayTag>(), Array.Empty<IGameplayEffectExecution>(), stacking, NoStun);
            var application = ApplyContext(target);
            var first = effects.Apply(persistent, application);
            Assert.That(first.Succeeded, Is.True);
            effects.Advance(2f);
            target.Tags.AddTag(Stun, ids.Allocate());
            var rejectedInstant = effects.Apply(instant, application);
            var rejectedStack = effects.Apply(persistent, application);
            Assert.That(rejectedInstant.Outcome, Is.EqualTo(GameplayEffectApplicationOutcome.RejectedByRequirements));
            Assert.That(rejectedStack.Outcome, Is.EqualTo(GameplayEffectApplicationOutcome.RejectedByRequirements));
            Assert.That(rejectedInstant.Succeeded || rejectedStack.Succeeded, Is.False);
            Assert.That(rejectedInstant.HasActiveHandle || rejectedStack.HasActiveHandle, Is.False);
            Assert.That(execution.Calls, Is.Zero);
            Assert.That(effects.TryGetStackCount(first.ActiveHandle, out int count), Is.True);
            Assert.That(count, Is.EqualTo(1));
            Assert.That(effects.TryGetRemainingDuration(first.ActiveHandle, out float remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(5f));
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(16f));
            Assert.That(ids.Allocate().Value, Is.EqualTo(4), "Rejected applications allocate no source ID.");
        }

        [Test]
        public void OngoingMismatchRemovesWholeStackAndNeverResurrects()
        {
            var ids = new GameplaySourceIdAllocator();
            var target = new Target(ids.Allocate());
            var effects = new GameplayEffectContainer(target.Attributes, ids, target.Tags);
            var stacking = new GameplayEffectStackingDefinition(new GameplayTag("Effect.Requirement.Ongoing"),
                GameplayEffectStackAggregation.AggregateByTarget, 3,
                GameplayEffectStackDurationRefreshPolicy.RefreshOnSuccessfulApplication,
                GameplayEffectStackPeriodResetPolicy.NeverReset,
                GameplayEffectStackOverflowPolicy.RefreshExistingAtLimit);
            var definition = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 7f, 0f,
                new[] { new GameplayEffectAttributeModifierDefinition(Attack, GameplayModifierOperation.Add, 2f) },
                new[] { Guard }, Array.Empty<IGameplayEffectExecution>(), stacking, NoStun, NoStun);
            var application = ApplyContext(target);
            var first = effects.Apply(definition, application);
            effects.Apply(definition, application);
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(18f));
            var stunSource = ids.Allocate();
            target.Tags.AddTag(Stun, stunSource);
            effects.Advance(0f);
            Assert.That(effects.IsActive(first.ActiveHandle), Is.False);
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(14f));
            Assert.That(target.Tags.HasTag(Guard), Is.False);
            target.Tags.RemoveTag(Stun, stunSource);
            effects.Advance(1f);
            Assert.That(effects.IsActive(first.ActiveHandle), Is.False);
            var restarted = effects.Apply(definition, application);
            Assert.That(restarted.ActiveHandle, Is.Not.EqualTo(first.ActiveHandle));
            Assert.That(restarted.StackCount, Is.EqualTo(1));
        }

        [Test]
        public void NewPersistentRequirementFailureHasNoEntryOrSourceAndPeriodCannotFire()
        {
            var ids = new GameplaySourceIdAllocator();
            var target = new Target(ids.Allocate());
            var effects = new GameplayEffectContainer(target.Attributes, ids, target.Tags);
            var execution = new CountingExecution();
            var definition = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 5f, 1f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), new[] { Guard },
                new IGameplayEffectExecution[] { execution }, null, GameplayTagQuery.Empty, NoStun);
            var stunSource = ids.Allocate();
            target.Tags.AddTag(Stun, stunSource);
            var rejected = effects.Apply(definition, ApplyContext(target));
            Assert.That(rejected.Outcome, Is.EqualTo(GameplayEffectApplicationOutcome.RejectedByRequirements));
            Assert.That(rejected.HasActiveHandle, Is.False);
            Assert.That(target.Tags.HasTag(Guard), Is.False);
            Assert.That(ids.Allocate().Value, Is.EqualTo(3));
            target.Tags.RemoveTag(Stun, stunSource);
            var applied = effects.Apply(definition, ApplyContext(target));
            target.Tags.AddTag(Stun, ids.Allocate());
            effects.Advance(1f);
            Assert.That(execution.Calls, Is.Zero);
            Assert.That(effects.IsActive(applied.ActiveHandle), Is.False);
            Assert.That(target.Tags.HasTag(Guard), Is.False);
        }

        [Test]
        public void PeriodicExecutionLosingOngoingRequirementStopsLargeDelta()
        {
            var ids = new GameplaySourceIdAllocator();
            var target = new Target(ids.Allocate());
            var effects = new GameplayEffectContainer(target.Attributes, ids, target.Tags);
            var execution = new CountingExecution { StunOnFirst = true };
            var definition = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 8f, 1f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), Array.Empty<GameplayTag>(),
                new IGameplayEffectExecution[] { execution }, null, NoStun, NoStun);
            var result = effects.Apply(definition, ApplyContext(target));
            effects.Advance(5f);
            Assert.That(execution.Calls, Is.EqualTo(1));
            Assert.That(effects.IsActive(result.ActiveHandle), Is.False);
            Assert.Throws<ArgumentException>(() => new GameplayEffectDefinition(
                GameplayEffectDurationPolicy.Instant, 0f, 0f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), Array.Empty<GameplayTag>(),
                new IGameplayEffectExecution[] { execution }, null, null, NoStun));
        }

        private static GameplayEffectApplicationContext ApplyContext(Target target) =>
            new GameplayEffectApplicationContext(new GameplayContext(target.TargetId,
                new GameplayTargetData(target.TargetId)), target, GameplaySetByCallerValues.Empty);

        private sealed class Target : IGameplayEffectExecutionTarget
        {
            public GameplaySourceId TargetId { get; }
            public AttributeSet Attributes { get; } = new AttributeSet();
            public GameplayTagContainer Tags { get; } = new GameplayTagContainer();
            public Target(GameplaySourceId id) { TargetId = id; Attributes.Register(Attack, 14f); }
        }

        private sealed class CountingExecution : IGameplayEffectExecution
        {
            public int Calls;
            public bool StunOnFirst;
            public void Execute(GameplayEffectApplicationContext application)
            {
                Calls++;
                if (StunOnFirst && Calls == 1)
                    application.Target.Tags.AddTag(Stun, application.Target.TargetId);
            }
        }
    }
}
