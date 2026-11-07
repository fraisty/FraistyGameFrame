using System;
using FRFrame.Gameplay.Extensions;
using FRFrame.Gameplay.Standard;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    [Serializable,Preserve]public sealed class GameplaySkillPointTargetAuthoring:GameplaySkillTargetAuthoring
    {
        public Vector3 Point;
        public override void Validate(GameplayAuthoringValidationContext v,string path){if(!float.IsFinite(Point.x)||!float.IsFinite(Point.y)||!float.IsFinite(Point.z))v.Error(path,"地点坐标必须有限。");}
        public override IGameplaySkillTarget Build()=>new GameplaySkillPointTarget(new GameplayPoint(Point.x,Point.y,Point.z));
    }
    /// <summary>Uses the actual triggering cast/hit target and point, instead of replacing them with Self.</summary>
    [Serializable,Preserve]public sealed class GameplaySkillContextTargetAuthoring:GameplaySkillTargetAuthoring
    {
        public override void Validate(GameplayAuthoringValidationContext v,string p) { }
        public override IGameplaySkillTarget Build()=>new GameplaySkillContextTarget();
    }
    [Serializable,Preserve]public sealed class GameplaySpawnProjectileActionAuthoring:GameplaySkillActionAuthoring
    {
        public GameplayProjectileDefinitionAsset Projectile;
        [Tooltip("从触发事件地点发射。搜索可改选目标，仍保留父弹体命中事件的出生点。")]
        public bool LaunchFromTriggerPoint;
        public override void Validate(GameplayAuthoringValidationContext v,string p)=>v.Asset(Projectile,p+".Projectile");
        public override void ValidateReferences(GameplayAuthoringValidationContext v,string p)=>v.Asset(Projectile,p+".Projectile");
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context)=>new GameplaySpawnProjectileAction(context.ResolveDefinition(Projectile, this, nameof(Projectile), () => Projectile.Build(context)), LaunchFromTriggerPoint);
    }
    [Serializable,Preserve]public sealed class GameplaySpawnAreaActionAuthoring:GameplaySkillActionAuthoring
    {
        public GameplayAreaDefinitionAsset Area;
        public override void Validate(GameplayAuthoringValidationContext v,string p)=>v.Asset(Area,p+".Area");
        public override void ValidateReferences(GameplayAuthoringValidationContext v,string p)=>v.Asset(Area,p+".Area");
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context)=>new GameplaySpawnAreaAction(context.ResolveDefinition(Area, this, nameof(Area), () => Area.Build(context)));
    }
    [Serializable,Preserve]public sealed class GameplayMoveTargetsActionAuthoring:GameplaySkillActionAuthoring
    {
        public GameplayDisplacementMode Mode;
        [SerializeReference]public GameplayFloatValueAuthoring Distance=new GameplayConstantFloatValueAuthoring{Value=2};
        [SerializeReference]public GameplayFloatValueAuthoring Speed=new GameplayConstantFloatValueAuthoring{Value=4};
        public int Priority=10;
        public bool CancelOnAbilityEnd=true,FollowSource;
        public float MinimumSeparation,Duration;
        public bool UseControlGate;
        public GameplayTagQueryAuthoring Blocked=new GameplayTagQueryAuthoring();
        [SerializeReference]public GameplaySkillActionAuthoring[] OnArrive=Array.Empty<GameplaySkillActionAuthoring>();
        [SerializeReference]public GameplaySkillActionAuthoring[] OnCancelled=Array.Empty<GameplaySkillActionAuthoring>();
        public override void Validate(GameplayAuthoringValidationContext v,string p)
        {
            v.Enum(Mode,p+".Mode");GameplaySpatialAuthoringUtility.Value(Distance,v,p+".Distance");GameplaySpatialAuthoringUtility.Value(Speed,v,p+".Speed");Blocked?.Validate(v,p+".Blocked");
            if(!float.IsFinite(MinimumSeparation)||MinimumSeparation<0||!float.IsFinite(Duration)||Duration<0)v.Error(p,"位移间距与持续时间必须有限且非负。");
            GameplaySpatialAuthoringUtility.Validate(OnArrive,v,p+".OnArrive");GameplaySpatialAuthoringUtility.Validate(OnCancelled,v,p+".OnCancelled");
        }
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context)=>new GameplayMoveTargetsAction(Mode,Distance.Build(),Speed.Build(),Priority,UseControlGate?Blocked?.Build():null,
            GameplaySpatialAuthoringUtility.Build(OnArrive,context),GameplaySpatialAuthoringUtility.Build(OnCancelled,context),CancelOnAbilityEnd,FollowSource,MinimumSeparation,Duration);
    }
    [Serializable,Preserve]public sealed class GameplayRotateToPointActionAuthoring:GameplaySkillActionAuthoring
    {
        public bool RotateOwner=true;
        public override void Validate(GameplayAuthoringValidationContext v,string p) { }
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context)=>new GameplayRotateToPointAction(RotateOwner);
    }
}
