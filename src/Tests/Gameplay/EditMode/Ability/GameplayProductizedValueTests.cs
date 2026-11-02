using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Ability
{
    /// <summary>Guards level/source snapshots, exact formula/charge boundaries and atomic resource payment.
    /// Visual skill behavior belongs to the permanent Skill Lab rather than a test per acceptance bullet.</summary>
    public sealed class GameplayProductizedValueTests
    {
        private static readonly GameplayAttributeId Mana = new GameplayAttributeId("Mana");
        private static readonly GameplayAttributeId Energy = new GameplayAttributeId("Energy");
        private static readonly GameplayAttributeId Attack = new GameplayAttributeId("Attack");

        [Test]
        public void GrantLevelAndChildSnapshotsRemainIndependentAcrossUpgrade()
        {
            var ids = new GameplaySourceIdAllocator(); var host = new Host(ids);
            var abilities = new GameplayAbilityContainer(host, ids);
            var definition = new GameplayAbilityDefinition(Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>(),
                Array.Empty<GameplayTag>(), maxLevel: 10);
            var first = abilities.Grant(definition, 5); var second = abilities.Grant(definition, 1);
            Assert.That(abilities.TryActivate(first), Is.True);
            Assert.That(abilities.TrySetLevel(first, 10), Is.False);
            Assert.That(abilities.TryCreateExecutionContext(first, new GameplayTargetData(host.OwnerId), out var cast), Is.True);
            var child = cast.WithSource(ids.Allocate()).WithTarget(new GameplayTargetData(host.OwnerId))
                .WithValues(GameplayContextValues.Empty);
            Assert.That(child.AbilityLevel, Is.EqualTo(5));
            Assert.That(abilities.End(first), Is.True);
            Assert.That(abilities.TrySetLevel(first, 10), Is.True);
            Assert.That(abilities.GetGrantedEntriesSnapshot()[0].Level, Is.EqualTo(10));
            Assert.That(abilities.GetGrantedEntriesSnapshot()[1].Level, Is.EqualTo(1));
            Assert.That(cast.AbilityLevel, Is.EqualTo(5));
            Assert.That(abilities.TrySetLevel(second, 11), Is.False);
        }

        [Test]
        public void FormulaUsesCapturedLevelLiveAttributesAndFailsClosed()
        {
            var ids = new GameplaySourceIdAllocator(); var host = new Host(ids);
            var expression = GameplayFormula.Compile("Caster.Attack * (1.45 + Level * 0.15)");
            var levels = new[] { 1, 5, 10 }; var expected = new[] { 160f, 220f, 295f };
            for (int i = 0; i < levels.Length; i++)
            {
                var context = new GameplayContext(host.OwnerId, new GameplayTargetData(host.OwnerId), abilityLevel: levels[i]);
                var application = new GameplayEffectApplicationContext(context, host, GameplaySetByCallerValues.Empty);
                Assert.That(GameplayValueEvaluator.Evaluate(expression, application), Is.EqualTo(expected[i]).Within(.001f));
            }
            var cast = new GameplayContext(host.OwnerId, new GameplayTargetData(host.OwnerId), abilityLevel: 5);
            var capture = new GameplayEffectApplicationContext(cast, host, GameplaySetByCallerValues.Empty);
            float snapshotted = GameplayValueEvaluator.Evaluate(expression, capture);
            host.Attributes.SetBaseValue(Attack, 200);
            Assert.That(GameplayValueEvaluator.Evaluate(expression, capture), Is.EqualTo(440).Within(.001f));
            Assert.That(snapshotted, Is.EqualTo(220).Within(.001f));
            Assert.That(GameplayValueEvaluator.Evaluate(GameplayFormula.Compile("clamp(max(Level, 2), 1, 4)"), capture), Is.EqualTo(4));
            var source = new[] { 1.6f, 2.2f }; var table = new GameplayLevelTableFloatValue(source); source[1] = 99;
            Assert.That(GameplayValueEvaluator.Evaluate(table, capture), Is.EqualTo(2.2f));
            Assert.Throws<FormatException>(() => GameplayFormula.Compile("Caster.Attack / 0"));
            Assert.Throws<FormatException>(() => GameplayFormula.Compile("Unknown + 1"));
            Assert.Throws<KeyNotFoundException>(() => GameplayValueEvaluator.Evaluate(GameplayFormula.Compile("Caster.Missing"), capture));
            var modifier=host.Attributes.GetAttribute(Mana).AddModifier(new GameplayAttributeModifier(GameplayModifierOperation.Add,50,ids.Allocate()));
            Assert.That(GameplayValueEvaluator.Evaluate(GameplayFormula.Compile("Caster.Mana"),capture),Is.EqualTo(150));
            Assert.That(GameplayValueEvaluator.Evaluate(GameplayFormula.Compile("Resource.Mana + TargetResource.Mana"),capture),Is.EqualTo(200),"Spendable formulas must not include the live modifier aggregate.");
            host.Attributes.GetAttribute(Mana).RemoveModifier(modifier);
        }

        [Test]
        public void ResourcePaymentAndChargeCommitAreAtomicAndDoNotRestartRecharge()
        {
            var ids = new GameplaySourceIdAllocator(); var host = new Host(ids);
            host.Attributes.SetBaseValue(Energy, 0);
            var rejected = new GameplayAttributeAbilityCost(new[] {
                new GameplayAttributeCostEntry(Mana, new GameplayConstantFloatValue(-20)),
                new GameplayAttributeCostEntry(Energy, new GameplayConstantFloatValue(-1)) });
            Assert.That(rejected.TryCommit(host, 1), Is.False);
            Assert.That(host.Attributes.GetCurrentValue(Mana), Is.EqualTo(100));
            var payment = new GameplayAttributeAbilityCost(new[] {
                new GameplayAttributeCostEntry(Mana, GameplayFormula.Compile("-4 * Level")) });
            var abilities = new GameplayAbilityContainer(host, ids);
            var definition = new GameplayAbilityDefinition(Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>(),
                Array.Empty<GameplayTag>(), charge: new GameplayAbilityChargeDefinition(2, 8), cost: payment, maxLevel: 10);
            var grant = abilities.Grant(definition, 5);
            Assert.That(abilities.TryActivate(grant), Is.True); abilities.End(grant);
            abilities.Advance(3);
            Assert.That(abilities.TryActivate(grant), Is.True); abilities.End(grant);
            Assert.That(abilities.TryActivate(grant), Is.False);
            Assert.That(host.Attributes.GetCurrentValue(Mana), Is.EqualTo(60));
            var contextPayment=new GameplayAttributeAbilityCost(new[]{new GameplayAttributeCostEntry(Mana,GameplayFormula.Compile("-Caller.Cost.Amount * Context.Cost.Factor"))});
            var contextGrant=abilities.Grant(new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),cost:contextPayment));
            Assert.That(abilities.GetStatus(contextGrant).BlockReason,Is.EqualTo(GameplayAbilityBlockReason.Cost),"Missing captured inputs fail eligibility without paying.");
            var amountKey=new GameplayTag("Cost.Amount");
            var inputs=GameplayContextValues.Empty.WithValue(amountKey,12).WithValue(new GameplayTag("Cost.Factor"),2);
            var request=new GameplayContext(host.OwnerId,new GameplayTargetData(host.OwnerId),values:inputs);
            Assert.That(abilities.TryBeginActivation(contextGrant,request),Is.True);
            host.Attributes.SetBaseValue(Mana,10);
            Assert.That(abilities.TryCommitActivation(contextGrant),Is.False);
            Assert.That(host.Attributes.GetCurrentValue(Mana),Is.EqualTo(10));
            host.Attributes.SetBaseValue(Mana,60);
            Assert.That(abilities.TryActivate(contextGrant,request),Is.True);
            Assert.That(host.Attributes.GetCurrentValue(Mana),Is.EqualTo(36));
            Assert.That(abilities.TryCreateExecutionContext(contextGrant,new GameplayTargetData(host.OwnerId),out var captured),Is.True);
            Assert.That(captured.Values.GetRequiredValue(amountKey),Is.EqualTo(12));
            abilities.End(contextGrant);
            host.Attributes.SetBaseValue(Mana,60); // Resume the original charge/recharge boundary below.
            abilities.Advance(5);
            Assert.That(abilities.GetStatus(grant).CurrentCharges, Is.EqualTo(1));
            Assert.That(abilities.TryActivate(grant), Is.True); abilities.End(grant);
            Assert.That(host.Attributes.GetCurrentValue(Mana), Is.EqualTo(40));
            var refund = new GameplayAttributeAbilityCost(new[] { new GameplayAttributeCostEntry(Mana, new GameplayConstantFloatValue(20)) });
            Assert.That(refund.TryCommit(host), Is.True);
            Assert.That(host.Attributes.GetCurrentValue(Mana), Is.EqualTo(60));
        }

        private sealed class Host : IGameplayAbilityHost
        {
            public GameplaySourceId OwnerId { get; }
            public GameplaySourceId TargetId => OwnerId;
            public AttributeSet Attributes { get; } = new AttributeSet();
            public GameplayTagContainer Tags { get; } = new GameplayTagContainer();
            public GameplayEffectContainer Effects { get; }
            public Host(GameplaySourceIdAllocator ids)
            {
                OwnerId = ids.Allocate();
                Attributes.Register(Mana, 100); Attributes.Register(Energy, 100); Attributes.Register(Attack, 100);
                Effects = new GameplayEffectContainer(Attributes, ids, Tags);
            }
        }
    }
}
