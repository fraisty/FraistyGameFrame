using FRFrame.Gameplay.Extensions;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Reads evaluated Unity sockets. An optional SDK installs a reader for its native anchor kind.</summary>
    public static class GameplayUnityHitAnchors
    {
        public delegate bool SdkSampler(Transform subject, GameplayHitAnchor anchor, out GameplayHitPose pose);
        public static SdkSampler SpineSampler { private get; set; }

        public static bool TrySample(Transform subject, GameplayHitAnchor anchor, out GameplayHitPose pose)
        {
            pose = default;
            if (!subject) return false;
            if (anchor.Kind == GameplayHitAnchorKind.SpineBone)
                return SpineSampler != null && SpineSampler(subject, anchor, out pose);
            var selected = anchor.Kind == GameplayHitAnchorKind.OwnerRoot ? subject : subject.Find(anchor.Path);
            return selected && FromWorld(selected.position, selected.TransformVector(Vector3.right), selected.GetInstanceID(), out pose);
        }

        /// <summary>Converts SDK world coordinates to the pure Standard pose contract.</summary>
        public static bool FromWorld(Vector3 point, Vector3 forward, long revision, out GameplayHitPose pose)
        {
            pose = default;
            if (forward.sqrMagnitude <= 1e-10f) return false;
            pose = new GameplayHitPose(new GameplayPoint(point.x, point.y, point.z),
                new GameplayDirection(forward.x, forward.y, forward.z), revision);
            return true;
        }
    }

    public sealed partial class GameplayUnityAnimationController : IGameplayHitAnchorProvider
    {
        private long _hitPoseRevision;
        public bool TrySample(GameplayHitAnchor anchor, double worldTime, out GameplayHitPose pose)
        {
            pose = default;
            if (_disposed || !Subject || !GameplayUnityHitAnchors.TrySample(Subject.transform, anchor, out var sampled))
                return false;
            pose = new GameplayHitPose(sampled.Position, sampled.Forward, System.HashCode.Combine(sampled.Revision, _hitPoseRevision, this));
            return true;
        }
    }
}

