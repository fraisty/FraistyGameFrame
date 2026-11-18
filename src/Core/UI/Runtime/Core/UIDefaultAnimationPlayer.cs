using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace FRFrame
{
    public static class UIDefaultAnimationPlayer
    {
        internal sealed class Playback
        {
            internal UITweenHandle Handle;
            internal Action Restore;
        }

        public static UniTask PlayOpenAsync(UIForm form, UIFormConfig config, CancellationToken token) =>
            PlayAsync(form, config, true, token);

        public static UniTask PlayCloseAsync(UIForm form, UIFormConfig config, CancellationToken token) =>
            PlayAsync(form, config, false, token);

        internal static void Stop(UIForm form)
        {
            var current = form.DefaultAnimation;
            if (current == null) return;
            form.DefaultAnimation = null;
            current.Handle?.Kill();
            current.Restore();
        }

        private static async UniTask PlayAsync(UIForm form, UIFormConfig config, bool opening, CancellationToken token)
        {
            if (!form || config == null) return;
            Stop(form);
            if (token.IsCancellationRequested || form.IsCovered && config.coverVisualMode != UICoverVisualMode.KeepVisible ||
                !(form.transform is RectTransform rect) || !form.gameObject.activeInHierarchy) return;
            var group = form.GetComponent<CanvasGroup>();
            UIFormTransition clip = opening ? config.openTransition : config.closeTransition;
            Action<float> seek;
            Action restore;
            float duration;
            UIAnimationEase ease;
            if (clip?.enabled == true)
            {
                UITransitionSample sample;
                try { sample = new UITransitionSample(rect, clip); }
                catch (ArgumentException diagnostic)
                {
                    FRLog.Warning($"UI transition {config.formId}: {diagnostic.Message}");
                    return;
                }
                duration = clip.duration;
                ease = UIAnimationEase.Linear;
                seek = sample.Seek;
                restore = sample.Restore;
            }
            else
            {
                var preset = opening ? config.openAnimation : config.closeAnimation;
                if (preset == UIAnimationPreset.None || !group) return;
                duration = Mathf.Max(.01f, opening ? config.openAnimationDuration : config.closeAnimationDuration);
                ease = opening ? config.openAnimationEase : config.closeAnimationEase;
                float alpha = group.alpha;
                var scale = rect.localScale;
                var position = rect.anchoredPosition;
                bool scales = preset == UIAnimationPreset.Scale || preset == UIAnimationPreset.FadeScale;
                Vector2 slide = !opening || preset <= UIAnimationPreset.SlideFromBottom ? GetSlideOffset(rect, preset) : Vector2.zero;
                seek = seconds =>
                {
                    float t = seconds / duration;
                    group.alpha = Mathf.Clamp01(opening ? t : Mathf.LerpUnclamped(alpha, 0, t));
                    float amount = opening ? 1 - t : t;
                    if (scales) rect.localScale = scale * (1 - .15f * amount);
                    if (slide != Vector2.zero) rect.anchoredPosition = position + slide * amount;
                };
                restore = () =>
                {
                    if (!rect || !group) return;
                    group.alpha = alpha;
                    if (scales) rect.localScale = scale;
                    if (slide != Vector2.zero) rect.anchoredPosition = position;
                };
            }

            var playback = new Playback { Restore = restore };
            form.DefaultAnimation = playback;
            try
            {
                seek(0);
                playback.Handle = UITween.Value(0, duration, duration, seconds =>
                {
                    if (!form) { playback.Handle?.Kill(); return; }
                    if (ReferenceEquals(form.DefaultAnimation, playback) && !token.IsCancellationRequested)
                        seek(seconds);
                }, ease);
                await playback.Handle.ToUniTask(token);
            }
            finally
            {
                if (form && ReferenceEquals(form.DefaultAnimation, playback)) Stop(form);
            }
        }

        private static Vector2 GetSlideOffset(RectTransform rect, UIAnimationPreset preset)
        {
            Rect source = rect.parent is RectTransform parent ? parent.rect : rect.rect;
            float width = Mathf.Max(source.width, rect.rect.width, 1f);
            float height = Mathf.Max(source.height, rect.rect.height, 1f);
            return preset switch
            {
                UIAnimationPreset.SlideFromLeft or UIAnimationPreset.SlideToLeft => new Vector2(-width, 0),
                UIAnimationPreset.SlideFromRight or UIAnimationPreset.SlideToRight => new Vector2(width, 0),
                UIAnimationPreset.SlideFromTop or UIAnimationPreset.SlideToTop => new Vector2(0, height),
                UIAnimationPreset.SlideFromBottom or UIAnimationPreset.SlideToBottom => new Vector2(0, -height),
                _ => Vector2.zero
            };
        }
    }
}
