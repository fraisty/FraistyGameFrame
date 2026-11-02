using System;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Attribute
{
    public sealed class GameplayAttributeTests
    {
        private static readonly GameplaySourceId SourceA = new GameplaySourceId(1);
        private static readonly GameplaySourceId SourceB = new GameplaySourceId(2);

        [Test]
        public void ConstructorAndSetBaseValue_KeepCurrentValueInSync()
        {
            var attribute = new GameplayAttribute(12f);

            Assert.That(attribute.BaseValue, Is.EqualTo(12f));
            Assert.That(attribute.CurrentValue, Is.EqualTo(12f));

            attribute.SetBaseValue(-4f);

            Assert.That(attribute.BaseValue, Is.EqualTo(-4f));
            Assert.That(attribute.CurrentValue, Is.EqualTo(-4f));
        }

        [Test]
        public void AddModifiers_AreSummed()
        {
            var attribute = new GameplayAttribute(10f);
            attribute.AddModifier(Add(3f, SourceA));
            attribute.AddModifier(Add(-1f, SourceB));

            Assert.That(attribute.CurrentValue, Is.EqualTo(12f));
        }

        [Test]
        public void MultiplyModifiers_AreMultiplied()
        {
            var attribute = new GameplayAttribute(10f);
            attribute.AddModifier(Multiply(1.5f, SourceA));
            attribute.AddModifier(Multiply(0.8f, SourceB));

            Assert.That(attribute.CurrentValue, Is.EqualTo(12f).Within(0.0001f));
        }

        [Test]
        public void AddAndMultiply_UseTheFixedAggregationFormula()
        {
            var attribute = new GameplayAttribute(10f);
            attribute.AddModifier(Multiply(2f, SourceA));
            attribute.AddModifier(Add(5f, SourceB));

            Assert.That(attribute.CurrentValue, Is.EqualTo(30f));
        }

        [Test]
        public void SetBaseValue_RecalculatesWithExistingAddAndMultiplyModifiers()
        {
            var attribute = new GameplayAttribute(10f);
            attribute.AddModifier(Add(5f, SourceA));
            attribute.AddModifier(Multiply(2f, SourceB));

            attribute.SetBaseValue(20f);

            Assert.That(attribute.BaseValue, Is.EqualTo(20f));
            Assert.That(attribute.CurrentValue, Is.EqualTo(50f));
        }

        [Test]
        public void EquivalentModifiers_ProduceTheSameResultRegardlessOfInsertionOrder()
        {
            var first = new GameplayAttribute(10f);
            first.AddModifier(Add(16777216f, SourceA));
            first.AddModifier(Add(1f, SourceA));
            first.AddModifier(Add(-16777216f, SourceA));
            first.AddModifier(Multiply(1.25f, SourceB));
            first.AddModifier(Multiply(0.8f, SourceB));

            var second = new GameplayAttribute(10f);
            second.AddModifier(Multiply(0.8f, SourceB));
            second.AddModifier(Add(-16777216f, SourceA));
            second.AddModifier(Add(16777216f, SourceA));
            second.AddModifier(Multiply(1.25f, SourceB));
            second.AddModifier(Add(1f, SourceA));

            Assert.That(first.CurrentValue, Is.EqualTo(11f));
            Assert.That(second.CurrentValue, Is.EqualTo(first.CurrentValue));
        }

        [Test]
        public void RemoveModifier_RemovesOnlyTheOwnedLiveHandle()
        {
            var first = new GameplayAttribute(10f);
            GameplayModifierHandle firstHandle = first.AddModifier(Add(3f, SourceA));
            GameplayModifierHandle retainedHandle = first.AddModifier(Add(7f, SourceB));
            var other = new GameplayAttribute(20f);
            GameplayModifierHandle foreignHandle = other.AddModifier(Add(5f, SourceA));

            Assert.That(first.RemoveModifier(default), Is.False);
            Assert.That(first.RemoveModifier(foreignHandle), Is.False);
            Assert.That(first.RemoveModifier(firstHandle), Is.True);
            Assert.That(first.RemoveModifier(firstHandle), Is.False);
            Assert.That(first.CurrentValue, Is.EqualTo(17f));
            Assert.That(other.CurrentValue, Is.EqualTo(25f));
            Assert.That(retainedHandle.IsValid, Is.True);
        }

        [Test]
        public void RepeatedModifierAdds_ReturnDistinctHandlesAndRemovedHandlesAreNotReused()
        {
            var attribute = new GameplayAttribute(1f);
            GameplayAttributeModifier modifier = Add(2f, SourceA);

            GameplayModifierHandle first = attribute.AddModifier(modifier);
            GameplayModifierHandle second = attribute.AddModifier(modifier);
            Assert.That(attribute.CurrentValue, Is.EqualTo(5f));

            Assert.That(attribute.RemoveModifier(first), Is.True);
            GameplayModifierHandle third = attribute.AddModifier(modifier);

            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(third, Is.Not.EqualTo(first));
            Assert.That(third, Is.Not.EqualTo(second));
            Assert.That(first != second, Is.True);
            Assert.That(third == default, Is.False);
            Assert.That(attribute.CurrentValue, Is.EqualTo(5f));
        }

        [Test]
        public void RemoveModifiersFromSource_RemovesOnlyMatchingModifiers()
        {
            var attribute = new GameplayAttribute(10f);
            attribute.AddModifier(Add(2f, SourceA));
            attribute.AddModifier(Multiply(3f, SourceA));
            attribute.AddModifier(Add(5f, SourceB));

            int removed = attribute.RemoveModifiersFromSource(SourceA);

            Assert.That(removed, Is.EqualTo(2));
            Assert.That(attribute.CurrentValue, Is.EqualTo(15f));
            Assert.That(attribute.RemoveModifiersFromSource(SourceA), Is.Zero);
            Assert.Throws<ArgumentException>(() => attribute.RemoveModifiersFromSource(default));
        }

        [TestCase(0f)]
        [TestCase(-2f)]
        public void Multiply_AllowsZeroAndNegativeMagnitudes(float magnitude)
        {
            var attribute = new GameplayAttribute(-3f);
            attribute.AddModifier(Multiply(magnitude, SourceA));

            Assert.That(attribute.CurrentValue, Is.EqualTo(-3f * magnitude));
        }

        [Test]
        public void ValueObjects_ExposeValidityAndOrdinalIdentity()
        {
            var upper = new GameplayAttributeId("Test.Attack");
            var same = new GameplayAttributeId("Test.Attack");
            var lower = new GameplayAttributeId("test.attack");
            var negativeSource = new GameplaySourceId(-7);

            Assert.That(upper.Value, Is.EqualTo("Test.Attack"));
            Assert.That(upper.IsValid, Is.True);
            Assert.That(default(GameplayAttributeId).IsValid, Is.False);
            Assert.That(upper.Equals(same), Is.True);
            Assert.That(upper == same, Is.True);
            Assert.That(upper != lower, Is.True);
            Assert.That(upper.GetHashCode(), Is.EqualTo(same.GetHashCode()));

            Assert.That(negativeSource.Value, Is.EqualTo(-7));
            Assert.That(negativeSource.IsValid, Is.True);
            Assert.That(default(GameplaySourceId).IsValid, Is.False);
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameplaySourceId(0));
        }

        [TestCase(null)]
        [TestCase("")]
        [TestCase("   ")]
        public void AttributeId_RejectsMissingValues(string value)
        {
            Assert.Throws<ArgumentException>(() => new GameplayAttributeId(value));
        }

        [Test]
        public void Modifier_ExposesItsValuesAndRejectsInvalidConstruction()
        {
            var modifier = new GameplayAttributeModifier(GameplayModifierOperation.Multiply, -2f, SourceA);

            Assert.That(modifier.Operation, Is.EqualTo(GameplayModifierOperation.Multiply));
            Assert.That(modifier.Magnitude, Is.EqualTo(-2f));
            Assert.That(modifier.SourceId.Value, Is.EqualTo(SourceA.Value));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GameplayAttributeModifier((GameplayModifierOperation)99, 1f, SourceA));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GameplayAttributeModifier(GameplayModifierOperation.Add, float.NaN, SourceA));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GameplayAttributeModifier(GameplayModifierOperation.Add, float.PositiveInfinity, SourceA));
            Assert.Throws<ArgumentException>(() =>
                new GameplayAttributeModifier(GameplayModifierOperation.Add, 1f, default));
        }

        [Test]
        public void Attribute_RejectsNonFiniteValuesAndDefaultModifier()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameplayAttribute(float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameplayAttribute(float.NegativeInfinity));

            var attribute = new GameplayAttribute(1f);

            Assert.Throws<ArgumentOutOfRangeException>(() => attribute.SetBaseValue(float.PositiveInfinity));
            Assert.Throws<ArgumentException>(() => attribute.AddModifier(default));
            Assert.That(attribute.BaseValue, Is.EqualTo(1f));
            Assert.That(attribute.CurrentValue, Is.EqualTo(1f));
        }

        [Test]
        public void AddModifier_WhenResultOverflows_IsTransactional()
        {
            var attribute = new GameplayAttribute(float.MaxValue);

            Assert.Throws<OverflowException>(() => attribute.AddModifier(Add(float.MaxValue, SourceA)));

            Assert.That(attribute.BaseValue, Is.EqualTo(float.MaxValue));
            Assert.That(attribute.CurrentValue, Is.EqualTo(float.MaxValue));

            GameplayModifierHandle handle = attribute.AddModifier(Add(-float.MaxValue, SourceA));
            Assert.That(handle.IsValid, Is.True);
            Assert.That(attribute.CurrentValue, Is.Zero);
        }

        [Test]
        public void SetBaseValue_WhenResultOverflows_IsTransactional()
        {
            var attribute = new GameplayAttribute(1f);
            attribute.AddModifier(Multiply(float.MaxValue, SourceA));

            Assert.Throws<OverflowException>(() => attribute.SetBaseValue(2f));

            Assert.That(attribute.BaseValue, Is.EqualTo(1f));
            Assert.That(attribute.CurrentValue, Is.EqualTo(float.MaxValue));
        }

        [Test]
        public void RemoveModifier_WhenResultOverflows_IsTransactional()
        {
            var attribute = new GameplayAttribute(float.MaxValue);
            GameplayModifierHandle additive = attribute.AddModifier(Add(-float.MaxValue / 2f, SourceA));
            GameplayModifierHandle multiplier = attribute.AddModifier(Multiply(2f, SourceB));
            float before = attribute.CurrentValue;

            Assert.Throws<OverflowException>(() => attribute.RemoveModifier(additive));
            Assert.That(attribute.CurrentValue, Is.EqualTo(before));

            Assert.That(attribute.RemoveModifier(multiplier), Is.True);
            Assert.That(attribute.CurrentValue, Is.EqualTo(float.MaxValue / 2f));
            Assert.That(attribute.RemoveModifier(additive), Is.True);
            Assert.That(attribute.CurrentValue, Is.EqualTo(float.MaxValue));
        }

        [Test]
        public void RemoveModifiersFromSource_WhenResultOverflows_IsTransactional()
        {
            var attribute = new GameplayAttribute(float.MaxValue);
            attribute.AddModifier(Add(-float.MaxValue / 2f, SourceA));
            GameplayModifierHandle multiplier = attribute.AddModifier(Multiply(2f, SourceB));
            float before = attribute.CurrentValue;

            Assert.Throws<OverflowException>(() => attribute.RemoveModifiersFromSource(SourceA));
            Assert.That(attribute.CurrentValue, Is.EqualTo(before));

            Assert.That(attribute.RemoveModifier(multiplier), Is.True);
            Assert.That(attribute.RemoveModifiersFromSource(SourceA), Is.EqualTo(1));
            Assert.That(attribute.CurrentValue, Is.EqualTo(float.MaxValue));
        }

        private static GameplayAttributeModifier Add(float magnitude, GameplaySourceId source)
        {
            return new GameplayAttributeModifier(GameplayModifierOperation.Add, magnitude, source);
        }

        private static GameplayAttributeModifier Multiply(float magnitude, GameplaySourceId source)
        {
            return new GameplayAttributeModifier(GameplayModifierOperation.Multiply, magnitude, source);
        }
    }
}
