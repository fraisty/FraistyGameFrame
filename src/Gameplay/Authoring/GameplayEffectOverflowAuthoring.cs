using System;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Player-safe overflow extension participating in the existing recursive build/reference workflow.</summary>
    [Serializable]
    public abstract class GameplayEffectOverflowAuthoring
    {
        [UnityEngine.HideInInspector,Graph.GameplayAuthoringIdentity] public string AuthoringId=Graph.GameplayAuthoringGraph.NewId();
        public abstract void Validate(GameplayAuthoringValidationContext validation, string path);
        public virtual void ValidateReferences(GameplayAuthoringValidationContext validation, string path) { }
        public abstract IGameplayEffectOverflowExecution Build(GameplayAuthoringBuildContext context);
    }

    [Serializable, Preserve]
    public sealed class GameplayApplyEffectOnOverflowAuthoring : GameplayEffectOverflowAuthoring
    {
        public GameplayEffectDefinitionAsset Effect;
        public override void Validate(GameplayAuthoringValidationContext validation, string path) { }
        public override void ValidateReferences(GameplayAuthoringValidationContext validation, string path) => validation.Asset(Effect, path + ".Effect");
        public override IGameplayEffectOverflowExecution Build(GameplayAuthoringBuildContext context) => new ApplyExecution(context.ResolveDefinition(Effect, this, nameof(Effect), () => Effect.Build(context)));
        private sealed class ApplyExecution : IGameplayEffectOverflowExecution
        {
            private readonly GameplayEffectDefinition _effect;
            internal ApplyExecution(GameplayEffectDefinition effect) => _effect = effect;
            public void Execute(GameplayEffectOverflowContext context) => context.Apply(_effect);
        }
    }

    [Serializable, Preserve]
    public sealed class GameplayRemoveCurrentOnOverflowAuthoring : GameplayEffectOverflowAuthoring
    {
        public override void Validate(GameplayAuthoringValidationContext validation, string path) { }
        public override IGameplayEffectOverflowExecution Build(GameplayAuthoringBuildContext context) => new RemoveExecution();
        private sealed class RemoveExecution : IGameplayEffectOverflowExecution
        { public void Execute(GameplayEffectOverflowContext context) => context.RemoveCurrent(); }
    }
}
