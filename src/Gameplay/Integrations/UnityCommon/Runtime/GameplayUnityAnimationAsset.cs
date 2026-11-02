using FRFrame.Gameplay.Authoring.Graph;
using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Authoring;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Actual Animator state or manual Playables clip. The subject may render sprites or
    /// meshes; the same definition and clip are used for preview and Runtime. Movement remains a rule action.</summary>
    [CreateAssetMenu(menuName="FRFrame/Gameplay/Animation/Animator or Playables")]
    public sealed class GameplayUnityAnimationAsset:GameplayAnimationAsset
    {
        public PlaybackBackend Backend;

        public GameObject PreviewPrefab;

        public AnimationClip Clip;

        public string StateName;

        public int Layer;

        [Min(0)]public float BlendDuration;

        public LevelVariant[] Variants=Array.Empty<LevelVariant>();

        public bool WeightedVariants;

        public enum PlaybackBackend { AnimatorState, PlayablesClip }

        [Serializable] public sealed class LevelVariant { public int MinimumLevel=1; public AnimationClip Clip; public string StateName; public float Weight=1; }
        public override GameplayLocalDefinitionPayload CaptureGraphValues() => CaptureValues();
        private GameplayUnityAnimationData CaptureValues() => new GameplayUnityAnimationData { Backend = Backend, PreviewPrefab = PreviewPrefab, Clip = Clip, StateName = StateName, Layer = Layer, BlendDuration = BlendDuration, Variants = Variants, WeightedVariants = WeightedVariants, Channel = Channel, Priority = Priority, Loop = Loop, RestorePrevious = RestorePrevious, Speed = Speed };
        public override string BackendName => CaptureValues().BackendName;
        public override GameObject CreatePreviewSubject() => CaptureValues().CreatePreviewSubject();
        protected override GameplayAnimationDefinition CreateDefinition(GameplayAuthoringBuildContext context) => CaptureValues().Build(context);
        protected override void ValidateAnimation(GameplayAuthoringValidationContext validation, string path) => CaptureValues().Validate(validation, path);

    }
}
