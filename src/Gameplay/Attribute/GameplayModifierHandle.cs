using System;
using System.Runtime.CompilerServices;

namespace FRFrame.Gameplay
{
    /// <summary>
    /// 一次添加的身份，只能用于产生它的属性。default 无效。
    /// IsValid 表示曾被分配，不表示修饰器仍存在；是否成功移除由 RemoveModifier 返回。
    /// </summary>
    public readonly struct GameplayModifierHandle : IEquatable<GameplayModifierHandle>
    {
        private readonly object _owner;
        private readonly long _sequence;

        public bool IsValid => _owner != null && _sequence != 0;

        internal GameplayModifierHandle(object owner, long sequence)
        {
            _owner = owner;
            _sequence = sequence;
        }

        public bool Equals(GameplayModifierHandle other) =>
            ReferenceEquals(_owner, other._owner) && _sequence == other._sequence;

        public override bool Equals(object obj) => obj is GameplayModifierHandle other && Equals(other);
        public override int GetHashCode()
        {
            unchecked
            {
                return ((_owner == null ? 0 : RuntimeHelpers.GetHashCode(_owner)) * 397) ^ _sequence.GetHashCode();
            }
        }

        public static bool operator ==(GameplayModifierHandle left, GameplayModifierHandle right) => left.Equals(right);
        public static bool operator !=(GameplayModifierHandle left, GameplayModifierHandle right) => !left.Equals(right);
    }
}
