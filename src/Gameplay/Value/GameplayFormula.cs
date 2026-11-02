using System;
using System.Globalization;

namespace FRFrame.Gameplay
{
    /// <summary>Compiles a bounded, invariant-culture arithmetic expression into existing immutable value nodes.
    /// Supports Level, StackCount, Caster./Target. attributes, Context./Caller. keys,
    /// + - * /, min/max/clamp and parentheses. No reflection, scripts, or silent zero fallback.
    /// Text is authoring source; the returned tree is the sole runtime evaluator.</summary>
    public static class GameplayFormula
    {
        public static IGameplayFloatValue Compile(string expression)
        {
            if (string.IsNullOrWhiteSpace(expression)) throw new FormatException("Formula is empty.");
            if (expression.Length > 4096) throw new FormatException("Formula exceeds 4096 characters.");
            return new Parser(expression).Compile();
        }

        private sealed class Parser
        {
            private readonly string _text;
            private int _position, _depth, _nodes;
            public Parser(string text) => _text = text;
            public IGameplayFloatValue Compile()
            {
                var value = Sum();
                Space();
                if (_position != _text.Length) throw Error("Unexpected token");
                // Constant errors are authoring errors, not deferred until the first cast.
                if (!value.RequiresApplicationContext) GameplayValueEvaluator.Evaluate(value);
                return value;
            }
            private IGameplayFloatValue Sum()
            {
                var value = Product();
                while (true)
                {
                    if (Take('+')) value = Binary(GameplayBinaryFloatOperation.Add, value, Product());
                    else if (Take('-')) value = Binary(GameplayBinaryFloatOperation.Subtract, value, Product());
                    else return value;
                }
            }
            private IGameplayFloatValue Product()
            {
                var value = Primary();
                while (true)
                {
                    if (Take('*')) value = Binary(GameplayBinaryFloatOperation.Multiply, value, Primary());
                    else if (Take('/')) value = Binary(GameplayBinaryFloatOperation.Divide, value, Primary());
                    else return value;
                }
            }
            private IGameplayFloatValue Primary()
            {
                if (++_depth > 64) throw Error("Formula nesting exceeds 64");
                if (++_nodes > 256) throw Error("Formula exceeds 256 nodes");
                try
                {
                    if (Take('+')) return Primary();
                    if (Take('-')) return Binary(GameplayBinaryFloatOperation.Subtract, new GameplayConstantFloatValue(0), Primary());
                    if (Take('(')) { var value = Sum(); Require(')'); return value; }
                    Space();
                    if (_position >= _text.Length) throw Error("Expected a value");
                    char first = _text[_position];
                    if (char.IsDigit(first) || first == '.') return Number();
                    int start = _position;
                    while (_position < _text.Length && (char.IsLetterOrDigit(_text[_position]) ||
                        _text[_position] == '_' || _text[_position] == '.')) _position++;
                    if (_position == start) throw Error("Expected a number or identifier");
                    string name = _text.Substring(start, _position - start);
                    if (Take('('))
                    {
                        var a = Sum(); Require(','); var b = Sum();
                        if (name == "clamp") { Require(','); var c = Sum(); Require(')'); return new GameplayClampFloatValue(a, b, c); }
                        Require(')');
                        if (name == "min") return Binary(GameplayBinaryFloatOperation.Min, a, b);
                        if (name == "max") return Binary(GameplayBinaryFloatOperation.Max, a, b);
                        throw Error("Unknown function: " + name);
                    }
                    if (name == "Level") return new GameplayAbilityLevelFloatValue();
                    if (name == "StackCount") return new GameplayStackCountFloatValue();
                    int dot = name.IndexOf('.');
                    if (dot <= 0 || dot == name.Length - 1) throw Error("Unknown identifier: " + name);
                    string prefix = name.Substring(0, dot), key = name.Substring(dot + 1);
                    if(prefix=="Resource"||prefix=="CasterResource"||prefix=="TargetResource")return new GameplayResourceFloatValue(
                        prefix=="TargetResource"?GameplayValueAttributeSource.Target:GameplayValueAttributeSource.Instigator,new GameplayAttributeId(key));
                    if (prefix == "Caster" || prefix == "Target") return new GameplayAttributeFloatValue(
                        prefix == "Caster" ? GameplayValueAttributeSource.Instigator : GameplayValueAttributeSource.Target,
                        new GameplayAttributeId(key));
                    if (prefix == "Context") return new GameplayContextFloatValue(new GameplayTag(key));
                    if (prefix == "Caller") return new GameplaySetByCallerFloatValue(new GameplayTag(key));
                    throw Error("Unknown identifier: " + name);
                }
                finally { _depth--; }
            }
            private IGameplayFloatValue Number()
            {
                int start = _position;
                while (_position < _text.Length && (char.IsDigit(_text[_position]) || _text[_position] == '.')) _position++;
                if (_position < _text.Length && (_text[_position] == 'e' || _text[_position] == 'E'))
                {
                    _position++;
                    if (_position < _text.Length && (_text[_position] == '+' || _text[_position] == '-')) _position++;
                    while (_position < _text.Length && char.IsDigit(_text[_position])) _position++;
                }
                if (!float.TryParse(_text.Substring(start, _position - start), NumberStyles.Float,
                    CultureInfo.InvariantCulture, out float value) || !float.IsFinite(value)) throw Error("Invalid finite number");
                return new GameplayConstantFloatValue(value);
            }
            private IGameplayFloatValue Binary(GameplayBinaryFloatOperation operation, IGameplayFloatValue left, IGameplayFloatValue right)
            {
                if (++_nodes > 256) throw Error("Formula exceeds 256 nodes");
                if (operation == GameplayBinaryFloatOperation.Divide && !right.RequiresApplicationContext &&
                    GameplayValueEvaluator.Evaluate(right) == 0) throw Error("Division by zero");
                return new GameplayBinaryFloatValue(operation, left, right);
            }
            private void Space() { while (_position < _text.Length && char.IsWhiteSpace(_text[_position])) _position++; }
            private bool Take(char token)
            {
                Space(); if (_position >= _text.Length || _text[_position] != token) return false;
                _position++; return true;
            }
            private void Require(char token) { if (!Take(token)) throw Error("Expected '" + token + "'"); }
            private FormatException Error(string message) => new FormatException(message + " at character " + (_position + 1) + ".");
        }
    }
}
