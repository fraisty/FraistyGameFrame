using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace FRFrame
{
    public sealed class EventBus
    {
        private readonly Dictionary<Type, IListenerList> _listenerLists = new Dictionary<Type, IListenerList>();
        private readonly Dictionary<Type, IAsyncListenerList> _asyncListenerLists = new Dictionary<Type, IAsyncListenerList>();
        private readonly Dictionary<object, List<IEventSubscriptionInternal>> _subscriptionsByOwner = new Dictionary<object, List<IEventSubscriptionInternal>>();
        private readonly Queue<IQueuedEvent> _postQueue = new Queue<IQueuedEvent>();

        private long _nextSubscriptionId = 1;
        private long _nextSequence = 1;

        public EventBusOptions Options { get; } = new EventBusOptions();

        public IDisposable Subscribe<TEvent>(Action<TEvent> handler, object owner = null, int priority = 0)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            ListenerList<TEvent> list = GetOrCreateListenerList<TEvent>();
            var subscription = new EventSubscription<TEvent>(this, list, handler, owner, priority, false, _nextSubscriptionId++, _nextSequence++);
            list.Add(subscription);
            RegisterOwnerSubscription(owner, subscription);
            return subscription;
        }

        public IDisposable SubscribeOnce<TEvent>(Action<TEvent> handler, object owner = null, int priority = 0)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            ListenerList<TEvent> list = GetOrCreateListenerList<TEvent>();
            var subscription = new EventSubscription<TEvent>(this, list, handler, owner, priority, true, _nextSubscriptionId++, _nextSequence++);
            list.Add(subscription);
            RegisterOwnerSubscription(owner, subscription);
            return subscription;
        }

        public IDisposable SubscribeAsync<TEvent>(Func<TEvent, UniTask> handler, object owner = null, int priority = 0)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            AsyncListenerList<TEvent> list = GetOrCreateAsyncListenerList<TEvent>();
            var subscription = new AsyncEventSubscription<TEvent>(this, list, handler, owner, priority, false, _nextSubscriptionId++, _nextSequence++);
            list.Add(subscription);
            RegisterOwnerSubscription(owner, subscription);
            return subscription;
        }

        public IDisposable SubscribeAsyncOnce<TEvent>(Func<TEvent, UniTask> handler, object owner = null, int priority = 0)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            AsyncListenerList<TEvent> list = GetOrCreateAsyncListenerList<TEvent>();
            var subscription = new AsyncEventSubscription<TEvent>(this, list, handler, owner, priority, true, _nextSubscriptionId++, _nextSequence++);
            list.Add(subscription);
            RegisterOwnerSubscription(owner, subscription);
            return subscription;
        }

        public int Unsubscribe<TEvent>(Action<TEvent> handler)
        {
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            if (!_listenerLists.TryGetValue(typeof(TEvent), out IListenerList baseList))
                return 0;

            return ((ListenerList<TEvent>)baseList).RemoveByHandler(handler);
        }

        public int UnsubscribeAll(object owner)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));

            if (!_subscriptionsByOwner.TryGetValue(owner, out List<IEventSubscriptionInternal> subscriptions))
                return 0;

            var snapshot = new List<IEventSubscriptionInternal>(subscriptions);
            int removed = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                IEventSubscriptionInternal sub = snapshot[i];
                if (sub == null || sub.IsDisposed)
                    continue;

                sub.Dispose();
                removed++;
            }

            return removed;
        }

        public void Publish<TEvent>(TEvent evt)
        {
            if (_listenerLists.TryGetValue(typeof(TEvent), out IListenerList baseList))
                ((ListenerList<TEvent>)baseList).Publish(evt);
        }

        public UniTask PublishAsync<TEvent>(TEvent evt, CancellationToken cancellationToken = default)
        {
            if (_asyncListenerLists.TryGetValue(typeof(TEvent), out IAsyncListenerList baseList))
                return ((AsyncListenerList<TEvent>)baseList).PublishAsync(evt, cancellationToken);

            return UniTask.CompletedTask;
        }

        public void Post<TEvent>(TEvent evt)
        {
            int max = Options.MaxPostQueueCount;
            if (max > 0 && _postQueue.Count >= max)
            {
                switch (Options.OverflowPolicy)
                {
                    case EventPostOverflowPolicy.Throw:
                        throw new InvalidOperationException($"Event post queue overflow. Max:{max}, Event:{typeof(TEvent).FullName}");
                    case EventPostOverflowPolicy.DropOldest:
                        _postQueue.Dequeue();
                        break;
                    case EventPostOverflowPolicy.WarningAndDropNewest:
                        FRLog.WarningT("Event", $"Event post queue overflow. Drop newest. Max:{max}, Event:{typeof(TEvent).FullName}");
                        return;
                    default:
                        return;
                }
            }

            _postQueue.Enqueue(new QueuedEvent<TEvent>(evt));
        }

        public void Flush()
        {
            int count = _postQueue.Count;
            for (int i = 0; i < count; i++)
            {
                IQueuedEvent queuedEvent = _postQueue.Dequeue();
                queuedEvent.Dispatch(this);
            }
        }

        public int Flush(int maxCount)
        {
            if (maxCount <= 0)
                return 0;

            int count = Math.Min(maxCount, _postQueue.Count);
            for (int i = 0; i < count; i++)
            {
                IQueuedEvent queuedEvent = _postQueue.Dequeue();
                queuedEvent.Dispatch(this);
            }

            return count;
        }

        public int ClearPostQueue()
        {
            int count = _postQueue.Count;
            _postQueue.Clear();
            return count;
        }

        public int ClearPostQueue<TEvent>()
        {
            return ClearPostQueue(typeof(TEvent));
        }

        public int ClearPostQueue(Type eventType)
        {
            if (eventType == null)
                throw new ArgumentNullException(nameof(eventType));

            int removed = 0;
            int count = _postQueue.Count;
            for (int i = 0; i < count; i++)
            {
                IQueuedEvent queuedEvent = _postQueue.Dequeue();
                if (queuedEvent.EventType == eventType)
                    removed++;
                else
                    _postQueue.Enqueue(queuedEvent);
            }

            return removed;
        }

        public void Clear<TEvent>()
        {
            if (_listenerLists.TryGetValue(typeof(TEvent), out IListenerList baseList))
                baseList.Clear();
        }

        public void ClearAll()
        {
            foreach (var pair in _listenerLists)
                pair.Value.Clear();

            foreach (var pair in _asyncListenerLists)
                pair.Value.Clear();

            _listenerLists.Clear();
            _asyncListenerLists.Clear();
            _subscriptionsByOwner.Clear();
            _postQueue.Clear();
        }

        public int GetListenerCount<TEvent>()
        {
            return GetListenerCount(typeof(TEvent));
        }

        public int GetListenerCount(Type eventType)
        {
            int count = 0;
            if (_listenerLists.TryGetValue(eventType, out IListenerList list))
                count += list.ListenerCount;
            if (_asyncListenerLists.TryGetValue(eventType, out IAsyncListenerList asyncList))
                count += asyncList.ListenerCount;
            return count;
        }

        public int GetTotalListenerCount()
        {
            int count = 0;
            foreach (var pair in _listenerLists)
                count += pair.Value.ListenerCount;
            foreach (var pair in _asyncListenerLists)
                count += pair.Value.ListenerCount;
            return count;
        }

        public int GetEventTypeCount()
        {
            var types = new HashSet<Type>(_listenerLists.Keys);
            foreach (var pair in _asyncListenerLists)
                types.Add(pair.Key);
            return types.Count;
        }

        public int GetOwnerCount()
        {
            return _subscriptionsByOwner.Count;
        }

        public int GetOwnerSubscriptionCount(object owner)
        {
            if (owner == null)
                return 0;

            return _subscriptionsByOwner.TryGetValue(owner, out List<IEventSubscriptionInternal> subscriptions) ? subscriptions.Count : 0;
        }

        public int GetPostQueueCount()
        {
            return _postQueue.Count;
        }

        public bool HasListener<TEvent>()
        {
            return GetListenerCount<TEvent>() > 0;
        }

        public EventBusSnapshot CreateSnapshot()
        {
            var listeners = new List<EventListenerSnapshot>();
            foreach (var pair in _listenerLists)
                listeners.Add(pair.Value.CreateSnapshot());
            foreach (var pair in _asyncListenerLists)
                listeners.Add(pair.Value.CreateSnapshot());

            var owners = new List<EventOwnerSnapshot>();
            foreach (var pair in _subscriptionsByOwner)
            {
                owners.Add(new EventOwnerSnapshot
                {
                    OwnerName = pair.Key?.ToString() ?? string.Empty,
                    OwnerType = pair.Key?.GetType(),
                    SubscriptionCount = pair.Value.Count
                });
            }

            return new EventBusSnapshot
            {
                TotalListenerCount = GetTotalListenerCount(),
                EventTypeCount = GetEventTypeCount(),
                OwnerCount = GetOwnerCount(),
                PostQueueCount = _postQueue.Count,
                Listeners = listeners,
                Owners = owners
            };
        }

        private ListenerList<TEvent> GetOrCreateListenerList<TEvent>()
        {
            Type eventType = typeof(TEvent);
            if (_listenerLists.TryGetValue(eventType, out IListenerList existing))
                return (ListenerList<TEvent>)existing;

            var list = new ListenerList<TEvent>(this);
            _listenerLists.Add(eventType, list);
            return list;
        }

        private AsyncListenerList<TEvent> GetOrCreateAsyncListenerList<TEvent>()
        {
            Type eventType = typeof(TEvent);
            if (_asyncListenerLists.TryGetValue(eventType, out IAsyncListenerList existing))
                return (AsyncListenerList<TEvent>)existing;

            var list = new AsyncListenerList<TEvent>(this);
            _asyncListenerLists.Add(eventType, list);
            return list;
        }

        private void RegisterOwnerSubscription(object owner, IEventSubscriptionInternal subscription)
        {
            if (owner == null || subscription == null)
                return;

            if (!_subscriptionsByOwner.TryGetValue(owner, out List<IEventSubscriptionInternal> list))
            {
                list = new List<IEventSubscriptionInternal>();
                _subscriptionsByOwner.Add(owner, list);
            }

            list.Add(subscription);
        }

        private void OnSubscriptionRemoved(IEventSubscriptionInternal subscription)
        {
            if (subscription == null || subscription.Owner == null)
                return;

            if (_subscriptionsByOwner.TryGetValue(subscription.Owner, out List<IEventSubscriptionInternal> list))
            {
                list.Remove(subscription);
                if (list.Count == 0)
                    _subscriptionsByOwner.Remove(subscription.Owner);
            }
        }

        private void HandleDispatchException(List<EventDispatchException> exceptions, Type eventType, object owner, Exception exception)
        {
            switch (Options.ExceptionPolicy)
            {
                case EventExceptionPolicy.LogAndContinue:
                    FRLog.Exception("Event", exception, $"Event listener failed. Event:{eventType.FullName}, Owner:{owner}");
                    break;
                case EventExceptionPolicy.CollectAndThrow:
                    exceptions.Add(new EventDispatchException { EventType = eventType, Owner = owner, Exception = exception });
                    break;
                default:
                    throw exception;
            }
        }

        private interface IListenerList
        {
            Type EventType { get; }
            int ListenerCount { get; }
            int OnceCount { get; }
            int MaxPriority { get; }
            int MinPriority { get; }
            void Clear();
            EventListenerSnapshot CreateSnapshot();
        }

        private interface IAsyncListenerList : IListenerList
        {
        }

        private interface IEventSubscriptionInternal : IDisposable
        {
            object Owner { get; }
            bool IsDisposed { get; }
        }

        private interface IQueuedEvent
        {
            Type EventType { get; }
            void Dispatch(EventBus bus);
        }

        private readonly struct QueuedEvent<TEvent> : IQueuedEvent
        {
            private readonly TEvent _event;
            public Type EventType => typeof(TEvent);

            public QueuedEvent(TEvent evt)
            {
                _event = evt;
            }

            public void Dispatch(EventBus bus)
            {
                bus.Publish(_event);
            }
        }

        private sealed class ListenerList<TEvent> : IListenerList
        {
            private readonly EventBus _bus;
            private readonly List<EventSubscription<TEvent>> _listeners = new List<EventSubscription<TEvent>>();
            private readonly List<EventSubscription<TEvent>> _pendingAdd = new List<EventSubscription<TEvent>>();
            private readonly List<EventSubscription<TEvent>> _pendingRemove = new List<EventSubscription<TEvent>>();
            private int _dispatchDepth;
            private bool _needsSort;

            public Type EventType => typeof(TEvent);
            public int ListenerCount => _listeners.Count + _pendingAdd.Count;
            public int OnceCount => CountOnce(_listeners) + CountOnce(_pendingAdd);
            public int MaxPriority => GetPriority(true);
            public int MinPriority => GetPriority(false);

            public ListenerList(EventBus bus)
            {
                _bus = bus;
            }

            public void Add(EventSubscription<TEvent> subscription)
            {
                if (_dispatchDepth > 0)
                    _pendingAdd.Add(subscription);
                else
                    _listeners.Add(subscription);

                _needsSort = true;
            }

            public void Remove(EventSubscription<TEvent> subscription)
            {
                if (subscription == null)
                    return;

                if (_dispatchDepth > 0)
                {
                    if (!_pendingRemove.Contains(subscription))
                        _pendingRemove.Add(subscription);
                }
                else
                {
                    bool removed = _listeners.Remove(subscription) | _pendingAdd.Remove(subscription);
                    if (removed)
                        _bus.OnSubscriptionRemoved(subscription);
                }
            }

            public int RemoveByHandler(Action<TEvent> handler)
            {
                int removed = 0;
                for (int i = 0; i < _listeners.Count; i++)
                {
                    var sub = _listeners[i];
                    if (!sub.IsDisposed && sub.Handler == handler)
                    {
                        sub.Dispose();
                        removed++;
                    }
                }

                for (int i = 0; i < _pendingAdd.Count; i++)
                {
                    var sub = _pendingAdd[i];
                    if (!sub.IsDisposed && sub.Handler == handler)
                    {
                        sub.Dispose();
                        removed++;
                    }
                }

                return removed;
            }

            public void Publish(TEvent evt)
            {
                if (_needsSort)
                    SortNow();

                List<EventDispatchException> exceptions = _bus.Options.ExceptionPolicy == EventExceptionPolicy.CollectAndThrow ? new List<EventDispatchException>() : null;
                _dispatchDepth++;
                try
                {
                    for (int i = 0; i < _listeners.Count; i++)
                    {
                        EventSubscription<TEvent> sub = _listeners[i];
                        if (sub.IsDisposed)
                            continue;

                        try
                        {
                            sub.Handler.Invoke(evt);
                        }
                        catch (Exception e)
                        {
                            _bus.HandleDispatchException(exceptions, typeof(TEvent), sub.Owner, e);
                        }

                        if (sub.IsOnce && !sub.IsDisposed)
                            sub.Dispose();
                    }
                }
                finally
                {
                    _dispatchDepth--;
                    if (_dispatchDepth == 0)
                        ApplyPendingChanges();
                }

                if (exceptions != null && exceptions.Count > 0)
                    throw new EventDispatchAggregateException(exceptions);
            }

            public void Clear()
            {
                var snapshotMain = new List<EventSubscription<TEvent>>(_listeners);
                var snapshotPending = new List<EventSubscription<TEvent>>(_pendingAdd);
                for (int i = 0; i < snapshotMain.Count; i++)
                    if (!snapshotMain[i].IsDisposed) snapshotMain[i].Dispose();
                for (int i = 0; i < snapshotPending.Count; i++)
                    if (!snapshotPending[i].IsDisposed) snapshotPending[i].Dispose();

                if (_dispatchDepth == 0)
                {
                    _listeners.Clear();
                    _pendingAdd.Clear();
                    _pendingRemove.Clear();
                    _needsSort = false;
                }
            }

            public EventListenerSnapshot CreateSnapshot()
            {
                return new EventListenerSnapshot { EventType = typeof(TEvent), ListenerCount = ListenerCount, OnceListenerCount = OnceCount, MaxPriority = MaxPriority, MinPriority = MinPriority };
            }

            private void ApplyPendingChanges()
            {
                for (int i = 0; i < _pendingRemove.Count; i++)
                {
                    EventSubscription<TEvent> sub = _pendingRemove[i];
                    bool removed = _listeners.Remove(sub) | _pendingAdd.Remove(sub);
                    if (removed)
                        _bus.OnSubscriptionRemoved(sub);
                }
                _pendingRemove.Clear();

                for (int i = 0; i < _pendingAdd.Count; i++)
                {
                    EventSubscription<TEvent> sub = _pendingAdd[i];
                    if (!sub.IsDisposed)
                        _listeners.Add(sub);
                    else
                        _bus.OnSubscriptionRemoved(sub);
                }
                _pendingAdd.Clear();

                if (_needsSort)
                    SortNow();
            }

            private void SortNow()
            {
                _listeners.Sort(SubscriptionComparison);
                _needsSort = false;
            }

            private int GetPriority(bool max)
            {
                bool has = false;
                int result = 0;
                for (int i = 0; i < _listeners.Count; i++)
                {
                    if (_listeners[i].IsDisposed)
                        continue;
                    if (!has || (max ? _listeners[i].Priority > result : _listeners[i].Priority < result))
                        result = _listeners[i].Priority;
                    has = true;
                }
                return has ? result : 0;
            }

            private static int CountOnce(List<EventSubscription<TEvent>> list)
            {
                int count = 0;
                for (int i = 0; i < list.Count; i++)
                    if (!list[i].IsDisposed && list[i].IsOnce) count++;
                return count;
            }

            private static int SubscriptionComparison(EventSubscription<TEvent> a, EventSubscription<TEvent> b)
            {
                int priorityCompare = b.Priority.CompareTo(a.Priority);
                return priorityCompare != 0 ? priorityCompare : a.Sequence.CompareTo(b.Sequence);
            }
        }

        private sealed class AsyncListenerList<TEvent> : IAsyncListenerList
        {
            private readonly EventBus _bus;
            private readonly List<AsyncEventSubscription<TEvent>> _listeners = new List<AsyncEventSubscription<TEvent>>();
            private bool _needsSort;

            public Type EventType => typeof(TEvent);
            public int ListenerCount => _listeners.Count;
            public int OnceCount => CountOnce(_listeners);
            public int MaxPriority => GetPriority(true);
            public int MinPriority => GetPriority(false);

            public AsyncListenerList(EventBus bus)
            {
                _bus = bus;
            }

            public void Add(AsyncEventSubscription<TEvent> subscription)
            {
                _listeners.Add(subscription);
                _needsSort = true;
            }

            public void Remove(AsyncEventSubscription<TEvent> subscription)
            {
                if (subscription != null && _listeners.Remove(subscription))
                    _bus.OnSubscriptionRemoved(subscription);
            }

            public async UniTask PublishAsync(TEvent evt, CancellationToken cancellationToken)
            {
                if (_needsSort)
                    SortNow();

                var snapshot = new List<AsyncEventSubscription<TEvent>>(_listeners);
                List<EventDispatchException> exceptions = _bus.Options.ExceptionPolicy == EventExceptionPolicy.CollectAndThrow ? new List<EventDispatchException>() : null;
                for (int i = 0; i < snapshot.Count; i++)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    AsyncEventSubscription<TEvent> sub = snapshot[i];
                    if (sub.IsDisposed)
                        continue;

                    try
                    {
                        await sub.Handler.Invoke(evt);
                    }
                    catch (Exception e)
                    {
                        _bus.HandleDispatchException(exceptions, typeof(TEvent), sub.Owner, e);
                    }

                    if (sub.IsOnce && !sub.IsDisposed)
                        sub.Dispose();
                }

                if (exceptions != null && exceptions.Count > 0)
                    throw new EventDispatchAggregateException(exceptions);
            }

            public void Clear()
            {
                var snapshot = new List<AsyncEventSubscription<TEvent>>(_listeners);
                for (int i = 0; i < snapshot.Count; i++)
                    if (!snapshot[i].IsDisposed) snapshot[i].Dispose();
                _listeners.Clear();
                _needsSort = false;
            }

            public EventListenerSnapshot CreateSnapshot()
            {
                return new EventListenerSnapshot { EventType = typeof(TEvent), ListenerCount = ListenerCount, OnceListenerCount = OnceCount, MaxPriority = MaxPriority, MinPriority = MinPriority };
            }

            private void SortNow()
            {
                _listeners.Sort(SubscriptionComparison);
                _needsSort = false;
            }

            private int GetPriority(bool max)
            {
                bool has = false;
                int result = 0;
                for (int i = 0; i < _listeners.Count; i++)
                {
                    if (_listeners[i].IsDisposed)
                        continue;
                    if (!has || (max ? _listeners[i].Priority > result : _listeners[i].Priority < result))
                        result = _listeners[i].Priority;
                    has = true;
                }
                return has ? result : 0;
            }

            private static int CountOnce(List<AsyncEventSubscription<TEvent>> list)
            {
                int count = 0;
                for (int i = 0; i < list.Count; i++)
                    if (!list[i].IsDisposed && list[i].IsOnce) count++;
                return count;
            }

            private static int SubscriptionComparison(AsyncEventSubscription<TEvent> a, AsyncEventSubscription<TEvent> b)
            {
                int priorityCompare = b.Priority.CompareTo(a.Priority);
                return priorityCompare != 0 ? priorityCompare : a.Sequence.CompareTo(b.Sequence);
            }
        }

        private sealed class EventSubscription<TEvent> : IEventSubscriptionInternal
        {
            public readonly ListenerList<TEvent> List;
            public readonly Action<TEvent> Handler;
            public object Owner { get; }
            public int Priority { get; }
            public bool IsOnce { get; }
            public long Sequence { get; }
            public bool IsDisposed { get; private set; }

            public EventSubscription(EventBus bus, ListenerList<TEvent> list, Action<TEvent> handler, object owner, int priority, bool isOnce, long id, long sequence)
            {
                List = list;
                Handler = handler;
                Owner = owner;
                Priority = priority;
                IsOnce = isOnce;
                Sequence = sequence;
            }

            public void Dispose()
            {
                if (IsDisposed)
                    return;
                IsDisposed = true;
                List.Remove(this);
            }
        }

        private sealed class AsyncEventSubscription<TEvent> : IEventSubscriptionInternal
        {
            public readonly AsyncListenerList<TEvent> List;
            public readonly Func<TEvent, UniTask> Handler;
            public object Owner { get; }
            public int Priority { get; }
            public bool IsOnce { get; }
            public long Sequence { get; }
            public bool IsDisposed { get; private set; }

            public AsyncEventSubscription(EventBus bus, AsyncListenerList<TEvent> list, Func<TEvent, UniTask> handler, object owner, int priority, bool isOnce, long id, long sequence)
            {
                List = list;
                Handler = handler;
                Owner = owner;
                Priority = priority;
                IsOnce = isOnce;
                Sequence = sequence;
            }

            public void Dispose()
            {
                if (IsDisposed)
                    return;
                IsDisposed = true;
                List.Remove(this);
            }
        }
    }
}
