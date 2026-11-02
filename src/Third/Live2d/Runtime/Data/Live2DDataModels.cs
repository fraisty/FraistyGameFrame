using System;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame.Live2D
{
    public enum Live2DLayerId
    {
        Effect = 0,
        Base = 1,
        Overlay = 2,
        OverlayExpression = 3,
        OverlayBody = 4,
        OverlayEffect = 5,
        Custom = 100
    }

    public enum Live2DMotionCategory
    {
        Unknown = 0,
        Idle = 1,
        Base = 2,
        Touch = 3,
        Drag = 4,
        TouchIdle = 5,
        Effect = 6,
        Transition = 7,
        Custom = 100
    }

    public enum Live2DLoopMode
    {
        UseSource = 0,
        ForceLoop = 1,
        ForceOnce = 2
    }

    public enum Live2DInterruptPolicy
    {
        ImmediateCrossFade = 0,
        QueueUntilFinish = 1,
        IgnoreIfPlaying = 2,
        ForceRestart = 3
    }

    public enum Live2DSelectionMode
    {
        First = 0,
        Random = 1,
        WeightedRandom = 2,
        Sequence = 3,
        ShuffleBag = 4
    }

    public enum Live2DTriggerType
    {
        Tap = 0,
        DragStart = 1,
        DragRepeat = 2,
        DragEnd = 3,
        LongPress = 4,
        HoverEnter = 5,
        HoverExit = 6,
        Swipe = 7
    }

    public enum Live2DInteractionActionType
    {
        PlayMotion = 0,
        PlayMotionGroup = 1,
        PlayParameterAnimation = 2,
        StopLayer = 3,
        SwitchLayout = 4,
        SendEvent = 5,
        PlayAudio = 6,
        SetParameterValue = 7,
        ToggleParameterValue = 8
    }

    public enum Live2DTouchShapeType
    {
        Rect = 0,
        Polygon = 1
    }

    public enum Live2DParameterBlendMode
    {
        Override = 0,
        Additive = 1,
        Multiply = 2
    }

    public enum Live2DParameterAnimationMode
    {
        SimpleSet = 0,
        CurveAnimation = 1,
        Mixed = 2
    }

    public enum Live2DParameterApplyMode
    {
        Immediate = 0,
        AsTrack = 1
    }

    public enum Live2DPlayableEventType
    {
        MotionUserData = 0,
        PlayAudio = 1,
        SendEvent = 2,
        SetParameter = 3,
        Custom = 100
    }

    [Serializable]
    public struct Live2DMotionReference
    {
        public Live2DMotionLibrary library;
        public string motionId;

        public bool IsValid => library != null && !string.IsNullOrEmpty(motionId);
    }

    [Serializable]
    public class Live2DCustomTimelineEvent
    {
        public float time;
        public Live2DPlayableEventType eventType = Live2DPlayableEventType.Custom;
        public string stringValue;
        public string audioKey;
        public UnityEngine.Object objectReference;
        public float floatValue;
    }

    [Serializable]
    public class Live2DMotionClip
    {
        public string motionId;
        public string displayName;
        public TextAsset motionAsset;
        public string sourceFilePath;
        public Live2DMotionCategory category = Live2DMotionCategory.Unknown;
        public Live2DLoopMode loopMode = Live2DLoopMode.UseSource;
        public Live2DLayerId defaultLayer = Live2DLayerId.Base;
        public Live2DInterruptPolicy interruptPolicy = Live2DInterruptPolicy.ImmediateCrossFade;
        public float fadeIn = -1f;
        public float fadeOut = -1f;
        public List<string> tags = new List<string>();
        public List<Live2DCustomTimelineEvent> events = new List<Live2DCustomTimelineEvent>();
    }

    [Serializable]
    public class Live2DMotionCandidate
    {
        public Live2DMotionReference motion;
        public float weight = 1f;
        public float cooldown;
        public bool enabled = true;
    }

    [Serializable]
    public class Live2DMotionGroup
    {
        public string groupId;
        public string displayName;
        public Live2DSelectionMode selectionMode = Live2DSelectionMode.ShuffleBag;
        public float groupCooldown;
        public bool avoidRepeatLast = true;
        public int repeatBlockCount = 1;
        public string fallbackGroupId;
        public List<Live2DMotionCandidate> candidates = new List<Live2DMotionCandidate>();

        [NonSerialized] public int sequenceIndex;
        [NonSerialized] public string lastMotionId;
        [NonSerialized] public float cooldownUntil;
        [NonSerialized] public readonly List<int> shuffleBag = new List<int>();
        [NonSerialized] public readonly Dictionary<string, float> candidateCooldownUntil = new Dictionary<string, float>();
    }
}
