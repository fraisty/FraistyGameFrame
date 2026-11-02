using FRFrame.Gameplay.Authoring.Graph;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Static Ability contract, built once per session. No grant handle, activation state or resource balance is cached in the asset.</summary>
    [System.Serializable, UnityEngine.Scripting.Preserve]
    public sealed class GameplayAbilityDefinitionData : GameplayLocalDefinitionPayload
    {
        public override System.Type DefinitionType => typeof(GameplayAbilityDefinition);
        public override object BuildDefinition(GameplayAuthoringBuildContext context) => Build(context);

        [Min(1), Tooltip("每份授予实例的等级上限；运行等级不写回共享资产。")]
        public int MaxLevel = 1;
        /// <summary>Owner state required before activation, using runtime hierarchical queries.</summary>
        public GameplayTagReference[] RequiredTags = Array.Empty<GameplayTagReference>();
        /// <summary>Owner state preventing activation; these are not Ability identity tags.</summary>
        public GameplayTagReference[] BlockedTags = Array.Empty<GameplayTagReference>();
        /// <summary>Owner tags supplied by this activation source until End/Cancel/Remove.</summary>
        public GameplayTagReference[] ActivationOwnedTags = Array.Empty<GameplayTagReference>();
        /// <summary>Definition identity for Ability relations; never added to owner state.</summary>
        public GameplayTagReference[] AbilityTags = Array.Empty<GameplayTagReference>();
        /// <summary>Identity queries of other active abilities to cancel after successful activation.</summary>
        public GameplayTagReference[] CancelAbilitiesWithTags = Array.Empty<GameplayTagReference>();
        /// <summary>Identity queries of other abilities prevented from activating while this one is active.</summary>
        public GameplayTagReference[] BlockAbilitiesWithTags = Array.Empty<GameplayTagReference>();
        /// <summary>Owner state that cancels this active ability when its container next advances.</summary>
        public GameplayTagReference[] OngoingBlockedTags = Array.Empty<GameplayTagReference>();
        /// <summary>Optional simple Duration/GrantedTag effect. DurationValue is evaluated once
        /// from the successful cast level/input and frozen; mutually exclusive with Charge.</summary>
        public GameplayEffectDefinitionAsset CooldownEffect;
        [SerializeReference] public GameplayAbilityChargeAuthoring Charge;
        [SerializeReference] public GameplayAbilityCostAuthoring Cost;
        /// <summary>Unstacked persistent effects owned and removed by the activation lifecycle.</summary>
        public GameplayEffectDefinitionAsset[] ActivationEffects = Array.Empty<GameplayEffectDefinitionAsset>();

        protected override void ValidateContent(GameplayAuthoringValidationContext v, string p)
        {
            if (MaxLevel < 1) v.Error(p + ".MaxLevel", "MaxLevel must be positive.");
            ValidateTags(v, p + ".RequiredTags", RequiredTags);
            ValidateTags(v, p + ".BlockedTags", BlockedTags);
            ValidateTags(v, p + ".ActivationOwnedTags", ActivationOwnedTags);
            ValidateTags(v, p + ".AbilityTags", AbilityTags);
            ValidateTags(v, p + ".CancelAbilitiesWithTags", CancelAbilitiesWithTags);
            ValidateTags(v, p + ".BlockAbilitiesWithTags", BlockAbilitiesWithTags);
            ValidateTags(v, p + ".OngoingBlockedTags", OngoingBlockedTags);
            if (CooldownEffect != null)
            {
                string path = p + ".CooldownEffect";
                v.Asset(CooldownEffect, path);
                if (CooldownEffect.DurationPolicy != GameplayEffectDurationPolicy.Duration)
                    v.Error(path + ".DurationPolicy", "Cooldown requires Duration policy.");
                if (CooldownEffect.GrantedTags != null && CooldownEffect.GrantedTags.Length == 0)
                    v.Error(path + ".GrantedTags", "Cooldown must grant at least one tag.");
                if (CooldownEffect.Modifiers != null && CooldownEffect.Modifiers.Length != 0)
                    v.Error(path + ".Modifiers", "Cooldown cannot contain modifiers.");
                if (CooldownEffect.Period != 0f) v.Error(path + ".Period", "Cooldown cannot be periodic.");
                if (CooldownEffect.PeriodValue != null) v.Error(path + ".PeriodValue", "Cooldown cannot have a dynamic period.");
                if (CooldownEffect.Executions != null && CooldownEffect.Executions.Length != 0)
                    v.Error(path + ".Executions", "Cooldown cannot contain executions.");
                if (CooldownEffect.Stacking != null) v.Error(path + ".Stacking", "Cooldown cannot stack.");
                if (CooldownEffect.CompletionExecutions != null && CooldownEffect.CompletionExecutions.Length != 0)
                    v.Error(path + ".CompletionExecutions", "Normal Ability cooldown uses context-free Apply; use a context-aware rule effect for completion.");
                if (HasRequirements(CooldownEffect.ApplicationRequirements))
                    v.Error(path + ".ApplicationRequirements", "Cooldown cannot have application requirements.");
                if (HasRequirements(CooldownEffect.OngoingRequirements))
                    v.Error(path + ".OngoingRequirements", "Cooldown cannot have ongoing requirements.");
            }
            if (Charge != null)
            {
                Charge.Validate(v, p + ".Charge");
                if (CooldownEffect != null) v.Error(p + ".Charge", "Charge and normal cooldown are mutually exclusive.");
            }
            Cost?.Validate(v, p + ".Cost");
            if (ActivationEffects == null) v.Error(p + ".ActivationEffects", "Activation effect list is null.");
            else for (int i = 0; i < ActivationEffects.Length; i++)
            {
                var effect = ActivationEffects[i];
                string path = p + $".ActivationEffects[{i}]";
                v.Asset(effect, path);
                if (effect == null) continue;
                if (effect.DurationPolicy == GameplayEffectDurationPolicy.Instant)
                    v.Error(path + ".DurationPolicy", "Activation effect must be persistent.");
                if (effect.Stacking != null) v.Error(path + ".Stacking", "Activation effect must be unstacked.");
            }
        }

        private static bool HasRequirements(GameplayTagQueryAuthoring query) => query != null &&
            ((query.All?.Length ?? 0) != 0 || (query.Any?.Length ?? 0) != 0 || (query.None?.Length ?? 0) != 0);

        private static void ValidateTags(GameplayAuthoringValidationContext v, string path, GameplayTagReference[] tags)
        {
            if (tags == null) { v.Error(path, "Tag list is null."); return; }
            for (int i = 0; i < tags.Length; i++) v.Tag(tags[i], path + $"[{i}]");
        }

        /// <summary>Builds Ability, Cost and referenced effects in the same identity-preserving session.</summary>
        public GameplayAbilityDefinition Build(GameplayAuthoringBuildContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            return context.Build(this, () =>
            {
                var effects = new List<GameplayEffectDefinition>();
                for (int i = 0; i < ActivationEffects.Length; i++) { int at = i; effects.Add(context.ResolveDefinition(ActivationEffects[i], this, "ActivationEffects.Array.data[" + i + "]", () => ActivationEffects[at].Build(context))); }
                var cost = Cost?.Build(context);
                if (Cost != null && cost == null) throw new InvalidOperationException("Configured Cost.Build returned null.");
                return new GameplayAbilityDefinition(GameplayTagQueryAuthoring.BuildTags(RequiredTags),
                    GameplayTagQueryAuthoring.BuildTags(BlockedTags), GameplayTagQueryAuthoring.BuildTags(ActivationOwnedTags),
                    context.ResolveOptionalDefinition(CooldownEffect, this, nameof(CooldownEffect), () => CooldownEffect.Build(context)), Charge?.Build(), GameplayTagQueryAuthoring.BuildTags(AbilityTags),
                    GameplayTagQueryAuthoring.BuildTags(CancelAbilitiesWithTags), GameplayTagQueryAuthoring.BuildTags(BlockAbilitiesWithTags),
                    GameplayTagQueryAuthoring.BuildTags(OngoingBlockedTags), cost, effects, MaxLevel);
            });
        }
    
    }
}
