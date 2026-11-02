using System;

namespace FRFrame.Gameplay
{
    /// <summary>不可变的规则时间间隔定义；每个 owner 的剩余时间由独立 Binding 持有。</summary>
    public sealed class GameplaySkillIntervalTriggerDefinition : IGameplaySkillTriggerDefinition
    {
        public GameplayTag SignalTag { get; }
        public float Interval { get; }

        public GameplaySkillIntervalTriggerDefinition(GameplayTag signalTag, float interval)
        {
            if (!signalTag.IsValid) throw new ArgumentException("Signal tag must be valid.", nameof(signalTag));
            if (!float.IsFinite(interval) || interval <= 0f)
                throw new ArgumentOutOfRangeException(nameof(interval));
            SignalTag = signalTag;
            Interval = interval;
        }

        public IGameplaySkillTriggerBinding Bind(IGameplaySkillHost host, GameplayEventDispatcher events,
            Action<GameplayEventData> onTriggered)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (!host.OwnerId.IsValid) throw new ArgumentException("Host owner ID must be valid.", nameof(host));
            if (events == null) throw new ArgumentNullException(nameof(events));
            if (onTriggered == null) throw new ArgumentNullException(nameof(onTriggered));
            return new Binding(host.OwnerId, SignalTag, Interval, onTriggered);
        }

        /// <summary>单个 owner 的间隔进度；到点先消费本次间隔，再传递原因 payload。</summary>
        public sealed class Binding : IGameplaySkillTriggerBinding, IGameplaySkillAdvanceableTriggerBinding
        {
            private readonly GameplaySourceId _ownerId;
            private readonly GameplayTag _signalTag;
            private readonly float _interval;
            private readonly Action<GameplayEventData> _onTriggered;
            private bool _disposed;
            private double _elapsed;
            private double _nextTriggerAt;
            /// <summary>Float observation of the precise pending interval; observations never feed back into scheduling.</summary>
            public float Remaining => (float)Math.Max(0d, _nextTriggerAt - _elapsed);
            public float Interval => _interval;

            internal Binding(GameplaySourceId ownerId, GameplayTag signalTag, float interval,
                Action<GameplayEventData> onTriggered)
            {
                _ownerId = ownerId;
                _signalTag = signalTag;
                _interval = interval;
                _onTriggered = onTriggered;
                _nextTriggerAt = interval;
            }

            public void Advance(float deltaTime)
            {
                if (!float.IsFinite(deltaTime) || deltaTime < 0f)
                    throw new ArgumentOutOfRangeException(nameof(deltaTime));
                if (_disposed) return;
                _elapsed += deltaTime;
                while ((float)_elapsed >= (float)_nextTriggerAt)
                {
                    // Commit before invoking callbacks, including reentrant Advance or Dispose.
                    _nextTriggerAt += _interval;
                    _onTriggered(new GameplayEventData(_signalTag, _ownerId, default, _interval));
                    if (_disposed) return;
                }
            }

            public void Dispose() => _disposed = true;
        }
    }
}
