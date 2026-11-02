using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Sequence
{
    public sealed class GameplaySequenceTests
    {
        private static readonly GameplayAttributeId Attack = new GameplayAttributeId("Test.Attack");
        private static readonly GameplayTag Charge = new GameplayTag("Cue.Test.Charge");
        private static readonly GameplayTag Release = new GameplayTag("Cue.Test.Release");

        [Test]
        public void StartRunsZeroOnlyAndAdvanceKeepsAuthoredOrderAtSameTime()
        {
            var owner = new Owner();
            var markers = new GameplaySequenceMarkerDefinition[] {
                new GameplaySequenceCueMarkerDefinition(.5f, Release),
                new GameplaySequenceCueMarkerDefinition(0f, Charge),
                new GameplaySequenceApplyEffectMarkerDefinition(.5f, Boost()),
                new GameplaySequenceCueMarkerDefinition(.5f, Charge) };
            var definition = new GameplaySequenceDefinition(.5f, markers);
            markers[0] = null;
            Assert.That(definition.Markers.Select(m => m.Time), Is.EqualTo(new[] { 0f, .5f, .5f, .5f }));
            var context = owner.Context();
            var handle = owner.Sequences.Start(definition, context, owner.Host);
            var cues = new List<GameplayCueRequest>();
            owner.Sequences.DrainCues(cues);
            Assert.That(cues.Select(c => c.CueTag), Is.EqualTo(new[] { Charge }));
            Assert.That(cues[0].Context, Is.SameAs(context));
            Assert.That(owner.Attributes.GetCurrentValue(Attack), Is.EqualTo(10f));
            owner.Sequences.Advance(.49f);
            owner.Sequences.DrainCues(cues);
            Assert.That(cues.Count, Is.EqualTo(1));
            owner.Sequences.Advance(.01f);
            owner.Sequences.DrainCues(cues);
            Assert.That(cues.Select(c => c.CueTag), Is.EqualTo(new[] { Charge, Release, Charge }));
            Assert.That(owner.Attributes.GetCurrentValue(Attack), Is.EqualTo(13f));
            Assert.That(owner.Sequences.IsActive(handle), Is.False);
            Assert.That(owner.Sequences.ActiveCount, Is.Zero);
            owner.Sequences.Advance(1f);
            Assert.That(owner.Attributes.GetCurrentValue(Attack), Is.EqualTo(13f));
        }

        [Test]
        public void NewStartAfterAdvanceDoesNotConsumeOldDeltaAndPauseFreezesTime()
        {
            var owner = new Owner();
            var definition = new GameplaySequenceDefinition(.05f,
                new[] { new GameplaySequenceCueMarkerDefinition(.05f, Release) });
            owner.Sequences.Advance(.1f);
            owner.Sequences.Start(definition, owner.Context(), owner.Host);
            var cues = new List<GameplayCueRequest>();
            owner.Sequences.DrainCues(cues);
            Assert.That(cues, Is.Empty);
            Assert.That(owner.Sequences.ActiveCount, Is.EqualTo(1));
            owner.Sequences.Advance(.05f);
            owner.Sequences.DrainCues(cues);
            Assert.That(cues.Select(c => c.CueTag), Is.EqualTo(new[] { Release }));
        }

        [Test]
        public void EffectLifetimeOutlivesSequenceAndClearDoesNotRemoveIt()
        {
            var owner = new Owner();
            owner.Sequences.Start(new GameplaySequenceDefinition(.5f,
                new[] { new GameplaySequenceApplyEffectMarkerDefinition(.5f, Boost()) }),
                owner.Context(), owner.Host);
            owner.Sequences.Advance(.5f);
            Assert.That(owner.Sequences.ActiveCount, Is.Zero);
            Assert.That(owner.Attributes.GetCurrentValue(Attack), Is.EqualTo(13f));
            owner.Sequences.Clear();
            owner.Effects.Advance(4.99f);
            Assert.That(owner.Attributes.GetCurrentValue(Attack), Is.EqualTo(13f));
            owner.Effects.Advance(.02f);
            Assert.That(owner.Attributes.GetCurrentValue(Attack), Is.EqualTo(10f));
        }

        [Test]
        public void CueMetadataAndMultipleStartsDrainOnceInStartOrder()
        {
            var owner = new Owner();
            var definition = new GameplaySequenceDefinition(0f,
                new[] { new GameplaySequenceCueMarkerDefinition(0f, Charge, 2f, .5f) });
            var first = owner.Context();
            var second = owner.Context();
            owner.Sequences.Start(definition, first, owner.Host);
            owner.Sequences.Start(definition, second, owner.Host);
            var cues = new List<GameplayCueRequest>();
            owner.Sequences.DrainCues(cues);
            Assert.That(cues.Count, Is.EqualTo(2));
            Assert.That(cues[0].Context, Is.SameAs(first));
            Assert.That(cues[1].Context, Is.SameAs(second));
            Assert.That(cues[0].Magnitude, Is.EqualTo(2f));
            Assert.That(cues[0].Duration, Is.EqualTo(.5f));
            owner.Sequences.DrainCues(cues);
            Assert.That(cues.Count, Is.EqualTo(2));
        }

        [TestCase(30, 250)]
        [TestCase(60, 500)]
        public void LongTimelineExecutesEndOnFinalFrameAndCompletesExactlyOnce(int rate, int frames)
        {
            var owner = new Owner();
            float end = 250f / 30f;
            var handle = owner.Sequences.Start(new GameplaySequenceDefinition(end,
                new[] { new GameplaySequenceCueMarkerDefinition(end, Release) }), owner.Context(), owner.Host);
            var cues = new List<GameplayCueRequest>();
            for (int i = 1; i < frames; i++) owner.Sequences.Advance(1f / rate);
            owner.Sequences.DrainCues(cues);
            Assert.That(cues, Is.Empty);
            Assert.That(owner.Sequences.IsActive(handle), Is.True);
            var snapshot = owner.Sequences.GetActiveEntriesSnapshot().Single();
            Assert.That(snapshot.Elapsed, Is.InRange(0f, end));
            Assert.That(owner.Sequences.GetActiveEntriesSnapshot().Single().Elapsed, Is.EqualTo(snapshot.Elapsed));
            owner.Sequences.Advance(1f / rate);
            owner.Sequences.DrainCues(cues);
            Assert.That(cues.Select(c => c.CueTag), Is.EqualTo(new[] { Release }));
            Assert.That(owner.Sequences.ActiveCount, Is.Zero);
            Assert.That(owner.Sequences.IsActive(handle), Is.False);
            Assert.That(owner.Sequences.GetActiveEntriesSnapshot(), Is.Empty);
            owner.Sequences.Advance(1f);
            owner.Sequences.DrainCues(cues);
            Assert.That(cues.Count, Is.EqualTo(1));
        }

        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void LongTimelinePartitionsKeepAllCrossedMarkersAndAuthoredOrder(int partition)
        {
            var owner = new Owner();
            float end = 250f / 30f;
            var handle = owner.Sequences.Start(new GameplaySequenceDefinition(end,
                new GameplaySequenceMarkerDefinition[] {
                    new GameplaySequenceCueMarkerDefinition(0, Charge),
                    new GameplaySequenceCueMarkerDefinition(7f / 30, Release),
                    new GameplaySequenceApplyEffectMarkerDefinition(18f / 30, Boost()),
                    new GameplaySequenceCueMarkerDefinition(end, Release),
                    new GameplaySequenceCueMarkerDefinition(end, Charge) }), owner.Context(), owner.Host);
            if (partition == 0) owner.Sequences.Advance(end);
            else if (partition == 1)
            { owner.Sequences.Advance(249f / 30); owner.Sequences.Advance(1f / 30); }
            else
            {
                int rate = partition == 2 ? 30 : 60;
                int frames = partition == 2 ? 250 : 500;
                for (int i = 0; i < frames; i++) owner.Sequences.Advance(1f / rate);
            }
            var cues = new List<GameplayCueRequest>();
            owner.Sequences.DrainCues(cues);
            Assert.That(cues.Select(c => c.CueTag), Is.EqualTo(new[] { Charge, Release, Release, Charge }));
            Assert.That(owner.Sequences.IsActive(handle), Is.False);
            Assert.That(owner.Attributes.GetCurrentValue(Attack), Is.EqualTo(13f));
            Assert.That(owner.Effects.GetActiveEntriesSnapshot().Count, Is.EqualTo(1));
        }

        [Test]
        public void FineTimelineDoesNotExecuteFutureMarkerEarly()
        {
            var owner = new Owner();
            var handle = owner.Sequences.Start(new GameplaySequenceDefinition(.31f,
                new[] { new GameplaySequenceCueMarkerDefinition(.31f, Release) }), owner.Context(), owner.Host);
            var cues = new List<GameplayCueRequest>();
            for (int i = 0; i < 9; i++) owner.Sequences.Advance(1f / 30);
            owner.Sequences.DrainCues(cues);
            Assert.That(cues, Is.Empty);
            Assert.That(owner.Sequences.IsActive(handle), Is.True);
            owner.Sequences.Advance(1f / 30);
            owner.Sequences.DrainCues(cues);
            Assert.That(cues.Select(c => c.CueTag), Is.EqualTo(new[] { Release }));
            Assert.That(owner.Sequences.ActiveCount, Is.Zero);
        }

        [TestCase(4f / 30, 3f / 30, 1f / 30)]
        [TestCase(16f / 30, 7f / 30, 9f / 30)]
        [TestCase(22f / 30, 7f / 30, 15f / 30)]
        public void ShortPartitionsRetainFloatAuthoredBoundary(float end, float first, float second)
        {
            var owner = new Owner();
            owner.Sequences.Start(new GameplaySequenceDefinition(end,
                new[] { new GameplaySequenceCueMarkerDefinition(end, Release) }), owner.Context(), owner.Host);
            var cues = new List<GameplayCueRequest>();
            owner.Sequences.Advance(first);
            owner.Sequences.DrainCues(cues);
            Assert.That(cues, Is.Empty);
            owner.Sequences.Advance(second);
            owner.Sequences.DrainCues(cues);
            Assert.That(cues.Select(c => c.CueTag), Is.EqualTo(new[] { Release }));
            Assert.That(owner.Sequences.ActiveCount, Is.Zero);
        }

        [TestCase(4f / 30)] [TestCase(250f / 30)]
        public void ImmediatelyPrecedingRepresentableAuthoredTimeDoesNotCrossMarker(float end)
        {
            var owner = new Owner();
            float preceding = BitConverter.Int32BitsToSingle(BitConverter.SingleToInt32Bits(end) - 1);
            var handle = owner.Sequences.Start(new GameplaySequenceDefinition(end,
                new[] { new GameplaySequenceCueMarkerDefinition(end, Release) }), owner.Context(), owner.Host);
            owner.Sequences.Advance(preceding);
            owner.Sequences.Advance(0);
            var cues = new List<GameplayCueRequest>();
            owner.Sequences.DrainCues(cues);
            Assert.That(cues, Is.Empty, "Even one representable float time before the marker is still future.");
            Assert.That(owner.Sequences.IsActive(handle), Is.True);
            Assert.That(owner.Sequences.GetActiveEntriesSnapshot().Single().Elapsed, Is.EqualTo(preceding));
            owner.Sequences.Advance(end - preceding);
            owner.Sequences.DrainCues(cues);
            Assert.That(cues.Select(c => c.CueTag), Is.EqualTo(new[] { Release }));
            Assert.That(owner.Sequences.ActiveCount, Is.Zero);
        }

        private static GameplayEffectDefinition Boost() => new GameplayEffectDefinition(
            GameplayEffectDurationPolicy.Duration, 5f,
            new[] { new GameplayEffectAttributeModifierDefinition(Attack, GameplayModifierOperation.Add, 3f) });

        private sealed class Owner
        {
            private readonly GameplaySourceIdAllocator _ids = new GameplaySourceIdAllocator();
            public readonly AttributeSet Attributes = new AttributeSet();
            public readonly GameplaySequenceContainer Sequences = new GameplaySequenceContainer();
            public readonly GameplayEffectContainer Effects;
            public readonly GameplaySkillOwnerHost Host;
            public Owner()
            {
                Attributes.Register(Attack, 10f);
                var tags = new GameplayTagContainer();
                Effects = new GameplayEffectContainer(Attributes, _ids, tags);
                Host = new GameplaySkillOwnerHost(_ids.Allocate(), tags, Attributes, Effects);
            }
            public GameplayContext Context() => new GameplayContext(Host.OwnerId, new GameplayTargetData(Host.OwnerId));
        }
    }
}
