using System;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Ability
{
    public sealed class GameplayAbilityContainerTests
    {
        private static readonly GameplayTag Owned = new GameplayTag("State.Casting");
        private static readonly GameplayTag Cooldown = new GameplayTag("Cooldown.Test");

        private static GameplayAbilityContainer CreateAbilities(GameplaySourceIdAllocator ids,
            AttributeSet attributes, GameplayTagContainer tags, GameplayEffectContainer effects) =>
            new GameplayAbilityContainer(new TestHost(ids.Allocate(), attributes, tags, effects), ids);

        private sealed class TestHost : IGameplayAbilityHost
        {
            public GameplaySourceId OwnerId { get; }
            public GameplaySourceId TargetId => OwnerId;
            public AttributeSet Attributes { get; }
            public GameplayTagContainer Tags { get; }
            public GameplayEffectContainer Effects { get; }

            public TestHost(GameplaySourceId ownerId, AttributeSet attributes,
                GameplayTagContainer tags, GameplayEffectContainer effects)
            { OwnerId = ownerId; Attributes = attributes; Tags = tags; Effects = effects; }
        }

        // A presentation query must not execute affordability/user callbacks for any grant.
        [Test]
        public void LevelQueryReadsOnlyItsGrantAndRejectsRemovedOrForeignHandles()
        {
            var ids=new GameplaySourceIdAllocator();var attributes=new AttributeSet();var tags=new GameplayTagContainer();
            var effects=new GameplayEffectContainer(attributes,ids,tags);
            var abilities=CreateAbilities(ids,attributes,tags,effects);
            var foreign=CreateAbilities(ids,attributes,tags,effects);
            var definition=new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),cost:new QueryForbiddenCost(),maxLevel:5);
            var handle=abilities.Grant(definition);var other=foreign.Grant(definition);
            Assert.That(abilities.TryGetLevel(handle,out var level),Is.True);Assert.That(level,Is.EqualTo(1));
            Assert.That(abilities.TrySetLevel(handle,5),Is.True);
            Assert.That(abilities.TryGetLevel(handle,out level),Is.True);Assert.That(level,Is.EqualTo(5));
            Assert.That(abilities.TryGetLevel(other,out level),Is.False);Assert.That(level,Is.Zero);
            abilities.Remove(handle);
            Assert.That(abilities.TryGetLevel(handle,out level),Is.False);Assert.That(level,Is.Zero);
        }
        private sealed class QueryForbiddenCost:IGameplayAbilityCost
        {
            public bool CanPay(IGameplayAbilityHost host)=>throw new InvalidOperationException("Level reads cannot evaluate cost.");
            public bool TryCommit(IGameplayAbilityHost host)=>throw new InvalidOperationException("Level reads cannot commit cost.");
        }

        [Test]
        public void RequiredAndBlockedTags_UseHierarchicalQueriesAndDefinitionCopies()
        {
            var sources = new GameplaySourceIdAllocator();
            var tags = new GameplayTagContainer();
            var attributes = new AttributeSet();
            var effects = new GameplayEffectContainer(attributes, sources, tags);
            var abilities = CreateAbilities(sources, attributes, tags, effects);
            var required = new[] { new GameplayTag("State.Ready"), new GameplayTag("Equipment") };
            var blocked = new[] { new GameplayTag("State.Control"), new GameplayTag("State.Dead") };
            var definition = new GameplayAbilityDefinition(required, blocked, Array.Empty<GameplayTag>());
            required[0] = new GameplayTag("Changed.Required");
            blocked[0] = new GameplayTag("Changed.Blocked");
            var handle = abilities.Grant(definition);
            var stateSource = sources.Allocate();
            tags.AddTag(new GameplayTag("State.Ready.Combat"), stateSource);
            Assert.That(abilities.CanActivate(handle), Is.False, "Required必须全部满足。");
            tags.AddTag(new GameplayTag("Equipment.Weapon"), stateSource);
            Assert.That(abilities.CanActivate(handle), Is.True);
            var stunSource = sources.Allocate();
            tags.AddTag(new GameplayTag("State.Control.Stun"), stunSource);
            Assert.That(abilities.TryActivate(handle), Is.False);
            tags.RemoveTagsFromSource(stunSource);
            Assert.That(abilities.TryActivate(handle), Is.True);
            Assert.That(abilities.CanActivate(handle), Is.False, "Active不能再次激活。");
            Assert.That(abilities.TryGetCooldownRemaining(handle, out float remaining), Is.False,
                "未配置冷却的Ability成功激活后也没有自身冷却Handle。");
            Assert.That(remaining, Is.Zero);
        }

        [Test]
        public void ActivationOwnedTags_EndCancelAndFailedCooldownCleanOnlyTheirSource()
        {
            var sources = new GameplaySourceIdAllocator();
            var tags = new GameplayTagContainer();
            var attributes = new AttributeSet();
            var effects = new GameplayEffectContainer(attributes, sources, tags);
            var abilities = CreateAbilities(sources, attributes, tags, effects);
            var external = sources.Allocate();
            tags.AddTag(Owned, external);
            var ownedInput = new[] { Owned };
            var definition = new GameplayAbilityDefinition(Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>(), ownedInput);
            ownedInput[0] = new GameplayTag("Changed.Owned");
            var handle = abilities.Grant(definition);
            Assert.That(abilities.TryActivate(handle), Is.True);
            Assert.That(abilities.End(handle), Is.True);
            Assert.That(tags.HasTagExact(Owned), Is.True, "外部来源仍贡献同名标签。");
            tags.RemoveTagsFromSource(external);
            Assert.That(tags.HasTagExact(Owned), Is.False);
            Assert.That(abilities.TryActivate(handle), Is.True);
            Assert.That(tags.HasTagExact(Owned), Is.True);
            Assert.That(abilities.Cancel(handle), Is.True);
            Assert.That(tags.HasTagExact(Owned), Is.False);
            Assert.That(abilities.End(handle), Is.False);

            // 故意漏绑Effect目标Tags，触发真实Apply失败，验证激活的失败一致性。
            var unboundAttributes = new AttributeSet();
            var unboundEffects = new GameplayEffectContainer(unboundAttributes, sources);
            var failing = CreateAbilities(sources, unboundAttributes, tags, unboundEffects);
            var failingHandle = failing.Grant(DefinitionWithCooldown());
            Assert.Throws<InvalidOperationException>(() => failing.TryActivate(failingHandle));
            Assert.That(failing.IsActive(failingHandle), Is.False);
            Assert.That(tags.HasTagExact(Owned), Is.False);
            Assert.That(tags.HasTagExact(Cooldown), Is.False);
        }

        [Test]
        public void CooldownIsOwnedByEffects_AndSurvivesEndCancelAndGrantRemoval()
        {
            var sources = new GameplaySourceIdAllocator();
            var tags = new GameplayTagContainer();
            var attributes = new AttributeSet();
            var effects = new GameplayEffectContainer(attributes, sources, tags);
            var abilities = CreateAbilities(sources, attributes, tags, effects);
            var handle = abilities.Grant(DefinitionWithCooldown());
            Assert.That(abilities.TryGetCooldownRemaining(default(GameplayAbilityHandle), out float remaining), Is.False);
            Assert.That(remaining, Is.Zero);
            Assert.That(abilities.TryGetCooldownRemaining(handle, out remaining), Is.False);
            Assert.That(remaining, Is.Zero);
            Assert.That(abilities.TryActivate(handle), Is.True);
            Assert.That(abilities.TryGetCooldownRemaining(handle, out remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(5f));
            Assert.That(abilities.End(handle), Is.True);
            Assert.That(abilities.TryGetCooldownRemaining(handle, out remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(5f));
            Assert.That(tags.HasTagExact(Owned), Is.False);
            Assert.That(tags.HasTagExact(Cooldown), Is.True);
            Assert.That(abilities.TryActivate(handle), Is.False);
            Assert.That(abilities.TryGetCooldownRemaining(handle, out remaining), Is.True,
                "失败激活不能覆盖最近一次成功启动的冷却Handle。");
            effects.Advance(4f);
            Assert.That(abilities.CanActivate(handle), Is.False);
            Assert.That(abilities.TryGetCooldownRemaining(handle, out remaining), Is.True);
            Assert.That(remaining, Is.EqualTo(1f));
            effects.Advance(1f);
            Assert.That(abilities.TryGetCooldownRemaining(handle, out remaining), Is.False);
            Assert.That(remaining, Is.Zero);
            Assert.That(abilities.TryActivate(handle), Is.True);
            Assert.That(abilities.Cancel(handle), Is.True);
            Assert.That(abilities.TryGetCooldownRemaining(handle, out remaining), Is.True);
            Assert.That(tags.HasTagExact(Cooldown), Is.True);
            effects.Advance(5f);
            Assert.That(abilities.TryActivate(handle), Is.True);
            Assert.That(abilities.Remove(handle), Is.True);
            Assert.That(abilities.TryGetCooldownRemaining(handle, out remaining), Is.False);
            Assert.That(remaining, Is.Zero);
            Assert.That(abilities.IsActive(handle), Is.False);
            Assert.That(tags.HasTagExact(Owned), Is.False);
            Assert.That(tags.HasTagExact(Cooldown), Is.True);
            Assert.That(abilities.CanActivate(handle), Is.False);
            effects.Advance(5f);
            Assert.That(tags.HasTagExact(Cooldown), Is.False);
        }

        [Test]
        public void SameDefinitionGrants_HaveIndependentHandlesAndActivationSources()
        {
            var sources = new GameplaySourceIdAllocator();
            var tags = new GameplayTagContainer();
            var attributes = new AttributeSet();
            var effects = new GameplayEffectContainer(attributes, sources, tags);
            var abilities = CreateAbilities(sources, attributes, tags, effects);
            var other = CreateAbilities(sources, attributes, tags, effects);
            var definition = new GameplayAbilityDefinition(Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>(), new[] { Owned });
            var first = abilities.Grant(definition);
            var second = abilities.Grant(definition);
            Assert.That(first, Is.Not.EqualTo(second));
            Assert.That(other.TryActivate(first), Is.False);
            Assert.That(abilities.TryActivate(default(GameplayAbilityHandle)), Is.False);
            Assert.That(abilities.TryActivate(first), Is.True);
            Assert.That(abilities.IsActive(second), Is.False);
            Assert.That(abilities.TryActivate(second), Is.True);
            Assert.That(abilities.Remove(first), Is.True);
            Assert.That(abilities.IsActive(second), Is.True);
            Assert.That(tags.HasTagExact(Owned), Is.True);
            Assert.That(abilities.End(second), Is.True);
            Assert.That(tags.HasTagExact(Owned), Is.False);
        }

        [Test]
        public void ChargeConsumesOnlySuccessfulActivationsAndPreservesSequentialOverflow()
        {
            var sources = new GameplaySourceIdAllocator();
            var tags = new GameplayTagContainer();
            var attributes = new AttributeSet();
            var effects = new GameplayEffectContainer(attributes, sources, tags);
            var abilities = CreateAbilities(sources, attributes, tags, effects);
            var charge = new GameplayAbilityChargeDefinition(3, 2f);
            var definition = new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),
                new[] { new GameplayTag("State.Control") }, Array.Empty<GameplayTag>(), charge: charge);
            var first = abilities.Grant(definition);
            var second = abilities.Grant(definition);
            Assert.That(abilities.GetStatus(first).CurrentCharges, Is.EqualTo(3));
            Assert.That(abilities.GetStatus(first).CanActivate, Is.True);
            var stunSource = sources.Allocate();
            tags.AddTag(new GameplayTag("State.Control.Stun"), stunSource);
            Assert.That(abilities.TryActivate(first), Is.False);
            Assert.That(abilities.GetStatus(first).BlockReason, Is.EqualTo(GameplayAbilityBlockReason.BlockedByTag));
            Assert.That(abilities.GetStatus(first).CurrentCharges, Is.EqualTo(3));
            tags.RemoveTagsFromSource(stunSource);
            for (int i = 0; i < 3; i++)
            {
                Assert.That(abilities.TryActivate(first), Is.True);
                Assert.That(abilities.End(first), Is.True);
                if (i == 0) abilities.Advance(.5f);
            }
            Assert.That(abilities.GetStatus(first).BlockReason, Is.EqualTo(GameplayAbilityBlockReason.NoCharges));
            Assert.That(abilities.TryActivate(first), Is.False);
            Assert.That(abilities.GetStatus(second).CurrentCharges, Is.EqualTo(3), "Each grant owns its charge state.");
            Assert.That(abilities.GetStatus(first).RechargeRemaining, Is.EqualTo(1.5f));
            abilities.Advance(5.5f);
            Assert.That(abilities.GetStatus(first).CurrentCharges, Is.EqualTo(3));
            Assert.That(abilities.GetStatus(first).RechargeRemaining, Is.Zero);
            Assert.That(abilities.Remove(first), Is.True);
            Assert.That(abilities.GetStatus(first).BlockReason, Is.EqualTo(GameplayAbilityBlockReason.InvalidHandle));
        }

        [Test]
        public void ChargeLargeDeltaRestoresMultipleLayersAndStatusHasStablePriority()
        {
            var sources = new GameplaySourceIdAllocator();
            var tags = new GameplayTagContainer();
            var attributes = new AttributeSet();
            var effects = new GameplayEffectContainer(attributes, sources, tags);
            var abilities = CreateAbilities(sources, attributes, tags, effects);
            var definition = new GameplayAbilityDefinition(new[] { new GameplayTag("State.Ready") },
                new[] { new GameplayTag("State.Control") }, Array.Empty<GameplayTag>(),
                charge: new GameplayAbilityChargeDefinition(3, 2f));
            var handle = abilities.Grant(definition);
            Assert.That(abilities.GetStatus(handle).BlockReason, Is.EqualTo(GameplayAbilityBlockReason.MissingRequiredTag));
            var source = sources.Allocate();
            tags.AddTag(new GameplayTag("State.Ready.Combat"), source);
            for (int i = 0; i < 3; i++) { Assert.That(abilities.TryActivate(handle), Is.True); abilities.End(handle); }
            tags.AddTag(new GameplayTag("State.Control.Stun"), source);
            Assert.That(abilities.GetStatus(handle).BlockReason, Is.EqualTo(GameplayAbilityBlockReason.BlockedByTag));
            abilities.Advance(5.5f);
            Assert.That(abilities.GetStatus(handle).CurrentCharges, Is.EqualTo(2));
            Assert.That(abilities.GetStatus(handle).RechargeRemaining, Is.EqualTo(.5f).Within(.0001f));
            tags.RemoveTagsFromSource(source);
            Assert.That(abilities.GetStatus(handle).BlockReason, Is.EqualTo(GameplayAbilityBlockReason.MissingRequiredTag));
        }

        [Test]
        public void ChargeAndNormalCooldownAreExclusiveAndAbilityAdvanceDoesNotTickEffects()
        {
            Assert.Throws<ArgumentException>(() => new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),
                Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>(), DefinitionWithCooldown().CooldownEffect,
                new GameplayAbilityChargeDefinition(2, 3f)));
            var sources = new GameplaySourceIdAllocator();
            var tags = new GameplayTagContainer();
            var attributes = new AttributeSet();
            var effects = new GameplayEffectContainer(attributes, sources, tags);
            var abilities = CreateAbilities(sources, attributes, tags, effects);
            var handle = abilities.Grant(DefinitionWithCooldown());
            Assert.That(abilities.TryActivate(handle), Is.True);
            abilities.End(handle);
            abilities.Advance(10f);
            Assert.That(abilities.GetStatus(handle).BlockReason, Is.EqualTo(GameplayAbilityBlockReason.Cooldown));
            Assert.That(abilities.GetStatus(handle).CooldownRemaining, Is.EqualTo(5f));
            effects.Advance(5f);
            Assert.That(abilities.GetStatus(handle).CanActivate, Is.True);
        }

        private static GameplayAbilityDefinition DefinitionWithCooldown()
        {
            var cooldown = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 5f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), new[] { Cooldown });
            return new GameplayAbilityDefinition(Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>(), new[] { Owned }, cooldown);
        }
    }
}
