using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Effect
{
    public sealed class GameplayFloatValueTests
    {
        private static readonly GameplayAttributeId Attack = new GameplayAttributeId("Test.Value.Attack");
        private static readonly GameplayTag CallerKey = new GameplayTag("Data.Value.Caller");
        private static readonly GameplayTag ContextKey = new GameplayTag("Data.Value.Context");

        [Test]
        public void TypedValuesUseImmutableInputsFiniteMathAndCustomCalculation()
        {
            var ids = new GameplaySourceIdAllocator();
            var source = new Target(ids.Allocate(), 20f);
            var target = new Target(ids.Allocate(), 8f);
            var input = new[] { new KeyValuePair<GameplayTag, float>(ContextKey, 3f) };
            var values = new GameplayContextValues(input);
            input[0] = new KeyValuePair<GameplayTag, float>(ContextKey, 99f);
            var context = new GameplayContext(source.TargetId,
                new GameplayTargetData(target.TargetId), ids.Allocate(), values);
            var application = new GameplayEffectApplicationContext(context, target,
                new GameplaySetByCallerValues(new[] { new KeyValuePair<GameplayTag, float>(CallerKey, 4f) }),
                source.Attributes, source.TargetId, 3);
            Assert.That(context.Values.GetRequiredValue(ContextKey), Is.EqualTo(3f));
            Assert.That(GameplayValueEvaluator.Evaluate(new GameplayConstantFloatValue(2f)), Is.EqualTo(2f));
            Assert.That(GameplayValueEvaluator.Evaluate(new GameplaySetByCallerFloatValue(CallerKey), application), Is.EqualTo(4f));
            Assert.That(GameplayValueEvaluator.Evaluate(new GameplayContextFloatValue(ContextKey), application), Is.EqualTo(3f));
            Assert.That(GameplayValueEvaluator.Evaluate(new GameplayAttributeFloatValue(
                GameplayValueAttributeSource.Instigator, Attack), application), Is.EqualTo(20f));
            Assert.That(GameplayValueEvaluator.Evaluate(new GameplayAttributeFloatValue(
                GameplayValueAttributeSource.Target, Attack), application), Is.EqualTo(8f));
            Assert.That(GameplayValueEvaluator.Evaluate(new GameplayStackCountFloatValue(), application), Is.EqualTo(3f));
            Assert.That(GameplayValueEvaluator.Evaluate(new GameplayCustomFloatValue(new QuarterSum()), application), Is.EqualTo(7f));
            Assert.That(Eval(GameplayBinaryFloatOperation.Add, 8f, 2f), Is.EqualTo(10f));
            Assert.That(Eval(GameplayBinaryFloatOperation.Subtract, 8f, 2f), Is.EqualTo(6f));
            Assert.That(Eval(GameplayBinaryFloatOperation.Multiply, 8f, 2f), Is.EqualTo(16f));
            Assert.That(Eval(GameplayBinaryFloatOperation.Divide, 8f, 2f), Is.EqualTo(4f));
            Assert.That(Eval(GameplayBinaryFloatOperation.Min, 8f, 2f), Is.EqualTo(2f));
            Assert.That(Eval(GameplayBinaryFloatOperation.Max, 8f, 2f), Is.EqualTo(8f));
            Assert.Throws<DivideByZeroException>(() => Eval(GameplayBinaryFloatOperation.Divide, 8f, 0f));
            Assert.Throws<ArithmeticException>(() => GameplayValueEvaluator.Evaluate(
                new GameplayCustomFloatValue(new NonFinite()), application));
            Assert.Throws<KeyNotFoundException>(() => GameplayValueEvaluator.Evaluate(
                new GameplaySetByCallerFloatValue(new GameplayTag("Data.Missing")), application));
            Assert.Throws<KeyNotFoundException>(() => GameplayValueEvaluator.Evaluate(
                new GameplayContextFloatValue(new GameplayTag("Data.Missing")), application));
            Assert.Throws<InvalidOperationException>(() => GameplayValueEvaluator.Evaluate(
                new GameplayAttributeFloatValue(GameplayValueAttributeSource.Instigator, Attack),
                new GameplayEffectApplicationContext(new GameplayContext(source.TargetId,
                    new GameplayTargetData(target.TargetId)), target, GameplaySetByCallerValues.Empty)));
            Assert.Throws<ArgumentException>(() => new GameplayEffectApplicationContext(context, target,
                GameplaySetByCallerValues.Empty, source.Attributes, target.TargetId));
        }

        [Test]
        public void DynamicModifierSnapshotsAtApplyAndEachNewStackLayer()
        {
            var ids = new GameplaySourceIdAllocator();
            var target = new Target(ids.Allocate(), 10f);
            var effects = new GameplayEffectContainer(target.Attributes, ids, target.Tags);
            var value = new GameplayBinaryFloatValue(GameplayBinaryFloatOperation.Multiply,
                new GameplayAttributeFloatValue(GameplayValueAttributeSource.Instigator, Attack),
                new GameplayConstantFloatValue(.3f));
            var stacking = new GameplayEffectStackingDefinition(new GameplayTag("Effect.Test.ValueStack"),
                GameplayEffectStackAggregation.AggregateByTarget, 3,
                GameplayEffectStackDurationRefreshPolicy.NeverRefresh,
                GameplayEffectStackPeriodResetPolicy.NeverReset,
                GameplayEffectStackOverflowPolicy.Reject);
            var definition = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 5f, 0f,
                new[] { new GameplayEffectAttributeModifierDefinition(Attack, GameplayModifierOperation.Add, value) },
                Array.Empty<GameplayTag>(), Array.Empty<IGameplayEffectExecution>(), stacking);
            var unstacked = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 5f,
                new[] { new GameplayEffectAttributeModifierDefinition(Attack, GameplayModifierOperation.Add, value) });
            Assert.Throws<InvalidOperationException>(() => effects.Apply(unstacked));
            Assert.That(ids.Allocate().Value, Is.EqualTo(2));
            var first = effects.Apply(definition, Application(target));
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(13f));
            target.Attributes.GetAttribute(Attack).SetBaseValue(20f);
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(23f), "Existing contribution is fixed.");
            effects.Apply(definition, Application(target));
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(29.9f).Within(.0001f));
            Assert.That(effects.TryGetStackCount(first.ActiveHandle, out int count), Is.True);
            Assert.That(count, Is.EqualTo(2));
            effects.Remove(first.ActiveHandle);
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(20f));
            var constant = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 1f,
                new[] { new GameplayEffectAttributeModifierDefinition(Attack, GameplayModifierOperation.Add, 2f) });
            var handle = effects.Apply(constant);
            Assert.That(handle.IsValid, Is.True);
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(22f));
            var countStack = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 5f, 0f,
                new[] { new GameplayEffectAttributeModifierDefinition(Attack, GameplayModifierOperation.Add,
                    new GameplayStackCountFloatValue()) }, Array.Empty<GameplayTag>(),
                Array.Empty<IGameplayEffectExecution>(), stacking);
            for (int i = 0; i < 3; i++) effects.Apply(countStack, Application(target));
            Assert.That(target.Attributes.GetCurrentValue(Attack), Is.EqualTo(28f),
                "New layers capture resulting stack counts 1+2+3.");
        }

        [Test]
        public void PeriodicAttributeIsLiveAndStackCountFormulaExecutesOncePerPeriod()
        {
            var ids = new GameplaySourceIdAllocator();
            var target = new Target(ids.Allocate(), 10f);
            var effects = new GameplayEffectContainer(target.Attributes, ids, target.Tags);
            var execution = new CaptureExecution(new GameplayBinaryFloatValue(
                GameplayBinaryFloatOperation.Multiply, new GameplayConstantFloatValue(2f),
                new GameplayStackCountFloatValue()));
            var stacking = new GameplayEffectStackingDefinition(new GameplayTag("Effect.Test.PeriodValue"),
                GameplayEffectStackAggregation.AggregateByTarget, 3,
                GameplayEffectStackDurationRefreshPolicy.NeverRefresh,
                GameplayEffectStackPeriodResetPolicy.NeverReset,
                GameplayEffectStackOverflowPolicy.Reject);
            var definition = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 5f, 1f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), Array.Empty<GameplayTag>(),
                new IGameplayEffectExecution[] { execution }, stacking);
            for (int i = 0; i < 3; i++) effects.Apply(definition, Application(target));
            effects.Advance(1f);
            Assert.That(execution.Calls, Is.EqualTo(1));
            Assert.That(execution.Last, Is.EqualTo(6f));
            var live = new CaptureExecution(new GameplayAttributeFloatValue(
                GameplayValueAttributeSource.Target, Attack));
            var liveEffect = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 3f, 1f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), Array.Empty<GameplayTag>(),
                new IGameplayEffectExecution[] { live });
            effects.Apply(liveEffect, Application(target));
            effects.Advance(1f);
            Assert.That(live.Last, Is.EqualTo(10f));
            target.Attributes.GetAttribute(Attack).SetBaseValue(17f);
            effects.Advance(1f);
            Assert.That(live.Last, Is.EqualTo(17f));

            var instant = new CaptureExecution(new GameplayStackCountFloatValue());
            var instantEffect = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Instant, 0f, 0f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), Array.Empty<GameplayTag>(),
                new IGameplayEffectExecution[] { instant });
            effects.Apply(instantEffect, Application(target, 7));
            Assert.That(instant.Last, Is.EqualTo(1f),
                "EffectContainer owns non-stack StackCount semantics.");
        }

        private static float Eval(GameplayBinaryFloatOperation op, float left, float right) =>
            GameplayValueEvaluator.Evaluate(new GameplayBinaryFloatValue(op,
                new GameplayConstantFloatValue(left), new GameplayConstantFloatValue(right)));

        private static GameplayEffectApplicationContext Application(Target target, int stackCount = 1) =>
            new GameplayEffectApplicationContext(new GameplayContext(target.TargetId,
                new GameplayTargetData(target.TargetId)), target, GameplaySetByCallerValues.Empty,
                stackCount: stackCount);

        private sealed class Target : IGameplayEffectExecutionTarget
        {
            public GameplaySourceId TargetId { get; }
            public AttributeSet Attributes { get; } = new AttributeSet();
            public GameplayTagContainer Tags { get; } = new GameplayTagContainer();
            public Target(GameplaySourceId id, float attack)
            { TargetId = id; Attributes.Register(Attack, attack); }
        }
        private sealed class CaptureExecution : IGameplayEffectExecution
        {
            private readonly IGameplayFloatValue _value;
            public int Calls;
            public float Last;
            public CaptureExecution(IGameplayFloatValue value) { _value = value; }
            public void Execute(GameplayEffectApplicationContext application)
            { Calls++; Last = GameplayValueEvaluator.Evaluate(_value, application); }
        }
        private sealed class QuarterSum : IGameplayCustomFloatCalculation
        {
            public float Calculate(GameplayValueEvaluationContext context) =>
                (context.Application.InstigatorAttributes.GetCurrentValue(Attack) +
                 context.Application.Target.Attributes.GetCurrentValue(Attack)) * .25f;
        }
        private sealed class NonFinite : IGameplayCustomFloatCalculation
        { public float Calculate(GameplayValueEvaluationContext context) => float.PositiveInfinity; }
    }
}
