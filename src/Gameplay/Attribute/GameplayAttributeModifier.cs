using System;

namespace FRFrame.Gameplay
{
    /// <summary>不可变的属性修饰数据；同一数据可添加多次，每次添加有独立 Handle。</summary>
    public readonly struct GameplayAttributeModifier
    {
        public GameplayModifierOperation Operation { get; }
        public float Magnitude { get; }
        public GameplaySourceId SourceId { get; }

        public GameplayAttributeModifier(GameplayModifierOperation operation, float magnitude, GameplaySourceId sourceId)
        {
            Validate(operation, magnitude, sourceId);
            Operation = operation;
            Magnitude = magnitude;
            SourceId = sourceId;
        }

        // 构造检查之外，添加入口也必须拒绝绕过构造器的 default struct。
        internal static void Validate(GameplayModifierOperation operation, float magnitude, GameplaySourceId sourceId)
        {
            if (operation != GameplayModifierOperation.Add && operation != GameplayModifierOperation.Multiply)
                throw new ArgumentOutOfRangeException(nameof(operation));
            if (float.IsNaN(magnitude) || float.IsInfinity(magnitude))
                throw new ArgumentOutOfRangeException(nameof(magnitude), "Magnitude must be finite.");
            if (!sourceId.IsValid)
                throw new ArgumentException("Source ID must be valid.", nameof(sourceId));
        }
    }
}
