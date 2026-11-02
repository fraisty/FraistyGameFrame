using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Authored marker-time Skill step. Reuses Skill nodes and the same asset build session.</summary>
    [Serializable, Preserve]
    public sealed class GameplaySequenceSkillStepMarkerAuthoring : GameplaySequenceMarkerAuthoring
    {
        [SerializeReference] public GameplaySkillConditionAuthoring[] Conditions = Array.Empty<GameplaySkillConditionAuthoring>();
        [SerializeReference] public GameplaySkillTargetAuthoring Target;
        [SerializeReference] public GameplaySkillActionAuthoring[] Actions = Array.Empty<GameplaySkillActionAuthoring>();

        public override void Validate(GameplayAuthoringValidationContext validation, string path)
        {
            base.Validate(validation, path);
            if (Conditions == null) validation.Error(path + ".Conditions", "Condition list is null.");
            else for (int i = 0; i < Conditions.Length; i++)
                if (Conditions[i] == null) validation.Error(path + $".Conditions[{i}]", "Null condition.");
                else Conditions[i].Validate(validation, path + $".Conditions[{i}]");
            if (Target == null) validation.Error(path + ".Target", "Missing target.");
            else Target.Validate(validation, path + ".Target");
            if (Actions == null || Actions.Length == 0) validation.Error(path + ".Actions", "At least one action is required.");
            else for (int i = 0; i < Actions.Length; i++)
                if (Actions[i] == null) validation.Error(path + $".Actions[{i}]", "Null action.");
                else
                {
                    Actions[i].Validate(validation, path + $".Actions[{i}]");
                    Actions[i].ValidateReferences(validation, path + $".Actions[{i}]");
                }
        }

        public override GameplaySequenceMarkerDefinition Build(GameplayAuthoringBuildContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            var conditions = new List<IGameplaySkillCondition>();
            foreach (var condition in Conditions) conditions.Add(condition.Build());
            var actions = new List<IGameplaySkillAction>();
            foreach (var action in Actions) actions.Add(context.BuildAction(action));
            return new GameplaySequenceSkillStepMarkerDefinition(Time, conditions, Target.Build(), actions);
        }
    }
}
