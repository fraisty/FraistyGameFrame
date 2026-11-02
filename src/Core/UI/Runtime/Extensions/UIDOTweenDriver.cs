using System;
using DG.Tweening;
using UnityEngine;

namespace FRFrame
{
    public sealed class UIDOTweenDriver : IUITweenDriver
    {
        public UITweenHandle Move(RectTransform target, Vector2 to, float duration, UIAnimationEase ease, bool ignoreTimeScale = true)
        {
            if (target == null)
                return new UITweenHandle(null);

            Tween tween = target.DOAnchorPos(to, Mathf.Max(0f, duration)).SetEase(ConvertEase(ease)).SetUpdate(ignoreTimeScale).SetLink(target.gameObject, LinkBehaviour.KillOnDestroy);
            return new UITweenHandle(tween);
        }

        public UITweenHandle Scale(Transform target, Vector3 to, float duration, UIAnimationEase ease, bool ignoreTimeScale = true)
        {
            if (target == null)
                return new UITweenHandle(null);

            Tween tween = target.DOScale(to, Mathf.Max(0f, duration)).SetEase(ConvertEase(ease)).SetUpdate(ignoreTimeScale).SetLink(target.gameObject, LinkBehaviour.KillOnDestroy);
            return new UITweenHandle(tween);
        }

        public UITweenHandle Fade(CanvasGroup target, float to, float duration, UIAnimationEase ease, bool ignoreTimeScale = true)
        {
            if (target == null)
                return new UITweenHandle(null);

            Tween tween = target.DOFade(to, Mathf.Max(0f, duration)).SetEase(ConvertEase(ease)).SetUpdate(ignoreTimeScale).SetLink(target.gameObject, LinkBehaviour.KillOnDestroy);
            return new UITweenHandle(tween);
        }

        public UITweenHandle Value(float from, float to, float duration, Action<float> onUpdate, UIAnimationEase ease, bool ignoreTimeScale = true)
        {
            Tween tween = DOTween.To(() => from, value => onUpdate?.Invoke(value), to, Mathf.Max(0f, duration)).SetEase(ConvertEase(ease)).SetUpdate(ignoreTimeScale);
            return new UITweenHandle(tween);
        }

        private static Ease ConvertEase(UIAnimationEase ease)
        {
            switch (ease)
            {
                case UIAnimationEase.InQuad:
                    return Ease.InQuad;
                case UIAnimationEase.OutQuad:
                    return Ease.OutQuad;
                case UIAnimationEase.OutBack:
                    return Ease.OutBack;
                case UIAnimationEase.InBack:
                    return Ease.InBack;
                default:
                    return Ease.Linear;
            }
        }
    }
}
