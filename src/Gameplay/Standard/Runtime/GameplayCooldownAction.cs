using System;

namespace FRFrame.Gameplay.Standard
{
    public enum GameplayCooldownOperation { SetRemaining, AdjustRemaining, Restart, SetCharges, AdjustCharges }
    /// <summary>Authored command for this actor's actual grant. SkillId=0 resolves only the
    /// captured active cast; a positive ID selects one installed skill on the same actor.
    /// All changes go through the existing Ability container, with no additional cooldown clock.</summary>
    public sealed class GameplayCooldownAction:IGameplaySkillAction
    {
        public int SkillId {get;}
        public GameplayCooldownOperation Operation {get;}
        public IGameplayFloatValue Value {get;}
        public GameplayCooldownAction(int skillId,GameplayCooldownOperation operation,IGameplayFloatValue value)
        {if(skillId<0||!Enum.IsDefined(typeof(GameplayCooldownOperation),operation))throw new ArgumentOutOfRangeException(nameof(skillId));SkillId=skillId;Operation=operation;Value=value??throw new ArgumentNullException(nameof(value));}
        public GameplaySkillActionResult Execute(GameplayContext context,GameplayEventData triggerEvent,IGameplaySkillHost host)
        {
            if(!(host is GameplayStandardActor actor)||actor.IsDisposed)return GameplaySkillActionResult.Rejected;
            GameplayAbilityHandle handle=default;
            if(SkillId==0){if(!GameplayStandardActivation.TryFindGrant(actor,context,out handle))return GameplaySkillActionResult.Rejected;}
            else foreach(var skill in actor.InstalledSkills)if(!skill.IsDisposed&&skill.Definition.SkillId==SkillId){handle=skill.Grant;break;}
            if(!handle.IsValid)return GameplaySkillActionResult.Rejected;
            bool succeeded;
            if(Operation==GameplayCooldownOperation.Restart)succeeded=actor.Abilities.TryResetCooldown(handle);
            else{
                float value=GameplayValueEvaluator.Evaluate(Value,new GameplayEffectApplicationContext(context,actor,context.Values.AsSetByCallerValues()));
                if(!float.IsFinite(value))return GameplaySkillActionResult.Rejected;
                switch(Operation){
                    case GameplayCooldownOperation.SetRemaining:succeeded=actor.Abilities.TrySetCooldownRemaining(handle,value);break;
                    case GameplayCooldownOperation.AdjustRemaining:succeeded=actor.Abilities.TryAdjustCooldownRemaining(handle,value);break;
                    default:
                        if((double)value<int.MinValue||(double)value>int.MaxValue||value!=MathF.Truncate(value))return GameplaySkillActionResult.Rejected;
                        succeeded=Operation==GameplayCooldownOperation.SetCharges?actor.Abilities.TrySetCharges(handle,(int)value):actor.Abilities.TryAdjustCharges(handle,(int)value);break;
                }
            }
            return succeeded?GameplaySkillActionResult.Succeeded:GameplaySkillActionResult.Rejected;
        }
    }
}
