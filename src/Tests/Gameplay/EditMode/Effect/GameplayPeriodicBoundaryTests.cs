using System;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Effect
{
    /// <summary>Regression authority for C1: periodic scheduling and expiry share one rule clock.</summary>
    public sealed class GameplayPeriodicBoundaryTests
    {
        private static readonly GameplayAttributeId Attack = new GameplayAttributeId("Test.Boundary.Attack");
        private static readonly GameplayTag Granted = new GameplayTag("State.Boundary.Active");

        [TestCase(10f, 1, 10f, 10)]
        [TestCase(10f, 10, 1f, 10)]
        [TestCase(10f, 50, .2f, 10)]
        [TestCase(10f, 300, 1f / 30f, 10)]
        [TestCase(10f, 600, 1f / 60f, 10)]
        [TestCase(3f, 1, 3f, 3)]
        [TestCase(3f, 180, 1f / 60f, 3)]
        public void PartitionedExpiry_ExecutesFinalPeriodBeforeSourceCleanup(
            float duration, int steps, float delta, int expected)
        {
            var fixture = new Fixture();
            fixture.Start(duration);
            Assert.That(fixture.Calls, Is.Zero, "Application is not a periodic execution.");
            fixture.OnTick = application =>
            {
                Assert.That(fixture.Effects.IsActive(fixture.Handle), Is.True);
                Assert.That(application.Target.Tags.HasTagExact(Granted), Is.True);
                Assert.That(application.Target.Attributes.GetCurrentValue(Attack), Is.EqualTo(15f));
            };
            for (int i = 0; i < steps; i++)
            {
                fixture.Effects.Advance(delta);
                fixture.AssertObservation();
            }
            Assert.That(fixture.Calls, Is.EqualTo(expected));
            fixture.AssertRemoved();
        }

        [TestCase(2.5f, 2)]
        [TestCase(.5f, 0)]
        [TestCase(.99999994f, 0)] // Immediately preceding representable float below Period=1.
        [TestCase(1.00000012f, 1)] // Immediately following representable float above Period=1.
        public void ExpiryComparedToPeriod_DoesNotInventNearbyTick(float duration, int expected)
        {
            var fixture = new Fixture();
            fixture.Start(duration);
            fixture.Effects.Advance(2.5f);
            Assert.That(fixture.Calls, Is.EqualTo(expected));
            fixture.AssertRemoved();
        }

        [Test]
        public void AdvanceJustBeforePeriod_WaitsForActualBoundary()
        {
            var fixture = new Fixture();
            fixture.Start(3f);
            fixture.Effects.Advance(.99999994f);
            Assert.That(fixture.Calls, Is.Zero);
            fixture.AssertObservation();
            fixture.Effects.Advance(1f - .99999994f);
            Assert.That(fixture.Calls, Is.EqualTo(1));
        }

        [Test]
        public void LargeDelta_StopsAtFiniteExpiryButInfiniteKeepsPhase()
        {
            var finite = new Fixture();
            finite.Start(10f);
            finite.Effects.Advance(12f);
            Assert.That(finite.Calls, Is.EqualTo(10));
            finite.AssertRemoved();

            var infinite = new Fixture();
            infinite.Start(0f, policy: GameplayEffectDurationPolicy.Infinite);
            infinite.Effects.Advance(10.25f);
            Assert.That(infinite.Calls, Is.EqualTo(10));
            Assert.That(infinite.Effects.IsActive(infinite.Handle), Is.True);
            Assert.That(infinite.Effects.TryGetRemainingDuration(infinite.Handle, out float remaining), Is.False);
            Assert.That(remaining, Is.Zero);
            Assert.That(infinite.Effects.TryGetPeriodRemaining(infinite.Handle, out remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(.75f));
        }

        [TestCase(GameplayEffectStackPeriodResetPolicy.NeverReset, .6f, 10)]
        [TestCase(GameplayEffectStackPeriodResetPolicy.ResetOnSuccessfulApplication, 1f, 10)]
        public void RefreshAtLimit_RefreshesExpiryAndKeepsOrResetsOnePeriodSchedule(
            GameplayEffectStackPeriodResetPolicy reset, float nextPeriod, int expected)
        {
            var fixture = new Fixture();
            var stacking = new GameplayEffectStackingDefinition(new GameplayTag("Effect.Boundary.Stack"),
                GameplayEffectStackAggregation.AggregateByTarget, 1,
                GameplayEffectStackDurationRefreshPolicy.RefreshOnSuccessfulApplication,
                reset, GameplayEffectStackOverflowPolicy.RefreshExistingAtLimit);
            fixture.Start(10f, stacking);
            fixture.Effects.Advance(.4f);
            var latest = fixture.Application(fixture.TargetId);
            var result = fixture.Effects.Apply(fixture.Definition, latest);
            Assert.That(result.Outcome, Is.EqualTo(GameplayEffectApplicationOutcome.RefreshedAtLimit));
            Assert.That(result.ActiveHandle, Is.EqualTo(fixture.Handle));
            Assert.That(fixture.Effects.GetActiveEntriesSnapshot().Count, Is.EqualTo(1));
            Assert.That(fixture.Effects.TryGetRemainingDuration(fixture.Handle, out float remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(10f));
            Assert.That(fixture.Effects.TryGetPeriodRemaining(fixture.Handle, out remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(nextPeriod).Within(.000001f));
            fixture.OnTick = application => Assert.That(application.Context, Is.SameAs(latest.Context));
            // Exact binary partition: tests refreshed coincidence without another rounded countdown.
            for (int i = 0; i < 40; i++) fixture.Effects.Advance(.25f);
            Assert.That(fixture.Calls, Is.EqualTo(expected));
            fixture.AssertRemoved();
        }

        [Test]
        public void PartitionedFinalThrow_CleansOwnedStateWithoutReplay()
        {
            var fixture = new Fixture();
            fixture.Start(3f);
            fixture.OnTick = application =>
            {
                if (fixture.Calls == 3) throw new InvalidOperationException("final tick");
            };
            for (int i = 0; i < 179; i++) fixture.Effects.Advance(1f / 60f);
            Assert.That(fixture.Calls, Is.EqualTo(2));
            Assert.Throws<InvalidOperationException>(() => fixture.Effects.Advance(1f / 60f));
            Assert.That(fixture.Calls, Is.EqualTo(3));
            fixture.AssertRemoved();
            fixture.Effects.Advance(10f);
            Assert.That(fixture.Calls, Is.EqualTo(3));
        }

        [Test]
        public void ReapplyDuringExecution_UsesCommittedBoundaryAndRemainingAdvance()
        {
            var fixture = new Fixture();
            var stacking = new GameplayEffectStackingDefinition(new GameplayTag("Effect.Boundary.Callback"),
                GameplayEffectStackAggregation.AggregateByTarget, 1,
                GameplayEffectStackDurationRefreshPolicy.RefreshOnSuccessfulApplication,
                GameplayEffectStackPeriodResetPolicy.ResetOnSuccessfulApplication,
                GameplayEffectStackOverflowPolicy.RefreshExistingAtLimit);
            fixture.Start(2f, stacking);
            fixture.OnTick = application =>
            {
                if (fixture.Calls == 1) fixture.Effects.Apply(fixture.Definition, application);
            };
            fixture.Effects.Advance(3f);
            Assert.That(fixture.Calls, Is.EqualTo(3));
            fixture.AssertRemoved();
        }

        /// <summary>Guards stale final-boundary expiry after a successful callback refresh, including remaining large-delta time.</summary>
        [TestCase(2f, 2, 2f, 1f)]
        [TestCase(3.5f, 3, .5f, .5f)]
        public void FinalBoundaryReapply_UsesRefreshedExpiryBeforeNaturalCompletion(
            float firstAdvance, int expectedCalls, float remaining, float nextPeriod)
        {
            var fixture = new Fixture();
            var stacking = new GameplayEffectStackingDefinition(new GameplayTag("Effect.Boundary.FinalRefresh"),
                GameplayEffectStackAggregation.AggregateByTarget, 1,
                GameplayEffectStackDurationRefreshPolicy.RefreshOnSuccessfulApplication,
                GameplayEffectStackPeriodResetPolicy.ResetOnSuccessfulApplication,
                GameplayEffectStackOverflowPolicy.RefreshExistingAtLimit);
            fixture.Start(2f, stacking, completion: true);
            int naturalRemovals = 0;
            fixture.Effects.Removed += observation =>
            {
                Assert.That(observation.Cause, Is.EqualTo(GameplayEffectRemovalCause.NaturalExpiry));
                Assert.That(fixture.Completions, Is.EqualTo(1));
                naturalRemovals++;
            };
            var latest = fixture.Application(fixture.TargetId);
            fixture.OnTick = application =>
            {
                Assert.That(fixture.Effects.IsActive(fixture.Handle), Is.True);
                Assert.That(fixture.Tags.HasTagExact(Granted), Is.True);
                if (fixture.Calls == 2)
                {
                    var result = fixture.Effects.Apply(fixture.Definition, latest);
                    Assert.That(result.Outcome, Is.EqualTo(GameplayEffectApplicationOutcome.RefreshedAtLimit));
                    Assert.That(result.ActiveHandle, Is.EqualTo(fixture.Handle));
                }
                else if (fixture.Calls > 2)
                    Assert.That(application.Context, Is.SameAs(latest.Context));
            };
            fixture.OnComplete = () =>
            {
                Assert.That(fixture.Calls, Is.EqualTo(4));
                fixture.AssertRemoved(); // Completion observes cleanup after the real final period.
            };

            fixture.Effects.Advance(firstAdvance);
            Assert.That(fixture.Calls, Is.EqualTo(expectedCalls));
            Assert.That(fixture.Effects.IsActive(fixture.Handle), Is.True);
            Assert.That(fixture.Completions, Is.Zero);
            Assert.That(naturalRemovals, Is.Zero);
            Assert.That(fixture.Effects.TryGetRemainingDuration(fixture.Handle, out float duration), Is.True);
            Assert.That(duration, Is.EqualTo(remaining));
            Assert.That(fixture.Effects.TryGetPeriodRemaining(fixture.Handle, out float period), Is.True);
            Assert.That(period, Is.EqualTo(nextPeriod));
            fixture.AssertObservation();

            fixture.Effects.Advance(4f - firstAdvance);
            Assert.That(fixture.Calls, Is.EqualTo(4));
            Assert.That(fixture.Completions, Is.EqualTo(1));
            Assert.That(naturalRemovals, Is.EqualTo(1));
            fixture.AssertRemoved();
            fixture.Effects.Advance(10f);
            Assert.That(fixture.Calls, Is.EqualTo(4));
            Assert.That(fixture.Completions, Is.EqualTo(1));
            Assert.That(naturalRemovals, Is.EqualTo(1));
        }

        private sealed class Fixture : IGameplayEffectExecutionTarget, IGameplayEffectExecution, IGameplayEffectCompletionExecution
        {
            private readonly GameplaySourceIdAllocator _ids = new GameplaySourceIdAllocator();
            public GameplaySourceId TargetId { get; }
            public AttributeSet Attributes { get; } = new AttributeSet();
            public GameplayTagContainer Tags { get; } = new GameplayTagContainer();
            public readonly GameplayEffectContainer Effects;
            public GameplayEffectHandle Handle;
            public GameplayEffectDefinition Definition;
            public int Calls;
            public Action<GameplayEffectApplicationContext> OnTick;
            public int Completions;
            public Action OnComplete;

            public Fixture()
            {
                TargetId = _ids.Allocate();
                Attributes.Register(Attack, 10f);
                Effects = new GameplayEffectContainer(Attributes, _ids, Tags);
            }

            public GameplayEffectApplicationContext Application(GameplaySourceId source) =>
                new GameplayEffectApplicationContext(new GameplayContext(source,
                    new GameplayTargetData(TargetId)), this, GameplaySetByCallerValues.Empty);

            public void Start(float duration, GameplayEffectStackingDefinition stacking = null,
                GameplayEffectDurationPolicy policy = GameplayEffectDurationPolicy.Duration, bool completion = false)
            {
                Definition = new GameplayEffectDefinition(policy, duration, 1f,
                    new[] { new GameplayEffectAttributeModifierDefinition(Attack, GameplayModifierOperation.Add, 5f) },
                    new[] { Granted }, new IGameplayEffectExecution[] { this }, stacking,
                    completionExecutions: completion ? new IGameplayEffectCompletionExecution[] { this } : null);
                Handle = Effects.Apply(Definition, Application(_ids.Allocate())).ActiveHandle;
            }

            public void Execute(GameplayEffectApplicationContext application)
            {
                Calls++;
                OnTick?.Invoke(application);
            }

            public void AssertObservation()
            {
                if (!Effects.IsActive(Handle)) return;
                Assert.That(Effects.TryGetRemainingDuration(Handle, out float duration), Is.True);
                Assert.That(Effects.TryGetPeriodRemaining(Handle, out float period), Is.True);
                Assert.That(duration, Is.GreaterThanOrEqualTo(0f));
                Assert.That(period, Is.GreaterThanOrEqualTo(0f));
                Assert.That(float.IsNaN(duration) || float.IsInfinity(duration) ||
                    float.IsNaN(period) || float.IsInfinity(period), Is.False);
                var snapshot = Effects.GetActiveEntriesSnapshot()[0];
                Assert.That(snapshot.Remaining, Is.EqualTo(duration));
                Assert.That(snapshot.PeriodRemaining, Is.EqualTo(period));
            }

            public void Execute(GameplayEffectCompletionContext completion)
            {
                Completions++;
                OnComplete?.Invoke();
            }

            public void AssertRemoved()
            {
                Assert.That(Effects.IsActive(Handle), Is.False);
                Assert.That(Effects.TryGetRemainingDuration(Handle, out float duration), Is.False);
                Assert.That(Effects.TryGetPeriodRemaining(Handle, out float period), Is.False);
                Assert.That(duration, Is.Zero);
                Assert.That(period, Is.Zero);
                Assert.That(Attributes.GetCurrentValue(Attack), Is.EqualTo(10f));
                Assert.That(Tags.HasTagExact(Granted), Is.False);
            }
        }
    }
}
