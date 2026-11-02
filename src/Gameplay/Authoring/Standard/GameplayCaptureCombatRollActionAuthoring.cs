using System;
using FRFrame.Gameplay.Standard;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    [Serializable,Preserve]
    public sealed class GameplayCaptureCombatRollActionAuthoring:GameplaySkillActionAuthoring
    {
        [SerializeReference]public GameplayFloatValueAuthoring CriticalChance=new GameplayConstantFloatValueAuthoring{Value=.25f};
        [SerializeReference]public GameplayFloatValueAuthoring MissChance=new GameplayConstantFloatValueAuthoring{Value=.1f};
        public bool PreserveExisting=true;
        public override void Validate(GameplayAuthoringValidationContext validation,string path)
        {
            if(CriticalChance==null)validation.Error(path+".CriticalChance","Probability required.");else CriticalChance.Validate(validation,path+".CriticalChance");
            if(MissChance==null)validation.Error(path+".MissChance","Probability required.");else MissChance.Validate(validation,path+".MissChance");
        }
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context)=>new GameplayCaptureCombatRollAction(CriticalChance.Build(),MissChance.Build(),PreserveExisting);
    }
}
