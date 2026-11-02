using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay
{
    /// <summary>Guards R0's actual clock regressions without duplicating the existing container suites.</summary>
    public sealed class GameplayRuleClockBoundaryTests
    {
        private static readonly GameplayTag Signal = new GameplayTag("Test.Clock.Interval");
        private static readonly GameplayTag Active = new GameplayTag("Test.Clock.Active");

        [TestCase("coarse")]
        [TestCase("30fps")]
        [TestCase("60fps")]
        [TestCase("irregular")]
        [TestCase("exact")]
        [TestCase("previous-float")]
        public void SharedBoundaryMatrix_UsesAuthoredFloatCrossing(string partition)
        {
            var finite = new Host();
            var infinite = new Host();
            var ability = finite.Grant(1);
            finite.Cast(ability);
            int intervals = 0;
            using (var binding = new GameplaySkillIntervalTriggerDefinition(Signal, 3f)
                .Bind(finite, new GameplayEventDispatcher(), e => intervals++))
            {
                var plain = finite.Effects.Apply(new GameplayEffectDefinition(
                    GameplayEffectDurationPolicy.Duration, 3f, Array.Empty<GameplayEffectAttributeModifierDefinition>()));
                var periodic = finite.StartPeriodic();
                var perpetual = infinite.StartPeriodic(infinite: true);
                var sequences = new GameplaySequenceContainer();
                var sequence = sequences.Start(new GameplaySequenceDefinition(3f,
                    new GameplaySequenceMarkerDefinition[] { new GameplaySequenceCueMarkerDefinition(3f, Signal) }),
                    finite.Application.Context, finite);
                double elapsed = 0d;
                foreach (float delta in Partition(partition))
                {
                    elapsed += delta;
                    finite.Abilities.Advance(delta);
                    ((IGameplaySkillAdvanceableTriggerBinding)binding).Advance(delta);
                    finite.Effects.Advance(delta);
                    infinite.Effects.Advance(delta);
                    sequences.Advance(delta);
                    bool due = (float)elapsed >= 3f;
                    Assert.That(finite.Abilities.GetStatus(ability).CurrentCharges, Is.EqualTo(due ? 1 : 0), "Charge");
                    Assert.That(intervals, Is.EqualTo(due ? 1 : 0), "Interval");
                    Assert.That(finite.Effects.IsActive(plain), Is.EqualTo(!due), "Duration");
                    Assert.That(sequences.IsActive(sequence), Is.EqualTo(!due), "Sequence");
                    Assert.That(finite.Effects.IsActive(periodic), Is.EqualTo(!due), "Periodic expiry");
                    int expectedTicks = (float)elapsed >= 3f ? 3 : (float)elapsed >= 2f ? 2 : (float)elapsed >= 1f ? 1 : 0;
                    Assert.That(finite.Ticks, Is.EqualTo(expectedTicks), "Finite periodic");
                    Assert.That(infinite.Ticks, Is.EqualTo(expectedTicks), "Infinite periodic");
                    Assert.That(infinite.Effects.IsActive(perpetual), Is.True);
                    Assert.That(((GameplaySkillIntervalTriggerDefinition.Binding)binding).Remaining, Is.GreaterThanOrEqualTo(0f));
                }
                // No replay when the same public boundary is observed without additional rule time.
                int ticks = infinite.Ticks;
                infinite.Effects.Advance(0f);
                Assert.That(infinite.Ticks, Is.EqualTo(ticks));
            }
        }

        [Test]
        public void RechargeAndInterval_CatchUpWithoutResetOrDisposedReplay()
        {
            var host = new Host();
            var ability = host.Grant(3);
            host.Cast(ability);
            host.Abilities.Advance(1f);
            host.Cast(ability); // Existing sequential recharge must retain its first second.
            host.Abilities.Advance(2f);
            Assert.That(host.Abilities.GetStatus(ability).CurrentCharges, Is.EqualTo(2));
            Assert.That(host.Abilities.GetStatus(ability).RechargeRemaining, Is.EqualTo(3f));
            host.Cast(ability);
            host.Abilities.Advance(6.5f);
            Assert.That(host.Abilities.GetStatus(ability).CurrentCharges, Is.EqualTo(3));
            Assert.That(host.Abilities.GetStatus(ability).RechargeRemaining, Is.Zero);
            host.Cast(ability); // Catch-up remainder from a full grant must not become ghost progress.
            Assert.That(host.Abilities.GetStatus(ability).RechargeRemaining, Is.EqualTo(3f));

            var payloads = new List<GameplayEventData>();
            IGameplaySkillTriggerBinding binding = null;
            binding = new GameplaySkillIntervalTriggerDefinition(Signal, 3f)
                .Bind(host, new GameplayEventDispatcher(), e =>
                {
                    payloads.Add(e);
                    if (payloads.Count == 2) binding.Dispose();
                });
            ((IGameplaySkillAdvanceableTriggerBinding)binding).Advance(10f);
            Assert.That(payloads.Count, Is.EqualTo(2), "Dispose inside callback stops the remaining catch-up.");
            Assert.That(payloads[0].EventTag, Is.EqualTo(Signal));
            Assert.That(payloads[0].InstigatorId, Is.EqualTo(host.OwnerId));
            Assert.That(payloads[0].Magnitude, Is.EqualTo(3f));
            ((IGameplaySkillAdvanceableTriggerBinding)binding).Advance(10f);
            Assert.That(payloads.Count, Is.EqualTo(2));
        }

        [Test]
        public void PeriodicProjection_PreservesPreciseProgressAndFinalCleanupOrder()
        {
            var finite = new Host();
            var handle = finite.StartPeriodic(completion: true);
            var infinite = new Host();
            infinite.StartPeriodic(infinite: true);
            foreach (float delta in Partition("irregular"))
            {
                finite.Effects.Advance(delta);
                infinite.Effects.Advance(delta);
            }
            Assert.That(finite.Order, Is.EqualTo(new[] { "tick", "tick", "tick", "completion" }));
            Assert.That(finite.Effects.IsActive(handle), Is.False);
            Assert.That(finite.Tags.HasTagExact(Active), Is.False);
            finite.Effects.Advance(10f);
            Assert.That(finite.Order.Count, Is.EqualTo(4));

            // The irregular sum is just below 3d, although its public float projection is 3f.
            // Snapping Elapsed to a due 3/4/5 boundary would invent a sixth tick in the next step.
            infinite.Effects.Advance(2f);
            infinite.Effects.Advance(.9999997615814209f);
            Assert.That(infinite.Ticks, Is.EqualTo(5), "Float projection must never feed back into accumulated time.");
        }

        private static IEnumerable<float> Partition(string partition)
        {
            if (partition == "coarse") { yield return 3f; yield break; }
            if (partition == "exact") { yield return 2.5f; yield return .5f; yield break; }
            if (partition == "previous-float") { yield return 2.9999997615814209f; yield break; }
            if (partition == "irregular")
            {
                for (int i = 0; i < 15; i++)
                { yield return .013f; yield return .047f; yield return .11f; yield return .03f; }
                yield break;
            }
            int steps = partition == "30fps" ? 90 : 180;
            float delta = partition == "30fps" ? 1f / 30f : 1f / 60f;
            for (int i = 0; i < steps; i++) yield return delta;
        }

        private sealed class Host : IGameplayAbilityHost, IGameplaySkillHost,
            IGameplayEffectExecution, IGameplayEffectCompletionExecution
        {
            private readonly GameplaySourceIdAllocator _ids = new GameplaySourceIdAllocator();
            public GameplaySourceId OwnerId { get; }
            public GameplaySourceId TargetId => OwnerId;
            public AttributeSet Attributes { get; } = new AttributeSet();
            public GameplayTagContainer Tags { get; } = new GameplayTagContainer();
            public GameplayEffectContainer Effects { get; }
            public GameplayAbilityContainer Abilities { get; }
            public GameplayEffectApplicationContext Application { get; }
            public readonly List<string> Order = new List<string>();
            public int Ticks;

            public Host()
            {
                OwnerId = _ids.Allocate();
                Effects = new GameplayEffectContainer(Attributes, _ids, Tags);
                Abilities = new GameplayAbilityContainer(this, _ids);
                Application = new GameplayEffectApplicationContext(new GameplayContext(OwnerId,
                    new GameplayTargetData(OwnerId)), this, GameplaySetByCallerValues.Empty);
            }

            public GameplayAbilityHandle Grant(int charges) => Abilities.Grant(new GameplayAbilityDefinition(
                Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>(),
                charge: new GameplayAbilityChargeDefinition(charges, 3f)));

            public void Cast(GameplayAbilityHandle handle)
            { Assert.That(Abilities.TryActivate(handle), Is.True); Abilities.End(handle); }

            public GameplayEffectHandle StartPeriodic(bool infinite = false, bool completion = false) =>
                Effects.Apply(new GameplayEffectDefinition(infinite ? GameplayEffectDurationPolicy.Infinite :
                    GameplayEffectDurationPolicy.Duration, 3f, 1f, Array.Empty<GameplayEffectAttributeModifierDefinition>(),
                    new[] { Active }, new IGameplayEffectExecution[] { this }, null,
                    completionExecutions: completion ? new IGameplayEffectCompletionExecution[] { this } : null),
                    Application).ActiveHandle;

            public void Execute(GameplayEffectApplicationContext application)
            { Assert.That(Tags.HasTagExact(Active), Is.True); Ticks++; Order.Add("tick"); }

            public void Execute(GameplayEffectCompletionContext completion)
            {
                Assert.That(Effects.IsActive(completion.Handle), Is.False);
                Assert.That(Tags.HasTagExact(Active), Is.False);
                Order.Add("completion");
            }
        }
    }
}
