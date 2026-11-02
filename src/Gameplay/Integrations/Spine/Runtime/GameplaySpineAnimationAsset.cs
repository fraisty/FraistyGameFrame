using FRFrame.Gameplay.Authoring.Graph;
using System;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Unity;
using Spine.Unity;
using UnityEngine;

namespace FRFrame.Gameplay.Spine
{
    /// <summary>One real Spine animation source shared by Runtime and preview. Level variants select
    /// the highest eligible minimum level; optional weights share captured selection with Preview.
    /// No mutable TrackEntry lives here.</summary>
    [CreateAssetMenu(menuName="FRFrame/Gameplay/Animation/Spine")]
    public sealed class GameplaySpineAnimationAsset : GameplayAnimationAsset
    {
        public SkeletonDataAsset Skeleton;

        public string AnimationName;

        public int TrackIndex;

        [Min(0)]public float BlendDuration;

        /// <summary>Optional starting pose of the disposable preview/Lab subject. Runtime animation
        /// commands always blend from the actual host track; this does not replace AnimationName.</summary>
        public string PreviewInitialAnimation;

        public string[] SkinNames = Array.Empty<string>();

        public Material Material;

        public LevelVariant[] Variants = Array.Empty<LevelVariant>();

        public bool WeightedVariants;

        [Serializable] public sealed class LevelVariant { public int MinimumLevel=1; public string AnimationName; public float Weight=1; }
        public override GameplayLocalDefinitionPayload CaptureGraphValues() => CaptureValues();
        private GameplaySpineAnimationData CaptureValues() => new GameplaySpineAnimationData { Skeleton = Skeleton, AnimationName = AnimationName, TrackIndex = TrackIndex, BlendDuration = BlendDuration, PreviewInitialAnimation = PreviewInitialAnimation, SkinNames = SkinNames, Material = Material, Variants = Variants, WeightedVariants = WeightedVariants, Channel = Channel, Priority = Priority, Loop = Loop, RestorePrevious = RestorePrevious, Speed = Speed };
        public override string BackendName => CaptureValues().BackendName;
        public override GameObject CreatePreviewSubject() => CaptureValues().CreatePreviewSubject();
        protected override GameplayAnimationDefinition CreateDefinition(GameplayAuthoringBuildContext context) => CaptureValues().Build(context);
        protected override void ValidateAnimation(GameplayAuthoringValidationContext validation, string path) => CaptureValues().Validate(validation, path);

    }
}
