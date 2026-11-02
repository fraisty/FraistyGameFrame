using FRFrame.Gameplay.Authoring.Graph;
using System;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Single projectile source with real motion/filter/callbacks. Unity resource binding
    /// extends this source through the optional view factory; mutable motion never lives in this SO.</summary>
    [CreateAssetMenu(menuName="FRFrame/Gameplay/Spatial/Logical Projectile")]
    public class GameplayProjectileDefinitionAsset:GameplayDefinitionAsset
    {
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

        [SerializeReference] public GameplaySkillActionAuthoring[] OnHit=Array.Empty<GameplaySkillActionAuthoring>();

        [SerializeReference] public GameplaySkillActionAuthoring[] OnArrive=Array.Empty<GameplaySkillActionAuthoring>();

        [SerializeReference] public GameplaySkillActionAuthoring[] OnRange=Array.Empty<GameplaySkillActionAuthoring>();

        [SerializeReference] public GameplaySkillActionAuthoring[] OnCancelled=Array.Empty<GameplaySkillActionAuthoring>();

        public override object BuildSharedDefinition(GameplayAuthoringBuildContext context) => Build(context);
        public override GameplayLocalDefinitionPayload CaptureGraphValues() => CaptureValues();
        protected virtual GameplayProjectileDefinitionData CaptureValues() => new GameplayProjectileDefinitionData { Motion = Motion, StraightDestination = StraightDestination, Plane = Plane, CancelOnAbilityEnd = CancelOnAbilityEnd, LaunchOffset = LaunchOffset, DestinationOffset = DestinationOffset, DirectionAngle = DirectionAngle, Speed = Speed, Duration = Duration, HitRadius = HitRadius, MaxRange = MaxRange, CurveHeight = CurveHeight, Control1Offset = Control1Offset, Control2Offset = Control2Offset, Faction = Faction, Tags = Tags, LayerMask = LayerMask, MaxHits = MaxHits, OnHit = OnHit, OnArrive = OnArrive, OnRange = OnRange, OnCancelled = OnCancelled };
        public FRFrame.Gameplay.Standard.GameplayProjectileDefinition Build(GameplayAuthoringBuildContext context) => context.Build(this, () => CaptureValues().Build(context));
        protected override void ValidateContent(GameplayAuthoringValidationContext validation, string path) => CaptureValues().Validate(validation, path);

    }
}
