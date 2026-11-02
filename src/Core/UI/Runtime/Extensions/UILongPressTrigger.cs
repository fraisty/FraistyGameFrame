using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace FRFrame
{
    [Serializable]
    public sealed class UILongPressProgressEvent : UnityEvent<float>
    {
    }

    public sealed class UILongPressTrigger : MonoBehaviour, IPointerDownHandler, IPointerUpHandler, IPointerExitHandler, IDragHandler
    {
        public float longPressThreshold = 0.8f;
        public float moveTolerance = 12f;
        public bool useUnscaledTime = true;
        public bool cancelOnExit = true;
        public bool cancelOnMove = true;
        public bool triggerClickAfterLongPress;
        public bool repeatLongPress;
        public float repeatInterval = 0.2f;

        public UnityEvent onPressed = new UnityEvent();
        public UnityEvent onReleased = new UnityEvent();
        public UnityEvent onClicked = new UnityEvent();
        public UnityEvent onLongPressed = new UnityEvent();
        public UnityEvent onLongPressRepeated = new UnityEvent();
        public UnityEvent onCanceled = new UnityEvent();
        public UILongPressProgressEvent onProgressChanged = new UILongPressProgressEvent();

        private bool _isPressed;
        private bool _isLongPressed;
        private float _pressTime;
        private float _nextRepeatTime;
        private Vector2 _pressPosition;

        public bool IsPressed => _isPressed;
        public bool IsLongPressed => _isLongPressed;
        public float Progress { get; private set; }

        public void OnPointerDown(PointerEventData eventData)
        {
            if (_isPressed)
                Cancel();

            _isPressed = true;
            _isLongPressed = false;
            Progress = 0f;
            _pressTime = Now;
            _nextRepeatTime = _pressTime + Mathf.Max(0.01f, longPressThreshold);
            _pressPosition = eventData.position;
            onPressed.Invoke();
            onProgressChanged.Invoke(0f);
        }

        public void OnPointerUp(PointerEventData eventData)
        {
            if (!_isPressed)
                return;

            bool shouldClick = !_isLongPressed || triggerClickAfterLongPress;
            bool moved = Vector2.Distance(_pressPosition, eventData.position) > moveTolerance;
            onReleased.Invoke();
            if (shouldClick && !moved)
                onClicked.Invoke();

            ResetState();
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            if (!_isPressed || !cancelOnExit)
                return;

            Cancel();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (_isPressed && cancelOnMove && Vector2.Distance(_pressPosition, eventData.position) > moveTolerance)
                Cancel();
        }

        private void OnDisable()
        {
            if (_isPressed)
                Cancel();
            else
                ResetState();
        }

        private void Update()
        {
            if (!_isPressed)
                return;

            float elapsed = Now - _pressTime;
            float threshold = Mathf.Max(0.01f, longPressThreshold);
            Progress = Mathf.Clamp01(elapsed / threshold);
            onProgressChanged.Invoke(Progress);

            if (!_isLongPressed && elapsed >= threshold)
            {
                _isLongPressed = true;
                _nextRepeatTime = Now + Mathf.Max(0.01f, repeatInterval);
                onLongPressed.Invoke();
            }

            if (_isLongPressed && repeatLongPress && Now >= _nextRepeatTime)
            {
                _nextRepeatTime = Now + Mathf.Max(0.01f, repeatInterval);
                onLongPressRepeated.Invoke();
            }
        }

        public void Cancel()
        {
            if (!_isPressed)
                return;

            ResetState();
            onCanceled.Invoke();
        }

        public void ResetState()
        {
            _isPressed = false;
            _isLongPressed = false;
            Progress = 0f;
            _pressPosition = Vector2.zero;
            onProgressChanged.Invoke(0f);
        }

        private float Now => useUnscaledTime ? Time.unscaledTime : Time.time;
    }
}
