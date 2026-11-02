using System;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Injected gameplay randomness; returns a finite number in [0,1). It is never read by
    /// animation/Editor sampling, and does not own a rule timer.</summary>
    public interface IGameplayRandomSource { double NextUnitInterval(); }
    /// <summary>Explicit seeded local sequence. Supply a game's source to the World when another
    /// randomness policy is needed; this is not a networking or replay prediction protocol.</summary>
    public sealed class GameplaySeededRandomSource:IGameplayRandomSource
    {
        private readonly Random _random;
        public GameplaySeededRandomSource(int seed)=>_random=new Random(seed);
        public double NextUnitInterval()=>_random.NextDouble();
    }
    /// <summary>Captures per-execution Crit/Miss flags into the existing immutable context. Place
    /// before damage, or inside each projectile/area/search callback to roll per actual target.</summary>
    public sealed class GameplayCaptureCombatRollAction:IGameplaySkillAction,IGameplaySkillContextAction
    {
        private readonly IGameplayFloatValue _critical,_miss;
        private readonly bool _preserve;
        public GameplayCaptureCombatRollAction(IGameplayFloatValue criticalChance,IGameplayFloatValue missChance,bool preserveExisting=true)
        {_critical=criticalChance??throw new ArgumentNullException(nameof(criticalChance));_miss=missChance??throw new ArgumentNullException(nameof(missChance));_preserve=preserveExisting;}
        public GameplaySkillActionResult Execute(GameplayContext context,GameplayEventData triggerEvent,IGameplaySkillHost host)=>ExecuteWithContext(context,triggerEvent,host).Result;
        public GameplaySkillActionExecution ExecuteWithContext(GameplayContext context,GameplayEventData triggerEvent,IGameplaySkillHost host)
        {
            if(!(host is GameplayStandardActor actor)||!actor.World.TryGetActor(context.TargetData.PrimaryTargetId,out var target))return new GameplaySkillActionExecution(GameplaySkillActionResult.Rejected,context);
            var application=new GameplayEffectApplicationContext(context,target,context.Values.AsSetByCallerValues(),actor.Attributes,actor.Id,instigatorTarget:actor);
            bool critNeeded=!_preserve||!context.Values.TryGetValue(GameplayDamageExecution.CriticalKey,out _);
            bool missNeeded=!_preserve||!context.Values.TryGetValue(GameplayDamageExecution.MissKey,out _);
            float crit=critNeeded?Chance(_critical,application):0,miss=missNeeded?Chance(_miss,application):0;
            var values=context.Values;
            if(critNeeded)values=values.WithValue(GameplayDamageExecution.CriticalKey,Roll(actor.World.Random,crit)?1:0);
            if(missNeeded)values=values.WithValue(GameplayDamageExecution.MissKey,Roll(actor.World.Random,miss)?1:0);
            return new GameplaySkillActionExecution(GameplaySkillActionResult.Succeeded,context.WithValues(values));
        }
        private static float Chance(IGameplayFloatValue value,GameplayEffectApplicationContext application)
        {float chance=GameplayValueEvaluator.Evaluate(value,application);if(chance<0||chance>1)throw new ArithmeticException("Combat probability must be in [0,1].");return chance;}
        private static bool Roll(IGameplayRandomSource source,float chance)
        {
            if(chance<=0)return false;if(chance>=1)return true;
            double value=source.NextUnitInterval();if(!double.IsFinite(value)||value<0||value>=1)throw new ArithmeticException("Random source must return [0,1).");
            return value<chance;
        }
    }
}
