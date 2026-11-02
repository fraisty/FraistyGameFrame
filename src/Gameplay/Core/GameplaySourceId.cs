using System;

namespace FRFrame.Gameplay
{
    /// <summary>
    /// 调用者提供的运行时来源标识；本模块不分配全局 ID。
    /// 任意非零 long 均有效，default 的 0 表示无效，不能用于添加或按来源移除 Modifier。
    /// </summary>
    public readonly struct GameplaySourceId : IEquatable<GameplaySourceId>
    {
        public long Value { get; }
        public bool IsValid => Value != 0;

        public GameplaySourceId(long value)
        {
            if (value == 0)
                throw new ArgumentOutOfRangeException(nameof(value), "Source ID must be nonzero.");
            Value = value;
        }

        public bool Equals(GameplaySourceId other) => Value == other.Value;
        public override bool Equals(object obj) => obj is GameplaySourceId other && Equals(other);
        public override int GetHashCode() => Value.GetHashCode();
        public override string ToString() => Value.ToString(System.Globalization.CultureInfo.InvariantCulture);
        public static bool operator ==(GameplaySourceId left, GameplaySourceId right) => left.Equals(right);
        public static bool operator !=(GameplaySourceId left, GameplaySourceId right) => !left.Equals(right);
    }
}
