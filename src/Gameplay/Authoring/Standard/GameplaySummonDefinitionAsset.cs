using FRFrame.Gameplay.Authoring.Graph;
using System;
using FRFrame.Gameplay.Standard;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Formal summoned composition; no mutable actor, grant or lifetime is stored here.</summary>
    [CreateAssetMenu(menuName="FRFrame/Gameplay/Spatial/Logical Summon")]
    public class GameplaySummonDefinitionAsset:GameplayDefinitionAsset
    {
        public GameplayActorProfileAsset Profile;

        public GameplaySkillDocumentAsset[] Skills=Array.Empty<GameplaySkillDocumentAsset>();

        public bool AutoCast;

        [SerializeReference]public GameplayFloatValueAuthoring Duration=new GameplayConstantFloatValueAuthoring{Value=4};

        [SerializeReference]public GameplaySkillActionAuthoring[] OnSpawn=Array.Empty<GameplaySkillActionAuthoring>(),OnEnd=Array.Empty<GameplaySkillActionAuthoring>();

        public override object BuildSharedDefinition(GameplayAuthoringBuildContext context) => Build(context);
        public override GameplayLocalDefinitionPayload CaptureGraphValues() => CaptureValues();
        protected virtual GameplaySummonDefinitionData CaptureValues() => new GameplaySummonDefinitionData { Profile = Profile, Skills = Skills, AutoCast = AutoCast, Duration = Duration, OnSpawn = OnSpawn, OnEnd = OnEnd };
        public FRFrame.Gameplay.Standard.GameplaySummonDefinition Build(GameplayAuthoringBuildContext context) => context.Build(this, () => CaptureValues().Build(context));
        protected override void ValidateContent(GameplayAuthoringValidationContext validation, string path) => CaptureValues().Validate(validation, path);

    }
    [Serializable,Preserve]
    public sealed class GameplaySpawnSummonActionAuthoring:GameplaySkillActionAuthoring
    {
        public GameplaySummonDefinitionAsset Summon;
        public override void Validate(GameplayAuthoringValidationContext validation,string path)=>validation.Asset(Summon,path+".Summon");
        public override void ValidateReferences(GameplayAuthoringValidationContext validation,string path)=>validation.Asset(Summon,path+".Summon");
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context)=>new GameplaySpawnSummonAction(context.ResolveDefinition(Summon, this, nameof(Summon), () => Summon.Build(context)));
    }
}
