using System;
using UnityEngine;

namespace FRFrame
{
    public abstract class GameEventAsset<T> : ScriptableObject
    {
        private readonly EventBus _bus = new EventBus();
        private int _raiseCount;
        private float _lastRaiseTime = -1f;

        public IDisposable Register(Action<T> listener, object owner = null, int priority = 0)
        {
            return _bus.Subscribe(listener, owner, priority);
        }

        public IDisposable RegisterOnce(Action<T> listener, object owner = null, int priority = 0)
        {
            return _bus.SubscribeOnce(listener, owner, priority);
        }

        public int Unregister(Action<T> listener)
        {
            return _bus.Unsubscribe(listener);
        }

        public int UnregisterAll(object owner)
        {
            return _bus.UnsubscribeAll(owner);
        }

        public void Raise(T value)
        {
            _raiseCount++;
            _lastRaiseTime = Time.realtimeSinceStartup;
            _bus.Publish(value);
        }

        public void Post(T value)
        {
            _bus.Post(value);
        }

        public void Flush()
        {
            _bus.Flush();
        }

        public int GetListenerCount()
        {
            return _bus.GetListenerCount<T>();
        }

        public GameEventAssetSnapshot CreateSnapshot()
        {
            return new GameEventAssetSnapshot
            {
                Name = name,
                ListenerCount = GetListenerCount(),
                PostQueueCount = _bus.GetPostQueueCount(),
                RaiseCount = _raiseCount,
                LastRaiseTime = _lastRaiseTime
            };
        }

        public void ClearAll()
        {
            _bus.ClearAll();
        }
    }
}
