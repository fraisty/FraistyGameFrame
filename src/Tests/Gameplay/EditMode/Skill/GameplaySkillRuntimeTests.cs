using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Skill
{
    public sealed class GameplaySkillRuntimeTests
    {
        private static readonly GameplayAttributeId Attack = new GameplayAttributeId("Test.Attack");
        private static readonly GameplayTag BattleStart = new GameplayTag("Event.Test.BattleStart");
        private static readonly GameplayTag Ready = new GameplayTag("State.Ready");

        [Test]
        public void RejectedEffectStopsLaterActionsAndConsumesTimerWithoutSuccessCue()
        {
            var ids = new GameplaySourceIdAllocator();
            var ownerId = ids.Allocate();
            var tags = new GameplayTagContainer();
            var attributes = new AttributeSet();
            attributes.Register(Attack, 10f);
            var effects = new GameplayEffectContainer(attributes, ids, tags);
            var blocked = new GameplayTag("State.Test.Blocked");
            var noBlocked = new GameplayTagQuery(Array.Empty<GameplayTag>(),
                Array.Empty<GameplayTag>(), new[] { blocked });
            var effect = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 5f, 0f,
                new[] { new GameplayEffectAttributeModifierDefinition(Attack, GameplayModifierOperation.Add, 2f) },
                Array.Empty<GameplayTag>(), Array.Empty<IGameplayEffectExecution>(), null, noBlocked);
            var later = new CountingAction();
            var definition = new GameplaySkillDefinition(
                new GameplaySkillIntervalTriggerDefinition(BattleStart, 3f),
                Array.Empty<IGameplaySkillCondition>(), new GameplaySkillSelfTarget(),
                new IGameplaySkillAction[] { new GameplaySkillApplyEffectAction(effect), later }, false);
            int cues = 0;
            var skillHost = new GameplaySkillOwnerHost(ownerId, tags, attributes, effects);
            using (var skill = new GameplaySkillRuntime(definition, skillHost,
                       new GameplayEventDispatcher(), _ => cues++))
            {
                var source = ids.Allocate();
                tags.AddTag(blocked, source);
                skill.Advance(3f);
                Assert.That(skill.ActivationCount, Is.Zero);
                Assert.That(cues, Is.Zero);
                Assert.That(later.Calls, Is.Zero);
                Assert.That(attributes.GetCurrentValue(Attack), Is.EqualTo(10f));
                tags.RemoveTag(blocked, source);
                skill.Advance(2f);
                Assert.That(skill.ActivationCount, Is.Zero, "Rejected timer interval was consumed.");
                skill.Advance(1f);
                Assert.That(skill.ActivationCount, Is.EqualTo(1));
                Assert.That(cues, Is.EqualTo(1));
                Assert.That(later.Calls, Is.EqualTo(1));
                Assert.That(attributes.GetCurrentValue(Attack), Is.EqualTo(12f));
            }
        }

        private sealed class CountingAction : IGameplaySkillAction
        {
            public int Calls;
            public GameplaySkillActionResult Execute(GameplayContext context,
                GameplayEventData triggerEvent, IGameplaySkillHost host)
            {
                Calls++;
                return GameplaySkillActionResult.Succeeded;
            }
        }

        [Test]
        public void SharedDefinition_ConditionsSelfOnceAndDisposeRemainPerOwner()
        {
            var boost = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 5f,
                new[] { new GameplayEffectAttributeModifierDefinition(Attack, GameplayModifierOperation.Multiply, 1.3f) });
            var conditions = new List<IGameplaySkillCondition>
                { new GameplaySkillTagCondition(new[] { Ready }, Array.Empty<GameplayTag>()) };
            var actions = new List<IGameplaySkillAction> { new GameplaySkillApplyEffectAction(boost) };
            var definition = new GameplaySkillDefinition(new GameplaySkillEventTriggerDefinition(BattleStart), conditions,
                new GameplaySkillSelfTarget(), actions, true);
            conditions.Clear();
            actions.Clear();
            Assert.That(definition.Conditions.Count, Is.EqualTo(1));
            Assert.That(definition.Actions.Count, Is.EqualTo(1));
            Assert.Throws<ArgumentException>(() => new GameplaySkillDefinition(new GameplaySkillEventTriggerDefinition(BattleStart),
                Array.Empty<IGameplaySkillCondition>(), new GameplaySkillSelfTarget(),
                Array.Empty<IGameplaySkillAction>(), true));
            var ids = new GameplaySourceIdAllocator();
            var events = new GameplayEventDispatcher();
            var firstAttributes = new AttributeSet();
            firstAttributes.Register(Attack, 10f);
            var secondAttributes = new AttributeSet();
            secondAttributes.Register(Attack, 20f);
            var firstTags = new GameplayTagContainer();
            var secondTags = new GameplayTagContainer();
            var firstId = ids.Allocate();
            var secondId = ids.Allocate();
            var firstEffects = new GameplayEffectContainer(firstAttributes, ids, firstTags);
            var secondEffects = new GameplayEffectContainer(secondAttributes, ids, secondTags);
            GameplayContext firstContext = null;
            GameplayContext secondContext = null;
            using (var first = new GameplaySkillRuntime(definition,
                       new GameplaySkillOwnerHost(firstId, firstTags, firstAttributes, firstEffects), events,
                       context => firstContext = context))
            using (var second = new GameplaySkillRuntime(definition,
                       new GameplaySkillOwnerHost(secondId, secondTags, secondAttributes, secondEffects), events,
                       context => secondContext = context))
            {
                firstTags.AddTag(Ready, ids.Allocate());
                events.Publish(new GameplayEventData(new GameplayTag("Event.Test.Unrelated"), default, default, 0f));
                Assert.That(first.ActivationCount, Is.Zero);
                events.Publish(new GameplayEventData(BattleStart, default, default, 0f));
                Assert.That(firstAttributes.GetCurrentValue(Attack), Is.EqualTo(13f).Within(.0001f));
                Assert.That(secondAttributes.GetCurrentValue(Attack), Is.EqualTo(20f));
                Assert.That(firstContext.TargetData.PrimaryTargetId, Is.EqualTo(firstId));
                Assert.That(first.ActivationCount, Is.EqualTo(1));
                secondTags.AddTag(Ready, ids.Allocate());
                events.Publish(new GameplayEventData(BattleStart, default, default, 0f));
                Assert.That(first.ActivationCount, Is.EqualTo(1));
                Assert.That(second.ActivationCount, Is.EqualTo(1));
                Assert.That(secondContext.EffectiveSourceId, Is.EqualTo(secondId));
                Assert.That(secondAttributes.GetCurrentValue(Attack), Is.EqualTo(26f).Within(.0001f));
                firstEffects.Advance(5f);
                Assert.That(firstAttributes.GetCurrentValue(Attack), Is.EqualTo(10f));
            }
            events.Publish(new GameplayEventData(BattleStart, default, default, 0f));
            Assert.That(firstAttributes.GetCurrentValue(Attack), Is.EqualTo(10f));
        }

        [Test]
        public void OwnerScopedTrigger_PreservesEventThroughTargetAndTypedHostActions()
        {
            var ids = new GameplaySourceIdAllocator();
            var events = new GameplayEventDispatcher();
            var ownerA = new ProbeHost(ids.Allocate(), ids);
            var ownerB = new ProbeHost(ids.Allocate(), ids);
            var targetA = ids.Allocate();
            var targetB = ids.Allocate();
            var trigger = new OwnerScopedTriggerDefinition(BattleStart);
            var target = new EventTarget();
            var actionA = new HostAction();
            var actionB = new HostAction();
            var definition = new GameplaySkillDefinition(trigger, Array.Empty<IGameplaySkillCondition>(), target,
                new IGameplaySkillAction[] { actionA, actionB }, true);
            GameplayContext successContext = null;
            using (var first = new GameplaySkillRuntime(definition, ownerA, events,
                       context => successContext = context))
            using (var second = new GameplaySkillRuntime(definition, ownerB, events))
            {
                var firstBinding = (OwnerScopedTriggerDefinition.Binding)first.TriggerBinding;
                var secondBinding = (OwnerScopedTriggerDefinition.Binding)second.TriggerBinding;
                Assert.That(firstBinding, Is.Not.SameAs(secondBinding));

                var firstEvent = new GameplayEventData(BattleStart, ownerA.OwnerId, targetA, 7f);
                events.Publish(firstEvent);
                Assert.That(firstBinding.AcceptedCount, Is.EqualTo(1));
                Assert.That(secondBinding.AcceptedCount, Is.Zero);
                Assert.That(first.ActivationCount, Is.EqualTo(1));
                Assert.That(second.ActivationCount, Is.Zero);
                Assert.That(target.LastEvent, Is.EqualTo(firstEvent));
                Assert.That(successContext.TargetData.PrimaryTargetId, Is.EqualTo(targetA));
                Assert.That(actionA.LastEvent, Is.EqualTo(firstEvent));
                Assert.That(actionA.LastContext, Is.SameAs(actionB.LastContext));
                Assert.That(actionA.LastContext, Is.SameAs(successContext));
                Assert.That(successContext.Values.GetRequiredValue(
                    GameplayContextValues.TriggerMagnitudeKey), Is.EqualTo(7f));
                Assert.That(ownerA.FireCount, Is.EqualTo(2));
                Assert.That(ownerB.FireCount, Is.Zero);

                events.Publish(new GameplayEventData(BattleStart, ownerB.OwnerId, targetB, 3f));
                Assert.That(firstBinding.AcceptedCount, Is.EqualTo(1));
                Assert.That(secondBinding.AcceptedCount, Is.EqualTo(1));
                Assert.That(second.ActivationCount, Is.EqualTo(1));
                Assert.That(ownerB.FireCount, Is.EqualTo(2));
                first.Dispose();
                events.Publish(firstEvent);
                Assert.That(firstBinding.AcceptedCount, Is.EqualTo(1));
            }
        }

        private interface IProbeActionHost
        {
            void Fire(GameplayContext context);
        }

        private sealed class ProbeHost : IGameplaySkillHost, IProbeActionHost
        {
            public GameplaySourceId OwnerId { get; }
            public GameplaySourceId TargetId => OwnerId;
            public GameplayTagContainer Tags { get; } = new GameplayTagContainer();
            public AttributeSet Attributes { get; } = new AttributeSet();
            public GameplayEffectContainer Effects { get; }
            public int FireCount { get; private set; }

            public ProbeHost(GameplaySourceId ownerId, GameplaySourceIdAllocator ids)
            {
                OwnerId = ownerId;
                Attributes.Register(Attack, 10f);
                Effects = new GameplayEffectContainer(Attributes, ids, Tags);
            }

            public void Fire(GameplayContext context)
            {
                Assert.That(context.InstigatorId, Is.EqualTo(OwnerId));
                FireCount++;
            }
        }

        private sealed class OwnerScopedTriggerDefinition : IGameplaySkillTriggerDefinition
        {
            private readonly GameplayTag _eventTag;

            public OwnerScopedTriggerDefinition(GameplayTag eventTag) => _eventTag = eventTag;

            public IGameplaySkillTriggerBinding Bind(IGameplaySkillHost host, GameplayEventDispatcher events,
                Action<GameplayEventData> onTriggered) => new Binding(host.OwnerId, _eventTag, events, onTriggered);

            public sealed class Binding : IGameplaySkillTriggerBinding
            {
                private readonly GameplayEventDispatcher _events;
                private readonly GameplayEventSubscriptionHandle _handle;
                private readonly GameplaySourceId _ownerId;
                private readonly Action<GameplayEventData> _onTriggered;
                public int AcceptedCount { get; private set; }

                public Binding(GameplaySourceId ownerId, GameplayTag eventTag, GameplayEventDispatcher events,
                    Action<GameplayEventData> onTriggered)
                {
                    _ownerId = ownerId;
                    _events = events;
                    _onTriggered = onTriggered;
                    _handle = events.Subscribe(eventTag, OnEvent);
                }

                private void OnEvent(GameplayEventData data)
                {
                    if (data.InstigatorId != _ownerId) return;
                    AcceptedCount++;
                    _onTriggered(data);
                }

                public void Dispose() => _events.Unsubscribe(_handle);
            }
        }

        private sealed class EventTarget : IGameplaySkillTarget
        {
            public GameplayEventData LastEvent { get; private set; }

            public GameplayTargetData Resolve(IGameplaySkillHost host, GameplayEventData triggerEvent)
            {
                LastEvent = triggerEvent;
                return new GameplayTargetData(triggerEvent.TargetId);
            }
        }

        private sealed class HostAction : IGameplaySkillAction
        {
            public GameplayContext LastContext { get; private set; }
            public GameplayEventData LastEvent { get; private set; }

            public GameplaySkillActionResult Execute(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host)
            {
                LastContext = context;
                LastEvent = triggerEvent;
                ((IProbeActionHost)host).Fire(context);
                return GameplaySkillActionResult.Succeeded;
            }
        }
    }
}
