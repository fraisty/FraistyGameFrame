using System;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame.Live2D
{
    [Serializable]
    public class Motion3JsonRoot
    {
        public int Version;
        public Motion3Meta Meta;
        public Motion3CurveJson[] Curves;
        public Motion3UserDataJson[] UserData;
    }

    [Serializable]
    public class Motion3Meta
    {
        public float Duration;
        public float Fps;
        public bool Loop;
        public bool AreBeziersRestricted;
        public int CurveCount;
        public int TotalSegmentCount;
        public int TotalPointCount;
        public int UserDataCount;
        public int TotalUserDataSize;
        public float FadeInTime = -1f;
        public float FadeOutTime = -1f;
    }

    [Serializable]
    public class Motion3CurveJson
    {
        public string Target;
        public string Id;
        public float FadeInTime = -1f;
        public float FadeOutTime = -1f;
        public float[] Segments;
    }

    [Serializable]
    public class Motion3UserDataJson
    {
        public float Time;
        public string Value;
    }

    public enum Live2DMotionTargetType
    {
        Parameter,
        PartOpacity,
        Model,
        Unknown
    }

    public enum Live2DMotionSegmentType
    {
        Linear = 0,
        Bezier = 1,
        Stepped = 2,
        InverseStepped = 3
    }

    public interface ILive2DPlayableClip
    {
        string ClipId { get; }
        float Duration { get; }
        bool Loop { get; }
        float FadeInTime { get; }
        float FadeOutTime { get; }
        IReadOnlyList<Live2DCustomTimelineEvent> CustomEvents { get; }
        void Evaluate(float time, List<Live2DEvaluatedParameter> results);
    }

    public readonly struct Live2DEvaluatedParameter
    {
        public readonly string Id;
        public readonly Live2DMotionTargetType TargetType;
        public readonly float Value;
        public readonly float Weight;
        public readonly Live2DParameterBlendMode BlendMode;

        public Live2DEvaluatedParameter(string id, Live2DMotionTargetType targetType, float value, float weight, Live2DParameterBlendMode blendMode)
        {
            Id = id;
            TargetType = targetType;
            Value = value;
            Weight = weight;
            BlendMode = blendMode;
        }
    }

    public interface ILive2DMotionSegment
    {
        float StartTime { get; }
        float EndTime { get; }
        float StartValue { get; }
        float EndValue { get; }
        float Evaluate(float time);
    }

    [Serializable]
    public class Live2DMotionCurveRuntime
    {
        public Live2DMotionTargetType TargetType;
        public string Id;
        public float FadeInTime = -1f;
        public float FadeOutTime = -1f;
        public readonly List<ILive2DMotionSegment> Segments = new List<ILive2DMotionSegment>();

        public float Evaluate(float time)
        {
            if (Segments.Count == 0) return 0f;
            if (time <= Segments[0].StartTime) return Segments[0].StartValue;
            for (int i = 0; i < Segments.Count; i++)
            {
                var segment = Segments[i];
                if (time <= segment.EndTime) return segment.Evaluate(time);
            }
            return Segments[Segments.Count - 1].EndValue;
        }

        public float GetFadeIn(float defaultFadeIn) => FadeInTime >= 0f ? FadeInTime : defaultFadeIn;
        public float GetFadeOut(float defaultFadeOut) => FadeOutTime >= 0f ? FadeOutTime : defaultFadeOut;
    }

    [Serializable]
    public class Live2DMotionRuntimeData : ILive2DPlayableClip
    {
        public string MotionId;
        public float MotionDuration;
        public bool JsonLoop;
        public bool ResolvedLoop;
        public float MotionFadeInTime = 0.25f;
        public float MotionFadeOutTime = 0.25f;
        public readonly List<Live2DMotionCurveRuntime> Curves = new List<Live2DMotionCurveRuntime>();
        public List<Live2DCustomTimelineEvent> CustomEvents { get; set; } = new List<Live2DCustomTimelineEvent>();

        public string ClipId => MotionId;
        public float Duration => MotionDuration;
        public bool Loop => ResolvedLoop;
        public float FadeInTime => MotionFadeInTime;
        public float FadeOutTime => MotionFadeOutTime;
        IReadOnlyList<Live2DCustomTimelineEvent> ILive2DPlayableClip.CustomEvents => CustomEvents;

        public void Evaluate(float time, List<Live2DEvaluatedParameter> results)
        {
            foreach (var curve in Curves)
                results.Add(new Live2DEvaluatedParameter(curve.Id, curve.TargetType, curve.Evaluate(time), 1f, Live2DParameterBlendMode.Override));
        }

        public static Live2DMotionRuntimeData FromTextAsset(TextAsset textAsset, string motionId = null)
        {
            if (textAsset == null) return null;
            Motion3JsonRoot root;
            try
            {
                root = JsonUtility.FromJson<Motion3JsonRoot>(textAsset.text);
            }
            catch (Exception e)
            {
                Debug.LogError($"Parse motion3 json failed: {textAsset.name}, {e.Message}");
                return null;
            }

            if (root == null || root.Meta == null || root.Curves == null)
            {
                Debug.LogError($"Parse motion3 json failed: {textAsset.name}");
                return null;
            }

            var data = new Live2DMotionRuntimeData
            {
                MotionId = string.IsNullOrEmpty(motionId) ? textAsset.name : motionId,
                MotionDuration = Mathf.Max(0f, root.Meta.Duration),
                JsonLoop = root.Meta.Loop,
                ResolvedLoop = root.Meta.Loop,
                MotionFadeInTime = root.Meta.FadeInTime >= 0f ? root.Meta.FadeInTime : 0.25f,
                MotionFadeOutTime = root.Meta.FadeOutTime >= 0f ? root.Meta.FadeOutTime : 0.25f
            };

            foreach (var curveJson in root.Curves)
            {
                if (curveJson == null || curveJson.Segments == null || curveJson.Segments.Length < 2) continue;
                var curve = new Live2DMotionCurveRuntime
                {
                    TargetType = ParseTarget(curveJson.Target),
                    Id = curveJson.Id,
                    FadeInTime = curveJson.FadeInTime,
                    FadeOutTime = curveJson.FadeOutTime
                };
                BuildSegments(curveJson.Segments, curve);
                if (curve.Segments.Count > 0) data.Curves.Add(curve);
            }

            if (root.UserData != null)
            {
                foreach (var userData in root.UserData)
                {
                    if (userData == null) continue;
                    data.CustomEvents.Add(new Live2DCustomTimelineEvent
                    {
                        time = userData.Time,
                        eventType = Live2DPlayableEventType.MotionUserData,
                        stringValue = userData.Value
                    });
                }
            }

            data.CustomEvents.Sort((a, b) => a.time.CompareTo(b.time));
            return data;
        }

        private static Live2DMotionTargetType ParseTarget(string target)
        {
            switch (target)
            {
                case "Parameter": return Live2DMotionTargetType.Parameter;
                case "PartOpacity": return Live2DMotionTargetType.PartOpacity;
                case "Model": return Live2DMotionTargetType.Model;
                default: return Live2DMotionTargetType.Unknown;
            }
        }

        private static void BuildSegments(float[] raw, Live2DMotionCurveRuntime curve)
        {
            int index = 0;
            if (!TryRead(raw, ref index, out float currentTime) || !TryRead(raw, ref index, out float currentValue)) return;

            while (index < raw.Length)
            {
                if (!TryRead(raw, ref index, out float typeFloat)) return;
                var type = (Live2DMotionSegmentType)Mathf.RoundToInt(typeFloat);
                switch (type)
                {
                    case Live2DMotionSegmentType.Linear:
                        if (!TryReadPoint(raw, ref index, out float lt, out float lv)) return;
                        curve.Segments.Add(new LinearSegment(currentTime, currentValue, lt, lv));
                        currentTime = lt; currentValue = lv;
                        break;
                    case Live2DMotionSegmentType.Bezier:
                        if (!TryRead(raw, ref index, out float c1t) || !TryRead(raw, ref index, out float c1v) ||
                            !TryRead(raw, ref index, out float c2t) || !TryRead(raw, ref index, out float c2v) ||
                            !TryRead(raw, ref index, out float bt) || !TryRead(raw, ref index, out float bv)) return;
                        curve.Segments.Add(new BezierSegment(currentTime, currentValue, c1t, c1v, c2t, c2v, bt, bv));
                        currentTime = bt; currentValue = bv;
                        break;
                    case Live2DMotionSegmentType.Stepped:
                        if (!TryReadPoint(raw, ref index, out float st, out float sv)) return;
                        curve.Segments.Add(new SteppedSegment(currentTime, currentValue, st, sv));
                        currentTime = st; currentValue = sv;
                        break;
                    case Live2DMotionSegmentType.InverseStepped:
                        if (!TryReadPoint(raw, ref index, out float it, out float iv)) return;
                        curve.Segments.Add(new InverseSteppedSegment(currentTime, currentValue, it, iv));
                        currentTime = it; currentValue = iv;
                        break;
                    default:
                        Debug.LogWarning($"Unknown motion segment type: {type}, curve={curve.Id}");
                        return;
                }
            }
        }

        private static bool TryReadPoint(float[] raw, ref int index, out float time, out float value)
        {
            time = 0f; value = 0f;
            return TryRead(raw, ref index, out time) && TryRead(raw, ref index, out value);
        }

        private static bool TryRead(float[] raw, ref int index, out float value)
        {
            value = 0f;
            if (raw == null || index < 0 || index >= raw.Length) return false;
            value = raw[index++];
            return true;
        }
    }

    public readonly struct LinearSegment : ILive2DMotionSegment
    {
        private readonly float _t0, _v0, _t1, _v1;
        public LinearSegment(float t0, float v0, float t1, float v1) { _t0 = t0; _v0 = v0; _t1 = t1; _v1 = v1; }
        public float StartTime => _t0; public float EndTime => _t1; public float StartValue => _v0; public float EndValue => _v1;
        public float Evaluate(float time) => Mathf.Approximately(_t0, _t1) ? _v1 : Mathf.Lerp(_v0, _v1, Mathf.InverseLerp(_t0, _t1, time));
    }

    public readonly struct BezierSegment : ILive2DMotionSegment
    {
        private readonly float _t0, _v0, _c1t, _c1v, _c2t, _c2v, _t1, _v1;
        public BezierSegment(float t0, float v0, float c1t, float c1v, float c2t, float c2v, float t1, float v1)
        { _t0 = t0; _v0 = v0; _c1t = c1t; _c1v = c1v; _c2t = c2t; _c2v = c2v; _t1 = t1; _v1 = v1; }
        public float StartTime => _t0; public float EndTime => _t1; public float StartValue => _v0; public float EndValue => _v1;
        public float Evaluate(float time)
        {
            if (Mathf.Approximately(_t0, _t1)) return _v1;
            float t = Mathf.InverseLerp(_t0, _t1, time);
            for (int i = 0; i < 5; i++)
            {
                float x = Cubic(_t0, _c1t, _c2t, _t1, t);
                float dx = CubicDerivative(_t0, _c1t, _c2t, _t1, t);
                if (Mathf.Abs(dx) < 0.00001f) break;
                t = Mathf.Clamp01(t - (x - time) / dx);
            }
            return Cubic(_v0, _c1v, _c2v, _v1, t);
        }
        private static float Cubic(float p0, float p1, float p2, float p3, float t)
        {
            float omt = 1f - t;
            return omt * omt * omt * p0 + 3f * omt * omt * t * p1 + 3f * omt * t * t * p2 + t * t * t * p3;
        }
        private static float CubicDerivative(float p0, float p1, float p2, float p3, float t)
        {
            float omt = 1f - t;
            return 3f * omt * omt * (p1 - p0) + 6f * omt * t * (p2 - p1) + 3f * t * t * (p3 - p2);
        }
    }

    public readonly struct SteppedSegment : ILive2DMotionSegment
    {
        private readonly float _t0, _v0, _t1, _v1;
        public SteppedSegment(float t0, float v0, float t1, float v1) { _t0 = t0; _v0 = v0; _t1 = t1; _v1 = v1; }
        public float StartTime => _t0; public float EndTime => _t1; public float StartValue => _v0; public float EndValue => _v1;
        public float Evaluate(float time) => _v0;
    }

    public readonly struct InverseSteppedSegment : ILive2DMotionSegment
    {
        private readonly float _t0, _v0, _t1, _v1;
        public InverseSteppedSegment(float t0, float v0, float t1, float v1) { _t0 = t0; _v0 = v0; _t1 = t1; _v1 = v1; }
        public float StartTime => _t0; public float EndTime => _t1; public float StartValue => _v0; public float EndValue => _v1;
        public float Evaluate(float time) => _v1;
    }
}
