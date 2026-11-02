using System;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Explicit spendable-balance value. Resource references share the existing attribute catalog.</summary>
    [Serializable,Preserve]
    public sealed class GameplayResourceFloatValueAuthoring : GameplayFloatValueAuthoring
    {
        public GameplayValueAttributeSource Source;
        public GameplayAttributeReference Resource;
        public override void Validate(GameplayAuthoringValidationContext validation,string path)
        {validation.Enum(Source,path+".Source");validation.Attribute(Resource,path+".Resource");}
        public override IGameplayFloatValue Build()=>new GameplayResourceFloatValue(Source,Resource.Build());
    }
}
