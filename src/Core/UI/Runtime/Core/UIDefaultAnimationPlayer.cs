using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace FRFrame
{
    public static class UIDefaultAnimationPlayer
    {
        public static UniTask PlayOpenAsync(UIForm form, UIFormConfig config, CancellationToken cancellationToken)
        {
            if (form == null || config == null || config.openAnimation == UIAnimationPreset.None)
                return UniTask.CompletedTask;

            return PlayAsync(form, config.openAnimation, config.openAnimationDuration, config.openAnimationEase, true, cancellationToken);
        }

        public static UniTask PlayCloseAsync(UIForm form, UIFormConfig config, CancellationToken cancellationToken)
        {
            if (form == null || config == null || config.closeAnimation == UIAnimationPreset.None)
                return UniTask.CompletedTask;

            return PlayAsync(form, config.closeAnimation, config.closeAnimationDuration, config.closeAnimationEase, false, cancellationToken);
        }

        private static async UniTask PlayAsync(UIForm form, UIAnimationPreset preset, float duration, UIAnimationEase ease, bool opening, CancellationToken cancellationToken)
        {
            RectTransform rect = form.transform as RectTransform;
            if (rect == null)
                return;

            CanvasGroup canvasGroup = form.GetComponent<CanvasGroup>();
            if (canvasGroup == null)
                canvasGroup = form.gameObject.AddComponent<CanvasGroup>();

            duration = Mathf.Max(0.01f, duration);
            Vector3 originalScale = rect.localScale;
            Vector2 originalPosition = rect.anchoredPosition;
            Vector2 slideOffset = GetSlideOffset(rect, preset);

            Prepare(rect, canvasGroup, preset, opening, originalScale, originalPosition, slideOffset);

            switch (preset)
            {
                case UIAnimationPreset.Fade:
                    await UITween.Fade(canvasGroup, opening ? 1f : 0f, duration, ease).ToUniTask(cancellationToken);
                    break;
                case UIAnimationPreset.Scale:
                    UITweenHandle scaleFade = UITween.Fade(canvasGroup, opening ? 1f : 0f, duration, ease);
                    UITweenHandle scaleOnly = UITween.Scale(rect, opening ? originalScale : originalScale * 0.85f, duration, ease);
                    await scaleOnly.ToUniTask(cancellationToken);
                    scaleFade.Kill(true);
                    break;
                case UIAnimationPreset.FadeScale:
                    UITweenHandle fade = UITween.Fade(canvasGroup, opening ? 1f : 0f, duration, ease);
                    UITweenHandle scale = UITween.Scale(rect, opening ? originalScale : originalScale * 0.85f, duration, ease);
                    await scale.ToUniTask(cancellationToken);
                    fade.Kill(true);
                    break;
                case UIAnimationPreset.SlideFromLeft:
                case UIAnimationPreset.SlideFromRight:
                case UIAnimationPreset.SlideFromTop:
                case UIAnimationPreset.SlideFromBottom:
                case UIAnimationPreset.SlideToLeft:
                case UIAnimationPreset.SlideToRight:
                case UIAnimationPreset.SlideToTop:
                case UIAnimationPreset.SlideToBottom:
                    Vector2 target = opening ? originalPosition : originalPosition + slideOffset;
                    UITweenHandle slideFade = UITween.Fade(canvasGroup, opening ? 1f : 0f, duration, ease);
                    UITweenHandle slideMove = UITween.Move(rect, target, duration, ease);
                    await slideMove.ToUniTask(cancellationToken);
                    slideFade.Kill(true);
                    break;
            }

            if (form != null && rect != null && canvasGroup != null && !cancellationToken.IsCancellationRequested)
            {
                canvasGroup.alpha = opening ? 1f : 0f;
                rect.localScale = originalScale;
                rect.anchoredPosition = originalPosition;
            }
        }

        private static void Prepare(RectTransform rect, CanvasGroup canvasGroup, UIAnimationPreset preset, bool opening, Vector3 originalScale, Vector2 originalPosition, Vector2 slideOffset)
        {
            if (!opening)
                return;

            switch (preset)
            {
                case UIAnimationPreset.Fade:
                    canvasGroup.alpha = 0f;
                    break;
                case UIAnimationPreset.Scale:
                    canvasGroup.alpha = 0f;
                    rect.localScale = originalScale * 0.85f;
                    break;
                case UIAnimationPreset.FadeScale:
                    canvasGroup.alpha = 0f;
                    rect.localScale = originalScale * 0.85f;
                    break;
                case UIAnimationPreset.SlideFromLeft:
                case UIAnimationPreset.SlideFromRight:
                case UIAnimationPreset.SlideFromTop:
                case UIAnimationPreset.SlideFromBottom:
                    canvasGroup.alpha = 0f;
                    rect.anchoredPosition = originalPosition + slideOffset;
                    break;
            }
        }

        private static Vector2 GetSlideOffset(RectTransform rect, UIAnimationPreset preset)
        {
            RectTransform parent = rect.parent as RectTransform;
            Rect sourceRect = parent != null ? parent.rect : rect.rect;
            float width = Mathf.Max(sourceRect.width, rect.rect.width, 1f);
            float height = Mathf.Max(sourceRect.height, rect.rect.height, 1f);
            switch (preset)
            {
                case UIAnimationPreset.SlideFromLeft:
                case UIAnimationPreset.SlideToLeft:
                    return new Vector2(-width, 0f);
                case UIAnimationPreset.SlideFromRight:
                case UIAnimationPreset.SlideToRight:
                    return new Vector2(width, 0f);
                case UIAnimationPreset.SlideFromTop:
                case UIAnimationPreset.SlideToTop:
                    return new Vector2(0f, height);
                case UIAnimationPreset.SlideFromBottom:
                case UIAnimationPreset.SlideToBottom:
                    return new Vector2(0f, -height);
                default:
                    return Vector2.zero;
            }
        }
    }
}
