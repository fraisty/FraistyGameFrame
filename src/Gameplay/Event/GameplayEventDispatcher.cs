using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay
{
    /// <summary>
    /// 同步分发规则层 Gameplay Event 的实例容器。
    /// 精确标签或订阅标签的后代会匹配；回调按注册顺序执行，异常直接传播并停止本次后续分发。
    /// </summary>
    public sealed class GameplayEventDispatcher
    {
        private readonly object _handleOwner = new object();
        private readonly List<Subscription> _subscriptions = new List<Subscription>();
        private readonly GameplayEventHistoryEntry[] _history;
        private int _historyStart;
        private int _historyCount;
        private long _publishSequence;
        private long _lastSequence;

        /// <summary>History is disabled by default; a positive capacity retains only the newest events.</summary>
        public GameplayEventDispatcher(int historyCapacity = 0)
        {
            if (historyCapacity < 0) throw new ArgumentOutOfRangeException(nameof(historyCapacity));
            _history = new GameplayEventHistoryEntry[historyCapacity];
        }

        /// <summary>Returns an oldest-to-newest detached history copy.</summary>
        public IReadOnlyList<GameplayEventHistoryEntry> GetHistorySnapshot()
        {
            var result = new List<GameplayEventHistoryEntry>(_historyCount);
            for (int i = 0; i < _historyCount; i++)
                result.Add(_history[(_historyStart + i) % _history.Length]);
            return result.AsReadOnly();
        }

        /// <summary>
        /// 订阅 eventTag 及其后代事件。标签必须有效，callback 不能为 null。
        /// 每次调用都创建独立订阅，并返回只能由本 Dispatcher 取消的 Handle。
        /// </summary>
        public GameplayEventSubscriptionHandle Subscribe(
            GameplayTag eventTag,
            Action<GameplayEventData> callback)
        {
            if (!eventTag.IsValid)
                throw new ArgumentException("Event tag must be valid.", nameof(eventTag));
            if (callback == null)
                throw new ArgumentNullException(nameof(callback));

            long sequence = checked(_lastSequence + 1);
            var handle = new GameplayEventSubscriptionHandle(_handleOwner, sequence);
            _subscriptions.Add(new Subscription(handle, eventTag, callback));
            _lastSequence = sequence;
            return handle;
        }

        /// <summary>
        /// 取消本 Dispatcher 的一项订阅。default、其他 Dispatcher 的 Handle 或已取消订阅返回 false。
        /// 已被当前 Publish 快照捕获的回调仍会在该次发布中执行。
        /// </summary>
        public bool Unsubscribe(GameplayEventSubscriptionHandle handle)
        {
            for (int i = 0; i < _subscriptions.Count; i++)
            {
                if (_subscriptions[i].Handle != handle)
                    continue;

                _subscriptions.RemoveAt(i);
                return true;
            }

            return false;
        }

        /// <summary>
        /// 按注册顺序同步发布事件。先快照全部匹配回调，因此分发期间的订阅变更只影响后续 Publish。
        /// default 或其他无效数据会在任何回调执行前被拒绝；回调异常原样传播并停止后续回调。
        /// </summary>
        public void Publish(GameplayEventData data)
        {
            Validate(data);

            long sequence = checked(++_publishSequence);
            if (_history.Length > 0)
            {
                int index = (_historyStart + _historyCount) % _history.Length;
                _history[index] = new GameplayEventHistoryEntry(sequence, data);
                if (_historyCount < _history.Length) _historyCount++;
                else _historyStart = (_historyStart + 1) % _history.Length;
            }

            var callbacks = new List<Action<GameplayEventData>>();
            foreach (Subscription subscription in _subscriptions)
            {
                if (data.EventTag == subscription.EventTag ||
                    data.EventTag.IsDescendantOf(subscription.EventTag))
                    callbacks.Add(subscription.Callback);
            }

            foreach (Action<GameplayEventData> callback in callbacks)
                callback(data);
        }

        private static void Validate(GameplayEventData data)
        {
            if (!data.EventTag.IsValid)
                throw new ArgumentException("Event data must contain a valid event tag.", nameof(data));
            if (float.IsNaN(data.Magnitude) || float.IsInfinity(data.Magnitude))
                throw new ArgumentException("Event data magnitude must be finite.", nameof(data));
        }

        private sealed class Subscription
        {
            internal readonly GameplayEventSubscriptionHandle Handle;
            internal readonly GameplayTag EventTag;
            internal readonly Action<GameplayEventData> Callback;

            internal Subscription(
                GameplayEventSubscriptionHandle handle,
                GameplayTag eventTag,
                Action<GameplayEventData> callback)
            {
                Handle = handle;
                EventTag = eventTag;
                Callback = callback;
            }
        }
    }
}
