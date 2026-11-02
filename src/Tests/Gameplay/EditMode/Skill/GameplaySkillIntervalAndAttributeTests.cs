using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Skill
{
    public sealed class GameplaySkillIntervalAndAttributeTests
    {
        private static readonly GameplayTag Signal = new GameplayTag("Trigger.Skill.Interval.Test");
        private static readonly GameplayAttributeId Attack = new GameplayAttributeId("Attack.Test");

        [Test]
        public void AttributeCondition_ValidatesAndComparesCurrentValue()
        {
            Assert.Throws<ArgumentException>(() => new GameplaySkillAttributeCondition(default,
                GameplaySkillAttributeComparison.Less, 1f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameplaySkillAttributeCondition(Attack,
                GameplaySkillAttributeComparison.Less, float.NaN));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameplaySkillAttributeCondition(Attack,
                (GameplaySkillAttributeComparison)99, 1f));
            var ids = new GameplaySourceIdAllocator();
            var host = Host(ids, 10f);
            host.Attributes.GetAttribute(Attack).AddModifier(new GameplayAttributeModifier(
                GameplayModifierOperation.Add, 5f, ids.Allocate()));
            Assert.That(new GameplaySkillAttributeCondition(Attack, GameplaySkillAttributeComparison.Less, 15f)
                .Evaluate(host, default), Is.False);
            Assert.That(new GameplaySkillAttributeCondition(Attack, GameplaySkillAttributeComparison.LessOrEqual, 15f)
                .Evaluate(host, default), Is.True);
            Assert.That(new GameplaySkillAttributeCondition(Attack, GameplaySkillAttributeComparison.Greater, 10f)
                .Evaluate(host, default), Is.True);
            Assert.That(new GameplaySkillAttributeCondition(Attack, GameplaySkillAttributeComparison.GreaterOrEqual, 15f)
                .Evaluate(host, default), Is.True);
            Assert.Throws<KeyNotFoundException>(() => new GameplaySkillAttributeCondition(
                new GameplayAttributeId("Missing"), GameplaySkillAttributeComparison.Less, 1f)
                .Evaluate(host, default));
        }

        [Test]
        public void Interval_ConsumesRejectedThresholdPreservesOverflowAndSeparatesOwners()
        {
            Assert.Throws<ArgumentException>(() => new GameplaySkillIntervalTriggerDefinition(default, 6f));
            Assert.Throws<ArgumentOutOfRangeException>(() => new GameplaySkillIntervalTriggerDefinition(Signal, 0f));
            var ids = new GameplaySourceIdAllocator();
            var first = Host(ids, 10f);
            var second = Host(ids, 10f);
            var events = new GameplayEventDispatcher();
            var gate = new Gate { Open = false };
            var action = new Capture();
            var definition = new GameplaySkillDefinition(new GameplaySkillIntervalTriggerDefinition(Signal, 6f),
                new[] { gate }, new GameplaySkillSelfTarget(), new[] { action }, false);
            using (var a = new GameplaySkillRuntime(definition, first, events))
            using (var b = new GameplaySkillRuntime(definition, second, events))
            {
                var bindingA = (GameplaySkillIntervalTriggerDefinition.Binding)a.TriggerBinding;
                var bindingB = (GameplaySkillIntervalTriggerDefinition.Binding)b.TriggerBinding;
                a.Advance(5.9f);
                Assert.That(a.ActivationCount, Is.Zero);
                Assert.That(bindingB.Remaining, Is.EqualTo(6f));
                a.Advance(.1f);
                Assert.That(a.ActivationCount, Is.Zero);
                Assert.That(bindingA.Remaining, Is.EqualTo(6f).Within(.0001f));
                gate.Open = true;
                a.Advance(13f);
                Assert.That(a.ActivationCount, Is.EqualTo(2));
                Assert.That(bindingA.Remaining, Is.EqualTo(5f).Within(.0001f));
                Assert.That(action.Last.EventTag, Is.EqualTo(Signal));
                Assert.That(action.Last.InstigatorId, Is.EqualTo(first.OwnerId));
                Assert.That(action.Last.TargetId.IsValid, Is.False);
                Assert.That(action.Last.Magnitude, Is.EqualTo(6f));
                b.Advance(6f);
                Assert.That(b.ActivationCount, Is.EqualTo(1));
                Assert.That(action.Last.InstigatorId, Is.EqualTo(second.OwnerId));
                a.Dispose();
                a.Advance(6f);
                Assert.That(a.ActivationCount, Is.EqualTo(2));
                Assert.Throws<ArgumentOutOfRangeException>(() => b.Advance(float.NaN));
            }
        }

        private static GameplaySkillOwnerHost Host(GameplaySourceIdAllocator ids, float attack)
        {
            var attributes = new AttributeSet();
            attributes.Register(Attack, attack);
            var tags = new GameplayTagContainer();
            return new GameplaySkillOwnerHost(ids.Allocate(), tags, attributes,
                new GameplayEffectContainer(attributes, ids, tags));
        }

        private sealed class Gate : IGameplaySkillCondition
        {
            public bool Open;
            public bool Evaluate(IGameplaySkillHost host, GameplayEventData triggerEvent) => Open;
        }

        private sealed class Capture : IGameplaySkillAction
        {
            public GameplayEventData Last;
            public GameplaySkillActionResult Execute(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host)
            {
                Last = triggerEvent;
                return GameplaySkillActionResult.Succeeded;
            }
        }
    }
}
