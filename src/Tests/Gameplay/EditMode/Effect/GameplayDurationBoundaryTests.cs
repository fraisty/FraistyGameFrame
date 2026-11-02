using System;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Effect
{
    /// <summary>Non-periodic Duration regression authority: one timeline for expiry, refresh and observations.</summary>
    public sealed class GameplayDurationBoundaryTests
    {
        private static readonly GameplayTag Active = new GameplayTag("Test.DurationBoundary.Active");
        private static readonly GameplayTag StackKey = new GameplayTag("Test.DurationBoundary.Stack");

        private sealed class Host : IGameplayAbilityHost, IGameplayEffectCompletionExecution
        {
            public readonly GameplaySourceIdAllocator Ids = new GameplaySourceIdAllocator();
            public GameplaySourceId OwnerId { get; }
            public GameplaySourceId TargetId => OwnerId;
            public AttributeSet Attributes { get; } = new AttributeSet();
            public GameplayTagContainer Tags { get; } = new GameplayTagContainer();
            public GameplayEffectContainer Effects { get; }
            public GameplayEffectApplicationContext Application { get; }
            public int Completions;

            public Host()
            {
                OwnerId = Ids.Allocate();
                Effects = new GameplayEffectContainer(Attributes, Ids, Tags);
                Application = new GameplayEffectApplicationContext(new GameplayContext(OwnerId,
                    new GameplayTargetData(OwnerId)), this, GameplaySetByCallerValues.Empty);
            }

            public void Execute(GameplayEffectCompletionContext completion)
            {
                Assert.That(Effects.IsActive(completion.Handle), Is.False);
                Assert.That(Tags.HasTagExact(Active), Is.False);
                Completions++;
            }

            public GameplayEffectDefinition Definition(float duration = 3f, bool completion = false,
                GameplayEffectStackingDefinition stacking = null) => new GameplayEffectDefinition(
                    GameplayEffectDurationPolicy.Duration, duration, 0f,
                    Array.Empty<GameplayEffectAttributeModifierDefinition>(), new[] { Active },
                    Array.Empty<IGameplayEffectExecution>(), stacking,
                    completionExecutions: completion ? new IGameplayEffectCompletionExecution[] { this } : null);

            public void AssertActive(GameplayEffectHandle handle)
            {
                Assert.That(Effects.IsActive(handle), Is.True);
                Assert.That(Tags.HasTagExact(Active), Is.True);
                Assert.That(Effects.TryGetRemainingDuration(handle, out float remaining), Is.True);
                Assert.That(remaining, Is.GreaterThanOrEqualTo(0f));
                Assert.That(Effects.GetActiveEntriesSnapshot()[0].Remaining, Is.EqualTo(remaining));
            }

            public void AssertExpired(GameplayEffectHandle handle)
            {
                Assert.That(Effects.IsActive(handle), Is.False);
                Assert.That(Tags.HasTagExact(Active), Is.False);
                Assert.That(Effects.TryGetRemainingDuration(handle, out float remaining), Is.False);
                Assert.That(remaining, Is.Zero);
                Assert.That(Effects.GetActiveEntriesSnapshot(), Is.Empty);
            }
        }

        [TestCase(1, 3f, false)]
        [TestCase(90, 1f / 30f, false)]
        [TestCase(180, 1f / 60f, false)]
        [TestCase(1, 3f, true)]
        [TestCase(90, 1f / 30f, true)]
        [TestCase(180, 1f / 60f, true)]
        public void AuthoredThreeSecondsExpiresAtBoundaryAcrossPartitions(int steps, float delta, bool completion)
        {
            var host = new Host();
            var definition = host.Definition(completion: completion);
            var handle = completion ? host.Effects.Apply(definition, host.Application).ActiveHandle :
                host.Effects.Apply(definition);
            for (int i = 1; i < steps; i++)
            {
                host.Effects.Advance(delta);
                host.AssertActive(handle);
                Assert.That(host.Completions, Is.Zero);
            }
            host.Effects.Advance(delta);
            host.AssertExpired(handle);
            Assert.That(host.Completions, Is.EqualTo(completion ? 1 : 0));
            host.Effects.Advance(delta);
            Assert.That(host.Completions, Is.EqualTo(completion ? 1 : 0), "No second completion after expiry.");
        }

        private static GameplayEffectStackingDefinition Stack(GameplayEffectStackDurationRefreshPolicy refresh,
            GameplayEffectStackOverflowPolicy overflow) => new GameplayEffectStackingDefinition(StackKey,
                GameplayEffectStackAggregation.AggregateByTarget, 1, refresh,
                GameplayEffectStackPeriodResetPolicy.NeverReset, overflow);

        [TestCase(90, 1f / 30f)]
        [TestCase(180, 1f / 60f)]
        public void SuccessfulRefreshMovesHorizonFromCurrentElapsed(int steps, float delta)
        {
            var host = new Host();
            var definition = host.Definition(completion: true, stacking: Stack(
                GameplayEffectStackDurationRefreshPolicy.RefreshOnSuccessfulApplication,
                GameplayEffectStackOverflowPolicy.RefreshExistingAtLimit));
            var handle = host.Effects.Apply(definition, host.Application).ActiveHandle;
            host.Effects.Advance(1.25f);
            var reapplied = host.Effects.Apply(definition, host.Application);
            Assert.That(reapplied.Outcome, Is.EqualTo(GameplayEffectApplicationOutcome.RefreshedAtLimit));
            Assert.That(reapplied.ActiveHandle, Is.EqualTo(handle));
            Assert.That(host.Effects.TryGetRemainingDuration(handle, out float remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(3f));
            for (int i = 1; i < steps; i++)
            {
                host.Effects.Advance(delta);
                host.AssertActive(handle);
                Assert.That(host.Completions, Is.Zero);
            }
            host.Effects.Advance(delta);
            host.AssertExpired(handle);
            Assert.That(host.Completions, Is.EqualTo(1));
        }

        [TestCase(GameplayEffectStackDurationRefreshPolicy.NeverRefresh, GameplayEffectStackOverflowPolicy.RefreshExistingAtLimit)]
        [TestCase(GameplayEffectStackDurationRefreshPolicy.RefreshOnSuccessfulApplication, GameplayEffectStackOverflowPolicy.Reject)]
        public void NeverRefreshOrRejectedReapplyPreservesOriginalHorizon(
            GameplayEffectStackDurationRefreshPolicy refresh, GameplayEffectStackOverflowPolicy overflow)
        {
            var host = new Host();
            var definition = host.Definition(stacking: Stack(refresh, overflow));
            var handle = host.Effects.Apply(definition, host.Application).ActiveHandle;
            for (int i = 0; i < 60; i++) host.Effects.Advance(1f / 60f);
            host.Effects.TryGetRemainingDuration(handle, out float before);
            var reapplied = host.Effects.Apply(definition, host.Application);
            Assert.That(reapplied.Succeeded, Is.EqualTo(overflow != GameplayEffectStackOverflowPolicy.Reject));
            host.Effects.TryGetRemainingDuration(handle, out float after);
            Assert.That(after, Is.EqualTo(before));
            for (int i = 60; i < 179; i++) host.Effects.Advance(1f / 60f);
            host.AssertActive(handle);
            host.Effects.Advance(1f / 60f);
            host.AssertExpired(handle);
        }

        [Test]
        public void NonAlignedDurationDoesNotExpireAtPreviousFrame()
        {
            var host = new Host();
            var handle = host.Effects.Apply(host.Definition(.31f));
            for (int i = 0; i < 9; i++) host.Effects.Advance(1f / 30f);
            host.AssertActive(handle);
            host.Effects.Advance(1f / 30f);
            host.AssertExpired(handle);
        }

        [Test]
        public void ImmediatelyPreviousRepresentableTimeDoesNotExpireEarly()
        {
            var host = new Host();
            var handle = host.Effects.Apply(host.Definition());
            const float previous = 2.999999761581421f;
            host.Effects.Advance(previous);
            host.AssertActive(handle);
            host.Effects.TryGetRemainingDuration(handle, out float remaining);
            Assert.That(remaining, Is.EqualTo(3f - previous));
            host.Effects.Advance(3f - previous);
            host.AssertExpired(handle);
        }

        [TestCase(90, 1f / 30f)]
        [TestCase(180, 1f / 60f)]
        public void NormalAbilityCooldownRestoresActivationAtAuthoredBoundary(int steps, float delta)
        {
            var host = new Host();
            var abilities = new GameplayAbilityContainer(host, host.Ids);
            var handle = abilities.Grant(new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),
                Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>(), host.Definition()));
            Assert.That(abilities.TryActivate(handle), Is.True);
            abilities.End(handle);
            for (int i = 1; i < steps; i++)
            {
                host.Effects.Advance(delta);
                Assert.That(host.Tags.HasTagExact(Active), Is.True);
                Assert.That(abilities.CanActivate(handle), Is.False);
                Assert.That(abilities.GetStatus(handle).BlockReason, Is.EqualTo(GameplayAbilityBlockReason.Cooldown));
            }
            host.Effects.Advance(delta);
            Assert.That(host.Tags.HasTagExact(Active), Is.False);
            Assert.That(abilities.CanActivate(handle), Is.True);
            Assert.That(abilities.GetStatus(handle).CooldownRemaining, Is.Zero);
            Assert.That(host.Completions, Is.Zero);
        }
    }
}
