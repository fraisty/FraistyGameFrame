using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using FRFrame.Gameplay.Standard;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay
{
    public sealed class GameplayShieldContributionTests
    {
        /// <summary>Prevents expired/cancelled shields from subtracting later or unowned capacity;
        /// a failing hit must leave both actual balance and per-source remaining amounts intact.</summary>
        [Test]
        public void ConsumedShieldExpiryAndExactCleanupPreserveOtherCapacity()
        {
            using(var world=new GameplayStandardWorld())
            {
                var caster=world.CreateActor(default);var target=world.CreateActor(default);
                var hp=new GameplayAttributeId("Health");var max=new GameplayAttributeId("MaxHealth");var shield=new GameplayAttributeId("Shield");
                target.Attributes.Register(hp,100);target.Attributes.Register(max,100);target.Attributes.Register(shield,20);
                target.CombatAttributes=new GameplayCombatAttributeMap(hp,max,shield,default);
                var callerKey=new GameplayTag("Data.Test.Shield.Amount");
                var context=new GameplayContext(caster.Id,new GameplayTargetData(target.Id),abilityLevel:5,values:GameplayContextValues.Empty.WithValue(callerKey,50));
                new GameplayGrantTimedShieldAction(new GameplaySetByCallerFloatValue(callerKey),N(2)).Execute(context,default,caster);
                new GameplayGrantTimedShieldAction(N(60),N(4)).Execute(context,default,caster);
                var entries=target.Shields.Snapshot();Assert.That(entries.Count,Is.EqualTo(2));
                var application=new GameplayEffectApplicationContext(context,target,GameplaySetByCallerValues.Empty,caster.Attributes,caster.Id,instigatorTarget:caster);
                var absorption=new GameplayDamageStage(GameplayDamageStageKind.Shield);
                new GameplayDamageExecution(N(70),new[]{absorption}).Execute(application);
                Assert.That(entries[0].Remaining,Is.Zero);Assert.That(entries[1].Remaining,Is.EqualTo(40));
                Assert.That(target.Attributes.GetCurrentValue(shield),Is.EqualTo(60));
                var invalid=new GameplayDamageStage(GameplayDamageStageKind.Scale,GameplayFormula.Compile("Context.Missing"));
                Assert.Throws<System.Collections.Generic.KeyNotFoundException>(()=>new GameplayDamageExecution(N(10),new[]{absorption,invalid}).Execute(application));
                Assert.That(entries[1].Remaining,Is.EqualTo(40));Assert.That(target.Attributes.GetCurrentValue(shield),Is.EqualTo(60));
                world.Advance(2);Assert.That(entries[0].IsDisposed,Is.True);Assert.That(target.Attributes.GetCurrentValue(shield),Is.EqualTo(60));
                entries[1].Dispose();entries[1].Dispose();Assert.That(target.Attributes.GetCurrentValue(shield),Is.EqualTo(20));
                Assert.That(target.Shields.Snapshot().Count,Is.Zero);Assert.That(target.Effects.GetActiveEntriesSnapshot().Count,Is.Zero);
                var grant=caster.Abilities.Grant(new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>()));
                Assert.That(caster.Abilities.TryActivate(grant),Is.True);
                caster.Abilities.TryCreateExecutionContext(grant,new GameplayTargetData(target.Id),out var cast);
                new GameplayGrantTimedShieldAction(N(30),N(1),true).Execute(cast,default,caster);
                world.Advance(1);Assert.That(target.Attributes.GetCurrentValue(shield),Is.EqualTo(20));
                Assert.That(caster.Abilities.GetGrantedEntriesSnapshot()[0].OwnedLifetimesCount,Is.Zero,"Expiry must release the completed relation from the still-active cast.");
                new GameplayGrantTimedShieldAction(N(50),N(10),true).Execute(cast,default,caster);
                caster.Abilities.Cancel(grant);Assert.That(target.Attributes.GetCurrentValue(shield),Is.EqualTo(20));
                Assert.That(target.Shields.Snapshot().Count,Is.Zero);
            }
        }

        /// <summary>Terminal actions see the consumed shield gone and the original cast still active.
        /// Completion can end that cast before its configured expiry and leaves the next cast usable.</summary>
        [Test]
        public void ShieldRemovalCompletesOriginalCastAfterRetaliation()
        {
            using var world = new GameplayStandardWorld();
            var caster = world.CreateActor(default);
            var attacker = world.CreateActor(default);
            var hp = new GameplayAttributeId("Health");
            var max = new GameplayAttributeId("MaxHealth");
            var shield = new GameplayAttributeId("Shield");
            caster.Attributes.Register(hp,100);
            caster.Attributes.Register(max,100);
            caster.Attributes.Register(shield,0);
            caster.CombatAttributes = new GameplayCombatAttributeMap(hp,max,shield,default);
            var grant = caster.Abilities.Grant(new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),
                Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>()));
            var calls = new List<string>();
            var action = new GameplayGrantTimedShieldAction(N(20),N(5),true,
                onBroken: new[]{new Observe((context, _) =>
                {
                    Assert.That(context.TargetData.PrimaryTargetId, Is.EqualTo(attacker.Id));
                    Assert.That(caster.Shields.Snapshot(), Is.Empty);
                    Assert.That(caster.Abilities.IsActive(grant), Is.True);
                    calls.Add("broken");
                })},
                onRemoved: new IGameplaySkillAction[]{new Observe((context, _) =>
                {
                    Assert.That(context.TargetData.PrimaryTargetId, Is.EqualTo(caster.Id));
                    calls.Add("removed");
                }),new GameplayEndAbilityAction()});

            Assert.That(caster.Abilities.TryActivate(grant), Is.True);
            Assert.That(caster.Abilities.TryCreateExecutionContext(grant,new GameplayTargetData(caster.Id),out var cast), Is.True);
            Assert.That(action.Execute(cast,default,caster), Is.EqualTo(GameplaySkillActionResult.Succeeded));
            var hit = new GameplayContext(attacker.Id,new GameplayTargetData(caster.Id));
            var application = new GameplayEffectApplicationContext(hit,caster,GameplaySetByCallerValues.Empty,attacker.Attributes,attacker.Id);
            new GameplayDamageExecution(N(20),new[]{new GameplayDamageStage(GameplayDamageStageKind.Shield)}).Execute(application);
            Assert.That(calls, Is.EqualTo(new[]{"broken","removed"}));
            Assert.That(caster.Abilities.TryActivate(grant), Is.True);
            Assert.That(caster.Abilities.TryCreateExecutionContext(grant,new GameplayTargetData(caster.Id),out cast), Is.True);
            Assert.That(action.Execute(cast,default,caster), Is.EqualTo(GameplaySkillActionResult.Succeeded));
            caster.Abilities.Cancel(grant);
            Assert.That(calls, Is.EqualTo(new[]{"broken","removed"}));
            Assert.That(caster.Attributes.GetCurrentValue(shield), Is.Zero);
        }

        private sealed class Observe : IGameplaySkillAction
        {
            private readonly Action<GameplayContext, IGameplaySkillHost> _observe;
            internal Observe(Action<GameplayContext, IGameplaySkillHost> observe) => _observe = observe;
            public GameplaySkillActionResult Execute(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host)
            {
                _observe(context, host);
                return GameplaySkillActionResult.Succeeded;
            }
        }
        private static IGameplayFloatValue N(float value)=>new GameplayConstantFloatValue(value);
    }
}
