using System.Linq;
using FRFrame.Gameplay.Authoring.Graph;
using System;
using FRFrame.Gameplay.Standard;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Formal summoned composition; no mutable actor, grant or lifetime is stored here.</summary>
    [System.Serializable, UnityEngine.Scripting.Preserve]
    public class GameplaySummonDefinitionData:GameplayLocalDefinitionPayload
    {
        public override System.Type DefinitionType => typeof(FRFrame.Gameplay.Standard.GameplaySummonDefinition);
        public override object BuildDefinition(GameplayAuthoringBuildContext context) => Build(context);

        public GameplayActorProfileAsset Profile;
        public GameplaySkillDocumentAsset[] Skills=Array.Empty<GameplaySkillDocumentAsset>();
        public bool AutoCast;
        [SerializeReference]public GameplayFloatValueAuthoring Duration=new GameplayConstantFloatValueAuthoring{Value=4};
        [SerializeReference]public GameplaySkillActionAuthoring[] OnSpawn=Array.Empty<GameplaySkillActionAuthoring>(),OnEnd=Array.Empty<GameplaySkillActionAuthoring>();
        protected virtual IGameplaySummonFactory CreateFactory(GameplayAuthoringBuildContext context)=>null;
        public GameplaySummonDefinition Build(GameplayAuthoringBuildContext context)=>context.Build(this,()=>new GameplaySummonDefinition(context.ResolveDefinition(Profile, this, nameof(Profile), () => Profile.Build(context)),Duration.Build(),
            Enumerable.Range(0, Skills.Length).Select(i => context.ResolveDefinition(Skills[i], this, "Skills.Array.data[" + i + "]", () => Skills[i].Build(context))).ToArray(),AutoCast,CreateFactory(context),GameplaySpatialAuthoringUtility.Build(OnSpawn,context),GameplaySpatialAuthoringUtility.Build(OnEnd,context)));
        protected override void ValidateContent(GameplayAuthoringValidationContext validation,string path)
        {
            validation.Asset(Profile,path+".Profile");GameplaySpatialAuthoringUtility.Value(Duration,validation,path+".Duration");
            if(Skills==null)validation.Error(path+".Skills","Skill list is null.");else foreach(var skill in Skills)validation.Asset(skill,path+".Skills");
            GameplaySpatialAuthoringUtility.Validate(OnSpawn,validation,path+".OnSpawn");GameplaySpatialAuthoringUtility.Validate(OnEnd,validation,path+".OnEnd");
        }
    
    }
}
