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
        public bool FitCircleToRadius;
        [Min(0)] public float FadeSeconds;

        public override GameplayLocalDefinitionPayload CaptureGraphValues() => CaptureValues();
        protected override GameplayAreaDefinitionData CaptureValues()
        {
            var values = new GameplayUnityAreaData { Prefab = Prefab, FitCircleToRadius = FitCircleToRadius, FadeSeconds = FadeSeconds };
            CopyValuesTo(values);
            return values;
        }
        protected override void ValidateContent(GameplayAuthoringValidationContext validation, string path) => CaptureValues().Validate(validation, path);

    }
}
