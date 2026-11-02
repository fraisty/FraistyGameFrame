using System;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Skill
{
    public sealed class GameplaySkillOwnerEventCountTriggerTests
    {
        private static readonly GameplayTag Fired = new GameplayTag("Event.Test.Weapon.Fired");

        [Test]
        public void SharedDefinition_CountsOnlyOwnerResetsBeforeCallbackAndCarriesThresholdEvent()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new GameplaySkillOwnerEventCountTriggerDefinition(Fired, 0));
            var ids = new GameplaySourceIdAllocator();
            var events = new GameplayEventDispatcher();
            var ownerA = ids.Allocate();
            var ownerB = ids.Allocate();
            var trigger = new GameplaySkillOwnerEventCountTriggerDefinition(Fired, 5);
            var condition = new GateCondition { Open = false };
            var target = new CapturingSelfTarget();
            var action = new CapturingAction();
            var definition = new GameplaySkillDefinition(trigger, new[] { condition }, target,
                new[] { action }, once: false);
            using (var first = new GameplaySkillRuntime(definition, Host(ownerA, ids), events))
            using (var second = new GameplaySkillRuntime(definition, Host(ownerB, ids), events))
            {
                var a = (GameplaySkillOwnerEventCountTriggerDefinition.Binding)first.TriggerBinding;
                var b = (GameplaySkillOwnerEventCountTriggerDefinition.Binding)second.TriggerBinding;
                for (int i = 0; i < 4; i++) events.Publish(Event(ownerA, i));
                events.Publish(Event(ownerB, 10));
                Assert.That(a.CurrentCount, Is.EqualTo(4));
                Assert.That(b.CurrentCount, Is.EqualTo(1));
                Assert.That(first.ActivationCount, Is.Zero);
                events.Publish(Event(ownerA, 5));
                Assert.That(a.CurrentCount, Is.Zero, "The threshold is consumed even when a condition rejects execution.");
                Assert.That(first.ActivationCount, Is.Zero);

                condition.Open = true;
                for (int i = 0; i < 4; i++) events.Publish(Event(ownerA, i));
                Assert.That(first.ActivationCount, Is.Zero);
                var fifth = Event(ownerA, 55);
                events.Publish(fifth);
                Assert.That(a.CurrentCount, Is.Zero);
                Assert.That(first.ActivationCount, Is.EqualTo(1));
                Assert.That(second.ActivationCount, Is.Zero);
                Assert.That(target.LastEvent, Is.EqualTo(fifth));
                Assert.That(action.LastEvent, Is.EqualTo(fifth));
                Assert.That(action.LastContext.TargetData.PrimaryTargetId, Is.EqualTo(ownerA));

                for (int i = 0; i < 5; i++) events.Publish(Event(ownerA, i));
                Assert.That(first.ActivationCount, Is.EqualTo(2));
                Assert.That(a.CurrentCount, Is.Zero);
                first.Dispose();
                events.Publish(Event(ownerA, 99));
                Assert.That(a.CurrentCount, Is.Zero);
                for (int i = 0; i < 4; i++) events.Publish(Event(ownerB, i));
                Assert.That(second.ActivationCount, Is.EqualTo(1));
                Assert.That(b.CurrentCount, Is.Zero);
            }
        }

        private static IGameplaySkillHost Host(GameplaySourceId owner, GameplaySourceIdAllocator ids)
        {
            var tags = new GameplayTagContainer();
            var attributes = new AttributeSet();
            return new GameplaySkillOwnerHost(owner, tags, attributes,
                new GameplayEffectContainer(attributes, ids, tags));
        }

        private static GameplayEventData Event(GameplaySourceId owner, float magnitude) =>
            new GameplayEventData(Fired, owner, default, magnitude);

        private sealed class GateCondition : IGameplaySkillCondition
        {
            public bool Open;
            public bool Evaluate(IGameplaySkillHost host, GameplayEventData triggerEvent) => Open;
        }

        private sealed class CapturingSelfTarget : IGameplaySkillTarget
        {
            public GameplayEventData LastEvent;
            public GameplayTargetData Resolve(IGameplaySkillHost host, GameplayEventData triggerEvent)
            {
                LastEvent = triggerEvent;
                return new GameplayTargetData(host.OwnerId);
            }
        }

        private sealed class CapturingAction : IGameplaySkillAction
        {
            public GameplayEventData LastEvent;
            public GameplayContext LastContext;
            public GameplaySkillActionResult Execute(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host)
            {
                LastEvent = triggerEvent;
                LastContext = context;
                return GameplaySkillActionResult.Succeeded;
            }
        }
    }
}
