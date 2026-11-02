using System.Collections.Generic;
using UnityEngine;

namespace FRFrame.Live2D
{
    public sealed class Live2DParameterAnimationClip : ILive2DPlayableClip
    {
        private readonly Live2DParameterAnimationAsset _asset;
        private readonly List<Live2DCustomTimelineEvent> _emptyEvents = new List<Live2DCustomTimelineEvent>();

        public Live2DParameterAnimationClip(Live2DParameterAnimationAsset asset)
        {
            _asset = asset;
        }

        public string ClipId => _asset != null ? _asset.animationId : string.Empty;
        public float Duration => _asset != null ? Mathf.Max(0f, _asset.duration) : 0f;
        public bool Loop => _asset != null && _asset.ResolvedLoop;
        public float FadeInTime => _asset != null ? Mathf.Max(0f, _asset.fadeIn) : 0f;
        public float FadeOutTime => _asset != null ? Mathf.Max(0f, _asset.fadeOut) : 0f;
        public IReadOnlyList<Live2DCustomTimelineEvent> CustomEvents => _asset != null && _asset.events != null ? _asset.events : _emptyEvents;
        public Live2DParameterAnimationAsset Asset => _asset;

        public void Evaluate(float time, List<Live2DEvaluatedParameter> results)
        {
            if (_asset == null) return;

            if (_asset.mode == Live2DParameterAnimationMode.SimpleSet || _asset.mode == Live2DParameterAnimationMode.Mixed)
            {
                foreach (var set in _asset.simpleParameters)
                {
                    if (set == null || string.IsNullOrEmpty(set.parameterId) || set.applyImmediately) continue;
                    results.Add(new Live2DEvaluatedParameter(set.parameterId, Live2DMotionTargetType.Parameter, set.value, 1f, set.blendMode));
                }
            }

            if (_asset.mode == Live2DParameterAnimationMode.CurveAnimation || _asset.mode == Live2DParameterAnimationMode.Mixed)
            {
                foreach (var curve in _asset.curves)
                {
                    if (curve == null || string.IsNullOrEmpty(curve.parameterId) || curve.curve == null) continue;
                    results.Add(new Live2DEvaluatedParameter(curve.parameterId, Live2DMotionTargetType.Parameter, curve.curve.Evaluate(time), 1f, curve.blendMode));
                }
            }
        }

        public void ApplyImmediate(Live2DCubismBindingCache bindingCache)
        {
            if (_asset == null || bindingCache == null) return;
            foreach (var set in _asset.simpleParameters)
            {
                if (set == null || !set.applyImmediately || string.IsNullOrEmpty(set.parameterId)) continue;
                bindingCache.SetPersistentParameter(set.parameterId, set.value, set.blendMode);
            }
        }
    }
}
