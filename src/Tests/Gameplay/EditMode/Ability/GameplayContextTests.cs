using System;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Ability
{
    public sealed class GameplayContextTests
    {
        [Test]
        public void PointTargetAndEffectiveSourceFlowIntoDamage()
        {
            var instigator = new GameplaySourceId(11);
            var target = new GameplaySourceId(22);
            var data = new GameplayTargetData(target, new GameplayPoint(12f, -3f));
            var context = new GameplayContext(instigator, data);
            var damage = new DamageContext(context, 7f, new AttributeSet());

            Assert.That(context.EffectiveSourceId, Is.EqualTo(instigator));
            Assert.That(data.PrimaryTargetId, Is.EqualTo(target));
            Assert.That(data.HasPoint, Is.True);
            Assert.That(data.Point.X, Is.EqualTo(12f));
            Assert.That(damage.GameplayContext, Is.SameAs(context));
            Assert.That(damage.SourceId, Is.EqualTo(context.EffectiveSourceId));
            var sourced = new GameplayContext(instigator, data, new GameplaySourceId(33));
            Assert.That(sourced.EffectiveSourceId, Is.EqualTo(new GameplaySourceId(33)));
        }

        [Test]
        public void AbilityRetainsOnlyActiveSuccessfulContext()
        {
            var ids = new GameplaySourceIdAllocator();
            var tags = new GameplayTagContainer();
            var attributes = new AttributeSet();
            var effects = new GameplayEffectContainer(attributes, ids, tags);
            var owner = ids.Allocate();
            var abilities = new GameplayAbilityContainer(new TestAbilityHost(owner, attributes, tags, effects), ids);
            var block = new GameplayTag("State.Control.Stun");
            var handle = abilities.Grant(new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),
                new[] { block }, Array.Empty<GameplayTag>()));
            var context = new GameplayContext(owner, new GameplayTargetData(default));
            var blockSource = ids.Allocate();
            tags.AddTag(block, blockSource);
            Assert.That(abilities.TryActivate(handle, context), Is.False);
            Assert.That(abilities.TryGetActiveContext(handle, out _), Is.False);
            tags.RemoveTagsFromSource(blockSource);
            Assert.That(abilities.TryActivate(handle, context), Is.True);
            Assert.That(abilities.TryGetActiveContext(handle, out GameplayContext active), Is.True);
            Assert.That(active, Is.SameAs(context));
            Assert.That(abilities.End(handle), Is.True);
            Assert.That(abilities.TryGetActiveContext(handle, out _), Is.False);
            Assert.That(abilities.TryActivate(handle, context), Is.True);
            Assert.That(abilities.Cancel(handle), Is.True);
            Assert.That(abilities.TryGetActiveContext(handle, out _), Is.False);
            Assert.That(abilities.TryActivate(handle, context), Is.True);
            Assert.That(abilities.Remove(handle), Is.True);
            Assert.That(abilities.TryGetActiveContext(handle, out _), Is.False);
        }

        private sealed class TestAbilityHost : IGameplayAbilityHost
        {
            public GameplaySourceId OwnerId { get; }
            public GameplaySourceId TargetId => OwnerId;
            public AttributeSet Attributes { get; }
            public GameplayTagContainer Tags { get; }
            public GameplayEffectContainer Effects { get; }
            public TestAbilityHost(GameplaySourceId owner, AttributeSet attributes,
                GameplayTagContainer tags, GameplayEffectContainer effects)
            { OwnerId = owner; Attributes = attributes; Tags = tags; Effects = effects; }
        }
    }
}
