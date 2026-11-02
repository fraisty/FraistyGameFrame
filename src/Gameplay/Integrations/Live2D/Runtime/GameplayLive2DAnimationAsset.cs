using FRFrame.Gameplay.Authoring.Graph;
using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Unity;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.Pose;
using Live2D.Cubism.Rendering;
using UnityEngine;

namespace FRFrame.Gameplay.Live2D
{
    /// <summary>Actual Cubism motion clip sampling and native model update, driven by the existing
    /// presentation delta. Automatic procedural/motion writers are paused only while this lease owns the model.
    /// This deterministic clip backend does not reproduce Cubism's motion fade controller.</summary>
    [CreateAssetMenu(menuName="FRFrame/Gameplay/Animation/Live2D")]
    public sealed class GameplayLive2DAnimationAsset:GameplayAnimationAsset
    {
        public GameObject ModelPrefab;

        public AnimationClip Motion;

        public override GameplayLocalDefinitionPayload CaptureGraphValues() => CaptureValues();
        private GameplayLive2DAnimationData CaptureValues() => new GameplayLive2DAnimationData { ModelPrefab = ModelPrefab, Motion = Motion, Channel = Channel, Priority = Priority, Loop = Loop, RestorePrevious = RestorePrevious, Speed = Speed };
        public override string BackendName => CaptureValues().BackendName;
        public override string CapabilityNote => CaptureValues().CapabilityNote;
        public override GameObject CreatePreviewSubject() => CaptureValues().CreatePreviewSubject();
        public override void ConfigurePreviewCamera(Camera camera) => CaptureValues().ConfigurePreviewCamera(camera);
        protected override GameplayAnimationDefinition CreateDefinition(GameplayAuthoringBuildContext context) => CaptureValues().Build(context);
        protected override void ValidateAnimation(GameplayAuthoringValidationContext validation, string path) => CaptureValues().Validate(validation, path);

    }
}
