using FRFrame.Gameplay.Authoring.Graph;
using System;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Single projectile source with real motion/filter/callbacks. Unity resource binding
    /// extends this source through the optional view factory; mutable motion never lives in this SO.</summary>
    [System.Serializable, UnityEngine.Scripting.Preserve]
    public class GameplayProjectileDefinitionData:GameplayLocalDefinitionPayload
    {
        public override System.Type DefinitionType => typeof(FRFrame.Gameplay.Standard.GameplayProjectileDefinition);
        public override object BuildDefinition(GameplayAuthoringBuildContext context) => Build(context);

        public GameplayProjectileMotionKind Motion;
        [Tooltip("Direction: legacy range shot. CapturedDestination: stop at the cast point/target snapshot; not live following.")]
        public GameplayStraightDestinationMode StraightDestination;
        public GameplaySpatialPlane Plane;
        public bool CancelOnAbilityEnd=true;
        public Vector3 LaunchOffset,DestinationOffset;
        public float DirectionAngle;
        [SerializeReference] public GameplayFloatValueAuthoring Speed=new GameplayConstantFloatValueAuthoring{Value=8};
        [SerializeReference] public GameplayFloatValueAuthoring Duration=new GameplayConstantFloatValueAuthoring{Value=3};
        [SerializeReference] public GameplayFloatValueAuthoring HitRadius=new GameplayConstantFloatValueAuthoring{Value=.25f};
        [SerializeReference] public GameplayFloatValueAuthoring MaxRange=new GameplayConstantFloatValueAuthoring{Value=20};
        [SerializeReference] public GameplayFloatValueAuthoring CurveHeight=new GameplayConstantFloatValueAuthoring{Value=2};
        public Vector3 Control1Offset=new Vector3(2,2,0),Control2Offset=new Vector3(-2,2,0);
        public GameplayFactionFilter Faction=GameplayFactionFilter.Enemies;
        public GameplayTagQueryAuthoring Tags=new GameplayTagQueryAuthoring();
        public int LayerMask=-1;
        [Min(1)]public int MaxHits=1;
        [Tooltip("只命中当前主要目标。用于从另一个单位所在位置起飞、且不应碰到该单位的弹体。")]
        public bool OnlyHitPrimaryTarget;
        [SerializeReference] public GameplaySkillActionAuthoring[] OnHit=Array.Empty<GameplaySkillActionAuthoring>();
        [SerializeReference] public GameplaySkillActionAuthoring[] OnArrive=Array.Empty<GameplaySkillActionAuthoring>();
        [SerializeReference] public GameplaySkillActionAuthoring[] OnRange=Array.Empty<GameplaySkillActionAuthoring>();
        [SerializeReference] public GameplaySkillActionAuthoring[] OnCancelled=Array.Empty<GameplaySkillActionAuthoring>();
        public GameplayProjectileDefinition Build(GameplayAuthoringBuildContext context)=>context.Build(this,()=>new GameplayProjectileDefinition(
            new GameplayProjectileMotionDefinition(Motion,new GameplayPoint(Control1Offset.x,Control1Offset.y,Control1Offset.z),new GameplayPoint(Control2Offset.x,Control2Offset.y,Control2Offset.z),StraightDestination),
            Speed.Build(),Duration.Build(),HitRadius.Build(),MaxRange.Build(),CurveHeight.Build(),Plane,Faction,Tags?.Build(),LayerMask,MaxHits,Motion==GameplayProjectileMotionKind.Homing,
            GameplaySpatialAuthoringUtility.Build(OnHit,context),GameplaySpatialAuthoringUtility.Build(OnArrive,context),GameplaySpatialAuthoringUtility.Build(OnRange,context),GameplaySpatialAuthoringUtility.Build(OnCancelled,context),CreateView(context),
            CancelOnAbilityEnd,new GameplayPoint(LaunchOffset.x,LaunchOffset.y,LaunchOffset.z),new GameplayPoint(DestinationOffset.x,DestinationOffset.y,DestinationOffset.z),DirectionAngle,OnlyHitPrimaryTarget));
        protected virtual IGameplaySpatialViewFactory CreateView(GameplayAuthoringBuildContext context)=>null;
        protected override void ValidateContent(GameplayAuthoringValidationContext v,string p)
        {
            v.Enum(Motion,p+".Motion");v.Enum(StraightDestination,p+".StraightDestination");v.Enum(Plane,p+".Plane");v.Enum(Faction,p+".Faction");if(MaxHits<1)v.Error(p+".MaxHits","MaxHits must be positive.");
            GameplaySpatialAuthoringUtility.Value(Speed,v,p+".Speed");GameplaySpatialAuthoringUtility.Value(Duration,v,p+".Duration");
            GameplaySpatialAuthoringUtility.Value(HitRadius,v,p+".HitRadius");GameplaySpatialAuthoringUtility.Value(MaxRange,v,p+".MaxRange");GameplaySpatialAuthoringUtility.Value(CurveHeight,v,p+".CurveHeight");
            Tags?.Validate(v,p+".Tags");
            if(Motion==GameplayProjectileMotionKind.Straight&&StraightDestination==GameplayStraightDestinationMode.CapturedDestination)
                v.Warning(p+".StraightDestination","固定终点捕获目标位置（无目标时使用点位），不跟随移动目标。MaxHits优先于到达；MaxRange不足或Duration先耗尽分别Range/Completed，不执行OnArrive。请检查Speed×Duration和MaxRange能覆盖目标距离。");
            else if(Motion==GameplayProjectileMotionKind.Straight&&OnArrive!=null&&OnArrive.Length>0)
                v.Warning(p+".OnArrive","方向直射仅按射程/寿命结束，不触发OnArrive；需要到达回调请选择固定终点模式。");
            GameplaySpatialAuthoringUtility.Validate(OnHit,v,p+".OnHit");GameplaySpatialAuthoringUtility.Validate(OnArrive,v,p+".OnArrive");
            GameplaySpatialAuthoringUtility.Validate(OnRange,v,p+".OnRange");GameplaySpatialAuthoringUtility.Validate(OnCancelled,v,p+".OnCancelled");
            v.Finite(DirectionAngle,p+".DirectionAngle");
            foreach(var offset in new[]{Control1Offset,Control2Offset,LaunchOffset,DestinationOffset})if(!float.IsFinite(offset.x)||!float.IsFinite(offset.y)||!float.IsFinite(offset.z))v.Error(p,"Projectile offsets must be finite.");
        }
    
    }
}
