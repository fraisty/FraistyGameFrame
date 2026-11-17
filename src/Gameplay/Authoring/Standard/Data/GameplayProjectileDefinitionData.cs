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
        [InspectorName("施法者倒下时结束弹体")] public bool EndOnOwnerDeath;
        public Vector3 LaunchOffset,DestinationOffset;
        public float DirectionAngle;
        [InspectorName("沿角色朝向的发射偏移")] public float ForwardLaunchOffset;
        [InspectorName("按角色朝向直射")] public bool UseOwnerFacing;
        [InspectorName("接触采样")] public GameplayProjectileContactMode ContactMode;
        [InspectorName("定点采样间隔（秒）")] public float ContactStep = .02f;
        [InspectorName("命中后继续飞行")] public bool ContinueAfterHit;
        [InspectorName("贝塞尔控制方式")] public GameplayBezierControlMode BezierControl;
        [InspectorName("相对控制点比例"), Range(0,1)] public float ControlAlong = .5f;
        [InspectorName("相对控制点世界偏移"), Tooltip("启用距离自适应时，Y 分量由距离和下方范围决定。")] public Vector3 RelativeControlOffset = Vector3.up;
        [InspectorName("控制点随距离增高"), Min(0), Tooltip("每单位起终点距离对应的世界 Y 偏移。0 使用固定世界偏移。")] public float RelativeHeightScale;
        [InspectorName("控制点高度下限"), Min(0)] public float RelativeHeightMin;
        [InspectorName("控制点高度上限"), Min(0)] public float RelativeHeightMax = 1.8f;
        [InspectorName("偏移方向")] public GameplayCurveSide CurveSide;
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
        /// <summary>Creates the same motion definition for the workbench trajectory and the actual projectile.</summary>
        public GameplayProjectileMotionDefinition BuildMotion() =>
            new GameplayProjectileMotionDefinition(Motion,new GameplayPoint(Control1Offset.x,Control1Offset.y,Control1Offset.z),new GameplayPoint(Control2Offset.x,Control2Offset.y,Control2Offset.z),StraightDestination,BezierControl,ControlAlong,
                new GameplayPoint(RelativeControlOffset.x,RelativeControlOffset.y,RelativeControlOffset.z),CurveSide,
                RelativeHeightScale,RelativeHeightMin,RelativeHeightMax);
        public GameplayProjectileDefinition Build(GameplayAuthoringBuildContext context)=>context.Build(this,()=>new GameplayProjectileDefinition(
            BuildMotion(),
            Speed.Build(),Duration.Build(),HitRadius.Build(),MaxRange.Build(),CurveHeight.Build(),Plane,Faction,Tags?.Build(),LayerMask,MaxHits,Motion==GameplayProjectileMotionKind.Homing,
            GameplaySpatialAuthoringUtility.Build(OnHit,context),GameplaySpatialAuthoringUtility.Build(OnArrive,context),GameplaySpatialAuthoringUtility.Build(OnRange,context),GameplaySpatialAuthoringUtility.Build(OnCancelled,context),CreateView(context),
            CancelOnAbilityEnd,new GameplayPoint(LaunchOffset.x,LaunchOffset.y,LaunchOffset.z),new GameplayPoint(DestinationOffset.x,DestinationOffset.y,DestinationOffset.z),DirectionAngle,OnlyHitPrimaryTarget,
            ForwardLaunchOffset,UseOwnerFacing,ContactMode,ContactStep,ContinueAfterHit,EndOnOwnerDeath));
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
            v.Finite(ForwardLaunchOffset,p+".ForwardLaunchOffset");
            v.Enum(ContactMode,p+".ContactMode");
            if (ContactMode == GameplayProjectileContactMode.FixedEndpointNearest && (!float.IsFinite(ContactStep) || ContactStep <= 0))
                v.Error(p+".ContactStep","定点采样间隔必须是正数。");
            if (Motion == GameplayProjectileMotionKind.Bezier)
            {
                v.Enum(BezierControl,p+".BezierControl");
                if (BezierControl == GameplayBezierControlMode.RelativeQuadratic)
                {
                    v.Enum(CurveSide,p+".CurveSide");
                    if (!(ControlAlong >= 0 && ControlAlong <= 1)) v.Error(p+".ControlAlong","控制点比例应在 0 到 1 之间。");
                    v.Finite(RelativeControlOffset.x,p+".RelativeControlOffset.x");
                    v.Finite(RelativeControlOffset.y,p+".RelativeControlOffset.y");
                    v.Finite(RelativeControlOffset.z,p+".RelativeControlOffset.z");
                    if (!float.IsFinite(RelativeHeightScale) || RelativeHeightScale < 0)
                        v.Error(p+".RelativeHeightScale","随距离增高的比例必须有限且非负。");
                    if (RelativeHeightScale > 0 &&
                        (!float.IsFinite(RelativeHeightMin) || !float.IsFinite(RelativeHeightMax) ||
                         RelativeHeightMin < 0 || RelativeHeightMax < RelativeHeightMin))
                        v.Error(p+".RelativeHeightMax","控制点高度上下限必须有限、非负且有序。");
                }
            }
            foreach(var offset in new[]{Control1Offset,Control2Offset,LaunchOffset,DestinationOffset})if(!float.IsFinite(offset.x)||!float.IsFinite(offset.y)||!float.IsFinite(offset.z))v.Error(p,"Projectile offsets must be finite.");
        }
    
    }
}
