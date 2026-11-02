using System;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Player-safe completion extension. References participate in recursive validation and one build session.</summary>
    [Serializable]
    public abstract class GameplayEffectCompletionAuthoring
    {
        [UnityEngine.HideInInspector,Graph.GameplayAuthoringIdentity] public string AuthoringId=Graph.GameplayAuthoringGraph.NewId();
        public abstract void Validate(GameplayAuthoringValidationContext validation, string path);
        public virtual void ValidateReferences(GameplayAuthoringValidationContext validation, string path) { }
        public abstract IGameplayEffectCompletionExecution Build(GameplayAuthoringBuildContext context);
    }

    /// <summary>Generic natural-expiry continuation applying a referenced effect to the same target.</summary>
    [Serializable, Preserve]
    public sealed class GameplayApplyEffectOnCompletionAuthoring : GameplayEffectCompletionAuthoring
    {
        public GameplayEffectDefinitionAsset Effect;
        public bool OwnedByAbility;
        /// <summary>Optional explicit installed skill whose currently active grant owns the continuation.
        /// Zero retains the original activation. Used when a cooldown survives passive disengagement.</summary>
        public int OwnerSkillId;
        public override void Validate(GameplayAuthoringValidationContext validation, string path)
        {if(OwnerSkillId<0||OwnerSkillId>0&&!OwnedByAbility)validation.Error(path,"续接技能ID必须非负且使用技能所有权。");}
        public override void ValidateReferences(GameplayAuthoringValidationContext validation, string path) =>
            validation.Asset(Effect, path + ".Effect");
        public override IGameplayEffectCompletionExecution Build(GameplayAuthoringBuildContext context) =>
            OwnedByAbility?(IGameplayEffectCompletionExecution)new FRFrame.Gameplay.Standard.GameplayApplyOwnedEffectOnCompletionExecution(context.ResolveDefinition(Effect, this, nameof(Effect), () => Effect.Build(context)),OwnerSkillId):
                new GameplayApplyEffectOnCompletionExecution(context.ResolveDefinition(Effect, this, nameof(Effect), () => Effect.Build(context)));
    }

    /// <summary>Built immutable continuation; no rule clock or mutable target is held in the execution.</summary>
    public sealed class GameplayApplyEffectOnCompletionExecution : IGameplayEffectCompletionExecution
    {
        public GameplayEffectDefinition Effect { get; }
        public GameplayApplyEffectOnCompletionExecution(GameplayEffectDefinition effect) =>
            Effect = effect ?? throw new ArgumentNullException(nameof(effect));
        public void Execute(GameplayEffectCompletionContext completion) => completion.Apply(Effect);
    }
}
