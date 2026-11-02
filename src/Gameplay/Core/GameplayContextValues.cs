using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FRFrame.Gameplay
{
    /// <summary>整条执行链共享的有限 float 输入快照；不同于单次 Effect 的 SetByCaller。</summary>
    public sealed class GameplayContextValues
    {
        public static readonly GameplayTag TriggerMagnitudeKey = new GameplayTag("Data.Context.Trigger.Magnitude");
        public static GameplayContextValues Empty { get; } =
            new GameplayContextValues(Array.Empty<KeyValuePair<GameplayTag, float>>());
        private readonly IReadOnlyDictionary<GameplayTag, float> _values;

        public GameplayContextValues(IEnumerable<KeyValuePair<GameplayTag, float>> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var copy = new Dictionary<GameplayTag, float>();
            foreach (var pair in values)
            {
                if (!pair.Key.IsValid || !float.IsFinite(pair.Value) || copy.ContainsKey(pair.Key))
                    throw new ArgumentException("Context values require unique valid tags and finite floats.", nameof(values));
                copy.Add(pair.Key, pair.Value);
            }
            _values = new ReadOnlyDictionary<GameplayTag, float>(copy);
        }

        /// <summary>Reads an optional scalar without exposing the backing dictionary.</summary>
        public bool TryGetValue(GameplayTag key, out float value)
        {
            if (!key.IsValid) throw new ArgumentException("Context value key must be valid.", nameof(key));
            return _values.TryGetValue(key, out value);
        }
        /// <summary>Returns a new snapshot with this key added/replaced; the input snapshot never changes.</summary>
        public GameplayContextValues WithValue(GameplayTag key, float value)
        {
            if (!key.IsValid || !float.IsFinite(value)) throw new ArgumentException("Context values require valid tags and finite floats.");
            var copy = new Dictionary<GameplayTag, float>();
            foreach (var pair in _values) copy.Add(pair.Key, pair.Value);
            copy[key] = value;
            return new GameplayContextValues(copy);
        }
        /// <summary>Returns a new scalar snapshot; the supplied snapshot wins duplicate keys.
        /// Used to combine captured cast defaults with explicit input, without mutating either source.</summary>
        public GameplayContextValues Merge(GameplayContextValues values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var copy = new Dictionary<GameplayTag, float>();
            foreach (var pair in _values) copy.Add(pair.Key, pair.Value);
            foreach (var pair in values._values) copy[pair.Key] = pair.Value;
            return new GameplayContextValues(copy);
        }

        public float GetRequiredValue(GameplayTag key)
        {
            if (!key.IsValid) throw new ArgumentException("Context value key must be valid.", nameof(key));
            if (!_values.TryGetValue(key, out float value))
                throw new KeyNotFoundException("Missing context value: " + key);
            return value;
        }
        /// <summary>Explicit consumer boundary: makes a detached per-application Caller snapshot
        /// from captured chain inputs. Core Effect Apply never performs this conversion implicitly;
        /// Standard consumers use it unless an explicit effect caller snapshot is supplied.</summary>
        public GameplaySetByCallerValues AsSetByCallerValues()=>_values.Count==0?GameplaySetByCallerValues.Empty:new GameplaySetByCallerValues(_values);
        internal GameplaySetByCallerValues AsCostCallerValues()=>AsSetByCallerValues();
    }
}
