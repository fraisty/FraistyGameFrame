using System;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Combat
{
    public sealed class DamagePipelineTests
    {
        private static readonly GameplaySourceId Source = new GameplaySourceId(1);
        private static readonly GameplayAttributeId Health = new GameplayAttributeId("Test.Health");
        private static readonly GameplayAttributeId Attack = new GameplayAttributeId("Test.Attack");
        private static readonly GameplayAttributeId Defense = new GameplayAttributeId("Test.Defense");

        [Test]
        public void EmptyPipeline_PreservesBaseDamageAndDoesNotChangeAttributes()
        {
            var sourceAttributes = new AttributeSet();
            sourceAttributes.Register(Attack, 22f);
            var targetAttributes = new AttributeSet();
            targetAttributes.Register(Health, 73f);
            var context = new DamageContext(Source, 18f, targetAttributes, sourceAttributes);

            DamageResult result = new DamagePipeline().Calculate(context);

            Assert.That(result.BaseDamage, Is.EqualTo(18f));
            Assert.That(result.FinalDamage, Is.EqualTo(18f));
            Assert.That(context.SourceId, Is.EqualTo(Source));
            Assert.That(context.SourceAttributes, Is.SameAs(sourceAttributes));
            Assert.That(context.TargetAttributes, Is.SameAs(targetAttributes));
            Assert.That(targetAttributes.GetCurrentValue(Health), Is.EqualTo(73f));
        }

        [Test]
        public void DefenseModifier_UsesCurrentNonNegativeDefenseValue()
        {
            var targetAttributes = new AttributeSet();
            targetAttributes.Register(Defense, 0f);
            targetAttributes.Register(Health, 73f);
            var context = new DamageContext(Source, 100f, targetAttributes);
            var pipeline = new DamagePipeline();
            pipeline.AddModifier(new DefenseDamageModifier(Defense));
            float[] defenses = { 0f, 100f, 300f, -20f };
            float[] expectedDamage = { 100f, 50f, 25f, 100f };

            for (int i = 0; i < defenses.Length; i++)
            {
                targetAttributes.SetBaseValue(Defense, defenses[i]);

                DamageResult result = pipeline.Calculate(context);

                Assert.That(result.FinalDamage, Is.EqualTo(expectedDamage[i]).Within(0.0001f));
            }

            // -20 基础防御 +120 Modifier =100，验证读取的是 CurrentValue。
            targetAttributes.GetAttribute(Defense).AddModifier(
                new GameplayAttributeModifier(GameplayModifierOperation.Add, 120f, Source));
            Assert.That(pipeline.Calculate(context).FinalDamage, Is.EqualTo(50f));
            Assert.That(targetAttributes.GetAttribute(Defense).BaseValue, Is.EqualTo(-20f));
            Assert.That(targetAttributes.GetCurrentValue(Health), Is.EqualTo(73f));
        }

        [Test]
        public void ModifierRegistrationOrder_DeterminesTheResult()
        {
            var context = new DamageContext(Source, 10f, new AttributeSet());
            var addThenDouble = new DamagePipeline();
            addThenDouble.AddModifier(new AddTenModifier());
            addThenDouble.AddModifier(new DoubleModifier());
            var doubleThenAdd = new DamagePipeline();
            doubleThenAdd.AddModifier(new DoubleModifier());
            doubleThenAdd.AddModifier(new AddTenModifier());

            DamageResult first = addThenDouble.Calculate(context);
            DamageResult second = doubleThenAdd.Calculate(context);

            Assert.That(first.BaseDamage, Is.EqualTo(10f));
            Assert.That(first.FinalDamage, Is.EqualTo(40f));
            Assert.That(second.BaseDamage, Is.EqualTo(10f));
            Assert.That(second.FinalDamage, Is.EqualTo(30f));
        }

        [Test]
        public void InvalidDamageInputAndModifierOutput_ThrowExplicitExceptions()
        {
            var targetAttributes = new AttributeSet();

            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new DamageContext(Source, -1f, targetAttributes));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new DamageContext(Source, float.NaN, targetAttributes));

            var pipeline = new DamagePipeline();
            pipeline.AddModifier(new NonFiniteModifier());
            var context = new DamageContext(Source, 10f, targetAttributes);

            Assert.Throws<InvalidOperationException>(() => pipeline.Calculate(context));
        }

        private sealed class AddTenModifier : IDamageModifier
        {
            public float Modify(DamageContext context, float currentDamage) => currentDamage + 10f;
        }

        private sealed class DoubleModifier : IDamageModifier
        {
            public float Modify(DamageContext context, float currentDamage) => currentDamage * 2f;
        }

        private sealed class NonFiniteModifier : IDamageModifier
        {
            public float Modify(DamageContext context, float currentDamage) => float.PositiveInfinity;
        }
    }
}
