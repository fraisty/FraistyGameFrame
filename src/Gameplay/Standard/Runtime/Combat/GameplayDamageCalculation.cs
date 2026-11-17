using System;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Read-only numeric inputs captured for one stage. Parameters are evaluated through the
    /// standard value system. MappedDefense is available to defense replacements on physical/magical hits.</summary>
    public readonly ref struct GameplayDamageCalculationInput
    {
        public float BaseDamage { get; }
        public float CurrentDamage { get; }
        public GameplayDamageType DamageType { get; }
        public int AbilityLevel { get; }
        public float? MappedDefense { get; }
        public ReadOnlySpan<float> Parameters { get; }

        internal GameplayDamageCalculationInput(float baseDamage, float currentDamage, GameplayDamageType damageType,
            int abilityLevel, float? mappedDefense, ReadOnlySpan<float> parameters)
        {
            BaseDamage = baseDamage;
            CurrentDamage = currentDamage;
            DamageType = damageType;
            AbilityLevel = abilityLevel;
            MappedDefense = mappedDefense;
            Parameters = parameters;
        }
    }

    /// <summary>Immutable, reusable math strategy. Return finite, nonnegative damage. Resource settlement,
    /// result flags and stopping the pipeline belong to Standard, outside this calculation.</summary>
    public interface IGameplayDamageCalculation
    {
        float Calculate(in GameplayDamageCalculationInput input);
    }

    /// <summary>Subtracts a nonnegative amount, bounded at zero before subsequent stages.</summary>
    public sealed class GameplayFixedBlockCalculation : IGameplayDamageCalculation
    {
        public float Calculate(in GameplayDamageCalculationInput input)
        {
            float amount = input.Parameters[0];
            if (amount < 0) throw new ArithmeticException("Fixed block must be nonnegative.");
            return Math.Max(0, input.CurrentDamage - amount);
        }
    }

    /// <summary>Applies percentage penetration first, then subtracts flat penetration from mapped defense.</summary>
    public sealed class GameplayArmorPenetrationCalculation : IGameplayDamageCalculation
    {
        public float Calculate(in GameplayDamageCalculationInput input)
        {
            float percent = input.Parameters[0], flat = input.Parameters[1];
            if (percent < 0 || percent > 1 || flat < 0)
                throw new ArithmeticException("Armor penetration requires percent in [0, 1] and nonnegative flat penetration.");
            double effective = Math.Max(0d, Math.Max(0d, input.MappedDefense.Value) * (1d - percent) - flat);
            return (float)(input.CurrentDamage * 100d / (100d + effective));
        }
    }
}
