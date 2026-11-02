using System;
using FRFrame.Gameplay;
using FRFrame.Gameplay.Standard;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay
{
    public sealed class GameplayTypedCombatTests
    {
        [Test]
        public void DamageTypeRoutesDefenseAndRandomCaptureDoesNotRerollExplicitInputs()
        {
            var random=new CountingRandom();using(var world=new GameplayStandardWorld(random:random))
            {
                var caster=world.CreateActor(default);var target=world.CreateActor(default);
                var hp=new GameplayAttributeId("Health");var max=new GameplayAttributeId("MaxHealth");var defense=new GameplayAttributeId("Defense");var magic=new GameplayAttributeId("MagicDefense");
                target.Attributes.Register(hp,1000);target.Attributes.Register(max,1000);target.Attributes.Register(defense,100);target.Attributes.Register(magic,300);
                target.CombatAttributes=new GameplayCombatAttributeMap(hp,max,default,defense,magic);
                var context=new GameplayContext(caster.Id,new GameplayTargetData(target.Id));
                var roll=new GameplayCaptureCombatRollAction(N(.5f),N(.2f));
                var captured=roll.ExecuteWithContext(context,default,caster).Context;
                Assert.That(random.Reads,Is.EqualTo(2));Assert.That(captured.Values.GetRequiredValue(GameplayDamageExecution.CriticalKey),Is.EqualTo(1));
                Assert.That(captured.Values.GetRequiredValue(GameplayDamageExecution.MissKey),Is.Zero);
                roll.ExecuteWithContext(captured,default,caster);Assert.That(random.Reads,Is.EqualTo(2),"Captured inputs must not reroll in nested callbacks.");
                Assert.Throws<ArithmeticException>(()=>new GameplayCaptureCombatRollAction(N(.5f),N(2)).ExecuteWithContext(context,default,caster));Assert.That(random.Reads,Is.EqualTo(2),"Validate both chances before consuming randomness.");
                var application=new GameplayEffectApplicationContext(context,target,GameplaySetByCallerValues.Empty,caster.Attributes,caster.Id,instigatorTarget:caster);
                var types=new[]{GameplayDamageType.Physical,GameplayDamageType.Magical,GameplayDamageType.True};var expected=new[]{50,25,100};
                for(int i=0;i<types.Length;i++)
                {
                    new GameplayDamageExecution(N(100),new[]{new GameplayDamageStage(GameplayDamageStageKind.Defense)},damageType:types[i]).Execute(application);
                    Assert.That(target.LastCombatOutcome.Value.Applied,Is.EqualTo(expected[i]));Assert.That(target.LastCombatOutcome.Value.DamageType,Is.EqualTo(types[i]));
                }
            }
        }
        private static IGameplayFloatValue N(float value)=>new GameplayConstantFloatValue(value);
        private sealed class CountingRandom:IGameplayRandomSource
        {public int Reads;public double NextUnitInterval()=>++Reads==1?.25:.75;}
    }
}
