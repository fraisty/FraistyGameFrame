using FRFrame.Gameplay.Unity;
using Spine.Unity;
using UnityEngine;

namespace FRFrame.Gameplay.Spine
{
    /// <summary>Enables native tint on a preconfigured Spine presentation subject.
    /// Add it to the visible SkeletonAnimation, which supplies the base color.</summary>
    [AddComponentMenu("FRFrame/Gameplay/Spine Visual Color")]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(SkeletonAnimation))]
    public sealed class GameplaySpineVisualColor : MonoBehaviour, IGameplayVisualColorTarget
    {
        private SkeletonAnimation _animation;
        private void Awake() => _animation = GetComponent<SkeletonAnimation>();

        public Renderer Renderer => _animation && _animation.Skeleton != null ? GetComponent<Renderer>() : null;
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
