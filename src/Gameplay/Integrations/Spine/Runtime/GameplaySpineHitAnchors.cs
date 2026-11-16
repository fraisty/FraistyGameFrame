using System.Runtime.CompilerServices;
using FRFrame.Gameplay.Standard;
using FRFrame.Gameplay.Unity;
using Spine.Unity;
using UnityEngine;

namespace FRFrame.Gameplay.Spine
{
    /// <summary>Reads the skeleton already evaluated by the current Spine host/player. No SDK update is invoked here.</summary>
    public static class GameplaySpineHitAnchors
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Register() => GameplayUnityHitAnchors.SpineSampler = TrySample;

        public static bool TrySample(Transform subject, GameplayHitAnchor anchor, out GameplayHitPose pose)
        {
            pose = default;
            var container = string.IsNullOrWhiteSpace(anchor.Path) ? subject : subject.Find(anchor.Path);
            if (!container) return false;
            var renderer = container.GetComponent<SkeletonAnimation>();
            if (!renderer)
            {
                var skeletons = container.GetComponentsInChildren<SkeletonAnimation>(true);
                if (skeletons.Length != 1) return false;
                renderer = skeletons[0];
            }
            var skeleton = renderer ? renderer.Skeleton : null;
            var bone = skeleton?.FindBone(anchor.Bone);
            if (bone == null || !bone.Active) return false;
            var transform = renderer.transform;
            var position = transform.TransformPoint(new Vector3(bone.WorldX, bone.WorldY, 0));
            var forward = transform.TransformVector(new Vector3(bone.A, bone.C, 0));
            int revision = System.HashCode.Combine(renderer.GetInstanceID(), RuntimeHelpers.GetHashCode(skeleton));
            var tracks = renderer.AnimationState?.Tracks;
            if (tracks != null) foreach (var track in tracks)
                if (track != null) revision = System.HashCode.Combine(revision, RuntimeHelpers.GetHashCode(track), track.Animation);
            return GameplayUnityHitAnchors.FromWorld(position, forward, revision, out pose);
        }
    }
}

