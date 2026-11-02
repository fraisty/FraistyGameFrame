using System;
using FRFrame.Gameplay;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    [Serializable, Preserve]
    public sealed class GameplaySkillPlaySequenceActionAuthoring : GameplaySkillActionAuthoring
    {
        public GameplaySequenceDefinitionAsset Sequence;
        public override void Validate(GameplayAuthoringValidationContext validation, string path) =>
            ValidateReferences(validation, path);
        public override void ValidateReferences(GameplayAuthoringValidationContext validation, string path) =>
            validation.Asset(Sequence, path + ".Sequence");
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context) =>
            new GameplaySkillPlaySequenceAction(context.ResolveDefinition(Sequence, this, nameof(Sequence), () => Sequence.Build(context)));
    }
}
