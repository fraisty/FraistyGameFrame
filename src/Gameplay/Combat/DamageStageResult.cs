using System;

namespace FRFrame.Gameplay
{
    [Flags]
    public enum DamageResultFlags { None = 0, Critical = 1, Miss = 2, Immune = 4, ShieldBroken = 8 }
    /// <summary>Pure stage output. Absorption is predicted, not committed; the standard execution
    /// consumes HP/Shield only once, after the complete calculation succeeds.</summary>
    public readonly struct DamageStageResult
    {
        public float Damage { get; }
        public float Absorbed { get; }
        public DamageResultFlags Flags { get; }
        public bool Stop { get; }
        public DamageStageResult(float damage, float absorbed = 0, DamageResultFlags flags = DamageResultFlags.None, bool stop = false)
        {
            if (!float.IsFinite(damage) || damage < 0 || !float.IsFinite(absorbed) || absorbed < 0) throw new ArgumentOutOfRangeException(nameof(damage));
            Damage = damage; Absorbed = absorbed; Flags = flags; Stop = stop;
        }
    }
    /// <summary>Optional metadata on the existing ordered DamagePipeline, preserving legacy numeric modifiers.</summary>
    public interface IDamageResultModifier : IDamageModifier
    {
        DamageStageResult Calculate(DamageContext context, float currentDamage);
    }
}
