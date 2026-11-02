using System;
using System.Collections.Generic;
using System.Threading;

namespace FRFrame
{
    public sealed class FRLogContext
    {
        private readonly Dictionary<string, string> _globalFields = new Dictionary<string, string>();
        private readonly AsyncLocal<Stack<KeyValuePair<string, string>>> _scopeFields = new AsyncLocal<Stack<KeyValuePair<string, string>>>();

        public IReadOnlyDictionary<string, string> GlobalFields => _globalFields;

        public void SetGlobal(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Log context key is null or empty", nameof(key));

            if (value == null)
                _globalFields.Remove(key);
            else
                _globalFields[key] = value;
        }

        public bool RemoveGlobal(string key)
        {
            return !string.IsNullOrEmpty(key) && _globalFields.Remove(key);
        }

        public void ClearGlobal()
        {
            _globalFields.Clear();
        }

        public IDisposable BeginScope(string key, string value)
        {
            if (string.IsNullOrEmpty(key))
                throw new ArgumentException("Log context key is null or empty", nameof(key));

            Stack<KeyValuePair<string, string>> stack = _scopeFields.Value;
            if (stack == null)
            {
                stack = new Stack<KeyValuePair<string, string>>();
                _scopeFields.Value = stack;
            }

            var field = new KeyValuePair<string, string>(key, value);
            stack.Push(field);
            return new FRLogScope(this, field);
        }

        internal Dictionary<string, string> CreateFieldsSnapshot()
        {
            Dictionary<string, string> fields = null;
            if (_globalFields.Count > 0)
                fields = new Dictionary<string, string>(_globalFields);

            Stack<KeyValuePair<string, string>> stack = _scopeFields.Value;
            if (stack == null || stack.Count == 0)
                return fields;

            fields ??= new Dictionary<string, string>();
            KeyValuePair<string, string>[] scopes = stack.ToArray();
            for (int i = scopes.Length - 1; i >= 0; i--)
            {
                fields[scopes[i].Key] = scopes[i].Value;
            }

            return fields;
        }

        private void EndScope(KeyValuePair<string, string> field)
        {
            Stack<KeyValuePair<string, string>> stack = _scopeFields.Value;
            if (stack == null || stack.Count == 0)
                return;

            if (EqualityComparer<KeyValuePair<string, string>>.Default.Equals(stack.Peek(), field))
            {
                stack.Pop();
                return;
            }

            KeyValuePair<string, string>[] values = stack.ToArray();
            stack.Clear();
            bool removed = false;
            for (int i = values.Length - 1; i >= 0; i--)
            {
                if (!removed && EqualityComparer<KeyValuePair<string, string>>.Default.Equals(values[i], field))
                {
                    removed = true;
                    continue;
                }

                stack.Push(values[i]);
            }
        }

        private sealed class FRLogScope : IDisposable
        {
            private readonly FRLogContext _context;
            private readonly KeyValuePair<string, string> _field;
            private bool _disposed;

            public FRLogScope(FRLogContext context, KeyValuePair<string, string> field)
            {
                _context = context;
                _field = field;
            }

            public void Dispose()
            {
                if (_disposed)
                    return;

                _disposed = true;
                _context.EndScope(_field);
            }
        }
    }
}
