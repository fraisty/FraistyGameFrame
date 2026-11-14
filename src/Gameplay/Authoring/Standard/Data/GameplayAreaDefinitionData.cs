using System.Linq;
using FRFrame.Gameplay.Authoring.Graph;
using System;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>One region source, including contribution-owned Aura and real Enter/Stay/Tick/Exit/End callbacks.</summary>
    [System.Serializable, UnityEngine.Scripting.Preserve]
    public class GameplayAreaDefinitionData:GameplayLocalDefinitionPayload
    {
        public override System.Type DefinitionType => typeof(FRFrame.Gameplay.Standard.GameplayAreaDefinition);
        public override object BuildDefinition(GameplayAuthoringBuildContext context) => Build(context);

        public GameplayAreaAuthoringShape Shape;
        public GameplaySpatialPlane Plane;
        public GameplaySearchOrigin Origin;
        public Vector3 OriginOffset;
        [SerializeReference] public GameplayFloatValueAuthoring Radius=new GameplayConstantFloatValueAuthoring{Value=2};
        [SerializeReference] public GameplayFloatValueAuthoring Angle=new GameplayConstantFloatValueAuthoring{Value=120};
        [SerializeReference] public GameplayFloatValueAuthoring Length=new GameplayConstantFloatValueAuthoring{Value=4};
        [SerializeReference] public GameplayFloatValueAuthoring Width=new GameplayConstantFloatValueAuthoring{Value=2};
        [SerializeReference] public GameplayFloatValueAuthoring Height=new GameplayConstantFloatValueAuthoring{Value=1};
        [SerializeReference] public GameplayFloatValueAuthoring Duration=new GameplayConstantFloatValueAuthoring{Value=4};
        [SerializeReference] public GameplayFloatValueAuthoring TickInterval=new GameplayConstantFloatValueAuthoring{Value=.5f};
        public bool TickOnEnter=true,FollowOwner;
        [Tooltip("开启时技能结束或取消即移除区域。关闭时区域由世界托管，继续执行区域回调。")]
        public bool CancelOnAbilityEnd = true;
        [Tooltip("施法者失去战斗资格时结束区域并释放全部 Aura 贡献。")]
        public bool EndOnOwnerDeath;
        public GameplayFactionFilter Faction=GameplayFactionFilter.Enemies;
        public GameplayTagQueryAuthoring Tags=new GameplayTagQueryAuthoring();
        public int LayerMask=-1;
        public Vector3[] Cells=Array.Empty<Vector3>();
        public float CellSize=1;
        [Tooltip("区域对每个成员持有独立的持续效果贡献，离开或结束时只解除自己的贡献。")]
        public GameplayEffectDefinitionAsset[] Aura=Array.Empty<GameplayEffectDefinitionAsset>();
        [SerializeReference]public GameplaySkillActionAuthoring[] OnEnter=Array.Empty<GameplaySkillActionAuthoring>();
        [SerializeReference]public GameplaySkillActionAuthoring[] OnStay=Array.Empty<GameplaySkillActionAuthoring>();
        [SerializeReference]public GameplaySkillActionAuthoring[] OnTick=Array.Empty<GameplaySkillActionAuthoring>();
        [SerializeReference]public GameplaySkillActionAuthoring[] OnExit=Array.Empty<GameplaySkillActionAuthoring>();
        [SerializeReference]public GameplaySkillActionAuthoring[] OnEnd=Array.Empty<GameplaySkillActionAuthoring>();
        [SerializeReference]public GameplayFloatValueAuthoring ArmingDelay=new GameplayConstantFloatValueAuthoring{Value=0};
        public int TriggerLimit;
        public GameplayAreaDefinition Build(GameplayAuthoringBuildContext context)=>context.Build(this,()=>new GameplayAreaDefinition(
            Shape==GameplayAreaAuthoringShape.Cells?GameplaySearchShape.GridCells:Shape==GameplayAreaAuthoringShape.Box?GameplaySearchShape.Box:Shape==GameplayAreaAuthoringShape.Sector?GameplaySearchShape.Sector:GameplaySearchShape.Circle,
            Plane,Origin,Radius.Build(),Angle.Build(),Length.Build(),Width.Build(),Height.Build(),Duration.Build(),TickInterval.Build(),TickOnEnter,FollowOwner,Faction,Tags?.Build(),LayerMask,
            Enumerable.Range(0, Aura.Length).Select(i => context.ResolveDefinition(Aura[i], this, "Aura.Array.data[" + i + "]", () => Aura[i].Build(context))).ToArray(),GameplaySpatialAuthoringUtility.Build(OnEnter,context),GameplaySpatialAuthoringUtility.Build(OnTick,context),
            GameplaySpatialAuthoringUtility.Build(OnExit,context),GameplaySpatialAuthoringUtility.Build(OnEnd,context),Array.ConvertAll(Cells,c=>new GameplayPoint(c.x,c.y,c.z)),CellSize,CreateView(context),GameplaySpatialAuthoringUtility.Build(OnStay,context),ArmingDelay.Build(),TriggerLimit,new GameplayPoint(OriginOffset.x,OriginOffset.y,OriginOffset.z),EndOnOwnerDeath,CancelOnAbilityEnd));
        protected virtual IGameplaySpatialViewFactory CreateView(GameplayAuthoringBuildContext context)=>null;
        protected override void ValidateContent(GameplayAuthoringValidationContext v,string p)
        {
            v.Enum(Shape,p+".Shape");v.Enum(Plane,p+".Plane");v.Enum(Origin,p+".Origin");v.Enum(Faction,p+".Faction");
            if(!float.IsFinite(OriginOffset.x)||!float.IsFinite(OriginOffset.y)||!float.IsFinite(OriginOffset.z))v.Error(p+".OriginOffset","原点偏移必须有限。");
            GameplaySpatialAuthoringUtility.Value(ArmingDelay,v,p+".ArmingDelay");if(TriggerLimit<0)v.Error(p+".TriggerLimit","TriggerLimit cannot be negative.");
            GameplaySpatialAuthoringUtility.Value(Radius,v,p+".Radius");GameplaySpatialAuthoringUtility.Value(Angle,v,p+".Angle");
            GameplaySpatialAuthoringUtility.Value(Length,v,p+".Length");GameplaySpatialAuthoringUtility.Value(Width,v,p+".Width");GameplaySpatialAuthoringUtility.Value(Height,v,p+".Height");
            GameplaySpatialAuthoringUtility.Value(Duration,v,p+".Duration");GameplaySpatialAuthoringUtility.Value(TickInterval,v,p+".TickInterval");Tags?.Validate(v,p+".Tags");
            if(Aura==null)v.Error(p+".Aura","Aura list is null.");else foreach(var effect in Aura){v.Asset(effect,p+".Aura");if(effect!=null&&effect.DurationPolicy==GameplayEffectDurationPolicy.Instant)v.Error(p+".Aura","Aura requires persistent effects.");}
            if(Cells==null||Shape==GameplayAreaAuthoringShape.Cells&&Cells.Length==0)v.Error(p+".Cells","Cells shape requires coordinates.");
            if(!float.IsFinite(CellSize)||CellSize<=0)v.Error(p+".CellSize","CellSize must be positive.");
            GameplaySpatialAuthoringUtility.Validate(OnEnter,v,p+".OnEnter");GameplaySpatialAuthoringUtility.Validate(OnStay,v,p+".OnStay");GameplaySpatialAuthoringUtility.Validate(OnTick,v,p+".OnTick");
            GameplaySpatialAuthoringUtility.Validate(OnExit,v,p+".OnExit");GameplaySpatialAuthoringUtility.Validate(OnEnd,v,p+".OnEnd");
        }
    
    }
}
