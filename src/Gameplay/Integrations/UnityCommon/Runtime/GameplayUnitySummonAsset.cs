using FRFrame.Gameplay.Authoring.Graph;
using System;
using System.Globalization;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Actual pooled prefab binding for a Standard summoned actor. The view lease is returned
    /// only after actor containers/presentation are disposed; a pooled view never retains old grants.</summary>
    [CreateAssetMenu(menuName="FRFrame/Gameplay/Spatial/Unity Summon")]
    public sealed class GameplayUnitySummonAsset:GameplaySummonDefinitionAsset
    {
        public GameObject Prefab;

        public GameplayFloatingTextAsset FloatingText;

        public override GameplayLocalDefinitionPayload CaptureGraphValues() => CaptureValues();
        protected override GameplaySummonDefinitionData CaptureValues() => new GameplayUnitySummonData { Prefab = Prefab, FloatingText = FloatingText, Profile = Profile, Skills = Skills, AutoCast = AutoCast, Duration = Duration, OnSpawn = OnSpawn, OnEnd = OnEnd };
        protected override void ValidateContent(GameplayAuthoringValidationContext validation, string path) => CaptureValues().Validate(validation, path);

    }
}
