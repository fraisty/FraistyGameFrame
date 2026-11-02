using System;
using FRFrame.Gameplay.Standard;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Timeline/rule command using the actual Ability grant. Positive charge deltas
    /// restore charges; negative cooldown deltas reduce remaining time. Zero SkillId means this cast.</summary>
    [Serializable,Preserve]
    public sealed class GameplayCooldownActionAuthoring:GameplaySkillActionAuthoring
    {
        public int SkillId;
        public GameplayCooldownOperation Operation=GameplayCooldownOperation.AdjustRemaining;
        [UnityEngine.SerializeReference]public GameplayFloatValueAuthoring Value=new GameplayConstantFloatValueAuthoring{Value=-1};
        public override void Validate(GameplayAuthoringValidationContext v,string p)
        {if(SkillId<0)v.Error(p+".SkillId","SkillId=0为当前cast，正数为当前角色已安装技能。");v.Enum(Operation,p+".Operation");if(Operation!=GameplayCooldownOperation.Restart){if(Value==null)v.Error(p+".Value","Missing cooldown value.");else Value.Validate(v,p+".Value");}}
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context)=>new GameplayCooldownAction(SkillId,Operation,Operation==GameplayCooldownOperation.Restart?new GameplayConstantFloatValue(0):Value.Build());
    }
}
