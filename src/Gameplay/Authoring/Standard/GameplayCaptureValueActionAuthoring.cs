using System;
using FRFrame.Gameplay.Standard;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Explicit snapshot producer; the output key is consumed by existing Context/Caller value nodes.</summary>
    [Serializable,Preserve]
    public sealed class GameplayCaptureValueActionAuthoring : GameplaySkillActionAuthoring
    {
        public GameplayTagReference Key;
        [SerializeReference] public GameplayFloatValueAuthoring Value=new GameplayAttributeFloatValueAuthoring{Attribute=new GameplayAttributeReference("Attack")};
        public override void Validate(GameplayAuthoringValidationContext validation,string path)
        {validation.Tag(Key,path+".Key");if(Value==null)validation.Error(path+".Value","Missing captured expression.");else Value.Validate(validation,path+".Value");}
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context)=>new GameplayCaptureValueAction(Key.Build(),Value.Build());
    }
}
