using System;
using FRFrame.Gameplay.Standard;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    [Serializable, Preserve]
    public sealed class GameplayOwnedSequenceActionAuthoring : GameplaySkillActionAuthoring
    {
        public GameplaySequenceDefinitionAsset Sequence;
        public override void Validate(GameplayAuthoringValidationContext v, string p) => v.Asset(Sequence, p + ".Sequence");
        public override void ValidateReferences(GameplayAuthoringValidationContext v, string p) => v.Asset(Sequence, p + ".Sequence");
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context) => new GameplayOwnedSequenceAction(context.ResolveDefinition(Sequence, this, nameof(Sequence), () => Sequence.Build(context)));
    }
    [Serializable, Preserve]
    public sealed class GameplayEndAbilityActionAuthoring : GameplaySkillActionAuthoring
    {
        public bool Cancel;
        public override void Validate(GameplayAuthoringValidationContext v, string p) { }
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context) => new GameplayEndAbilityAction(Cancel);
    }
}
