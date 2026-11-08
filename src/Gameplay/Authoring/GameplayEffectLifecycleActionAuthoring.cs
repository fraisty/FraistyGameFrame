using System;
using System.Linq;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>One ordered action slot amongst the existing typed overflow executions.</summary>
    [Serializable, Preserve]
    public sealed class GameplayEffectOverflowActionsAuthoring : GameplayEffectOverflowAuthoring
    {
        [SerializeReference] public GameplaySkillActionAuthoring[] Actions = Array.Empty<GameplaySkillActionAuthoring>();
        public override void Validate(GameplayAuthoringValidationContext validation, string path) =>
            GameplayEffectLifecycleActionAuthoring.Validate(Actions, validation, path + ".Actions");
        public override IGameplayEffectOverflowExecution Build(GameplayAuthoringBuildContext context) =>
            new GameplayEffectLifecycleActions(Actions.Select(action => context.BuildAction(action)));
    }

    /// <summary>One ordered action slot amongst the existing natural-completion executions.</summary>
    [Serializable, Preserve]
    public sealed class GameplayEffectCompletionActionsAuthoring : GameplayEffectCompletionAuthoring
    {
        [SerializeReference] public GameplaySkillActionAuthoring[] Actions = Array.Empty<GameplaySkillActionAuthoring>();
        public override void Validate(GameplayAuthoringValidationContext validation, string path) =>
            GameplayEffectLifecycleActionAuthoring.Validate(Actions, validation, path + ".Actions");
        public override IGameplayEffectCompletionExecution Build(GameplayAuthoringBuildContext context) =>
            new GameplayEffectLifecycleActions(Actions.Select(action => context.BuildAction(action)));
    }

    internal static class GameplayEffectLifecycleActionAuthoring
    {
        internal static GameplayEffectLifecycleActions Build(GameplaySkillActionAuthoring[] actions, GameplayAuthoringBuildContext context) =>
            actions.Length == 0 ? null : new GameplayEffectLifecycleActions(actions.Select(action => context.BuildAction(action)));

        internal static void Validate(GameplaySkillActionAuthoring[] actions, GameplayAuthoringValidationContext validation, string path)
        {
            for (int i = 0; i < actions.Length; i++)
            {
                var action = actions[i];
                string location = path + "[" + i + "]";
                if (action?.SupportsEffectLifecycle != true)
                {
                    validation.Error(location, "此行为需要施放授权，不能用于 Effect 生命周期。");
                    continue;
                }
                action.Validate(validation, location);
                action.ValidateReferences(validation, location);
                if (action is GameplaySkillBranchActionAuthoring branch)
                    foreach (var nested in Graph.GameplayGraphFields.ActionArrays(branch))
                        Validate((GameplaySkillActionAuthoring[])nested.Value, validation, location + "." + nested.Path);
            }
        }
    }
}
