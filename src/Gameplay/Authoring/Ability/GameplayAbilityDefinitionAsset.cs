using FRFrame.Gameplay.Authoring.Graph;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Static Ability contract, built once per session. No grant handle, activation state or resource balance is cached in the asset.</summary>
    [CreateAssetMenu(menuName = "FRFrame Gameplay/Ability Definition")]
    public sealed class GameplayAbilityDefinitionAsset : GameplayDefinitionAsset
    {
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

        public override object BuildSharedDefinition(GameplayAuthoringBuildContext context) => Build(context);
        public override GameplayLocalDefinitionPayload CaptureGraphValues() => CaptureValues();
        private GameplayAbilityDefinitionData CaptureValues() => new GameplayAbilityDefinitionData { MaxLevel = MaxLevel, RequiredTags = RequiredTags, BlockedTags = BlockedTags, ActivationOwnedTags = ActivationOwnedTags, AbilityTags = AbilityTags, CancelAbilitiesWithTags = CancelAbilitiesWithTags, BlockAbilitiesWithTags = BlockAbilitiesWithTags, OngoingBlockedTags = OngoingBlockedTags, CooldownEffect = CooldownEffect, Charge = Charge, Cost = Cost, ActivationEffects = ActivationEffects };
        public GameplayAbilityDefinition Build(GameplayAuthoringBuildContext context) => context.Build(this, () => CaptureValues().Build(context));
        protected override void ValidateContent(GameplayAuthoringValidationContext validation, string path) => CaptureValues().Validate(validation, path);

    }
}
