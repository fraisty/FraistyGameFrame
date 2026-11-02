using FRFrame.Gameplay.Authoring.Graph;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    [CreateAssetMenu(menuName="FRFrame/Gameplay/Spatial/Scene Projectile")]
    public sealed class GameplayUnityProjectileAsset:GameplayProjectileDefinitionAsset
    {
        public GameObject Prefab;

        public override GameplayLocalDefinitionPayload CaptureGraphValues() => CaptureValues();
        protected override GameplayProjectileDefinitionData CaptureValues() => new GameplayUnityProjectileData { Prefab = Prefab, Motion = Motion, StraightDestination = StraightDestination, Plane = Plane, CancelOnAbilityEnd = CancelOnAbilityEnd, LaunchOffset = LaunchOffset, DestinationOffset = DestinationOffset, DirectionAngle = DirectionAngle, Speed = Speed, Duration = Duration, HitRadius = HitRadius, MaxRange = MaxRange, CurveHeight = CurveHeight, Control1Offset = Control1Offset, Control2Offset = Control2Offset, Faction = Faction, Tags = Tags, LayerMask = LayerMask, MaxHits = MaxHits, OnHit = OnHit, OnArrive = OnArrive, OnRange = OnRange, OnCancelled = OnCancelled };
        protected override void ValidateContent(GameplayAuthoringValidationContext validation, string path) => CaptureValues().Validate(validation, path);

    }
}
