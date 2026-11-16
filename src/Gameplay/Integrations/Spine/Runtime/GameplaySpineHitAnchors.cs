using System.Runtime.CompilerServices;
using FRFrame.Gameplay.Standard;
using FRFrame.Gameplay.Unity;
using global::Spine;
using Spine.Unity;
using UnityEngine;

namespace FRFrame.Gameplay.Spine
{
    /// <summary>Reads the skeleton already evaluated by the current Spine host/player. No SDK update is invoked here.</summary>
    public static class GameplaySpineHitAnchors
    {
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        public static void Register() => GameplayUnityHitAnchors.ResolveSpine = Resolve;

        private static GameplayUnityHitAnchors.SdkSampler Resolve(Transform container, string boneName)
        {
            SkeletonAnimation renderer = null;
            Skeleton skeleton = null;
            Bone bone = null;
            return Sample;

            bool Sample(out GameplayHitPose pose)
            {
                pose = default;
                if (!renderer || renderer.transform != container && !renderer.transform.IsChildOf(container))
                {
                    renderer = container.GetComponent<SkeletonAnimation>();
                    if (!renderer)
                    {
                        var candidates = container.GetComponentsInChildren<SkeletonAnimation>(true);
                        if (candidates.Length != 1) return false;
                        renderer = candidates[0];
                    }
                }
                var current = renderer.Skeleton;
                if (!ReferenceEquals(skeleton, current))
                {
                    skeleton = current;
                    bone = skeleton?.FindBone(boneName);
                }
                if (bone == null || !bone.Active) return false;
                var transform = renderer.transform;
                var position = transform.TransformPoint(new Vector3(bone.WorldX, bone.WorldY, 0));
                var forward = transform.TransformVector(new Vector3(bone.A, bone.C, 0));
                var up = transform.TransformVector(new Vector3(bone.B, bone.D, 0));
                int revision = System.HashCode.Combine(renderer.GetInstanceID(), RuntimeHelpers.GetHashCode(skeleton));
                var tracks = renderer.AnimationState?.Tracks;
                if (tracks != null)
                    for (int i = 0; i < tracks.Count; i++)
                    {
                        var track = tracks.Items[i];
                        if (track != null) revision = System.HashCode.Combine(revision, RuntimeHelpers.GetHashCode(track), track.Animation);
                    }
                return GameplayUnityHitAnchors.FromWorld(position, forward, up, revision, out pose);
            }
        }
    }
}

