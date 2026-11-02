using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay
{
    /// <summary>Immutable piecewise linear numeric curve. Input uses the common value AST;
    /// values outside the authored domain clamp to endpoints. Keys must be finite and strictly
    /// increasing; this is a rule curve, not a Unity animation or weighted tangent curve.</summary>
    public sealed class GameplayCurveFloatValue : IGameplayFloatValue
    {
        private readonly KeyValuePair<float,float>[] _keys;
        private readonly IGameplayFloatValue _input;
        public bool RequiresApplicationContext=>_input.RequiresApplicationContext;
        public GameplayCurveFloatValue(IGameplayFloatValue input,IEnumerable<KeyValuePair<float,float>> keys)
        {
            _input=input??throw new ArgumentNullException(nameof(input));
            if(keys==null)throw new ArgumentNullException(nameof(keys));
            var copy=new List<KeyValuePair<float,float>>();
            foreach(var key in keys){
                if(!float.IsFinite(key.Key)||!float.IsFinite(key.Value)||(copy.Count>0&&key.Key<=copy[copy.Count-1].Key))
                    throw new ArgumentException("Curve keys must be finite and strictly increasing.",nameof(keys));
                copy.Add(key);
            }
            if(copy.Count==0)throw new ArgumentException("Curve needs at least one key.",nameof(keys));
            _keys=copy.ToArray();
        }
        public float Evaluate(GameplayValueEvaluationContext context)
        {
            float x=GameplayValueEvaluator.Evaluate(_input,context.Application);
            if(x<=_keys[0].Key)return _keys[0].Value;
            for(int i=1;i<_keys.Length;i++)if(x<=_keys[i].Key){
                var a=_keys[i-1];var b=_keys[i];
                double t=((double)x-a.Key)/((double)b.Key-a.Key);
                return (float)(a.Value+((double)b.Value-a.Value)*t);
            }
            return _keys[_keys.Length-1].Value;
        }
    }
    /// <summary>Reads the captured cast level, never a mutable shared Definition or live grant.</summary>
    public sealed class GameplayAbilityLevelFloatValue : IGameplayFloatValue
    {
        public bool RequiresApplicationContext => true;
        public float Evaluate(GameplayValueEvaluationContext context) => context.RequireApplication().Context.AbilityLevel;
    }

    /// <summary>Discrete one-based level table. Levels beyond the authored table clamp to its last entry.
    /// A defensive copy prevents authoring changes from rewriting already built Definitions.</summary>
    public sealed class GameplayLevelTableFloatValue : IGameplayFloatValue
    {
        private readonly float[] _values;
        public bool RequiresApplicationContext => true;
        public GameplayLevelTableFloatValue(IEnumerable<float> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var copy = new List<float>();
            foreach (float value in values)
            {
                if (!float.IsFinite(value)) throw new ArgumentOutOfRangeException(nameof(values));
                copy.Add(value);
            }
            if (copy.Count == 0) throw new ArgumentException("Level table cannot be empty.", nameof(values));
            _values = copy.ToArray();
        }
        public float Evaluate(GameplayValueEvaluationContext context) =>
            _values[Math.Min(context.RequireApplication().Context.AbilityLevel, _values.Length) - 1];
    }

    /// <summary>Clamps an expression using the same runtime evaluator as Effect magnitudes.</summary>
    public sealed class GameplayClampFloatValue : IGameplayFloatValue
    {
        private readonly IGameplayFloatValue _value, _minimum, _maximum;
        public IGameplayFloatValue Value => _value;
        public IGameplayFloatValue Minimum => _minimum;
        public IGameplayFloatValue Maximum => _maximum;
        public bool RequiresApplicationContext => _value.RequiresApplicationContext ||
            _minimum.RequiresApplicationContext || _maximum.RequiresApplicationContext;
        public GameplayClampFloatValue(IGameplayFloatValue value, IGameplayFloatValue minimum, IGameplayFloatValue maximum)
        {
            _value = value ?? throw new ArgumentNullException(nameof(value));
            _minimum = minimum ?? throw new ArgumentNullException(nameof(minimum));
            _maximum = maximum ?? throw new ArgumentNullException(nameof(maximum));
        }
        public float Evaluate(GameplayValueEvaluationContext context)
        {
            float minimum = GameplayValueEvaluator.Evaluate(_minimum, context.Application);
            float maximum = GameplayValueEvaluator.Evaluate(_maximum, context.Application);
            if (minimum > maximum) throw new ArithmeticException("Clamp minimum exceeds maximum.");
            return Math.Max(minimum, Math.Min(maximum, GameplayValueEvaluator.Evaluate(_value, context.Application)));
        }
    }
}
