using System;
using System.Threading;
using Cysharp.Threading.Tasks;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace FRFrame
{
    public enum UIScrollTextMode
    {
        HorizontalLoop = 0,
        VerticalLoop = 1,
        PingPong = 2,
        Once = 3
    }

    public sealed class UIScrollText : MonoBehaviour
    {
        public RectTransform viewport;
        public RectTransform textRoot;
        public Text uguiText;
        public TMP_Text tmpText;
        public UIScrollTextMode mode = UIScrollTextMode.HorizontalLoop;
        public float speed = 60f;
        public float startDelay = 0.5f;
        public float endDelay = 0.5f;
        public float spacing = 40f;
        public bool playOnEnable = true;
        public bool useUnscaledTime = true;

        private UITweenHandle _tween;
        private CancellationTokenSource _playCancellation;
        private Vector2 _origin;

        public bool IsOverflow { get; private set; }
        public bool IsPlaying { get; private set; }

        private void Awake()
        {
            CacheComponents();
        }

        private void OnEnable()
        {
            CacheComponents();
            Refresh();
            if (playOnEnable)
                Play();
        }

        private void OnDisable()
        {
            Stop(true);
        }

        public void SetText(string text, bool play = true)
        {
            Stop(true);
            CacheComponents();
            if (tmpText != null)
                tmpText.text = text;
            else if (uguiText != null)
                uguiText.text = text;
            Refresh();
            if (play)
                Play();
        }

        public void Refresh()
        {
            CacheComponents();
            if (viewport == null || textRoot == null)
                return;

            LayoutRebuilder.ForceRebuildLayoutImmediate(textRoot);
            _origin = textRoot.anchoredPosition;
            Vector2 viewSize = viewport.rect.size;
            Vector2 preferred = GetPreferredSize();
            IsOverflow = mode == UIScrollTextMode.VerticalLoop ? preferred.y > viewSize.y : preferred.x > viewSize.x;
        }

        public void Play()
        {
            Stop(false);
            if (!IsOverflow || viewport == null || textRoot == null || !isActiveAndEnabled)
                return;

            IsPlaying = true;
            textRoot.anchoredPosition = _origin;
            _playCancellation = new CancellationTokenSource();
            PlayLoopAsync(_playCancellation).Forget();
        }

        public void Stop(bool resetPosition = true)
        {
            IsPlaying = false;
            _playCancellation?.Cancel();
            _playCancellation?.Dispose();
            _playCancellation = null;
            _tween?.Kill();
            _tween = null;
            if (resetPosition && textRoot != null)
                textRoot.anchoredPosition = _origin;
        }

        private async UniTask PlayLoopAsync(CancellationTokenSource cancellation)
        {
            CancellationToken token = cancellation.Token;
            try
            {
                if (!await DelayAsync(startDelay, token))
                    return;

                bool towardEnd = true;
                while (!token.IsCancellationRequested)
                {
                    Vector2 target = towardEnd ? BuildEndPosition() : _origin;
                    float distance = Vector2.Distance(textRoot.anchoredPosition, target);
                    float duration = distance / Mathf.Max(1f, speed);
                    if (duration > 0f)
                    {
                        _tween = UITween.Move(textRoot, target, duration, UIAnimationEase.Linear, useUnscaledTime);
                        UITweenHandle tween = _tween;
                        await tween.ToUniTask(token);
                        if (token.IsCancellationRequested || !tween.IsCompleted)
                            return;
                        if (_tween == tween)
                            _tween = null;
                    }
                    else
                    {
                        textRoot.anchoredPosition = target;
                    }

                    if (!await DelayAsync(endDelay, token))
                        return;

                    if (mode == UIScrollTextMode.Once)
                        break;

                    if (mode == UIScrollTextMode.PingPong)
                    {
                        towardEnd = !towardEnd;
                        continue;
                    }

                    textRoot.anchoredPosition = _origin;
                    if (!await DelayAsync(startDelay, token))
                        return;
                }
            }
            finally
            {
                if (cancellation == _playCancellation)
                {
                    IsPlaying = false;
                    _playCancellation.Dispose();
                    _playCancellation = null;
                    _tween = null;
                }
            }
        }

        private async UniTask<bool> DelayAsync(float seconds, CancellationToken token)
        {
            if (seconds <= 0f)
                return !token.IsCancellationRequested;

            bool canceled = await UniTask.Delay(TimeSpan.FromSeconds(seconds), ignoreTimeScale: useUnscaledTime, cancellationToken: token).SuppressCancellationThrow();
            return !canceled;
        }

        private Vector2 BuildEndPosition()
        {
            Vector2 viewSize = viewport.rect.size;
            Vector2 preferred = GetPreferredSize();
            Vector2 to = _origin;
            if (mode == UIScrollTextMode.VerticalLoop)
                to.y += preferred.y - viewSize.y + spacing;
            else
                to.x -= preferred.x - viewSize.x + spacing;
            return to;
        }

        private Vector2 GetPreferredSize()
        {
            if (tmpText != null)
                return tmpText.GetPreferredValues(tmpText.text);
            if (uguiText != null)
                return new Vector2(uguiText.preferredWidth, uguiText.preferredHeight);
            return textRoot != null ? textRoot.rect.size : Vector2.zero;
        }

        private void CacheComponents()
        {
            if (viewport == null)
                viewport = transform as RectTransform;
            if (textRoot == null && transform.childCount > 0)
                textRoot = transform.GetChild(0) as RectTransform;
            if (tmpText == null)
                tmpText = GetComponentInChildren<TMP_Text>(true);
            if (uguiText == null)
                uguiText = GetComponentInChildren<Text>(true);
        }
    }
}
