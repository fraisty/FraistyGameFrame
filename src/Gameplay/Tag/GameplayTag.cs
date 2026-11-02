using System;

namespace FRFrame.Gameplay
{
    /// <summary>
    /// 不可变的点号层级标签，身份按区分大小写的 Ordinal 字符串比较。
    /// 不裁剪名称；拒绝空层级和任何空白字符。default 无效。
    /// </summary>
    public readonly struct GameplayTag : IEquatable<GameplayTag>
    {
        public string Name { get; }
        public bool IsValid => Name != null;

        public GameplayTag(string name)
        {
            if (string.IsNullOrEmpty(name))
                throw new ArgumentException("Tag name must not be null or empty.", nameof(name));
            for (int i = 0; i < name.Length; i++)
            {
                if (char.IsWhiteSpace(name[i]) ||
                    (name[i] == '.' && (i == 0 || i == name.Length - 1 || name[i - 1] == '.')))
                    throw new ArgumentException("Tag name must contain nonempty segments and no whitespace.", nameof(name));
            }
            Name = name;
        }

        /// <summary>
        /// 是否为 parent 的严格后代（不包含自身）；任一标签无效则返回 false。
        /// 点号必须位于完整层级边界，State.Stunned 不是 State.Stun 的后代。
        /// </summary>
        public bool IsDescendantOf(GameplayTag parent) =>
            IsValid && parent.IsValid && Name.Length > parent.Name.Length &&
            Name[parent.Name.Length] == '.' && Name.StartsWith(parent.Name, StringComparison.Ordinal);

        public bool Equals(GameplayTag other) => StringComparer.Ordinal.Equals(Name, other.Name);
        public override bool Equals(object obj) => obj is GameplayTag other && Equals(other);
        // 哈希仅供集合分桶；Equals 始终比较完整名称，不把哈希当作语义 ID。
        public override int GetHashCode() => Name == null ? 0 : StringComparer.Ordinal.GetHashCode(Name);
        public override string ToString() => Name ?? string.Empty;
        public static bool operator ==(GameplayTag left, GameplayTag right) => left.Equals(right);
        public static bool operator !=(GameplayTag left, GameplayTag right) => !left.Equals(right);
    }
}
