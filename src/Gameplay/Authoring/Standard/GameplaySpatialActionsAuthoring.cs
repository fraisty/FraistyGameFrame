using System;
using FRFrame.Gameplay.Extensions;
using FRFrame.Gameplay.Standard;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    [Serializable, Preserve]
    public sealed class GameplayFacingConditionAuthoring : GameplaySkillConditionAuthoring
    {
        [InspectorName("判定平面")] public GameplaySpatialPlane Plane;
        [InspectorName("夹角全角"), Range(1, 360)] public float Angle = 180;
        [InspectorName("读取目标实时位置")] public bool UseLiveTarget;
        public override void Validate(GameplayAuthoringValidationContext v, string p)
        {
            v.Enum(Plane, p + ".Plane");
            if (!(Angle > 0 && Angle <= 360)) v.Error(p + ".Angle", "夹角全角必须大于 0 且不超过 360°。");
        }
        public override IGameplaySkillCondition Build() => new GameplayFacingCondition(Plane, Angle, UseLiveTarget);
    }

    [Serializable, Preserve]
    public sealed class GameplayCaptureWorldPointActionAuthoring : GameplaySkillActionAuthoring
    {
        [InspectorName("搜索平面")] public GameplaySpatialPlane Plane;
        [SerializeReference, InspectorName("搜索与空场前向距离")]
        public GameplayFloatValueAuthoring Range = new GameplayConstantFloatValueAuthoring { Value = 9 };
        [InspectorName("自动搜索阵营")] public GameplayFactionFilter Faction = GameplayFactionFilter.Enemies;
        [InspectorName("自动搜索全角"), Range(1, 360), Tooltip("360° 为全方向。缩小角度后，仅从面向范围内选择最近目标。没有候选时沿朝向取空场落点。")]
        public float SearchAngle = 360;
        public override void Validate(GameplayAuthoringValidationContext v, string p)
        {
            v.Enum(Plane, p + ".Plane");
            v.Enum(Faction, p + ".Faction");
            if (!(SearchAngle > 0 && SearchAngle <= 360)) v.Error(p + ".SearchAngle", "搜索全角必须大于 0 且不超过 360°。");
            GameplaySpatialAuthoringUtility.Value(Range, v, p + ".Range");
        }
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context) =>
            new GameplayCaptureWorldPointAction(Plane, Range.Build(), Faction, SearchAngle);
    }

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
        [InspectorName("沿用时间轴启动时的捕获目标"), Tooltip("开启后，时间事件读取启动该编排的当前上下文。关闭后仍读取原触发事件。")]
        public bool UseActionContext;
        public override IGameplaySkillTarget Build()=>new GameplaySkillContextTarget(UseActionContext);
    }
    /// <summary>Targets the event publisher's actual actor or recipient. A missing or defeated actor rejects the rule.</summary>
    [Serializable,Preserve] public sealed class GameplaySkillEventActorTargetAuthoring : GameplaySkillTargetAuthoring
    {
        public enum Role
        {
            [InspectorName("事件发起者（攻击方）")] Instigator,
            [InspectorName("事件实际受者")] Recipient
        }
        [InspectorName("事件角色")] public Role Actor;
        public override void Validate(GameplayAuthoringValidationContext v,string path)
        { if (!Enum.IsDefined(typeof(Role), Actor)) v.Error(path + ".Actor", "请选择事件角色。"); }
        public override IGameplaySkillTarget Build() => new GameplaySkillEventActorTarget(Actor == Role.Instigator);
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
