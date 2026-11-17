using FRFrame.Gameplay.Unity;
using Spine.Unity;
using UnityEngine;

namespace FRFrame.Gameplay.Spine
{
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SkeletonAnimation))]
    internal sealed class GameplaySpineVisualColor : MonoBehaviour, IGameplayVisualColorTarget
    {
        private SkeletonAnimation _animation;
        private void Awake() => _animation = GetComponent<SkeletonAnimation>();

        public Renderer Renderer => GetComponent<Renderer>();
        public Color Color
        {
            get => _animation.Skeleton.GetColor();
            set
            {
                _animation.Skeleton.SetColor(value);
                if (!_animation.enabled) _animation.LateUpdate();
            }
        }
    }
}
