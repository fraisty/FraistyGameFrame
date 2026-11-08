using FRFrame.Gameplay.Authoring.Graph;
using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using UnityEngine;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Backend-specific authoring of a persistent Effect's optional presentation.</summary>
    [Serializable]
    public abstract class GameplayEffectPresentationAuthoring
    {
        public abstract void Validate(GameplayAuthoringValidationContext validation, string path);
        public abstract IGameplayEffectPresentationDefinition Build();
    }

    [CreateAssetMenu(menuName = "FRFrame Gameplay/Effect Definition")]
    public sealed class GameplayEffectDefinitionAsset : GameplayDefinitionAsset
    {
        public GameplayEffectDurationPolicy DurationPolicy;

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

        public override object BuildSharedDefinition(GameplayAuthoringBuildContext context) => Build(context);
        public override GameplayLocalDefinitionPayload CaptureGraphValues() => CaptureValues();
        private GameplayEffectDefinitionData CaptureValues() => new GameplayEffectDefinitionData { DurationPolicy = DurationPolicy, Duration = Duration, Period = Period, EffectTags = EffectTags, PeriodicStart = PeriodicStart, DurationValue = DurationValue, PeriodValue = PeriodValue, Modifiers = Modifiers, GrantedTags = GrantedTags, Stacking = Stacking, ApplicationRequirements = ApplicationRequirements, OngoingRequirements = OngoingRequirements, Executions = Executions, CompletionExecutions = CompletionExecutions, Presentation = Presentation, OnApplied = OnApplied, OnStackChanged = OnStackChanged, OnEnterMax = OnEnterMax, OnPeriod = OnPeriod };
        public GameplayEffectDefinition Build(GameplayAuthoringBuildContext context) => context.Build(this, () => CaptureValues().Build(context));
        protected override void ValidateContent(GameplayAuthoringValidationContext validation, string path) => CaptureValues().Validate(validation, path);

    }
}
