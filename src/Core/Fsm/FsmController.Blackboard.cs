using System;
using System.Collections.Generic;

namespace FRFrame
{
    public partial class FsmController
    {
        private readonly Dictionary<string, object> _blackboard = new Dictionary<string, object>(8);

        public void SetData(string key, object data)
        {
            _blackboard[key] = data;
        }

        public object GetData(string key)
        {
            if (!_blackboard.TryGetValue(key, out var value))
            {
                var msg = $"黑板中不存在 Key : {key}";
                LogError($"[FSM] {Name} GetData Error => {msg}");
                throw new Exception(msg);
            }

            return value;
        }

        public T GetData<T>(string key)
        {
            return (T)GetData(key);
        }

        public void SetData<Tag>(object value)
        {
            SetData(typeof(Tag).FullName, value);
        }

        public T GetData<T, Tag>()
        {
            return GetData<T>(typeof(Tag).FullName);
        }

        public void SetData<T>(T value)
        {
            SetData(typeof(T).FullName, value);
        }

        public T GetData<T>()
        {
            return GetData<T>(typeof(T).FullName);
        }

        public bool TryGetData(string key, out object value)
        {
            return _blackboard.TryGetValue(key, out value);
        }

        public bool TryGetData(Type type, out object value)
        {
            if (type == null)
            {
                value = null;
                return false;
            }

            return TryGetData(type.FullName, out value);
        }

        public bool TryGetData<T>(string key, out T value)
        {
            if (_blackboard.TryGetValue(key, out var obj))
            {
                if (obj is T t)
                {
                    value = t;
                    return true;
                }

                LogWarning($"Blackboard type mismatch. Fsm:{Name}, Key:{key}, Expected:{typeof(T).FullName}, Actual:{obj?.GetType().FullName ?? "null"}");
            }

            value = default;
            return false;
        }

        public bool TryGetData<T>(out T value)
        {
            return TryGetData(typeof(T).FullName, out value);
        }

        public bool TryGetData<TValue, TTag>(out TValue value)
        {
            return TryGetData(typeof(TTag).FullName, out value);
        }

        public T GetDataOrDefault<T>(string key, T defaultValue = default)
        {
            return TryGetData(key, out T value) ? value : defaultValue;
        }

        public T GetDataOrDefault<T>(T defaultValue = default)
        {
            return TryGetData(out T value) ? value : defaultValue;
        }

        public bool ContainsData(string key)
        {
            return _blackboard.ContainsKey(key);
        }

        public void RemoveData(string key)
        {
            _blackboard.Remove(key);
        }

        public void ClearBlackboard()
        {
            _blackboard.Clear();
        }

        protected void GetBlackboardKeys(List<string> results)
        {
            results.Clear();
            foreach (var pair in _blackboard)
                results.Add(pair.Key);
        }
    }
}
