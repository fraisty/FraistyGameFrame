using FRFrame.Gameplay.Authoring.Graph;
using System;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Authoring
{
    public enum GameplayAreaAuthoringShape { Circle,Sector,Box,Cells }
    /// <summary>One region source, including contribution-owned Aura and real Enter/Stay/Tick/Exit/End callbacks.</summary>
    [CreateAssetMenu(menuName="FRFrame/Gameplay/Spatial/Logical Area")]
    public class GameplayAreaDefinitionAsset:GameplayDefinitionAsset
    {
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

        public GameplayEffectDefinitionAsset[] Aura=Array.Empty<GameplayEffectDefinitionAsset>();

        [SerializeReference]public GameplaySkillActionAuthoring[] OnEnter=Array.Empty<GameplaySkillActionAuthoring>();

        [SerializeReference]public GameplaySkillActionAuthoring[] OnStay=Array.Empty<GameplaySkillActionAuthoring>();

        [SerializeReference]public GameplaySkillActionAuthoring[] OnTick=Array.Empty<GameplaySkillActionAuthoring>();

        [SerializeReference]public GameplaySkillActionAuthoring[] OnExit=Array.Empty<GameplaySkillActionAuthoring>();

        [SerializeReference]public GameplaySkillActionAuthoring[] OnEnd=Array.Empty<GameplaySkillActionAuthoring>();

        [SerializeReference]public GameplayFloatValueAuthoring ArmingDelay=new GameplayConstantFloatValueAuthoring{Value=0};

        public int TriggerLimit;

        public override object BuildSharedDefinition(GameplayAuthoringBuildContext context) => Build(context);
        public override GameplayLocalDefinitionPayload CaptureGraphValues() => CaptureValues();
        protected virtual GameplayAreaDefinitionData CaptureValues()
        {
            var values = new GameplayAreaDefinitionData();
            CopyValuesTo(values);
            return values;
        }
        /// <summary>Copies the common Area fields for both logical and Unity presentation definitions.</summary>
        protected void CopyValuesTo(GameplayAreaDefinitionData values)
        {
            values.Shape = Shape; values.Plane = Plane; values.Origin = Origin; values.OriginOffset = OriginOffset;
            values.Radius = Radius; values.Angle = Angle; values.Length = Length; values.Width = Width; values.Height = Height;
            values.Duration = Duration; values.TickInterval = TickInterval; values.TickOnEnter = TickOnEnter;
            values.FollowOwner = FollowOwner; values.CancelOnAbilityEnd = CancelOnAbilityEnd; values.EndOnOwnerDeath = EndOnOwnerDeath;
            values.Faction = Faction; values.Tags = Tags; values.LayerMask = LayerMask; values.Cells = Cells; values.CellSize = CellSize;
            values.Aura = Aura; values.OnEnter = OnEnter; values.OnStay = OnStay; values.OnTick = OnTick;
            values.OnExit = OnExit; values.OnEnd = OnEnd; values.ArmingDelay = ArmingDelay; values.TriggerLimit = TriggerLimit;
        }
        public FRFrame.Gameplay.Standard.GameplayAreaDefinition Build(GameplayAuthoringBuildContext context) => context.Build(this, () => CaptureValues().Build(context));
        protected override void ValidateContent(GameplayAuthoringValidationContext validation, string path) => CaptureValues().Validate(validation, path);

    }
}
