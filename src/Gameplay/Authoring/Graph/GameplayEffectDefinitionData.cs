using FRFrame.Gameplay.Authoring.Graph;
using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using UnityEngine;

namespace FRFrame.Gameplay.Authoring
{
    [System.Serializable, UnityEngine.Scripting.Preserve]
    public sealed class GameplayEffectDefinitionData : GameplayLocalDefinitionPayload
    {
        public override System.Type DefinitionType => typeof(GameplayEffectDefinition);
        public override object BuildDefinition(GameplayAuthoringBuildContext context) => Build(context);

        public GameplayEffectDurationPolicy DurationPolicy = GameplayEffectDurationPolicy.Instant;
        public float Duration;
        public float Period;
        public GameplayTagReference[] EffectTags = Array.Empty<GameplayTagReference>();
        public GameplayEffectPeriodicStartPolicy PeriodicStart;
        [SerializeReference] public GameplayFloatValueAuthoring DurationValue;
        [SerializeReference] public GameplayFloatValueAuthoring PeriodValue;
        public GameplayEffectModifierAuthoring[] Modifiers = Array.Empty<GameplayEffectModifierAuthoring>();
        public GameplayTagReference[] GrantedTags = Array.Empty<GameplayTagReference>();
        [SerializeReference] public GameplayEffectStackingAuthoring Stacking;
        [SerializeReference] public GameplayTagQueryAuthoring ApplicationRequirements;
        [SerializeReference] public GameplayTagQueryAuthoring OngoingRequirements;
        [SerializeReference] public GameplayEffectExecutionAuthoring[] Executions = Array.Empty<GameplayEffectExecutionAuthoring>();
        /// <summary>Only Duration natural expiry runs these, after modifiers, tags and handle have been removed.</summary>
        [SerializeReference] public GameplayEffectCompletionAuthoring[] CompletionExecutions = Array.Empty<GameplayEffectCompletionAuthoring>();
        [SerializeReference] public GameplayEffectPresentationAuthoring Presentation;
        [SerializeReference] public GameplaySkillActionAuthoring[] OnApplied = Array.Empty<GameplaySkillActionAuthoring>();
        [SerializeReference] public GameplaySkillActionAuthoring[] OnStackChanged = Array.Empty<GameplaySkillActionAuthoring>();
        [SerializeReference] public GameplaySkillActionAuthoring[] OnEnterMax = Array.Empty<GameplaySkillActionAuthoring>();
        [SerializeReference] public GameplaySkillActionAuthoring[] OnPeriod = Array.Empty<GameplaySkillActionAuthoring>();

