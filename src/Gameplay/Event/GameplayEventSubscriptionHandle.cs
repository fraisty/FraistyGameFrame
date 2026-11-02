using System;
using System.Runtime.CompilerServices;

namespace FRFrame.Gameplay
{
    /// <summary>
    /// 某个 GameplayEventDispatcher 内一次订阅的身份，只能用于产生它的 Dispatcher，default 无效。
    /// IsValid 只表示 Handle 曾被分配，不表示对应订阅当前仍存在；它不是 GameplaySourceId。
    /// </summary>
    public readonly struct GameplayEventSubscriptionHandle : IEquatable<GameplayEventSubscriptionHandle>
    {
        private readonly object _owner;
        private readonly long _sequence;

        public bool IsValid => _owner != null && _sequence != 0;

        internal GameplayEventSubscriptionHandle(object owner, long sequence)
        {
            _owner = owner;
            _sequence = sequence;
        }

        public bool Equals(GameplayEventSubscriptionHandle other) =>
            ReferenceEquals(_owner, other._owner) && _sequence == other._sequence;

        public override bool Equals(object obj) =>
            obj is GameplayEventSubscriptionHandle other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                return ((_owner == null ? 0 : RuntimeHelpers.GetHashCode(_owner)) * 397) ^
                       _sequence.GetHashCode();
            }
        }

        public static bool operator ==(
            GameplayEventSubscriptionHandle left,
            GameplayEventSubscriptionHandle right) => left.Equals(right);

        public static bool operator !=(
            GameplayEventSubscriptionHandle left,
            GameplayEventSubscriptionHandle right) => !left.Equals(right);
    }
}
