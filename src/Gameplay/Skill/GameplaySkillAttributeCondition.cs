using System;

namespace FRFrame.Gameplay
{
    /// <summary>与常量比较 owner 属性当前值的操作；不比较 BaseValue。</summary>
    public enum GameplaySkillAttributeComparison
    {
        Less,
        LessOrEqual,
        Greater,
        GreaterOrEqual
    }

    /// <summary>不可变的当前属性值条件；缺失属性沿用 AttributeSet 的失败语义。</summary>
    public sealed class GameplaySkillAttributeCondition : IGameplaySkillCondition
    {
        public GameplayAttributeId AttributeId { get; }
        public GameplaySkillAttributeComparison Comparison { get; }
        public float Constant { get; }

        public GameplaySkillAttributeCondition(GameplayAttributeId attributeId,
            GameplaySkillAttributeComparison comparison, float constant)
        {
            if (!attributeId.IsValid) throw new ArgumentException("Attribute ID must be valid.", nameof(attributeId));
            if (!Enum.IsDefined(typeof(GameplaySkillAttributeComparison), comparison))
                throw new ArgumentOutOfRangeException(nameof(comparison));
            if (!float.IsFinite(constant)) throw new ArgumentOutOfRangeException(nameof(constant));
            AttributeId = attributeId;
            Comparison = comparison;
            Constant = constant;
        }

        public bool Evaluate(IGameplaySkillHost host, GameplayEventData triggerEvent)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            float current = host.Attributes.GetCurrentValue(AttributeId);
            switch (Comparison)
            {
                case GameplaySkillAttributeComparison.Less: return current < Constant;
                case GameplaySkillAttributeComparison.LessOrEqual: return current <= Constant;
                case GameplaySkillAttributeComparison.Greater: return current > Constant;
                case GameplaySkillAttributeComparison.GreaterOrEqual: return current >= Constant;
                default: throw new InvalidOperationException("Unsupported attribute comparison.");
            }
        }
    }
}
