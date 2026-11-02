using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using DG.Tweening;

namespace FRFrame
{
    public sealed class UITweenHandle
    {
        private readonly Tween _tween;
        private bool _completed;

        public UITweenHandle(Tween tween)
        {
            _tween = tween;
            _completed = tween == null;
            _tween?.OnComplete(() => _completed = true);
        }

        public bool IsActive => _tween != null && _tween.IsActive();
        public bool IsCompleted => _completed || _tween != null && _tween.IsComplete();

        public void Kill(bool complete = false)
        {
            if (_tween != null && _tween.IsActive())
                _tween.Kill(complete);
        }

        public async UniTask ToUniTask(CancellationToken cancellationToken = default)
        {
            if (_tween == null)
                return;

            while (_tween.IsActive() && !_tween.IsComplete())
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    Kill();
                    return;
                }

                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken).SuppressCancellationThrow();
            }
        }
    }

    public interface IUITweenDriver
    {
        UITweenHandle Move(UnityEngine.RectTransform target, UnityEngine.Vector2 to, float duration, UIAnimationEase ease, bool ignoreTimeScale = true);
        UITweenHandle Scale(UnityEngine.Transform target, UnityEngine.Vector3 to, float duration, UIAnimationEase ease, bool ignoreTimeScale = true);
        UITweenHandle Fade(UnityEngine.CanvasGroup target, float to, float duration, UIAnimationEase ease, bool ignoreTimeScale = true);
        UITweenHandle Value(float from, float to, float duration, Action<float> onUpdate, UIAnimationEase ease, bool ignoreTimeScale = true);
    }
}
