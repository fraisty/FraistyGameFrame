using System;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame
{
    public abstract class GameEventAsset : ScriptableObject
    {
        private readonly Dictionary<object, List<Subscription>> _subscriptionsByOwner = new Dictionary<object, List<Subscription>>();
        private readonly List<Subscription> _listeners = new List<Subscription>();
        private readonly List<Subscription> _pendingAdd = new List<Subscription>();
        private readonly List<Subscription> _pendingRemove = new List<Subscription>();
        private readonly Queue<Action> _postQueue = new Queue<Action>();

        private int _dispatchDepth;
        private bool _needsSort;
        private long _nextId = 1;
        private long _nextSequence = 1;

        public int RaiseCount { get; private set; }
        public float LastRaiseTime { get; private set; } = -1f;

        public IDisposable Register(Action listener, object owner = null, int priority = 0)
        {
            if (listener == null)
                throw new ArgumentNullException(nameof(listener));

            var sub = new Subscription(this, listener, owner, priority, false, _nextId++, _nextSequence++);
            Add(sub);
            RegisterOwnerSubscription(owner, sub);
            return sub;
        }

        public IDisposable RegisterOnce(Action listener, object owner = null, int priority = 0)
        {
            if (listener == null)
                throw new ArgumentNullException(nameof(listener));

            var sub = new Subscription(this, listener, owner, priority, true, _nextId++, _nextSequence++);
            Add(sub);
            RegisterOwnerSubscription(owner, sub);
            return sub;
        }

        public int Unregister(Action listener)
        {
            if (listener == null)
                throw new ArgumentNullException(nameof(listener));

            int removed = 0;
            for (int i = 0; i < _listeners.Count; i++)
            {
                var sub = _listeners[i];
                if (!sub.IsDisposed && sub.Listener == listener)
                {
                    sub.Dispose();
                    removed++;
                }
            }

            for (int i = 0; i < _pendingAdd.Count; i++)
            {
                var sub = _pendingAdd[i];
                if (!sub.IsDisposed && sub.Listener == listener)
                {
                    sub.Dispose();
                    removed++;
                }
            }

            return removed;
        }

        public int UnregisterAll(object owner)
        {
            if (owner == null)
                throw new ArgumentNullException(nameof(owner));

            if (!_subscriptionsByOwner.TryGetValue(owner, out List<Subscription> subscriptions))
                return 0;

            var snapshot = new List<Subscription>(subscriptions);
            int removed = 0;
            for (int i = 0; i < snapshot.Count; i++)
            {
                var sub = snapshot[i];
                if (sub == null || sub.IsDisposed)
                    continue;
                sub.Dispose();
                removed++;
            }

            return removed;
        }

        public void Raise()
        {
            RaiseCount++;
            LastRaiseTime = Time.realtimeSinceStartup;
            if (_needsSort)
                SortNow();

            _dispatchDepth++;
            try
            {
                for (int i = 0; i < _listeners.Count; i++)
                {
                    var sub = _listeners[i];
                    if (sub.IsDisposed)
                        continue;

                    sub.Listener.Invoke();
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
        }

        public void Post()
        {
            _postQueue.Enqueue(Raise);
        }

        public void Flush()
        {
            int count = _postQueue.Count;
            for (int i = 0; i < count; i++)
                _postQueue.Dequeue().Invoke();
        }

        public int GetListenerCount()
        {
            return _listeners.Count + _pendingAdd.Count;
        }

        public GameEventAssetSnapshot CreateSnapshot()
        {
            return new GameEventAssetSnapshot
            {
                Name = name,
                ListenerCount = GetListenerCount(),
                PostQueueCount = _postQueue.Count,
                RaiseCount = RaiseCount,
                LastRaiseTime = LastRaiseTime
            };
        }

        public void ClearAll()
        {
            var snapshotMain = new List<Subscription>(_listeners);
            var snapshotPending = new List<Subscription>(_pendingAdd);
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
                _subscriptionsByOwner.Clear();
                _postQueue.Clear();
            }
        }

        private void Add(Subscription sub)
        {
            if (_dispatchDepth > 0)
                _pendingAdd.Add(sub);
            else
                _listeners.Add(sub);
            _needsSort = true;
        }

        private void Remove(Subscription sub)
        {
            if (_dispatchDepth > 0)
            {
                if (!_pendingRemove.Contains(sub))
                    _pendingRemove.Add(sub);
            }
            else
            {
                bool removed = _listeners.Remove(sub) | _pendingAdd.Remove(sub);
                if (removed)
                    OnSubscriptionRemoved(sub);
            }
        }

        private void ApplyPendingChanges()
        {
            for (int i = 0; i < _pendingRemove.Count; i++)
            {
                Subscription sub = _pendingRemove[i];
                bool removed = _listeners.Remove(sub) | _pendingAdd.Remove(sub);
                if (removed)
                    OnSubscriptionRemoved(sub);
            }
            _pendingRemove.Clear();

            for (int i = 0; i < _pendingAdd.Count; i++)
            {
                Subscription sub = _pendingAdd[i];
                if (!sub.IsDisposed)
                    _listeners.Add(sub);
                else
                    OnSubscriptionRemoved(sub);
            }
            _pendingAdd.Clear();

            if (_needsSort)
                SortNow();
        }

        private void SortNow()
        {
            _listeners.Sort(CompareSubscription);
            _needsSort = false;
        }

        private static int CompareSubscription(Subscription a, Subscription b)
        {
            int priorityCompare = b.Priority.CompareTo(a.Priority);
            return priorityCompare != 0 ? priorityCompare : a.Sequence.CompareTo(b.Sequence);
        }

        private void RegisterOwnerSubscription(object owner, Subscription sub)
        {
            if (owner == null)
                return;

            if (!_subscriptionsByOwner.TryGetValue(owner, out List<Subscription> list))
            {
                list = new List<Subscription>();
                _subscriptionsByOwner.Add(owner, list);
            }

            list.Add(sub);
        }

        private void OnSubscriptionRemoved(Subscription sub)
        {
            if (sub == null || sub.Owner == null)
                return;

            if (_subscriptionsByOwner.TryGetValue(sub.Owner, out List<Subscription> list))
            {
                list.Remove(sub);
                if (list.Count == 0)
                    _subscriptionsByOwner.Remove(sub.Owner);
            }
        }

        private sealed class Subscription : IDisposable
        {
            private readonly GameEventAsset _asset;
            public Action Listener { get; }
            public object Owner { get; }
            public int Priority { get; }
            public bool IsOnce { get; }
            public long Sequence { get; }
            public bool IsDisposed { get; private set; }

            public Subscription(GameEventAsset asset, Action listener, object owner, int priority, bool isOnce, long id, long sequence)
            {
                _asset = asset;
                Listener = listener;
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
                _asset.Remove(this);
            }
        }
    }
}
