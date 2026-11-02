using System;
using UnityEngine;

namespace FRFrame.Live2D
{
    public enum Live2DTrackEndReason
    {
        NaturalComplete = 0,
        Interrupted = 1,
        Stopped = 2,
        Replaced = 3
    }

    public readonly struct Live2DMotionEvent
    {
        public readonly Live2DLayerId LayerId;
        public readonly string ClipId;
        public readonly Live2DTrackEndReason Reason;

        public Live2DMotionEvent(Live2DLayerId layerId, string clipId, Live2DTrackEndReason reason = Live2DTrackEndReason.NaturalComplete)
        {
            LayerId = layerId;
            ClipId = clipId;
            Reason = reason;
        }
    }

    public readonly struct Live2DPlayableEvent
    {
        public readonly Live2DLayerId LayerId;
        public readonly string ClipId;
        public readonly Live2DPlayableEventType EventType;
        public readonly float Time;
        public readonly string StringValue;
        public readonly string AudioKey;
        public readonly UnityEngine.Object ObjectReference;
        public readonly float FloatValue;

        public Live2DPlayableEvent(Live2DLayerId layerId, string clipId, Live2DCustomTimelineEvent e)
        {
            LayerId = layerId;
            ClipId = clipId;
            EventType = e != null ? e.eventType : Live2DPlayableEventType.Custom;
            Time = e != null ? e.time : 0f;
            StringValue = e != null ? e.stringValue : string.Empty;
            AudioKey = e != null ? e.audioKey : string.Empty;
            ObjectReference = e != null ? e.objectReference : null;
            FloatValue = e != null ? e.floatValue : 0f;
        }
    }

    public readonly struct Live2DAudioEventData
    {
        public readonly string AudioKey;
        public readonly AudioClip FallbackClip;
        public readonly float Volume;
        public readonly float Pitch;

        public Live2DAudioEventData(string audioKey, AudioClip fallbackClip, float volume, float pitch)
        {
            AudioKey = audioKey;
            FallbackClip = fallbackClip;
            Volume = volume;
            Pitch = pitch;
        }
    }

    public interface ILive2DAudioEventHandler
    {
        void PlayAudio(Live2DAudioEventData data);
    }
}
