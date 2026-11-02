using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Event
{
    public sealed class GameplayEventDispatcherTests
    {
        private static readonly GameplayTag Damage = new GameplayTag("Event.Combat.Damage");
        private static readonly GameplayTag DamageApplied = new GameplayTag("Event.Combat.Damage.Applied");

        [Test]
        public void HierarchyAndRegistrationOrder_DeliverParentForChildButNotChildForParent()
        {
            var dispatcher = new GameplayEventDispatcher();
            var calls = new List<string>();
            dispatcher.Subscribe(Damage, _ => calls.Add("parent-first"));
            dispatcher.Subscribe(DamageApplied, _ => calls.Add("child"));
            dispatcher.Subscribe(Damage, _ => calls.Add("parent-last"));

            dispatcher.Publish(Data(DamageApplied));
            CollectionAssert.AreEqual(
                new[] { "parent-first", "child", "parent-last" },
                calls);

            calls.Clear();
            dispatcher.Publish(Data(Damage));
            CollectionAssert.AreEqual(new[] { "parent-first", "parent-last" }, calls);
        }

        [Test]
        public void PublishSnapshot_DefersAdditionAndStillInvokesRemovedLaterCallback()
        {
            var dispatcher = new GameplayEventDispatcher();
            var calls = new List<string>();
            GameplayEventSubscriptionHandle first = default;
            GameplayEventSubscriptionHandle later = default;
            GameplayEventSubscriptionHandle added = default;

            first = dispatcher.Subscribe(Damage, _ =>
            {
                calls.Add("first");
                dispatcher.Unsubscribe(first);
                dispatcher.Unsubscribe(later);
                if (!added.IsValid)
                    added = dispatcher.Subscribe(Damage, __ => calls.Add("added"));
            });
            later = dispatcher.Subscribe(Damage, _ => calls.Add("later"));

            dispatcher.Publish(Data(DamageApplied));
            CollectionAssert.AreEqual(new[] { "first", "later" }, calls);

            calls.Clear();
            dispatcher.Publish(Data(DamageApplied));
            CollectionAssert.AreEqual(new[] { "added" }, calls);
        }

        [Test]
        public void HandlesValidationAndCallbackExceptions_KeepDispatcherOwnershipAndFailFast()
        {
            var dispatcher = new GameplayEventDispatcher();
            var other = new GameplayEventDispatcher();
            int calls = 0;
            var handle = dispatcher.Subscribe(Damage, _ => throw new InvalidOperationException("expected"));
            dispatcher.Subscribe(Damage, _ => calls++);

            Assert.That(default(GameplayEventSubscriptionHandle).IsValid, Is.False);
            Assert.That(other.Unsubscribe(handle), Is.False);
            Assert.That(dispatcher.Unsubscribe(default), Is.False);
            Assert.Throws<ArgumentException>(() => dispatcher.Publish(default));
            Assert.Throws<InvalidOperationException>(() => dispatcher.Publish(Data(DamageApplied)));
            Assert.That(calls, Is.Zero, "异常后的回调不应继续执行。");
            Assert.That(dispatcher.Unsubscribe(handle), Is.True);
            Assert.That(dispatcher.Unsubscribe(handle), Is.False);

            dispatcher.Publish(Data(DamageApplied));
            Assert.That(calls, Is.EqualTo(1));
        }

        private static GameplayEventData Data(GameplayTag eventTag) =>
            new GameplayEventData(eventTag, new GameplaySourceId(1), new GameplaySourceId(2), 3f);
    }
}