        protected override void ValidateContent(GameplayAuthoringValidationContext v, string p)
        {
            v.Enum(DurationPolicy, p + ".DurationPolicy"); v.Finite(Duration, p + ".Duration"); v.Finite(Period, p + ".Period");
            if (DurationPolicy == GameplayEffectDurationPolicy.Duration && Duration <= 0) v.Error(p + ".Duration", "Duration must be positive.");
            if (Period < 0) v.Error(p + ".Period", "Period must be nonnegative.");
            v.Enum(PeriodicStart, p + ".PeriodicStart");
            DurationValue?.Validate(v, p + ".DurationValue"); PeriodValue?.Validate(v, p + ".PeriodValue");
            if (DurationValue != null && DurationPolicy != GameplayEffectDurationPolicy.Duration) v.Error(p + ".DurationValue", "Requires Duration policy.");
            if ((PeriodValue != null || PeriodicStart != GameplayEffectPeriodicStartPolicy.AfterFirstPeriod) &&
                (DurationPolicy == GameplayEffectDurationPolicy.Instant || Period <= 0)) v.Error(p + ".Period", "Dynamic period/immediate start require periodic persistent effect.");
            if (Stacking?.Lifetime == GameplayEffectStackLifetimePolicy.IndependentLayerLifetime && DurationPolicy != GameplayEffectDurationPolicy.Duration)
                v.Error(p + ".Stacking.Lifetime", "Independent layer lifetime requires Duration policy.");
            if (EffectTags == null) v.Error(p + ".EffectTags", "Null classification list.");
            else for (int i = 0; i < EffectTags.Length; i++) v.Tag(EffectTags[i], p + $".EffectTags[{i}]");
            if (Modifiers == null) v.Error(p + ".Modifiers", "Modifier list is null.");
            else for (int i = 0; i < Modifiers.Length; i++)
                if (Modifiers[i] == null) v.Error(p + $".Modifiers[{i}]", "Null modifier.");
                else Modifiers[i].Validate(v, p + $".Modifiers[{i}]");
            if (GrantedTags == null) v.Error(p + ".GrantedTags", "Tag list is null.");
            else for (int i = 0; i < GrantedTags.Length; i++) v.Tag(GrantedTags[i], p + $".GrantedTags[{i}]");
            Stacking?.Validate(v, p + ".Stacking");
            ApplicationRequirements?.Validate(v, p + ".ApplicationRequirements");
            OngoingRequirements?.Validate(v, p + ".OngoingRequirements");
            Presentation?.Validate(v, p + ".Presentation");
            GameplayEffectLifecycleActionAuthoring.Validate(OnApplied, v, p + ".OnApplied");
            GameplayEffectLifecycleActionAuthoring.Validate(OnStackChanged, v, p + ".OnStackChanged");
            GameplayEffectLifecycleActionAuthoring.Validate(OnEnterMax, v, p + ".OnEnterMax");
            GameplayEffectLifecycleActionAuthoring.Validate(OnPeriod, v, p + ".OnPeriod");
            if (Stacking == null && (OnStackChanged.Length > 0 || OnEnterMax.Length > 0))
                v.Error(p + ".Stacking", "层数变化和进入满层需要叠层策略。");
            if (CompletionExecutions == null) v.Error(p + ".CompletionExecutions", "Completion list is null.");
            else
            {
                if (CompletionExecutions.Length > 0 && DurationPolicy != GameplayEffectDurationPolicy.Duration)
                    v.Error(p + ".CompletionExecutions", "Only Duration effects can have natural completion.");
                for (int i = 0; i < CompletionExecutions.Length; i++)
                {
                    string path = p + $".CompletionExecutions[{i}]";
                    if (CompletionExecutions[i] == null) v.Error(path, "Null completion.");
                    else
                    {
                        CompletionExecutions[i].Validate(v, path);
                        CompletionExecutions[i].ValidateReferences(v, path);
                    }
                }
            }
            if (Executions == null) v.Error(p + ".Executions", "Execution list is null.");
            else for (int i = 0; i < Executions.Length; i++)
                if (Executions[i] == null) v.Error(p + $".Executions[{i}]", "Null execution.");
                else Executions[i].Validate(v, p + $".Executions[{i}]");
            int modifierCount = Modifiers?.Length ?? 0, tagCount = GrantedTags?.Length ?? 0, executionCount = Executions?.Length ?? 0;
            if (DurationPolicy == GameplayEffectDurationPolicy.Instant)
            {
                if (Presentation != null) v.Error(p + ".Presentation", "状态表现需要持续效果。");
                if (OnApplied.Length + OnStackChanged.Length + OnEnterMax.Length + OnPeriod.Length > 0)
                    v.Error(p, "生命周期动作需要持续效果。");
                if (Period != 0 || Stacking != null || modifierCount != 0 || tagCount != 0 || executionCount == 0)
                    v.Error(p, "Instant effects require executions and reject period, stack, modifiers and granted tags.");
                if (OngoingRequirements != null && ((OngoingRequirements.All?.Length ?? 0) > 0 ||
                    (OngoingRequirements.Any?.Length ?? 0) > 0 || (OngoingRequirements.None?.Length ?? 0) > 0))
                    v.Error(p + ".OngoingRequirements", "Instant effects cannot have ongoing requirements.");
            }
            else if ((Period > 0) != (executionCount != 0 || OnPeriod.Length > 0))
                v.Error(p, "周期大于零时需要周期结算或已连接的周期动作。");
        }

        public GameplayEffectDefinition Build(GameplayAuthoringBuildContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return context.Build(this, () =>
            {
                var modifiers = new List<GameplayEffectAttributeModifierDefinition>();
                foreach (var item in Modifiers) modifiers.Add(item.Build());
                var executions = new List<IGameplayEffectExecution>();
                foreach (var item in Executions) executions.Add(item.Build());
                var completions = new List<IGameplayEffectCompletionExecution>();
                foreach (var item in CompletionExecutions) completions.Add(item.Build(context));
                return new GameplayEffectDefinition(DurationPolicy, Duration, Period, modifiers,
                    GameplayTagQueryAuthoring.BuildTags(GrantedTags), executions, Stacking?.Build(context),
                    ApplicationRequirements?.Build(), OngoingRequirements?.Build(), completions,
                    GameplayTagQueryAuthoring.BuildTags(EffectTags), PeriodicStart, DurationValue?.Build(), PeriodValue?.Build(), Presentation?.Build(),
                    GameplayEffectLifecycleActionAuthoring.Build(OnApplied, context), GameplayEffectLifecycleActionAuthoring.Build(OnStackChanged, context),
                    GameplayEffectLifecycleActionAuthoring.Build(OnEnterMax, context), GameplayEffectLifecycleActionAuthoring.Build(OnPeriod, context));
            });
        }
    
    }
}
