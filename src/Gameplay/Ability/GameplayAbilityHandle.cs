using System;
using System.Runtime.CompilerServices;

namespace FRFrame.Gameplay
{
    /// <summary>
    /// 某个 GameplayAbilityContainer 内一次 Grant 的身份，只能用于产生它的容器，default 无效。
    /// Handle 与 Definition、Activation SourceId 及冷却 Effect Handle 均不是同一种身份。
    /// IsValid 只表示 Handle 曾被分配，不表示对应 Grant 仍存在或当前处于 Active。
    /// </summary>
    public readonly struct GameplayAbilityHandle : IEquatable<GameplayAbilityHandle>
    {
        private readonly object _owner;
        private readonly long _sequence;

        public bool IsValid => _owner != null && _sequence != 0;
        internal long Sequence => _sequence;
        /// <summary>Container-local grant sequence for read-only diagnostics.</summary>
        public long LocalSequence => _sequence;

        internal GameplayAbilityHandle(object owner, long sequence)
        {
            _owner = owner;
            _sequence = sequence;
        }

        public bool Equals(GameplayAbilityHandle other) =>
            ReferenceEquals(_owner, other._owner) && _sequence == other._sequence;

        public override bool Equals(object obj) => obj is GameplayAbilityHandle other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((_owner == null ? 0 : RuntimeHelpers.GetHashCode(_owner)) * 397) ^ _sequence.GetHashCode();
            }
        }

        public static bool operator ==(GameplayAbilityHandle left, GameplayAbilityHandle right) => left.Equals(right);
        public static bool operator !=(GameplayAbilityHandle left, GameplayAbilityHandle right) => !left.Equals(right);
    }
}
