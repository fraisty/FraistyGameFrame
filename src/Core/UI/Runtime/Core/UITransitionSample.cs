using System;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame
{
    /// <summary>Resolved pose for one clip and one actual prefab instance. The player and editor seek the same pose.</summary>
    public sealed class UITransitionSample
    {
        private readonly List<Channel> _channels = new List<Channel>();
        private readonly float _duration;

        private sealed class Channel
        {
            internal RectTransform Rect;
            internal CanvasGroup Group;
            internal UITransitionProperty Property;
            internal Vector3 Baseline;
            internal Quaternion Rotation;
            internal readonly List<UITransitionTrack> Tracks = new List<UITransitionTrack>();

            internal void Write(Vector3 value)
            {
                if (!Rect) return;
                switch (Property)
                {
                    case UITransitionProperty.Alpha: if(Group) Group.alpha = Mathf.Clamp01(value.x); break;
                    case UITransitionProperty.Position: Rect.anchoredPosition = Baseline + value; break;
                    case UITransitionProperty.Scale: Rect.localScale = Vector3.Scale(Baseline, value); break;
                    case UITransitionProperty.Rotation: Rect.localRotation = Rotation * Quaternion.Euler(value); break;
                }
            }
        }

        public UITransitionSample(RectTransform root, UIFormTransition clip)
        {
            if (!TryValidate(clip, root, out string error)) throw new ArgumentException(error, nameof(clip));
            _duration = clip.duration;
            foreach (var track in clip.tracks)
            {
                var rect = (RectTransform)Resolve(root, track.targetPath);
                var channel = _channels.Find(c => c.Rect == rect && c.Property == track.property);
                if (channel == null)
                {
                    channel = new Channel { Rect = rect, Property = track.property };
                    switch (track.property)
                    {
                        case UITransitionProperty.Alpha:
                            channel.Group = rect.GetComponent<CanvasGroup>();
                            channel.Baseline = new Vector3(channel.Group.alpha, 0, 0);
                            break;
                        case UITransitionProperty.Position: channel.Baseline = rect.anchoredPosition; break;
                        case UITransitionProperty.Scale: channel.Baseline = rect.localScale; break;
                        case UITransitionProperty.Rotation: channel.Rotation = rect.localRotation; break;
                    }
                    _channels.Add(channel);
                }
                channel.Tracks.Add(track);
            }
            foreach (var channel in _channels) channel.Tracks.Sort((a,b) => a.start.CompareTo(b.start));
        }

        /// <summary>Seek by seconds. Gaps hold the previous track, and a touching successor wins at its start.</summary>
        public void Seek(float seconds)
        {
            seconds = Mathf.Clamp(seconds, 0, _duration);
            foreach (var channel in _channels)
            {
                var track = channel.Tracks[0];
                for (int i = 1; i < channel.Tracks.Count && channel.Tracks[i].start <= seconds; i++)
                    track = channel.Tracks[i];
                float blend = track.curve.Evaluate(Mathf.Clamp01((seconds - track.start) / track.duration));
                channel.Write(Vector3.LerpUnclamped(track.from, track.to, blend));
            }
        }

        /// <summary>Return this binding to its captured base pose and leave raycast state to UIForm.</summary>
        public void Restore()
        {
            foreach (var channel in _channels)
                channel.Write(channel.Property switch
                {
                    UITransitionProperty.Alpha => channel.Baseline,
                    UITransitionProperty.Scale => Vector3.one,
                    _ => Vector3.zero
                });
        }

        /// <summary>Validate authored time, paths, layout ownership and writable intervals before playback.</summary>
        public static bool TryValidate(UIFormTransition clip, RectTransform root, out string error,bool allowFormCanvasGroup = false)
        {
            error = null;
            if (clip == null || !float.IsFinite(clip.duration) || clip.duration <= 0 || clip.tracks == null || clip.tracks.Length == 0)
                error = "多通道动画需要正数总时长和至少一个通道。";
            if (error != null) return false;
            for (int i = 0; i < clip.tracks.Length; i++)
            {
                var track = clip.tracks[i];
                if (track == null || !Enum.IsDefined(typeof(UITransitionProperty), track.property) ||
                    !float.IsFinite(track.start) || track.start < 0 || !float.IsFinite(track.duration) || track.duration <= 0 ||
                    track.start + track.duration > clip.duration + .00001f || !Finite(track.from) || !Finite(track.to) ||
                    track.curve == null || track.curve.length == 0)
                { error = $"通道 {i + 1} 的属性、时间、值或曲线无效。"; return false; }
                foreach (var key in track.curve.keys)
                    if (!float.IsFinite(key.time) || !float.IsFinite(key.value) || key.time < 0 || key.time > 1 ||
                        float.IsNaN(key.inTangent) || float.IsNaN(key.outTangent))
                    { error = $"通道 {i + 1} 的曲线须使用 0–1 时间与有限键值。"; return false; }

                if (root)
                {
                    var target = Resolve(root, track.targetPath) as RectTransform;
                    if (!target || track.property == UITransitionProperty.Alpha && !target.GetComponent<CanvasGroup>() &&
                        !(allowFormCanvasGroup && target == root))
                    { error = $"通道 {i + 1} 需要唯一的 RectTransform 路径，透明度还需 CanvasGroup。"; return false; }
                    if (track.property == UITransitionProperty.Position && target.drivenByObject)
                    { error = $"通道 {i + 1} 的位置由布局驱动。请选择布局节点内的视觉子节点。"; return false; }
                }
                for (int j = 0; j < i; j++)
                {
                    var prior = clip.tracks[j];
                    if ((prior.targetPath ?? "") == (track.targetPath ?? "") && prior.property == track.property &&
                        prior.start < track.start + track.duration - .00001f &&
                        track.start < prior.start + prior.duration - .00001f)
                    { error = $"通道 {j + 1} 和 {i + 1} 同时写入同一属性。"; return false; }
                }
            }
            return true;
        }

        private static Transform Resolve(Transform root, string path)
        {
            if (string.IsNullOrEmpty(path)) return root;
            foreach (string name in path.Split('/'))
            {
                Transform next = null;
                for (int i = 0; i < root.childCount; i++)
                {
                    var child = root.GetChild(i);
                    if (child.name != name) continue;
                    if (next) return null;
                    next = child;
                }
                if (!next) return null;
                root = next;
            }
            return root;
        }

        private static bool Finite(Vector3 v) => float.IsFinite(v.x) && float.IsFinite(v.y) && float.IsFinite(v.z);
    }
}
