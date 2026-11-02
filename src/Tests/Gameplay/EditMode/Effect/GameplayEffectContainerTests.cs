using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Effect
{
    public sealed class GameplayEffectContainerTests
    {
        private static readonly GameplayAttributeId Attack = new GameplayAttributeId("Test.Attack");
        private static readonly GameplayTag Invincible = new GameplayTag("State.Invincible");

        [Test]
        public void Duration_ExpiresAndRestoresAttribute()
        {
            var target = new AttributeSet();
            target.Register(Attack, 100f);
            var container = new GameplayEffectContainer(target, new GameplaySourceIdAllocator());
            var handle = container.Apply(Boost(GameplayEffectDurationPolicy.Duration));

            Assert.That(target.GetCurrentValue(Attack), Is.EqualTo(130f).Within(0.0001f));
            Assert.That(container.TryGetRemainingDuration(handle, out float remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(5f));
            container.Advance(4f);
            Assert.That(container.IsActive(handle), Is.True);
            Assert.That(container.TryGetRemainingDuration(handle, out remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(1f));
            container.Advance(1f);
            Assert.That(container.IsActive(handle), Is.False);
            Assert.That(container.TryGetRemainingDuration(handle, out remaining), Is.False);
            Assert.That(remaining, Is.Zero);
            Assert.That(target.GetCurrentValue(Attack), Is.EqualTo(100f));
            Assert.That(container.Remove(handle), Is.False);
        }

        [Test]
        public void Infinite_PersistsUntilRemovedByItsOwnContainer()
        {
            var target = new AttributeSet();
            target.Register(Attack, 100f);
            var allocator = new GameplaySourceIdAllocator();
            var container = new GameplayEffectContainer(target, allocator);
            var other = new GameplayEffectContainer(target, allocator);
            var handle = container.Apply(Boost(GameplayEffectDurationPolicy.Infinite));

            container.Advance(100000f);
            Assert.That(container.IsActive(handle), Is.True);
            Assert.That(container.TryGetRemainingDuration(handle, out float remaining), Is.False);
            Assert.That(remaining, Is.Zero);
            Assert.That(other.TryGetRemainingDuration(handle, out remaining), Is.False);
            Assert.That(remaining, Is.Zero);
            Assert.That(other.Remove(handle), Is.False);
            Assert.That(container.Remove(default(GameplayEffectHandle)), Is.False);
            Assert.That(target.GetCurrentValue(Attack), Is.EqualTo(130f).Within(0.0001f));
            Assert.That(container.Remove(handle), Is.True);
            Assert.That(target.GetCurrentValue(Attack), Is.EqualTo(100f));
            Assert.That(container.Remove(handle), Is.False);
        }

        [Test]
        public void SameDefinition_HasIndependentInstancesAndPreservesOtherSources()
        {
            var target = new AttributeSet();
            var attack = target.Register(Attack, 100f);
            var allocator = new GameplaySourceIdAllocator();
            attack.AddModifier(new GameplayAttributeModifier(GameplayModifierOperation.Add, 10f, allocator.Allocate()));
            var container = new GameplayEffectContainer(target, allocator);
            var definition = Boost(GameplayEffectDurationPolicy.Duration);
            var first = container.Apply(definition);
            container.Advance(2f);
            var second = container.Apply(definition);

            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(target.GetCurrentValue(Attack), Is.EqualTo(185.9f).Within(0.0001f));
            Assert.That(container.Remove(first), Is.True);
            Assert.That(container.IsActive(second), Is.True);
            Assert.That(target.GetCurrentValue(Attack), Is.EqualTo(143f).Within(0.0001f));
            container.Advance(3f);
            Assert.That(container.IsActive(second), Is.True);
            container.Advance(2f);
            Assert.That(target.GetCurrentValue(Attack), Is.EqualTo(110f));
        }

        [Test]
        public void FailedApply_RollsBackPartialModifiersAndAllowsLaterApplications()
        {
            var target = new AttributeSet();
            target.Register(Attack, 100f);
            var defense = new GameplayAttributeId("Test.Defense");
            target.Register(defense, 20f);
            var missing = new GameplayAttributeId("Test.Missing");
            var container = new GameplayEffectContainer(target, new GameplaySourceIdAllocator());
            var existing = container.Apply(Boost(GameplayEffectDurationPolicy.Infinite));
            var definition = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 5f,
                new[]
                {
                    new GameplayEffectAttributeModifierDefinition(Attack, GameplayModifierOperation.Add, 50f),
                    new GameplayEffectAttributeModifierDefinition(defense, GameplayModifierOperation.Multiply, 2f),
                    new GameplayEffectAttributeModifierDefinition(missing, GameplayModifierOperation.Add, 1f)
                });

            Assert.Throws<KeyNotFoundException>(() => container.Apply(definition));
            Assert.That(target.GetCurrentValue(Attack), Is.EqualTo(130f).Within(0.0001f));
            Assert.That(target.GetCurrentValue(defense), Is.EqualTo(20f));
            Assert.That(container.IsActive(existing), Is.True);
            var later = container.Apply(Boost(GameplayEffectDurationPolicy.Duration));
            container.Advance(5f);
            Assert.That(container.IsActive(later), Is.False);
            Assert.That(container.Remove(existing), Is.True);
            Assert.That(target.GetCurrentValue(Attack), Is.EqualTo(100f));
        }

        [Test]
        public void ModifierAndGrantedTag_TwoInstancesRemoveOneAndExpireTheOther()
        {
            var target = new AttributeSet();
            target.Register(Attack, 100f);
            var targetTags = new GameplayTagContainer();
            var container = new GameplayEffectContainer(target, new GameplaySourceIdAllocator(), targetTags);
            var modifiers = new List<GameplayEffectAttributeModifierDefinition>
            {
                new GameplayEffectAttributeModifierDefinition(Attack, GameplayModifierOperation.Multiply, 1.3f)
            };
            var grantedTags = new List<GameplayTag> { Invincible };
            var definition = new GameplayEffectDefinition(
                GameplayEffectDurationPolicy.Duration, 5f, modifiers, grantedTags);
            modifiers.Clear();
            grantedTags.Clear();

            var first = container.Apply(definition);
            var second = container.Apply(definition);

            Assert.That(target.GetCurrentValue(Attack), Is.EqualTo(169f).Within(0.0001f));
            Assert.That(targetTags.HasTagExact(Invincible), Is.True);
            Assert.That(container.Remove(first), Is.True);
            Assert.That(container.IsActive(second), Is.True);
            Assert.That(target.GetCurrentValue(Attack), Is.EqualTo(130f).Within(0.0001f));
            Assert.That(targetTags.HasTagExact(Invincible), Is.True);

            container.Advance(5f);

            Assert.That(container.IsActive(second), Is.False);
            Assert.That(target.GetCurrentValue(Attack), Is.EqualTo(100f));
            Assert.That(targetTags.HasTagExact(Invincible), Is.False);
        }

        [Test]
        public void FailedApply_RollsBackPartialAttributesAndDoesNotChangeExistingTags()
        {
            var target = new AttributeSet();
            target.Register(Attack, 100f);
            var missing = new GameplayAttributeId("Test.Missing");
            var preserved = new GameplayTag("State.Preserved");
            var transient = new GameplayTag("State.Transient");
            var allocator = new GameplaySourceIdAllocator();
            var targetTags = new GameplayTagContainer();
            targetTags.AddTag(preserved, allocator.Allocate());
            var definition = new GameplayEffectDefinition(
                GameplayEffectDurationPolicy.Duration,
                5f,
                new[]
                {
                    new GameplayEffectAttributeModifierDefinition(Attack, GameplayModifierOperation.Add, 50f),
                    new GameplayEffectAttributeModifierDefinition(missing, GameplayModifierOperation.Add, 1f)
                },
                new[] { preserved, transient });

            var withoutTags = new GameplayEffectContainer(target, allocator);
            Assert.Throws<InvalidOperationException>(() => withoutTags.Apply(definition));
            Assert.That(target.GetCurrentValue(Attack), Is.EqualTo(100f));

            var container = new GameplayEffectContainer(target, allocator, targetTags);
            Assert.Throws<KeyNotFoundException>(() => container.Apply(definition));

            Assert.That(target.GetCurrentValue(Attack), Is.EqualTo(100f));
            Assert.That(targetTags.HasTagExact(preserved), Is.True);
            Assert.That(targetTags.HasTagExact(transient), Is.False);
        }

        private static GameplayEffectDefinition Boost(GameplayEffectDurationPolicy policy)
        {
            return new GameplayEffectDefinition(policy, 5f, new[]
            {
                new GameplayEffectAttributeModifierDefinition(Attack, GameplayModifierOperation.Multiply, 1.3f)
            });
        }
    }
}
