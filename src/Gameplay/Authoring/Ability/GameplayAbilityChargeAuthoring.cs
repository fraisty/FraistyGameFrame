using System;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Optional sequential recharge configuration. Counts and timers belong to each granted Ability.</summary>
    [Serializable, Preserve]
    public sealed class GameplayAbilityChargeAuthoring
    {
        public int MaxCharges = 1;
        public float RechargeDuration = 1f;

        public void Validate(GameplayAuthoringValidationContext validation, string path)
        {
            if (MaxCharges <= 0) validation.Error(path + ".MaxCharges", "MaxCharges must be positive.");
            validation.Finite(RechargeDuration, path + ".RechargeDuration");
            if (RechargeDuration <= 0f) validation.Error(path + ".RechargeDuration", "RechargeDuration must be positive.");
        }

        public GameplayAbilityChargeDefinition Build() => new GameplayAbilityChargeDefinition(MaxCharges, RechargeDuration);
    }
}
