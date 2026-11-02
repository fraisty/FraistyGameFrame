using System;
using System.Runtime.CompilerServices;

namespace FRFrame.Gameplay
{
    /// <summary>
    /// 一次 Effect Apply 的容器内身份，只能用于产生它的 GameplayEffectContainer，default 无效。
    /// Handle 用于调用方精确寻址 Effect；它不是清理 Attribute Modifier 所用的 SourceId。
    /// IsValid 只表示 Handle 曾被分配，不表示对应 Effect 当前仍处于活动状态。
    /// </summary>
    public readonly struct GameplayEffectHandle : IEquatable<GameplayEffectHandle>
    {
        private readonly object _owner;
        private readonly long _sequence;

        public bool IsValid => _owner != null && _sequence != 0;
        internal long Sequence => _sequence;
        /// <summary>Container-local sequence for read-only diagnostics; not a global identity.</summary>
        public long LocalSequence => _sequence;

        internal GameplayEffectHandle(object owner, long sequence)
        {
            _owner = owner;
            _sequence = sequence;
        }

        public bool Equals(GameplayEffectHandle other) =>
            ReferenceEquals(_owner, other._owner) && _sequence == other._sequence;

        public override bool Equals(object obj) => obj is GameplayEffectHandle other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((_owner == null ? 0 : RuntimeHelpers.GetHashCode(_owner)) * 397) ^ _sequence.GetHashCode();
            }
        }

        public static bool operator ==(GameplayEffectHandle left, GameplayEffectHandle right) => left.Equals(right);
        public static bool operator !=(GameplayEffectHandle left, GameplayEffectHandle right) => !left.Equals(right);
    }
}
