using System;
using UnityEngine;

namespace FRFrame.Live2D
{
    public enum Live2DConditionCompareMode
    {
        Equals = 0,
        NotEquals = 1,
        Greater = 2,
        GreaterOrEqual = 3,
        Less = 4,
        LessOrEqual = 5,
        Exists = 6,
        NotExists = 7
    }

    public enum Live2DConditionStringCompareMode
    {
        Equals = 0,
        NotEquals = 1,
        Contains = 2,
        StartsWith = 3,
        EndsWith = 4,
        IsNullOrEmpty = 5,
        IsNotNullOrEmpty = 6
    }

    public readonly struct Live2DInteractionContext
    {
        public readonly Live2DMotionController Controller;
        public readonly Live2DTriggerType TriggerType;
        public readonly string AreaId;
        public readonly string CurrentBaseMotion;
        public readonly string CurrentOverlayMotion;

        public Live2DInteractionContext(Live2DMotionController controller, Live2DTriggerType triggerType, string areaId)
        {
            Controller = controller;
            TriggerType = triggerType;
            AreaId = areaId;
            CurrentBaseMotion = controller != null ? controller.CurrentBaseClipId : string.Empty;
            CurrentOverlayMotion = controller != null ? controller.CurrentOverlayClipId : string.Empty;
        }
    }

    public interface ILive2DExternalStateProvider
    {
        bool TryGetBool(string key, out bool value);
        bool TryGetInt(string key, out int value);
        bool TryGetFloat(string key, out float value);
        bool TryGetString(string key, out string value);
    }

    public abstract class Live2DInteractionCondition : ScriptableObject
    {
        public abstract bool Evaluate(Live2DInteractionContext context);
    }
}
