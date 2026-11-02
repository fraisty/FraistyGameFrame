using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    [CreateAssetMenu(menuName = "FRFrame Gameplay/Skill Definition")]
    public sealed class GameplaySkillDefinitionAsset : GameplayDefinitionAsset
    {
        [SerializeField] private string displayName;
        /// <summary>Presentation-only label; it never enters the built rule definition.</summary>
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? name : displayName;
        public bool Once;
        [Tooltip("主动技能的正式 Cast 入口保留施放来源、等级与参数；普通旧事件技能默认保持 Owner Context。")]
        public bool PreserveTriggerContext;
        [SerializeReference] public GameplaySkillTriggerAuthoring Trigger;
        [SerializeReference] public GameplaySkillConditionAuthoring[] Conditions = Array.Empty<GameplaySkillConditionAuthoring>();
        [SerializeReference] public GameplaySkillTargetAuthoring Target;
        [SerializeReference] public GameplaySkillActionAuthoring[] Actions = Array.Empty<GameplaySkillActionAuthoring>();

        public override object BuildSharedDefinition(GameplayAuthoringBuildContext context) => Build(context);
        protected override void ValidateContent(GameplayAuthoringValidationContext v, string p)
        {
            if (Trigger == null) v.Error(p + ".Trigger", "Missing trigger."); else Trigger.Validate(v, p + ".Trigger");
            if (Conditions == null) v.Error(p + ".Conditions", "Condition list is null.");
            else for (int i = 0; i < Conditions.Length; i++)
                if (Conditions[i] == null) v.Error(p + $".Conditions[{i}]", "Null condition.");
                else Conditions[i].Validate(v, p + $".Conditions[{i}]");
            if (Target == null) v.Error(p + ".Target", "Missing target."); else Target.Validate(v, p + ".Target");
            if (Actions == null || Actions.Length == 0) v.Error(p + ".Actions", "At least one action is required.");
            else for (int i = 0; i < Actions.Length; i++)
                if (Actions[i] == null) v.Error(p + $".Actions[{i}]", "Null action.");
                else Actions[i].Validate(v, p + $".Actions[{i}]");
        }
        public GameplaySkillDefinition Build(GameplayAuthoringBuildContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return context.Build(this, () =>
            {
                var conditions = new List<IGameplaySkillCondition>();
                foreach (var item in Conditions) conditions.Add(item.Build());
                var actions = new List<IGameplaySkillAction>();
                foreach (var item in Actions) actions.Add(context.BuildAction(item));
                return new GameplaySkillDefinition(Trigger.Build(), conditions, Target.Build(), actions, Once, PreserveTriggerContext);
            });
        }
    }
}
