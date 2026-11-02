using System;

namespace FRFrame.Gameplay
{
    /// <summary>同一 owner 的指定事件累计 N 次后触发；定义可共享，计数和订阅属于每次 Bind。</summary>
    public sealed class GameplaySkillOwnerEventCountTriggerDefinition : IGameplaySkillTriggerDefinition
    {
        public GameplayTag EventTag { get; }
        public int RequiredCount { get; }

        public GameplaySkillOwnerEventCountTriggerDefinition(GameplayTag eventTag, int requiredCount)
        {
            if (!eventTag.IsValid) throw new ArgumentException("Event tag must be valid.", nameof(eventTag));
            if (requiredCount <= 0) throw new ArgumentOutOfRangeException(nameof(requiredCount));
            EventTag = eventTag;
            RequiredCount = requiredCount;
        }

        public IGameplaySkillTriggerBinding Bind(IGameplaySkillHost host, GameplayEventDispatcher events,
            Action<GameplayEventData> onTriggered)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (!host.OwnerId.IsValid) throw new ArgumentException("Host owner ID must be valid.", nameof(host));
            if (events == null) throw new ArgumentNullException(nameof(events));
            if (onTriggered == null) throw new ArgumentNullException(nameof(onTriggered));
            return new Binding(host.OwnerId, EventTag, RequiredCount, events, onTriggered);
        }

        /// <summary>单个 owner 的计数与订阅；达到阈值时先清零再回调，回调失败也不重放。</summary>
        public sealed class Binding : IGameplaySkillTriggerBinding
        {
            private readonly GameplaySourceId _ownerId;
            private readonly int _requiredCount;
            private readonly GameplayEventDispatcher _events;
            private readonly Action<GameplayEventData> _onTriggered;
            private readonly GameplayEventSubscriptionHandle _subscription;
            private bool _disposed;

            public int CurrentCount { get; private set; }
            public int RequiredCount => _requiredCount;

            internal Binding(GameplaySourceId ownerId, GameplayTag eventTag, int requiredCount,
                GameplayEventDispatcher events, Action<GameplayEventData> onTriggered)
            {
                _ownerId = ownerId;
                _requiredCount = requiredCount;
                _events = events;
                _onTriggered = onTriggered;
                _subscription = events.Subscribe(eventTag, OnEvent);
            }

            private void OnEvent(GameplayEventData data)
            {
                if (_disposed || data.InstigatorId != _ownerId) return;
                CurrentCount++;
                if (CurrentCount < _requiredCount) return;
                CurrentCount = 0;
                _onTriggered(data);
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _events.Unsubscribe(_subscription);
            }
        }
    }
}
