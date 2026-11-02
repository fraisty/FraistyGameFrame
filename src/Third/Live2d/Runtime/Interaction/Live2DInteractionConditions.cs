using System;
using UnityEngine;

namespace FRFrame.Live2D
{
    [CreateAssetMenu(fileName = "Live2DBaseMotionCondition", menuName = "FRFrame/Live2D/Conditions/Base Motion")]
    public class Live2DBaseMotionCondition : Live2DInteractionCondition
    {
        public Live2DConditionStringCompareMode compareMode = Live2DConditionStringCompareMode.Equals;
        public string motionId;
        public bool ignoreCase = true;

        public override bool Evaluate(Live2DInteractionContext context)
        {
            return Compare(context.CurrentBaseMotion, motionId, compareMode, ignoreCase);
        }

        internal static bool Compare(string actual, string expected, Live2DConditionStringCompareMode mode, bool ignoreCase)
        {
            var comparison = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            switch (mode)
            {
                case Live2DConditionStringCompareMode.NotEquals: return !string.Equals(actual, expected, comparison);
                case Live2DConditionStringCompareMode.Contains: return !string.IsNullOrEmpty(actual) && actual.IndexOf(expected ?? string.Empty, comparison) >= 0;
                case Live2DConditionStringCompareMode.StartsWith: return !string.IsNullOrEmpty(actual) && actual.StartsWith(expected ?? string.Empty, comparison);
                case Live2DConditionStringCompareMode.EndsWith: return !string.IsNullOrEmpty(actual) && actual.EndsWith(expected ?? string.Empty, comparison);
                case Live2DConditionStringCompareMode.IsNullOrEmpty: return string.IsNullOrEmpty(actual);
                case Live2DConditionStringCompareMode.IsNotNullOrEmpty: return !string.IsNullOrEmpty(actual);
                default: return string.Equals(actual, expected, comparison);
            }
        }
    }

    [CreateAssetMenu(fileName = "Live2DOverlayMotionCondition", menuName = "FRFrame/Live2D/Conditions/Overlay Motion")]
    public class Live2DOverlayMotionCondition : Live2DInteractionCondition
    {
        public Live2DConditionStringCompareMode compareMode = Live2DConditionStringCompareMode.Equals;
        public string motionId;
        public bool ignoreCase = true;

        public override bool Evaluate(Live2DInteractionContext context)
        {
            return Live2DBaseMotionCondition.Compare(context.CurrentOverlayMotion, motionId, compareMode, ignoreCase);
        }
    }

    [CreateAssetMenu(fileName = "Live2DBaseMotionInGroupCondition", menuName = "FRFrame/Live2D/Conditions/Base Motion In Group")]
    public class Live2DBaseMotionInGroupCondition : Live2DInteractionCondition
    {
        public string groupId;
        public bool invert;

        public override bool Evaluate(Live2DInteractionContext context)
        {
            var library = context.Controller != null ? context.Controller.motionLibrary : null;
            bool matched = library != null && library.IsMotionInGroup(context.CurrentBaseMotion, groupId);
            return invert ? !matched : matched;
        }
    }

    [CreateAssetMenu(fileName = "Live2DOverlayPlayingCondition", menuName = "FRFrame/Live2D/Conditions/Overlay Playing")]
    public class Live2DOverlayPlayingCondition : Live2DInteractionCondition
    {
        public bool expectedPlaying;

        public override bool Evaluate(Live2DInteractionContext context)
        {
            bool playing = context.Controller != null && context.Controller.IsOverlayPlaying;
            return playing == expectedPlaying;
        }
    }

    [CreateAssetMenu(fileName = "Live2DAreaCondition", menuName = "FRFrame/Live2D/Conditions/Area")]
    public class Live2DAreaCondition : Live2DInteractionCondition
    {
        public Live2DConditionStringCompareMode compareMode = Live2DConditionStringCompareMode.Equals;
        public string areaId;
        public bool ignoreCase = true;

        public override bool Evaluate(Live2DInteractionContext context)
        {
            return Live2DBaseMotionCondition.Compare(context.AreaId, areaId, compareMode, ignoreCase);
        }
    }

    [CreateAssetMenu(fileName = "Live2DTriggerCondition", menuName = "FRFrame/Live2D/Conditions/Trigger")]
    public class Live2DTriggerCondition : Live2DInteractionCondition
    {
        public Live2DTriggerType triggerType = Live2DTriggerType.Tap;
        public bool invert;

