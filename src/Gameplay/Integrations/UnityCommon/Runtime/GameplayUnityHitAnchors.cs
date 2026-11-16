using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Extensions;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Reads evaluated Unity sockets. An optional SDK installs a reader for its native anchor kind.</summary>
    public static class GameplayUnityHitAnchors
    {
        public delegate bool SdkSampler(out GameplayHitPose pose);
        public static Func<Transform, string, SdkSampler> ResolveSpine { private get; set; }

        /// <summary>Anchor bindings owned by one runtime or preview subject, reused across its windows.</summary>
        public sealed class Session
        {
            private sealed class Binding
            {
                internal readonly string[] Segments;
                internal Transform Socket;
                internal SdkSampler SampleSpine;

                internal Binding(string path) => Segments = string.IsNullOrEmpty(path) ? Array.Empty<string>() : path.Split('/');

                internal bool IsCurrent(Transform subject)
                {
                    var current = Socket;
                    for (int i = Segments.Length - 1; i >= 0; i--)
                    {
                        if (!current || current.name != Segments[i]) return false;
                        current = current.parent;
                    }
                    return current == subject;
                }
            }

            private readonly Dictionary<(GameplayHitAnchorKind Kind, string Path, string Bone), Binding> _bindings = new();
            private Transform _subject;

            public bool TrySample(Transform subject, GameplayHitAnchor anchor, out GameplayHitPose pose)
            {
                pose = default;
                if (_subject != subject) { _bindings.Clear(); _subject = subject; }
                if (!subject) return false;
                var key = (anchor.Kind, anchor.Path, anchor.Bone);
                if (!_bindings.TryGetValue(key, out var binding)) _bindings.Add(key, binding = new Binding(anchor.Path));
                if (!binding.IsCurrent(subject))
                {
                    binding.Socket = binding.Segments.Length == 0 ? subject : subject.Find(anchor.Path);
                    binding.SampleSpine = null;
                }
                var socket = binding.Socket;
                if (!socket) return false;
                if (anchor.Kind == GameplayHitAnchorKind.SpineBone)
                {
                    binding.SampleSpine ??= ResolveSpine?.Invoke(socket, anchor.Bone);
                    return binding.SampleSpine != null && binding.SampleSpine(out pose);
                }
                return FromWorld(socket.position, socket.TransformVector(Vector3.right), socket.TransformVector(Vector3.up),
                    socket.GetInstanceID(), out pose);
            }

            public void Clear()
            {
                _bindings.Clear();
                _subject = null;
            }
        }

        /// <summary>Converts SDK world coordinates to the pure Standard pose contract.</summary>
        public static bool FromWorld(Vector3 point, Vector3 forward, Vector3 up, long revision, out GameplayHitPose pose)
        {
            pose = default;
            if (forward.sqrMagnitude <= 1e-10f) return false;
            pose = new GameplayHitPose(new GameplayPoint(point.x, point.y, point.z),
                new GameplayDirection(forward.x, forward.y, forward.z),
                up.sqrMagnitude > 1e-10f ? new GameplayDirection(up.x, up.y, up.z) : default, revision);
            return true;
        }
    }

    public sealed partial class GameplayUnityAnimationController : IGameplayHitAnchorProvider
    {
        private long _hitPoseRevision;
        private readonly GameplayUnityHitAnchors.Session _hitAnchors = new();

        private void CutHitAnchorBindings()
        {
            _hitPoseRevision++;
            _hitAnchors.Clear();
        }

        public bool TrySample(GameplayHitAnchor anchor, double worldTime, out GameplayHitPose pose)
        {
            pose = default;
            if (_disposed || !Subject || !_hitAnchors.TrySample(Subject.transform, anchor, out var sampled))
                return false;
            pose = new GameplayHitPose(sampled.Position, sampled.Forward, sampled.Up,
                HashCode.Combine(sampled.Revision, _hitPoseRevision, this));
            return true;
        }
    }
}

