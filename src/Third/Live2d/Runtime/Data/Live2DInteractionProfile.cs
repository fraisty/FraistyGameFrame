using System;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame.Live2D
{
    [Serializable]
    public class Live2DInteractionAction
    {
        public Live2DInteractionActionType actionType = Live2DInteractionActionType.PlayMotionGroup;
        public Live2DMotionReference motion;
        public Live2DMotionReference afterMotion;
        public bool playAfterMotionOnComplete;
        public Live2DLayerId afterMotionLayerId = Live2DLayerId.Base;
        public Live2DInterruptPolicy afterMotionInterruptPolicy = Live2DInterruptPolicy.ImmediateCrossFade;
        public string groupId;
        public Live2DParameterAnimationAsset parameterAnimation;
        public Live2DLayerId layerId = Live2DLayerId.Overlay;
        public Live2DInterruptPolicy interruptPolicy = Live2DInterruptPolicy.ImmediateCrossFade;
        public Live2DParameterApplyMode parameterApplyMode = Live2DParameterApplyMode.AsTrack;
        public string layoutId;
        public string eventName;
        public string payload;
        public string audioKey;
        public AudioClip fallbackClip;
        public float volume = 1f;
        public float pitch = 1f;
        public string parameterId;
        public float parameterValue;
        public float toggleOffValue;
        public float toggleOnValue = 1f;
        public float toggleThreshold = 0.5f;
    }

    [Serializable]
    public class Live2DInteractionBinding
    {
        public Live2DTriggerType trigger = Live2DTriggerType.Tap;
        public bool enabled = true;
        public float cooldown;
        public List<Live2DInteractionCondition> conditions = new List<Live2DInteractionCondition>();
        public List<Live2DInteractionAction> actions = new List<Live2DInteractionAction>();

        [NonSerialized] public float cooldownUntil;
    }

    [Serializable]
    public class Live2DTouchArea
    {
        public string areaId;
        public string displayName;
        public bool enabled = true;
        public int priority;
        public Live2DTouchShapeType shapeType = Live2DTouchShapeType.Rect;
        public Rect normalizedRect = new Rect(0.4f, 0.4f, 0.2f, 0.2f);
        public List<Vector2> normalizedPoints = new List<Vector2>();
        public List<Live2DInteractionBinding> bindings = new List<Live2DInteractionBinding>();

        public bool Contains(Vector2 normalizedPoint)
        {
            if (!enabled) return false;
            if (shapeType == Live2DTouchShapeType.Rect) return normalizedRect.Contains(normalizedPoint);
            return ContainsPolygon(normalizedPoint);
        }

        private bool ContainsPolygon(Vector2 p)
        {
            if (normalizedPoints == null || normalizedPoints.Count < 3) return false;
            bool inside = false;
            for (int i = 0, j = normalizedPoints.Count - 1; i < normalizedPoints.Count; j = i++)
            {
                Vector2 pi = normalizedPoints[i];
                Vector2 pj = normalizedPoints[j];
                bool intersect = ((pi.y > p.y) != (pj.y > p.y)) &&
                                 (p.x < (pj.x - pi.x) * (p.y - pi.y) / Mathf.Max(0.000001f, pj.y - pi.y) + pi.x);
                if (intersect) inside = !inside;
            }
            return inside;
        }
    }

    [Serializable]
    public class Live2DInteractionLayout
    {
        public string layoutId = "Default";
        public string displayName = "Default";
        public List<Live2DTouchArea> areas = new List<Live2DTouchArea>();
    }

    [Serializable]
    public class Live2DLayoutBinding
    {
        public string baseMotionId;
        public string baseGroupId;
        public string layoutId;
    }

    [Serializable]
    public class Live2DRawImageTransformPreset
    {
        public bool enabled;
        public Vector2 anchoredPosition;
        public Vector3 localScale = Vector3.one;
    }

    [Serializable]
    public class Live2DModelTransformPreset
    {
        public bool enabled;
        public Vector3 localPosition;
        public Vector3 localEulerAngles;
        public Vector3 localScale = Vector3.one;
    }

    [CreateAssetMenu(fileName = "Live2DInteractionProfile", menuName = "FRFrame/Live2D/Interaction Profile")]
    public class Live2DInteractionProfile : ScriptableObject
    {
        public Live2DMotionLibrary motionLibrary;
        public string defaultLayoutId = "Default";
        public List<Live2DInteractionLayout> layouts = new List<Live2DInteractionLayout>();
        public List<Live2DLayoutBinding> layoutBindings = new List<Live2DLayoutBinding>();
        public Live2DRawImageTransformPreset rawImageTransform = new Live2DRawImageTransformPreset();
        public Live2DModelTransformPreset modelTransform = new Live2DModelTransformPreset();

        public Live2DInteractionLayout GetLayout(string layoutId)
        {
            if (string.IsNullOrEmpty(layoutId)) return null;
            foreach (var layout in layouts)
                if (layout != null && string.Equals(layout.layoutId, layoutId, StringComparison.Ordinal)) return layout;
            return null;
        }

        public Live2DInteractionLayout GetDefaultLayout() => GetLayout(defaultLayoutId) ?? (layouts.Count > 0 ? layouts[0] : null);

        public Live2DInteractionLayout GetLayoutForBase(string baseMotionId)
        {
            foreach (var binding in layoutBindings)
            {
                if (binding == null || string.IsNullOrEmpty(binding.layoutId)) continue;
                if (!string.IsNullOrEmpty(binding.baseMotionId) && string.Equals(binding.baseMotionId, baseMotionId, StringComparison.OrdinalIgnoreCase))
                    return GetLayout(binding.layoutId) ?? GetDefaultLayout();
            }
            return GetDefaultLayout();
        }

        public Live2DTouchArea Hit(Vector2 normalizedPoint, string baseMotionId)
        {
            var layout = GetLayoutForBase(baseMotionId);
            if (layout == null || layout.areas == null) return null;
            Live2DTouchArea best = null;
            int bestPriority = int.MinValue;
            foreach (var area in layout.areas)
            {
                if (area == null || !area.Contains(normalizedPoint)) continue;
                if (area.priority >= bestPriority)
                {
                    bestPriority = area.priority;
                    best = area;
                }
            }
            return best;
        }
    }
}
