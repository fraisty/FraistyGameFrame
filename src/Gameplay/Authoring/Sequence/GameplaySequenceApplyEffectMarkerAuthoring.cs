using System;
using FRFrame.Gameplay;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    [Serializable, Preserve]
    public sealed class GameplaySequenceApplyEffectMarkerAuthoring : GameplaySequenceMarkerAuthoring
    {
        public GameplayEffectDefinitionAsset Effect;
        public override void Validate(GameplayAuthoringValidationContext validation, string path)
        {
            base.Validate(validation, path);
            validation.Asset(Effect, path + ".Effect");
        }
        public override GameplaySequenceMarkerDefinition Build(GameplayAuthoringBuildContext context) =>
            new GameplaySequenceApplyEffectMarkerDefinition(Time, Effect.Build(context));
    }
}