        public override bool Evaluate(Live2DInteractionContext context)
        {
            bool matched = context.TriggerType == triggerType;
            return invert ? !matched : matched;
        }
    }

    [CreateAssetMenu(fileName = "Live2DExternalBoolCondition", menuName = "FRFrame/Live2D/Conditions/External Bool")]
    public class Live2DExternalBoolCondition : Live2DInteractionCondition
    {
        public string key;
        public Live2DConditionCompareMode compareMode = Live2DConditionCompareMode.Equals;
        public bool expectedValue = true;

        public override bool Evaluate(Live2DInteractionContext context)
        {
            var provider = context.Controller != null ? context.Controller.ExternalStateProvider : null;
            bool value = false;
            bool exists = provider != null && provider.TryGetBool(key, out value);
            if (compareMode == Live2DConditionCompareMode.Exists) return exists;
            if (compareMode == Live2DConditionCompareMode.NotExists) return !exists;
            if (!exists) return false;
            bool equals = value == expectedValue;
            return compareMode == Live2DConditionCompareMode.NotEquals ? !equals : equals;
        }
    }

    [CreateAssetMenu(fileName = "Live2DExternalIntCondition", menuName = "FRFrame/Live2D/Conditions/External Int")]
    public class Live2DExternalIntCondition : Live2DInteractionCondition
    {
        public string key;
        public Live2DConditionCompareMode compareMode = Live2DConditionCompareMode.Equals;
        public int expectedValue;

        public override bool Evaluate(Live2DInteractionContext context)
        {
            var provider = context.Controller != null ? context.Controller.ExternalStateProvider : null;
            int value = 0;
            bool exists = provider != null && provider.TryGetInt(key, out value);
            if (compareMode == Live2DConditionCompareMode.Exists) return exists;
            if (compareMode == Live2DConditionCompareMode.NotExists) return !exists;
            return exists && Compare(value, expectedValue, compareMode);
        }

        internal static bool Compare(float actual, float expected, Live2DConditionCompareMode mode)
        {
            switch (mode)
            {
                case Live2DConditionCompareMode.NotEquals: return !Mathf.Approximately(actual, expected);
                case Live2DConditionCompareMode.Greater: return actual > expected;
                case Live2DConditionCompareMode.GreaterOrEqual: return actual >= expected;
                case Live2DConditionCompareMode.Less: return actual < expected;
                case Live2DConditionCompareMode.LessOrEqual: return actual <= expected;
                default: return Mathf.Approximately(actual, expected);
            }
        }
    }

    [CreateAssetMenu(fileName = "Live2DExternalFloatCondition", menuName = "FRFrame/Live2D/Conditions/External Float")]
    public class Live2DExternalFloatCondition : Live2DInteractionCondition
    {
        public string key;
        public Live2DConditionCompareMode compareMode = Live2DConditionCompareMode.Equals;
        public float expectedValue;

        public override bool Evaluate(Live2DInteractionContext context)
        {
            var provider = context.Controller != null ? context.Controller.ExternalStateProvider : null;
            float value = 0f;
            bool exists = provider != null && provider.TryGetFloat(key, out value);
            if (compareMode == Live2DConditionCompareMode.Exists) return exists;
            if (compareMode == Live2DConditionCompareMode.NotExists) return !exists;
            return exists && Live2DExternalIntCondition.Compare(value, expectedValue, compareMode);
        }
    }

    [CreateAssetMenu(fileName = "Live2DExternalStringCondition", menuName = "FRFrame/Live2D/Conditions/External String")]
    public class Live2DExternalStringCondition : Live2DInteractionCondition
    {
        public string key;
        public Live2DConditionStringCompareMode compareMode = Live2DConditionStringCompareMode.Equals;
        public string expectedValue;
        public bool ignoreCase = true;

        public override bool Evaluate(Live2DInteractionContext context)
        {
            var provider = context.Controller != null ? context.Controller.ExternalStateProvider : null;
            if (provider == null || !provider.TryGetString(key, out string value))
                return compareMode == Live2DConditionStringCompareMode.IsNullOrEmpty;
            return Live2DBaseMotionCondition.Compare(value, expectedValue, compareMode, ignoreCase);
        }
    }
}
