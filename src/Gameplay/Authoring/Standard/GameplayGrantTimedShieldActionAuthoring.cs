using System;
using FRFrame.Gameplay.Standard;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Separate timed consumable shield. Duration uses the existing target Effect clock.</summary>
    [Serializable,Preserve]
    public sealed class GameplayGrantTimedShieldActionAuthoring : GameplaySkillActionAuthoring
    {
        [SerializeReference] public GameplayFloatValueAuthoring Amount=new GameplayConstantFloatValueAuthoring{Value=100};
        [SerializeReference] public GameplayFloatValueAuthoring Duration=new GameplayConstantFloatValueAuthoring{Value=5};
        public bool OwnedByAbility;
        public override void Validate(GameplayAuthoringValidationContext validation,string path)
        {
            if(Amount==null)validation.Error(path+".Amount","Shield amount required.");else Amount.Validate(validation,path+".Amount");
            if(Duration==null)validation.Error(path+".Duration","Shield duration required.");else Duration.Validate(validation,path+".Duration");
        }
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context)=>new GameplayGrantTimedShieldAction(Amount.Build(),Duration.Build(),OwnedByAbility);
    }
}
