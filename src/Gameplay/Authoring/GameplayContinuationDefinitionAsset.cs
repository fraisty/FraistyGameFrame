using FRFrame.Gameplay.Authoring.Graph;
using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using UnityEngine;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Authored child callback list; the game owns callback routing and the child's disposable lifetime.</summary>
    [CreateAssetMenu(menuName="FRFrame Gameplay/Continuation Definition")]
    public sealed class GameplayContinuationDefinitionAsset : GameplayDefinitionAsset
    {
        [SerializeReference] public GameplaySkillActionAuthoring[] Actions = Array.Empty<GameplaySkillActionAuthoring>();

        public override object BuildSharedDefinition(GameplayAuthoringBuildContext context) => Build(context);
        public override GameplayLocalDefinitionPayload CaptureGraphValues() => CaptureValues();
        private GameplayContinuationDefinitionData CaptureValues() => new GameplayContinuationDefinitionData { Actions = Actions };
        public GameplayContinuationDefinition Build(GameplayAuthoringBuildContext context) => context.Build(this, () => CaptureValues().Build(context));
        protected override void ValidateContent(GameplayAuthoringValidationContext validation, string path) => CaptureValues().Validate(validation, path);

    }
}
