using System;
using System.Collections.Generic;

namespace FRFrame
{
    public sealed class ObjectPool<T> : IObjectPool<T> where T : class
    {
        private readonly Stack<T> _stack = new Stack<T>();
        private readonly Func<T> _factory;
        private readonly Action<T> _onGet;
        private readonly Action<T> _onRelease;
        private readonly Action<T> _onDestroy;
        private readonly int _maxCount;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly HashSet<T> _inactiveSet = new HashSet<T>();
#endif

        public string Name { get; }
        public string ObjectTypeName => typeof(T).FullName;
        public int CountInactive => _stack.Count;
        public int CountActive { get; private set; }
        public int CountAll => CountActive + CountInactive;
        public int TotalCreated { get; private set; }
        public int TotalAcquired { get; private set; }
        public int TotalReleased { get; private set; }
        public int HitCount { get; private set; }
        public int MissCount { get; private set; }
        public bool EnableDuplicateReleaseCheck { get; set; } = true;

        public ObjectPool(
            string name,
            Func<T> factory,
            Action<T> onGet = null,
            Action<T> onRelease = null,
            Action<T> onDestroy = null,
            int maxCount = 0)
        {
            Name = name;
            _factory = factory ?? throw new ArgumentNullException(nameof(factory));
            _onGet = onGet;
            _onRelease = onRelease;
            _onDestroy = onDestroy;
            _maxCount = maxCount;
        }

        public T Acquire()
        {
            T obj;
            if (_stack.Count > 0)
            {
                obj = _stack.Pop();
                HitCount++;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
                _inactiveSet.Remove(obj);
#endif
            }
            else
            {
                obj = _factory.Invoke();
                TotalCreated++;
                MissCount++;
            }

            CountActive++;
            TotalAcquired++;
            _onGet?.Invoke(obj);
            return obj;
        }

        public void Release(T obj)
        {
            if (obj == null)
                return;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            if (EnableDuplicateReleaseCheck && _inactiveSet.Contains(obj))
            {
                FRLog.Warning($"ObjectPool duplicate release ignored : {Name}");
                return;
            }
#endif

            CountActive = Math.Max(0, CountActive - 1);
            TotalReleased++;
            _onRelease?.Invoke(obj);

            if (_maxCount > 0 && _stack.Count >= _maxCount)
            {
                _onDestroy?.Invoke(obj);
                return;
            }

            _stack.Push(obj);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _inactiveSet.Add(obj);
#endif
        }

        public void Clear()
        {
            if (_onDestroy != null)
            {
                while (_stack.Count > 0)
                {
                    T obj = _stack.Pop();
                    _onDestroy.Invoke(obj);
                }
            }
            else
            {
                _stack.Clear();
            }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            _inactiveSet.Clear();
#endif
        }
    }
}
