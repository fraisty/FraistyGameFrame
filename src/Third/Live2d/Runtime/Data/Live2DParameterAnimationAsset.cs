using System;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame.Live2D
{
    [Serializable]
    public class Live2DSimpleParameterSet
    {
        public string parameterId;
        public float value = 1f;
        public bool applyImmediately = true;
        public Live2DParameterBlendMode blendMode = Live2DParameterBlendMode.Override;
    }

    [Serializable]
    public class Live2DParameterCurve
    {
        public string parameterId;
        public Live2DParameterBlendMode blendMode = Live2DParameterBlendMode.Override;
        public AnimationCurve curve = AnimationCurve.Linear(0f, 0f, 1f, 1f);
    }

    [CreateAssetMenu(fileName = "Live2DParameterAnimation", menuName = "FRFrame/Live2D/Parameter Animation")]
    public class Live2DParameterAnimationAsset : ScriptableObject
    {
        public string animationId;
        public string displayName;
        public Live2DParameterAnimationMode mode = Live2DParameterAnimationMode.SimpleSet;
        public float duration = 0.25f;
        public Live2DLoopMode loopMode = Live2DLoopMode.ForceOnce;
        public float fadeIn = 0.05f;
        public float fadeOut = 0.05f;
        public Live2DLayerId defaultLayerId = Live2DLayerId.Overlay;
        public List<Live2DSimpleParameterSet> simpleParameters = new List<Live2DSimpleParameterSet>();
        public List<Live2DParameterCurve> curves = new List<Live2DParameterCurve>();
        public List<Live2DCustomTimelineEvent> events = new List<Live2DCustomTimelineEvent>();

        public bool ResolvedLoop => loopMode == Live2DLoopMode.ForceLoop;
    }
}
