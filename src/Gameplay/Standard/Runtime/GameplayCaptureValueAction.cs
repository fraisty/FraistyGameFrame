using System;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Evaluates once at this actual action/target boundary and writes an immutable context
    /// scalar for subsequent actions and child callbacks. Use Context/Caller key values to read the
    /// captured result; ordinary Attribute values continue to read live attributes.</summary>
    public sealed class GameplayCaptureValueAction : IGameplaySkillAction,IGameplaySkillContextAction
    {
        public GameplayTag Key {get;}
        private readonly IGameplayFloatValue _value;
        public GameplayCaptureValueAction(GameplayTag key,IGameplayFloatValue value)
        {if(!key.IsValid)throw new ArgumentException("Capture key required.",nameof(key));Key=key;_value=value??throw new ArgumentNullException(nameof(value));}
        public GameplaySkillActionResult Execute(GameplayContext context,GameplayEventData triggerEvent,IGameplaySkillHost host)=>ExecuteWithContext(context,triggerEvent,host).Result;
        public GameplaySkillActionExecution ExecuteWithContext(GameplayContext context,GameplayEventData triggerEvent,IGameplaySkillHost host)
        {
            if(!(host is GameplayStandardActor actor)||!actor.World.TryGetActor(context.TargetData.PrimaryTargetId,out var target))
                return new GameplaySkillActionExecution(GameplaySkillActionResult.Rejected,context);
            var application=new GameplayEffectApplicationContext(context,target,context.Values.AsSetByCallerValues(),actor.Attributes,actor.Id,instigatorTarget:actor);
            float captured=GameplayValueEvaluator.Evaluate(_value,application);
            return new GameplaySkillActionExecution(GameplaySkillActionResult.Succeeded,context.WithValues(context.Values.WithValue(Key,captured)));
        }
    }
}
