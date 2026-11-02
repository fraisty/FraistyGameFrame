using System;

namespace FRFrame.Gameplay
{
    /// <summary>
    /// 区分大小写、按 Ordinal 比较的属性键；不裁剪或正规化调用者的字符串。
    /// default 是无效键，AttributeSet 的所有键入口均拒绝它。
    /// </summary>
    public readonly struct GameplayAttributeId : IEquatable<GameplayAttributeId>
    {
        public string Value { get; }
        public bool IsValid => !string.IsNullOrWhiteSpace(Value);

        public GameplayAttributeId(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("Attribute ID must not be null, empty or whitespace.", nameof(value));
            Value = value;
        }

        public bool Equals(GameplayAttributeId other) => StringComparer.Ordinal.Equals(Value, other.Value);
        public override bool Equals(object obj) => obj is GameplayAttributeId other && Equals(other);
        public override int GetHashCode() => Value == null ? 0 : StringComparer.Ordinal.GetHashCode(Value);
        public override string ToString() => Value ?? string.Empty;
        public static bool operator ==(GameplayAttributeId left, GameplayAttributeId right) => left.Equals(right);
        public static bool operator !=(GameplayAttributeId left, GameplayAttributeId right) => !left.Equals(right);
    }
}
