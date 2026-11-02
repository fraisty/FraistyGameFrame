using System;

namespace FRFrame.Gameplay
{
    /// <summary>
    /// Effect 所提供的一条不可变属性修饰定义。
    /// SourceId 不属于静态定义，而由每次 Apply 创建的 Effect 实例独立持有。
    /// </summary>
    public sealed class GameplayEffectAttributeModifierDefinition
    {
        public GameplayAttributeId AttributeId { get; }
        public GameplayModifierOperation Operation { get; }
        public IGameplayFloatValue MagnitudeValue { get; }
        /// <summary>仅旧常量构造器的静态 magnitude；动态值通过 MagnitudeValue 求值。</summary>
        public float Magnitude => MagnitudeValue is GameplayConstantFloatValue constant ? constant.Value :
            throw new InvalidOperationException("Dynamic modifier has no static magnitude.");

        public GameplayEffectAttributeModifierDefinition(
            GameplayAttributeId attributeId,
            GameplayModifierOperation operation,
            float magnitude)
            : this(attributeId, operation, new GameplayConstantFloatValue(magnitude))
        { }

        public GameplayEffectAttributeModifierDefinition(
            GameplayAttributeId attributeId, GameplayModifierOperation operation,
            IGameplayFloatValue magnitudeValue)
        {
            if (!attributeId.IsValid)
                throw new ArgumentException("Attribute ID must be valid.", nameof(attributeId));
            if (operation != GameplayModifierOperation.Add && operation != GameplayModifierOperation.Multiply)
                throw new ArgumentOutOfRangeException(nameof(operation));
            MagnitudeValue = magnitudeValue ?? throw new ArgumentNullException(nameof(magnitudeValue));

            AttributeId = attributeId;
            Operation = operation;
        }
    }
}
