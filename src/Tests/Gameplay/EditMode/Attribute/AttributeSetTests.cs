using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Attribute
{
    public sealed class AttributeSetTests
    {
        private static readonly GameplayAttributeId Attack = new GameplayAttributeId("Test.Attack");
        private static readonly GameplayAttributeId Defense = new GameplayAttributeId("Test.Defense");
        private static readonly GameplayAttributeId Unknown = new GameplayAttributeId("Test.Unknown");

        [Test]
        public void Register_QueryAndSetBaseValue_UseTheRegisteredAttribute()
        {
            var set = new AttributeSet();
            GameplayAttribute registered = set.Register(Attack, 10f);

            Assert.That(set.Contains(Attack), Is.True);
            Assert.That(set.Contains(Unknown), Is.False);
            Assert.That(set.GetAttribute(Attack), Is.SameAs(registered));
            Assert.That(set.GetCurrentValue(Attack), Is.EqualTo(10f));

            set.SetBaseValue(Attack, -5f);

            Assert.That(registered.BaseValue, Is.EqualTo(-5f));
            Assert.That(set.GetCurrentValue(Attack), Is.EqualTo(-5f));
        }

        [Test]
        public void Register_DuplicateIdThrowsWithoutOverwritingTheOriginal()
        {
            var set = new AttributeSet();
            GameplayAttribute original = set.Register(Attack, 10f);

            Assert.Throws<ArgumentException>(() => set.Register(Attack, 99f));

            Assert.That(set.GetAttribute(Attack), Is.SameAs(original));
            Assert.That(set.GetCurrentValue(Attack), Is.EqualTo(10f));
        }

        [Test]
        public void UnknownValidId_HasExplicitQueryAndMutationBehavior()
        {
            var set = new AttributeSet();

            Assert.That(set.Contains(Unknown), Is.False);
            Assert.Throws<KeyNotFoundException>(() => set.GetAttribute(Unknown));
            Assert.Throws<KeyNotFoundException>(() => set.GetCurrentValue(Unknown));
            Assert.Throws<KeyNotFoundException>(() => set.SetBaseValue(Unknown, 1f));
        }

        [Test]
        public void InvalidId_IsRejectedByEveryAttributeSetEntryPoint()
        {
            var set = new AttributeSet();
            GameplayAttributeId invalid = default;

            Assert.Throws<ArgumentException>(() => set.Register(invalid, 1f));
            Assert.Throws<ArgumentException>(() => set.Contains(invalid));
            Assert.Throws<ArgumentException>(() => set.GetAttribute(invalid));
            Assert.Throws<ArgumentException>(() => set.GetCurrentValue(invalid));
            Assert.Throws<ArgumentException>(() => set.SetBaseValue(invalid, 1f));
        }

        [Test]
        public void RegisterAndSetBaseValue_RejectNonFiniteValuesTransactionally()
        {
            var set = new AttributeSet();

            Assert.Throws<ArgumentOutOfRangeException>(() => set.Register(Attack, float.NaN));
            Assert.That(set.Contains(Attack), Is.False);

            set.Register(Attack, 4f);
            Assert.Throws<ArgumentOutOfRangeException>(() => set.SetBaseValue(Attack, float.NegativeInfinity));

            Assert.That(set.GetCurrentValue(Attack), Is.EqualTo(4f));
        }

        [Test]
        public void RemoveModifiersFromSource_RemovesMatchesAcrossAllAttributes()
        {
            var set = new AttributeSet();
            GameplayAttribute attack = set.Register(Attack, 10f);
            GameplayAttribute defense = set.Register(Defense, 20f);
            var removedSource = new GameplaySourceId(10);
            var retainedSource = new GameplaySourceId(20);

            attack.AddModifier(new GameplayAttributeModifier(GameplayModifierOperation.Add, 2f, removedSource));
            attack.AddModifier(new GameplayAttributeModifier(GameplayModifierOperation.Multiply, 2f, retainedSource));
            defense.AddModifier(new GameplayAttributeModifier(GameplayModifierOperation.Add, 3f, removedSource));
            defense.AddModifier(new GameplayAttributeModifier(GameplayModifierOperation.Multiply, 0.5f, removedSource));

            int removed = set.RemoveModifiersFromSource(removedSource);

            Assert.That(removed, Is.EqualTo(3));
            Assert.That(set.GetCurrentValue(Attack), Is.EqualTo(20f));
            Assert.That(set.GetCurrentValue(Defense), Is.EqualTo(20f));
            Assert.Throws<ArgumentException>(() => set.RemoveModifiersFromSource(default));
        }

        [Test]
        public void RemoveModifiersFromSource_WhenOneAttributeOverflows_LeavesEveryAttributeUnchanged()
        {
            var set = new AttributeSet();
            var removedSource = new GameplaySourceId(10);
            var retainedSource = new GameplaySourceId(20);

            GameplayAttribute normal = set.Register(Attack, 10f);
            normal.AddModifier(new GameplayAttributeModifier(
                GameplayModifierOperation.Add, 2f, removedSource));

            GameplayAttribute overflowing = set.Register(Defense, float.MaxValue);
            overflowing.AddModifier(new GameplayAttributeModifier(
                GameplayModifierOperation.Add, -float.MaxValue / 2f, removedSource));
            GameplayModifierHandle multiplier = overflowing.AddModifier(new GameplayAttributeModifier(
                GameplayModifierOperation.Multiply, 2f, retainedSource));
            float overflowingValueBefore = overflowing.CurrentValue;

            Assert.Throws<OverflowException>(() => set.RemoveModifiersFromSource(removedSource));
            Assert.That(normal.CurrentValue, Is.EqualTo(12f));
            Assert.That(overflowing.CurrentValue, Is.EqualTo(overflowingValueBefore));

            Assert.That(overflowing.RemoveModifier(multiplier), Is.True);
            Assert.That(set.RemoveModifiersFromSource(removedSource), Is.EqualTo(2));
            Assert.That(normal.CurrentValue, Is.EqualTo(10f));
            Assert.That(overflowing.CurrentValue, Is.EqualTo(float.MaxValue));
        }
    }
}
