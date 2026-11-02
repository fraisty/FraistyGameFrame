using System;
using System.Collections.Generic;
using Live2D.Cubism.Core;
using UnityEngine;

namespace FRFrame.Live2D
{
    public sealed class Live2DMotionMixer
    {
        private readonly Live2DCubismBindingCache _bindingCache = new Live2DCubismBindingCache();
        private readonly Dictionary<Live2DLayerId, Live2DMotionLayer> _layers = new Dictionary<Live2DLayerId, Live2DMotionLayer>();
        private readonly List<Live2DEvaluatedParameter> _evaluated = new List<Live2DEvaluatedParameter>(256);
        private readonly List<Live2DPlayableEvent> _events = new List<Live2DPlayableEvent>(32);
        private readonly Live2DLayerId[] _applyOrder = { Live2DLayerId.Effect, Live2DLayerId.Base, Live2DLayerId.Overlay, Live2DLayerId.OverlayExpression, Live2DLayerId.OverlayBody, Live2DLayerId.OverlayEffect };

        public event Action<Live2DMotionEvent> MotionStarted;
        public event Action<Live2DMotionEvent> MotionCompleted;
        public event Action<Live2DPlayableEvent> PlayableEventTriggered;

        public Live2DCubismBindingCache BindingCache => _bindingCache;
        public bool ResetToDefaultBeforeApply { get; set; } = true;
        public float DefaultFadeIn { get; set; } = 0.25f;
        public float DefaultFadeOut { get; set; } = 0.25f;

        public void Build(CubismModel model)
        {
            _bindingCache.Build(model);
            EnsureLayer(Live2DLayerId.Effect);
            EnsureLayer(Live2DLayerId.Base);
            EnsureLayer(Live2DLayerId.Overlay);
            EnsureLayer(Live2DLayerId.OverlayExpression);
            EnsureLayer(Live2DLayerId.OverlayBody);
            EnsureLayer(Live2DLayerId.OverlayEffect);
        }

        public Live2DMotionLayer GetLayer(Live2DLayerId layerId) => EnsureLayer(layerId);

        public string GetCurrentClipId(Live2DLayerId layerId) => EnsureLayer(layerId).CurrentClipId;

        public void Play(Live2DLayerId layerId, ILive2DPlayableClip clip)
        {
            if (clip == null) return;
            EnsureLayer(layerId).Play(clip);
            MotionStarted?.Invoke(new Live2DMotionEvent(layerId, clip.ClipId));
        }

        public void Stop(Live2DLayerId layerId, float fadeOut)
        {
            EnsureLayer(layerId).Stop(fadeOut);
        }

        public void ResetRuntimeState()
        {
            foreach (var layer in _layers.Values) layer.Clear();
            _evaluated.Clear();
            _events.Clear();
            _bindingCache.ClearPersistentParameters();
            _bindingCache.ResetToDefaults();
        }

        public void Tick(float dt)
        {
            _events.Clear();
            foreach (var layer in _layers.Values) layer.Update(dt, _events);
            foreach (var e in _events) PlayableEventTriggered?.Invoke(e);

            foreach (var layer in _layers.Values)
            {
                if (layer.IsCurrentNaturallyFinished())
                {
                    string clipId = layer.CurrentClipId;
                    layer.Clear();
                    MotionCompleted?.Invoke(new Live2DMotionEvent(layer.LayerId, clipId));
                }
            }

            Apply();
        }

        private void Apply()
        {
            if (ResetToDefaultBeforeApply) _bindingCache.ResetToDefaults();
            foreach (var layerId in _applyOrder)
            {
                if (!_layers.TryGetValue(layerId, out var layer)) continue;
                _evaluated.Clear();
                layer.Evaluate(DefaultFadeIn, DefaultFadeOut, _evaluated);
                foreach (var e in _evaluated) _bindingCache.Apply(e);
            }
            _bindingCache.ApplyPersistentParameters();
        }

        private Live2DMotionLayer EnsureLayer(Live2DLayerId layerId)
        {
            if (_layers.TryGetValue(layerId, out var layer)) return layer;
            layer = new Live2DMotionLayer(layerId);
            _layers.Add(layerId, layer);
            return layer;
        }
    }
}
