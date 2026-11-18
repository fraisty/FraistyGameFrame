using System;
using UnityEngine;

namespace FRFrame
{
    public enum UITransitionProperty { Alpha, Position, Scale, Rotation }

    /// <summary>An opt-in open or close clip. Time is expressed in unscaled seconds.</summary>
    [Serializable]
    public sealed class UIFormTransition
    {
        public bool enabled;
        public float duration = 0.3f;
        public UITransitionTrack[] tracks = Array.Empty<UITransitionTrack>();
    }

    /// <summary>One property on a root-relative RectTransform. The curve maps local 0–1 time to blend.</summary>
    [Serializable]
    public sealed class UITransitionTrack
    {
        public string targetPath = "";
        public UITransitionProperty property;
        public float start;
        public float duration = 0.3f;
        public Vector3 from;
        public Vector3 to = Vector3.one;
        public AnimationCurve curve = AnimationCurve.Linear(0, 0, 1, 1);
    }
}
