using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem.EnhancedTouch;
using UnityEngine.UI;

namespace FRFrame
{
    [Serializable]
    public sealed class UIPinchZoomScaleEvent : UnityEvent<float>
    {
    }

    public sealed class UIPinchZoom : MonoBehaviour
    {
        public RectTransform target;
        public RectTransform viewport;
        public float minScale = 0.5f;
        public float maxScale = 3f;
        public float zoomSpeed = 1f;
        public float panSpeed = 1f;
        public bool clampToViewport = true;
        public bool disableParentScrollRectWhilePinching = true;
        public float reboundDuration = 0.2f;
        public UnityEvent onZoomStarted = new UnityEvent();
        public UIPinchZoomScaleEvent onZoomChanged = new UIPinchZoomScaleEvent();
        public UnityEvent onZoomEnded = new UnityEvent();

        private ScrollRect _parentScrollRect;
        private bool _pinching;
        private bool _enhancedTouchEnabled;
        private bool _parentScrollWasEnabled;
        private bool _parentScrollTemporarilyDisabled;
        private int _firstTouchId = -1;
        private int _secondTouchId = -1;
        private float _lastDistance;
        private Vector2 _lastCenter;
        private Vector3 _initialScale;
        private UITweenHandle _scaleReboundTween;
        private UITweenHandle _moveReboundTween;

        public bool IsPinching => _pinching;
        public float CurrentScale => GetCurrentScale();

        private void Awake()
        {
            if (target == null)
                target = transform as RectTransform;
            if (viewport == null)
                viewport = transform.parent as RectTransform;
            _initialScale = target != null ? target.localScale : Vector3.one;
            _parentScrollRect = GetComponentInParent<ScrollRect>();
        }

        private void OnEnable()
        {
            EnhancedTouchSupport.Enable();
            _enhancedTouchEnabled = true;
        }

        private void OnDisable()
        {
            CancelPinch();
            KillRebounds();
            if (_enhancedTouchEnabled)
            {
                EnhancedTouchSupport.Disable();
                _enhancedTouchEnabled = false;
            }
        }

        private void OnDestroy()
        {
            CancelPinch();
            KillRebounds();
            if (_enhancedTouchEnabled)
            {
                EnhancedTouchSupport.Disable();
                _enhancedTouchEnabled = false;
            }
        }

        private void Update()
        {
            if (target == null)
                return;

            var touches = UnityEngine.InputSystem.EnhancedTouch.Touch.activeTouches;
            if (!_pinching)
            {
                if (touches.Count < 2)
                    return;

                var first = touches[0];
                var second = touches[1];
                if (!IsInsideViewport(first.screenPosition) || !IsInsideViewport(second.screenPosition))
                    return;

                BeginPinch(first.touchId, second.touchId, first.screenPosition, second.screenPosition);
                return;
            }

            bool foundFirst = false;
            bool foundSecond = false;
            Vector2 p0 = Vector2.zero;
            Vector2 p1 = Vector2.zero;
            for (int i = 0; i < touches.Count; i++)
            {
                var touch = touches[i];
                if (touch.touchId == _firstTouchId)
                {
                    foundFirst = true;
                    p0 = touch.screenPosition;
                }
                else if (touch.touchId == _secondTouchId)
                {
                    foundSecond = true;
                    p1 = touch.screenPosition;
                }
            }

            if (!foundFirst || !foundSecond)
            {
                EndPinch(true);
                return;
            }

            float distance = Vector2.Distance(p0, p1);
            Vector2 center = (p0 + p1) * 0.5f;
            float delta = distance - _lastDistance;
            float scale = Mathf.Clamp(CurrentScale + delta * 0.005f * zoomSpeed, minScale, maxScale);
            target.localScale = _initialScale * scale;

            Vector2 centerDelta = (center - _lastCenter) * panSpeed;
            target.anchoredPosition += centerDelta;
            if (clampToViewport)
                ClampToViewport(false);

            _lastDistance = distance;
            _lastCenter = center;
            onZoomChanged.Invoke(scale);
        }

        public void ResetZoom(bool animated = true)
        {
            if (target == null)
                return;

            KillRebounds();
            if (animated && reboundDuration > 0f)
            {
                _scaleReboundTween = UITween.Scale(target, _initialScale, reboundDuration, UIAnimationEase.OutQuad);
                _moveReboundTween = UITween.Move(target, Vector2.zero, reboundDuration, UIAnimationEase.OutQuad);
            }
            else
            {
                target.localScale = _initialScale;
                target.anchoredPosition = Vector2.zero;
            }
        }

        public void CancelPinch()
        {
            EndPinch(false);
        }

        private void BeginPinch(int firstTouchId, int secondTouchId, Vector2 firstPosition, Vector2 secondPosition)
        {
            _pinching = true;
            _firstTouchId = firstTouchId;
            _secondTouchId = secondTouchId;
            _lastDistance = Vector2.Distance(firstPosition, secondPosition);
            _lastCenter = (firstPosition + secondPosition) * 0.5f;
            KillRebounds();
            DisableParentScroll();
            onZoomStarted.Invoke();
        }

        private void EndPinch(bool rebound)
        {
            if (!_pinching)
                return;

            _pinching = false;
            _firstTouchId = -1;
            _secondTouchId = -1;
            RestoreParentScroll();
            if (rebound && clampToViewport)
                ClampToViewport(true);
            onZoomEnded.Invoke();
        }

        private bool IsInsideViewport(Vector2 screenPosition)
        {
            return viewport != null && RectTransformUtility.RectangleContainsScreenPoint(viewport, screenPosition);
        }

        private float GetCurrentScale()
        {
            if (target == null)
                return 1f;
            if (!Mathf.Approximately(_initialScale.x, 0f))
                return target.localScale.x / _initialScale.x;
            return 1f;
        }

        private void ClampToViewport(bool animated)
        {
            if (viewport == null || target == null)
                return;

            Vector2 viewportSize = viewport.rect.size;
            Vector2 targetSize = Vector2.Scale(target.rect.size, new Vector2(Mathf.Abs(target.localScale.x), Mathf.Abs(target.localScale.y)));
            Vector2 maxOffset = Vector2.Max(Vector2.zero, (targetSize - viewportSize) * 0.5f);
            Vector2 clamped = target.anchoredPosition;
            clamped.x = Mathf.Clamp(clamped.x, -maxOffset.x, maxOffset.x);
            clamped.y = Mathf.Clamp(clamped.y, -maxOffset.y, maxOffset.y);

            _moveReboundTween?.Kill();
            _moveReboundTween = null;
            if (animated && reboundDuration > 0f)
                _moveReboundTween = UITween.Move(target, clamped, reboundDuration, UIAnimationEase.OutQuad);
            else
                target.anchoredPosition = clamped;
        }

        private void KillRebounds()
        {
            _scaleReboundTween?.Kill();
            _moveReboundTween?.Kill();
            _scaleReboundTween = null;
            _moveReboundTween = null;
        }

        private void DisableParentScroll()
        {
            if (!disableParentScrollRectWhilePinching || _parentScrollRect == null)
                return;

            _parentScrollWasEnabled = _parentScrollRect.enabled;
            _parentScrollRect.enabled = false;
            _parentScrollTemporarilyDisabled = true;
        }

        private void RestoreParentScroll()
        {
            if (!_parentScrollTemporarilyDisabled)
                return;

            if (_parentScrollRect != null)
                _parentScrollRect.enabled = _parentScrollWasEnabled;
            _parentScrollTemporarilyDisabled = false;
        }
    }
}
