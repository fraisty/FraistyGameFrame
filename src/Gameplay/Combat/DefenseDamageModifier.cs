using System;

namespace FRFrame.Gameplay
{
    /// <summary>
    /// 演示防御阶段：currentDamage * 100 / (100 + max(0, Defense))。
    /// 属性键由调用者提供，不定义游戏专属属性；每次读取目标的 CurrentValue，不修改属性或生命。
    /// </summary>
    public sealed class DefenseDamageModifier : IDamageModifier
    {
        private readonly GameplayAttributeId _defenseAttributeId;

        /// <summary>指定防御属性键。default 键抛 ArgumentException；键需在目标属性集中注册。</summary>
        public DefenseDamageModifier(GameplayAttributeId defenseAttributeId)
        {
            if (!defenseAttributeId.IsValid)
                throw new ArgumentException("Defense attribute ID must be valid.", nameof(defenseAttributeId));
            _defenseAttributeId = defenseAttributeId;
        }

        /// <summary>
        /// 按目标当前防御计算；负防御按 0 参与本公式，不回写属性。
        /// 缺少防御属性时传播 KeyNotFoundException，避免把漏配静默当作零防御。
        /// null Context 或非法伤害分别抛 ArgumentNullException、ArgumentOutOfRangeException。
        /// </summary>
        public float Modify(DamageContext context, float currentDamage)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (float.IsNaN(currentDamage) || float.IsInfinity(currentDamage) || currentDamage < 0f)
                throw new ArgumentOutOfRangeException(nameof(currentDamage), "Damage must be finite and nonnegative.");

            float defense = context.TargetAttributes.GetCurrentValue(_defenseAttributeId);
            // double 中间值避免先乘 100 时产生不必要的 float 溢出。
            return (float)(currentDamage * 100d / (100d + Math.Max(0d, defense)));
        }
    }
}
