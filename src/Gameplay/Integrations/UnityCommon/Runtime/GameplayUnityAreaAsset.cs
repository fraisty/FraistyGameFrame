using FRFrame.Gameplay.Authoring.Graph;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    [CreateAssetMenu(menuName="FRFrame/Gameplay/Spatial/Scene Area")]
    public sealed class GameplayUnityAreaAsset:GameplayAreaDefinitionAsset
    {
        public GameObject Prefab;

        public override GameplayLocalDefinitionPayload CaptureGraphValues() => CaptureValues();
        protected override GameplayAreaDefinitionData CaptureValues() => new GameplayUnityAreaData { Prefab = Prefab, Shape = Shape, Plane = Plane, Origin = Origin, OriginOffset = OriginOffset, Radius = Radius, Angle = Angle, Length = Length, Width = Width, Height = Height, Duration = Duration, TickInterval = TickInterval, TickOnEnter = TickOnEnter, FollowOwner = FollowOwner, Faction = Faction, Tags = Tags, LayerMask = LayerMask, Cells = Cells, CellSize = CellSize, Aura = Aura, OnEnter = OnEnter, OnStay = OnStay, OnTick = OnTick, OnExit = OnExit, OnEnd = OnEnd, ArmingDelay = ArmingDelay, TriggerLimit = TriggerLimit };
        protected override void ValidateContent(GameplayAuthoringValidationContext validation, string path) => CaptureValues().Validate(validation, path);

    }
}
