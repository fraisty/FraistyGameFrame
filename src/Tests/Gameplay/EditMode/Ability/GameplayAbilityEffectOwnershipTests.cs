using System;
using System.Collections;
using System.Reflection;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Ability
{
    public sealed class GameplayAbilityEffectOwnershipTests
    {
        private sealed class Setup : IGameplayAbilityHost
        {
            public GameplaySourceId OwnerId { get; }
            public GameplaySourceId TargetId => OwnerId;
            public AttributeSet Attributes { get; } = new AttributeSet();
            public GameplayTagContainer Tags { get; } = new GameplayTagContainer();
            public GameplayEffectContainer Effects { get; }
            public GameplayAbilityContainer Abilities { get; }
            public readonly GameplayAbilityHandle Ability;
            public Setup(GameplayEffectDefinition cooldown = null)
            {
                var ids = new GameplaySourceIdAllocator();
                OwnerId = ids.Allocate();
                Effects = new GameplayEffectContainer(Attributes, ids, Tags);
                Abilities = new GameplayAbilityContainer(this, ids);
                Ability = Abilities.Grant(Definition(cooldown));
                Assert.That(Abilities.TryActivate(Ability), Is.True);
            }
            public GameplayEffectApplicationResult Apply(GameplayEffectDefinition effect = null) =>
                Effects.Apply(effect ?? Effect(), new GameplayEffectApplicationContext(
                    new GameplayContext(OwnerId, new GameplayTargetData(OwnerId)), this,
                    GameplaySetByCallerValues.Empty));
        }

        private static GameplayAbilityDefinition Definition(GameplayEffectDefinition cooldown = null) =>
            new GameplayAbilityDefinition(Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>(),
                Array.Empty<GameplayTag>(), cooldown);

        private static GameplayEffectDefinition Effect(GameplayEffectDurationPolicy policy = GameplayEffectDurationPolicy.Infinite,
            GameplayEffectStackingDefinition stacking = null, GameplayTagQuery application = null, GameplayTagQuery ongoing = null) =>
            new GameplayEffectDefinition(policy, 1f, 0f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), Array.Empty<GameplayTag>(),
                Array.Empty<IGameplayEffectExecution>(), stacking, application, ongoing);

        // Test-only inspection of cleanup relations avoids adding a public mutable list or debugger API.
        private static int OwnedCount(Setup s)
        {
            var entries = (IDictionary)typeof(GameplayAbilityContainer).GetField("_entries",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(s.Abilities);
            var entry = entries[s.Ability];
            return ((ICollection)entry.GetType().GetField("OwnedEffectHandles",
                BindingFlags.Instance | BindingFlags.NonPublic).GetValue(entry)).Count;
        }

        [TestCase("End")][TestCase("Cancel")][TestCase("Remove")]
        public void DynamicNewEffectIsRemovedByEveryAbilityFinishPath(string finish)
        {
            var s = new Setup(); var result = s.Apply();
            Assert.That(s.Abilities.TryOwnEffect(s.Ability, result), Is.True);
            Assert.That(OwnedCount(s), Is.EqualTo(1));
            bool done = finish == "End" ? s.Abilities.End(s.Ability) :
                finish == "Cancel" ? s.Abilities.Cancel(s.Ability) : s.Abilities.Remove(s.Ability);
            Assert.That(done, Is.True);
            Assert.That(s.Effects.IsActive(result.ActiveHandle), Is.False);
        }

        [TestCase("Expire")][TestCase("Remove")][TestCase("Ongoing")]
        public void EarlierEffectRemovalLeavesSafeStaleRelation(string removal)
        {
            var s = new Setup(); var blocked = new GameplayTag("State.Blocked");
            var query = new GameplayTagQuery(Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>(), new[] { blocked });
            var result = s.Apply(Effect(GameplayEffectDurationPolicy.Duration, ongoing: query));
            s.Abilities.TryOwnEffect(s.Ability, result);
            if (removal == "Remove") s.Effects.Remove(result.ActiveHandle);
            else if (removal == "Expire") s.Effects.Advance(1f);
            else { s.Tags.AddTag(blocked, s.OwnerId); s.Effects.Advance(0f); }
            Assert.That(s.Effects.IsActive(result.ActiveHandle), Is.False);
            Assert.That(s.Abilities.TryOwnEffect(s.Ability, result), Is.False);
            Assert.That(s.Abilities.End(s.Ability), Is.True);
            Assert.That(OwnedCount(s), Is.Zero);
        }

        [Test]
        public void InactiveRemovedInvalidAndForeignGrantsCannotOwnEffects()
        {
            var s = new Setup(); var other = new Setup(); var result = s.Apply();
            var inactive = s.Abilities.Grant(Definition());
            Assert.That(s.Abilities.TryOwnEffect(inactive, result), Is.False);
            Assert.That(s.Abilities.TryOwnEffect(other.Ability, result), Is.False);
            Assert.That(s.Abilities.TryOwnEffect(default, result), Is.False);
            s.Abilities.Remove(inactive);
            Assert.That(s.Abilities.TryOwnEffect(inactive, result), Is.False);
            s.Abilities.End(s.Ability);
            Assert.That(s.Abilities.TryOwnEffect(s.Ability, result), Is.False);
            Assert.That(s.Effects.IsActive(result.ActiveHandle), Is.True);
        }

        [Test]
        public void ForeignDefaultAndStaleEffectsCannotBeRegistered()
        {
            var s = new Setup(); var foreign = new Setup(); var result = s.Apply();
            Assert.That(s.Abilities.TryOwnEffect(s.Ability, foreign.Apply()), Is.False);
            Assert.That(s.Abilities.TryOwnEffect(s.Ability, default), Is.False);
            s.Effects.Remove(result.ActiveHandle);
            Assert.That(s.Abilities.TryOwnEffect(s.Ability, result), Is.False);
            Assert.That(OwnedCount(s), Is.Zero);
        }

        [Test]
        public void DuplicateIsIdempotentAndAnotherGrantCannotTakeSameResult()
        {
            var s = new Setup(); var result = s.Apply();
            Assert.That(s.Abilities.TryOwnEffect(s.Ability, result), Is.True);
            Assert.That(s.Abilities.TryOwnEffect(s.Ability, result), Is.True);
            Assert.That(OwnedCount(s), Is.EqualTo(1));
            var other = s.Abilities.Grant(Definition()); s.Abilities.TryActivate(other);
            Assert.That(s.Abilities.TryOwnEffect(other, result), Is.False);
            s.Abilities.End(other);
            Assert.That(s.Effects.IsActive(result.ActiveHandle), Is.True);
            s.Abilities.End(s.Ability);
            Assert.That(s.Effects.IsActive(result.ActiveHandle), Is.False);
        }

        [TestCase(2, GameplayEffectStackOverflowPolicy.RefreshExistingAtLimit, GameplayEffectApplicationOutcome.StackIncreased)]
        [TestCase(1, GameplayEffectStackOverflowPolicy.RefreshExistingAtLimit, GameplayEffectApplicationOutcome.RefreshedAtLimit)]
        [TestCase(1, GameplayEffectStackOverflowPolicy.Reject, GameplayEffectApplicationOutcome.RejectedAtLimit)]
        public void MergedOrRejectedStackCannotBeStolen(int limit, GameplayEffectStackOverflowPolicy overflow,
            GameplayEffectApplicationOutcome expected)
        {
            var s = new Setup();
            var stack = new GameplayEffectStackingDefinition(new GameplayTag("Effect.Shared"),
                GameplayEffectStackAggregation.AggregateByTarget, limit,
                GameplayEffectStackDurationRefreshPolicy.RefreshOnSuccessfulApplication,
                GameplayEffectStackPeriodResetPolicy.ResetOnSuccessfulApplication, overflow);
            var definition = Effect(stacking: stack); var existing = s.Apply(definition); var result = s.Apply(definition);
            Assert.That(result.Outcome, Is.EqualTo(expected));
            Assert.That(s.Abilities.TryOwnEffect(s.Ability, result), Is.False);
            Assert.That(OwnedCount(s), Is.Zero);
            s.Abilities.End(s.Ability);
            Assert.That(s.Effects.IsActive(existing.ActiveHandle), Is.True);
        }

        [Test]
        public void RequirementsRejectionCannotMutateOwnership()
        {
            var s = new Setup(); var query = new GameplayTagQuery(new[] { new GameplayTag("State.Missing") },
                Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>());
            var result = s.Apply(Effect(application: query));
            Assert.That(result.Outcome, Is.EqualTo(GameplayEffectApplicationOutcome.RejectedByRequirements));
            Assert.That(s.Abilities.TryOwnEffect(s.Ability, result), Is.False);
            Assert.That(OwnedCount(s), Is.Zero);
        }

        [Test]
        public void NormalCooldownRemainsIndependentOfDynamicCleanup()
        {
            var cooldown = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 5f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), new[] { new GameplayTag("Cooldown.Test") });
            var s = new Setup(cooldown); var result = s.Apply();
            s.Abilities.TryOwnEffect(s.Ability, result); s.Abilities.End(s.Ability);
            Assert.That(s.Effects.IsActive(result.ActiveHandle), Is.False);
            Assert.That(s.Abilities.TryGetCooldownRemaining(s.Ability, out float remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(5f));
        }

        [Test]
        public void RepeatedReplacementCompactsStaleRelationsAndKeepsOtherLiveEffects()
        {
            var s = new Setup(); var persistent = s.Apply(); s.Abilities.TryOwnEffect(s.Ability, persistent);
            for (int i = 0; i < 256; i++)
            {
                var next = s.Apply(); Assert.That(s.Abilities.TryOwnEffect(s.Ability, next), Is.True);
                Assert.That(OwnedCount(s), Is.EqualTo(2));
                s.Effects.Remove(next.ActiveHandle);
            }
            s.Abilities.End(s.Ability);
            Assert.That(s.Effects.IsActive(persistent.ActiveHandle), Is.False);
            Assert.That(OwnedCount(s), Is.Zero);
        }
    }
}
