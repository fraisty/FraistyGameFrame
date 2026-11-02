using System;
using System.Collections.Generic;

namespace FRFrame
{
    public sealed class ReferencePoolModule
    {
        private readonly Dictionary<Type, Stack<IReference>> _pools = new Dictionary<Type, Stack<IReference>>();
        private readonly Dictionary<Type, int> _maxCachedCounts = new Dictionary<Type, int>();
        private int _defaultMaxCachedCount;

        public bool HasPool<T>() where T : class, IReference, new()
        {
            return _pools.ContainsKey(typeof(T));
        }

        public T Acquire<T>() where T : class, IReference, new()
        {
            var type = typeof(T);
            if (_pools.TryGetValue(type, out var stack) && stack.Count > 0)
            {
                return stack.Pop() as T;
            }

            return new T();
        }

        public void Release(IReference reference)
        {
            if (reference == null) return;

            var type = reference.GetType();
            reference.Clear();

            if (!_pools.TryGetValue(type, out var stack))
            {
                stack = new Stack<IReference>();
                _pools.Add(type, stack);
            }

            int maxCachedCount = GetMaxCachedCount(type);
            if (maxCachedCount > 0 && stack.Count >= maxCachedCount)
                return;

            stack.Push(reference);
        }

        public int GetCachedCount<T>() where T : class, IReference, new()
        {
            if (_pools.TryGetValue(typeof(T), out var stack))
                return stack.Count;
            return 0;
        }

        public void SetDefaultMaxCachedCount(int count)
        {
            _defaultMaxCachedCount = Math.Max(0, count);
        }

        public void SetMaxCachedCount<T>(int count) where T : class, IReference, new()
        {
            _maxCachedCounts[typeof(T)] = Math.Max(0, count);
        }

        public int GetMaxCachedCount<T>() where T : class, IReference, new()
        {
            return GetMaxCachedCount(typeof(T));
        }

        public void Clear<T>() where T : class, IReference, new()
        {
            _pools.Remove(typeof(T));
        }

        public int Trim<T>(int targetCount) where T : class, IReference, new()
        {
            return Trim(typeof(T), targetCount);
        }

        public int TrimAll(float keepPercent)
        {
            keepPercent = Math.Max(0f, Math.Min(1f, keepPercent));
            int removed = 0;
            var types = new List<Type>(_pools.Keys);
            for (int i = 0; i < types.Count; i++)
            {
                var type = types[i];
                if (_pools.TryGetValue(type, out var stack))
                {
                    int targetCount = (int)(stack.Count * keepPercent);
                    removed += Trim(type, targetCount);
                }
            }

            return removed;
        }

        public void ClearAll()
        {
            _pools.Clear();
        }

        public int GetTotalCachedCount()
        {
            int count = 0;
            foreach (var pair in _pools)
            {
                count += pair.Value.Count;
            }

            return count;
        }

        public IReadOnlyList<ReferencePoolSnapshot> GetSnapshots()
        {
            var snapshots = new List<ReferencePoolSnapshot>(_pools.Count);
            foreach (var pair in _pools)
            {
                snapshots.Add(new ReferencePoolSnapshot
                {
                    Type = pair.Key,
                    CachedCount = pair.Value.Count,
                    MaxCachedCount = GetMaxCachedCount(pair.Key)
                });
            }

            return snapshots;
        }

        private int Trim(Type type, int targetCount)
        {
            targetCount = Math.Max(0, targetCount);
            if (!_pools.TryGetValue(type, out var stack))
                return 0;

            int removed = 0;
            while (stack.Count > targetCount)
            {
                stack.Pop();
                removed++;
            }

            if (stack.Count == 0)
                _pools.Remove(type);

            return removed;
        }

        private int GetMaxCachedCount(Type type)
        {
            return _maxCachedCounts.TryGetValue(type, out var count) ? count : _defaultMaxCachedCount;
        }
    }
}
