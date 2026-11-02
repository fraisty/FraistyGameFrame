using System;
using System.Collections.Generic;
using System.Reflection;
using Live2D.Cubism.Core;
using UnityEngine;

namespace FRFrame.Live2D
{
    public sealed class Live2DCubismBindingCache
    {
        private readonly Dictionary<string, int> _parameterIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, int> _partIndexMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> _persistentParameterValues = new Dictionary<string, float>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, Live2DParameterBlendMode> _persistentParameterBlendModes = new Dictionary<string, Live2DParameterBlendMode>(StringComparer.OrdinalIgnoreCase);
        private CubismParameter[] _parameters = Array.Empty<CubismParameter>();
        private CubismPart[] _parts = Array.Empty<CubismPart>();
        private float[] _defaultParameterValues = Array.Empty<float>();
        private float[] _defaultPartOpacities = Array.Empty<float>();

        public CubismModel Model { get; private set; }

        public void Build(CubismModel model)
        {
            Model = model;
            _parameterIndexMap.Clear();
            _partIndexMap.Clear();
            if (model == null)
            {
                _parameters = Array.Empty<CubismParameter>();
                _parts = Array.Empty<CubismPart>();
                _defaultParameterValues = Array.Empty<float>();
                _defaultPartOpacities = Array.Empty<float>();
                _persistentParameterValues.Clear();
                _persistentParameterBlendModes.Clear();
                return;
            }

            _parameters = model.Parameters ?? Array.Empty<CubismParameter>();
            _parts = model.Parts ?? Array.Empty<CubismPart>();
            _defaultParameterValues = new float[_parameters.Length];
            _defaultPartOpacities = new float[_parts.Length];

            for (int i = 0; i < _parameters.Length; i++)
            {
                _defaultParameterValues[i] = _parameters[i].Value;
                string id = GetCubismIdString(_parameters[i]);
                if (!string.IsNullOrEmpty(id) && !_parameterIndexMap.ContainsKey(id)) _parameterIndexMap.Add(id, i);
            }

            for (int i = 0; i < _parts.Length; i++)
            {
                _defaultPartOpacities[i] = _parts[i].Opacity;
                string id = GetCubismIdString(_parts[i]);
                if (!string.IsNullOrEmpty(id) && !_partIndexMap.ContainsKey(id)) _partIndexMap.Add(id, i);
            }
        }

        public void ResetToDefaults()
        {
            for (int i = 0; i < _parameters.Length; i++) _parameters[i].Value = _defaultParameterValues[i];
            for (int i = 0; i < _parts.Length; i++) _parts[i].Opacity = _defaultPartOpacities[i];
        }

        public void Apply(Live2DEvaluatedParameter evaluated)
        {
            if (evaluated.TargetType == Live2DMotionTargetType.Parameter)
                ApplyParameter(evaluated.Id, evaluated.Value, evaluated.Weight, evaluated.BlendMode);
            else if (evaluated.TargetType == Live2DMotionTargetType.PartOpacity)
                ApplyPartOpacity(evaluated.Id, evaluated.Value, evaluated.Weight);
        }

        public bool TryGetParameterValue(string parameterId, out float value)
        {
            value = 0f;
            if (!_parameterIndexMap.TryGetValue(parameterId, out int index)) return false;
            value = _parameters[index].Value;
            return true;
        }

        public bool ApplyParameter(string parameterId, float value, float weight, Live2DParameterBlendMode blendMode)
        {
            if (!_parameterIndexMap.TryGetValue(parameterId, out int index)) return false;
            float current = _parameters[index].Value;
            float target;
            switch (blendMode)
            {
                case Live2DParameterBlendMode.Additive:
                    target = current + value;
                    break;
                case Live2DParameterBlendMode.Multiply:
                    target = current * value;
                    break;
                default:
                    target = value;
                    break;
            }
            _parameters[index].Value = Mathf.Lerp(current, target, Mathf.Clamp01(weight));
            return true;
        }

        public void ApplyPartOpacity(string partId, float value, float weight)
        {
            if (!_partIndexMap.TryGetValue(partId, out int index)) return;
            _parts[index].Opacity = Mathf.Lerp(_parts[index].Opacity, value, Mathf.Clamp01(weight));
        }

        public bool SetPersistentParameter(string parameterId, float value, Live2DParameterBlendMode blendMode)
        {
            if (string.IsNullOrEmpty(parameterId)) return false;
            _persistentParameterValues[parameterId] = value;
            _persistentParameterBlendModes[parameterId] = blendMode;
            return ApplyParameter(parameterId, value, 1f, blendMode);
        }

        public void ClearPersistentParameter(string parameterId)
        {
            if (string.IsNullOrEmpty(parameterId)) return;
            _persistentParameterValues.Remove(parameterId);
            _persistentParameterBlendModes.Remove(parameterId);
        }

        public void ClearPersistentParameters()
        {
            _persistentParameterValues.Clear();
            _persistentParameterBlendModes.Clear();
        }

        public void ApplyPersistentParameters()
        {
            foreach (var pair in _persistentParameterValues)
            {
                _persistentParameterBlendModes.TryGetValue(pair.Key, out var blendMode);
                ApplyParameter(pair.Key, pair.Value, 1f, blendMode);
            }
        }

        private static string GetCubismIdString(Component comp)
        {
            if (comp == null) return string.Empty;
            Type t = comp.GetType();
            var prop = t.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null) return ConvertCubismIdObject(prop.GetValue(comp, null));
            var field = t.GetField("Id", BindingFlags.Public | BindingFlags.Instance);
            if (field != null) return ConvertCubismIdObject(field.GetValue(comp));
            return comp.name;
        }

        private static string ConvertCubismIdObject(object idObj)
        {
            if (idObj == null) return string.Empty;
            if (idObj is string s) return s;
            Type t = idObj.GetType();
            var prop = t.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance) ?? t.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
            {
                object v = prop.GetValue(idObj, null);
                if (v != null) return v.ToString();
            }
            return idObj.ToString();
        }
    }
}
