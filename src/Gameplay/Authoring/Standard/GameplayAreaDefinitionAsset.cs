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
        protected virtual GameplayAreaDefinitionData CaptureValues() => new GameplayAreaDefinitionData { Shape = Shape, Plane = Plane, Origin = Origin, OriginOffset = OriginOffset, Radius = Radius, Angle = Angle, Length = Length, Width = Width, Height = Height, Duration = Duration, TickInterval = TickInterval, TickOnEnter = TickOnEnter, FollowOwner = FollowOwner, Faction = Faction, Tags = Tags, LayerMask = LayerMask, Cells = Cells, CellSize = CellSize, Aura = Aura, OnEnter = OnEnter, OnStay = OnStay, OnTick = OnTick, OnExit = OnExit, OnEnd = OnEnd, ArmingDelay = ArmingDelay, TriggerLimit = TriggerLimit };
        public FRFrame.Gameplay.Standard.GameplayAreaDefinition Build(GameplayAuthoringBuildContext context) => context.Build(this, () => CaptureValues().Build(context));
        protected override void ValidateContent(GameplayAuthoringValidationContext validation, string path) => CaptureValues().Validate(validation, path);

    }
}
