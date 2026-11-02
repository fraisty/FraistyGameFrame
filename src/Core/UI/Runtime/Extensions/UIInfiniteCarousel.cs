using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;

namespace FRFrame
{
    public sealed class UIInfiniteCarouselItemContext
    {
        public GameObject Slot { get; internal set; }
        public RectTransform RectTransform { get; internal set; }
        public object Data { get; internal set; }
        public int DataIndex { get; internal set; }
        public int SlotIndex { get; internal set; }
        public bool IsCenter { get; internal set; }
    }

    [Serializable]
    public sealed class UIInfiniteCarouselIndexEvent : UnityEvent<int>
    {
    }

    [Serializable]
    public sealed class UIInfiniteCarouselItemEvent : UnityEvent<UIInfiniteCarouselItemContext>
    {
    }

    public sealed class UIInfiniteCarousel : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public RectTransform viewport;
        public RectTransform content;
        public GameObject itemPrefab;
        public int slotCount = 5;
        public float settleDuration = 0.25f;
        public float dragThreshold = 0.2f;
        public UIInfiniteCarouselIndexEvent onIndexChanged = new UIInfiniteCarouselIndexEvent();
        public UIInfiniteCarouselIndexEvent onSettled = new UIInfiniteCarouselIndexEvent();
        public UIInfiniteCarouselItemEvent onItemClicked = new UIInfiniteCarouselItemEvent();
        public UnityEvent onDragStarted = new UnityEvent();
        public UnityEvent onDragEnded = new UnityEvent();

        private readonly List<object> _data = new List<object>();
        private readonly List<SlotState> _slots = new List<SlotState>();
        private Action<UIInfiniteCarouselItemContext> _bind;
        private int _currentIndex;
        private float _offset;
        private float _step;
        private float _autoPlayElapsed;
        private bool _autoPlay;
        private float _interval = 3f;
        private bool _autoPlayPaused;
        private bool _dragging;
        private Vector2 _lastDragPoint;
        private UITweenHandle _tween;
        private int _tweenVersion;

        public int CurrentIndex => Count == 0 ? 0 : _currentIndex;
        public int Count => _data.Count;
        public bool IsSettling { get; private set; }
        public bool AutoPlay => _autoPlay;
        public float Interval => _interval;

        private sealed class SlotState
        {
            public GameObject GameObject;
            public RectTransform Rect;
            public UIInfiniteCarouselItemContext Context;
            public UIInfiniteCarouselSlotClick Click;
            public int Position;
            public int DataIndex;
        }

        private void OnDisable()
        {
            CancelTween();
            _dragging = false;
            _offset = 0f;
            LayoutSlots();
        }

        private void OnDestroy()
        {
            CancelTween();
        }

        private void Update()
        {
            if (IsSettling && (_tween == null || !_tween.IsActive))
            {
                if (_tween != null && _tween.IsCompleted)
                    CompleteSettlement(_tweenVersion);
                else
                    CancelTween();
            }

            if (!_autoPlay || _autoPlayPaused || _dragging || IsSettling || Count <= 1)
                return;

            _autoPlayElapsed += Time.unscaledDeltaTime;
            if (_autoPlayElapsed >= _interval)
            {
                _autoPlayElapsed = 0f;
                Next();
            }
        }

        public void Configure(RectTransform newViewport, RectTransform newContent, GameObject newItemPrefab, int newSlotCount = 5)
        {
            viewport = newViewport;
            content = newContent;
            itemPrefab = newItemPrefab;
            slotCount = Mathf.Max(3, newSlotCount);
            if ((slotCount & 1) == 0)
                slotCount++;
            RebuildSlots();
            Refresh();
        }

        public void SetData<T>(IList<T> data, Action<UIInfiniteCarouselItemContext> bind)
        {
            CancelTween();
            _data.Clear();
            if (data != null)
            {
                for (int i = 0; i < data.Count; i++)
                    _data.Add(data[i]);
            }
            _bind = bind;
            _currentIndex = Count == 0 ? 0 : Mathf.Clamp(_currentIndex, 0, Count - 1);
            _offset = 0f;
            _autoPlayElapsed = 0f;
            EnsureSlots();
            Refresh();
        }

        public void Next(bool animated = true)
        {
            MoveBy(1, animated);
        }

        public void Previous(bool animated = true)
        {
            MoveBy(-1, animated);
        }

        public void MoveToIndex(int index, bool animated = true)
        {
            if (Count == 0)
                return;

            index = Wrap(index, Count);
            int forward = Wrap(index - _currentIndex, Count);
            int backward = forward - Count;
            int delta = forward <= -backward ? forward : backward;
            MoveBy(delta, animated);
        }

        public void SetAutoPlay(bool enabled, float interval)
        {
            _autoPlay = enabled;
            _interval = Mathf.Max(0.05f, interval);
            _autoPlayElapsed = 0f;
            _autoPlayPaused = false;
        }

        public void PauseAutoPlay()
        {
            _autoPlayPaused = true;
        }

        public void ResumeAutoPlay()
        {
            _autoPlayPaused = false;
            _autoPlayElapsed = 0f;
        }

        public void Refresh()
        {
            CancelTween();
            if (viewport == null || content == null)
                return;

            Canvas.ForceUpdateCanvases();
            _step = Mathf.Max(1f, viewport.rect.width);
            content.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, _step * Mathf.Max(1, _slots.Count));
            for (int i = 0; i < _slots.Count; i++)
                ConfigureSlotRect(_slots[i].Rect, i);
            _offset = 0f;
            AssignPositions();
            BindSlots();
            LayoutSlots();
        }

        public void Clear()
        {
            CancelTween();
            _data.Clear();
            _bind = null;
            _currentIndex = 0;
            _offset = 0f;
            _autoPlayElapsed = 0f;
            for (int i = 0; i < _slots.Count; i++)
                _slots[i].GameObject.SetActive(false);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            if (Count <= 1 || viewport == null)
                return;

            CancelTween();
            _dragging = true;
            _autoPlayElapsed = 0f;
            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, eventData.position, eventData.pressEventCamera, out _lastDragPoint);
            onDragStarted.Invoke();
        }

        public void OnDrag(PointerEventData eventData)
        {
            if (!_dragging)
                return;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(viewport, eventData.position, eventData.pressEventCamera, out Vector2 point);
            _offset += point.x - _lastDragPoint.x;
            _lastDragPoint = point;
            NormalizeOffset(true);
            LayoutSlots();
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            if (!_dragging)
                return;

            _dragging = false;
            onDragEnded.Invoke();
            float target = Mathf.Abs(_offset) >= _step * Mathf.Clamp01(dragThreshold)
                ? (_offset < 0f ? -_step : _step)
                : 0f;
            StartOffsetTween(target);
        }

        internal void HandleSlotClick(UIInfiniteCarouselSlotClick click)
        {
            if (click == null || _dragging || Count == 0 || click.SlotIndex < 0 || click.SlotIndex >= _slots.Count)
                return;

            onItemClicked.Invoke(_slots[click.SlotIndex].Context);
        }

        private void MoveBy(int delta, bool animated)
        {
            if (Count <= 1 || delta == 0)
            {
                if (Count == 1 && !animated)
                    onSettled.Invoke(0);
                return;
            }

            CancelTween();
            float target = _offset - delta * _step;
            if (!animated || settleDuration <= 0f)
            {
                _offset = target;
                NormalizeOffset(true);
                _offset = 0f;
                LayoutSlots();
                onSettled.Invoke(_currentIndex);
                ResetAutoPlayTimer();
                return;
            }
            StartOffsetTween(target);
        }

        private void StartOffsetTween(float target)
        {
            CancelTween();
            if (Mathf.Approximately(_offset, target) && Mathf.Approximately(target, 0f))
            {
                onSettled.Invoke(CurrentIndex);
                ResetAutoPlayTimer();
                return;
            }

            int version = ++_tweenVersion;
            float distance = target - _offset;
            float previous = 0f;
            IsSettling = true;
            _tween = UITween.Value(0f, distance, settleDuration, value =>
            {
                if (version != _tweenVersion)
                    return;
                _offset += value - previous;
                previous = value;
                NormalizeOffset(true);
                LayoutSlots();
            }, UIAnimationEase.OutQuad);
        }

        private void CompleteSettlement(int version)
        {
            if (!IsSettling || version != _tweenVersion)
                return;
            IsSettling = false;
            _tween = null;
            NormalizeOffset(true);
            _offset = 0f;
            LayoutSlots();
            onSettled.Invoke(CurrentIndex);
            ResetAutoPlayTimer();
        }

        private void CancelTween()
        {
            ++_tweenVersion;
            IsSettling = false;
            _tween?.Kill();
            _tween = null;
        }

        private void NormalizeOffset(bool notify)
        {
            if (Count <= 1 || _step <= 0f)
                return;

            while (_offset <= -_step)
            {
                _offset += _step;
                _currentIndex = Wrap(_currentIndex + 1, Count);
                RotateFirstToLast();
                BindSlots();
                if (notify)
                    onIndexChanged.Invoke(_currentIndex);
            }
            while (_offset >= _step)
            {
                _offset -= _step;
                _currentIndex = Wrap(_currentIndex - 1, Count);
                RotateLastToFirst();
                BindSlots();
                if (notify)
                    onIndexChanged.Invoke(_currentIndex);
            }
        }

        private void RotateFirstToLast()
        {
            if (_slots.Count == 0)
                return;
            SlotState first = _slots[0];
            _slots.RemoveAt(0);
            _slots.Add(first);
            AssignPositions();
        }

        private void RotateLastToFirst()
        {
            if (_slots.Count == 0)
                return;
            int lastIndex = _slots.Count - 1;
            SlotState last = _slots[lastIndex];
            _slots.RemoveAt(lastIndex);
            _slots.Insert(0, last);
            AssignPositions();
        }

        private void EnsureSlots()
        {
            int desired = Mathf.Max(3, slotCount);
            if ((desired & 1) == 0)
                desired++;
            if (_slots.Count != desired)
                RebuildSlots();
        }

        private void RebuildSlots()
        {
            CancelTween();
            for (int i = 0; i < _slots.Count; i++)
            {
                if (_slots[i].GameObject != null)
                    Destroy(_slots[i].GameObject);
            }
            _slots.Clear();

            if (content == null || itemPrefab == null)
                return;

            int desired = Mathf.Max(3, slotCount);
            if ((desired & 1) == 0)
                desired++;
            slotCount = desired;
            for (int i = 0; i < desired; i++)
            {
                GameObject instance = Instantiate(itemPrefab, content, false);
                instance.name = itemPrefab.name + "_Slot" + i;
                RectTransform rect = instance.GetComponent<RectTransform>();
                if (rect == null)
                    rect = instance.AddComponent<RectTransform>();
                UIInfiniteCarouselSlotClick click = instance.GetComponent<UIInfiniteCarouselSlotClick>();
                if (click == null)
                    click = instance.AddComponent<UIInfiniteCarouselSlotClick>();
                click.Owner = this;
                click.SlotIndex = i;
                var context = new UIInfiniteCarouselItemContext
                {
                    Slot = instance,
                    RectTransform = rect,
                    SlotIndex = i
                };
                _slots.Add(new SlotState
                {
                    GameObject = instance,
                    Rect = rect,
                    Context = context,
                    Click = click
                });
                ConfigureSlotRect(rect, i);
            }
            AssignPositions();
        }

        private void AssignPositions()
        {
            int half = _slots.Count / 2;
            for (int i = 0; i < _slots.Count; i++)
            {
                SlotState slot = _slots[i];
                slot.Position = i - half;
                slot.Context.SlotIndex = i;
                slot.Click.SlotIndex = i;
                slot.Rect.SetSiblingIndex(i);
            }
        }

        private void BindSlots()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                SlotState slot = _slots[i];
                bool active = Count > 0;
                slot.GameObject.SetActive(active);
                if (!active)
                    continue;
                slot.DataIndex = Wrap(_currentIndex + slot.Position, Count);
                UIInfiniteCarouselItemContext context = slot.Context;
                context.Data = _data[slot.DataIndex];
                context.DataIndex = slot.DataIndex;
                context.IsCenter = slot.Position == 0;
                _bind?.Invoke(context);
            }
        }

        private void ConfigureSlotRect(RectTransform rect, int siblingIndex)
        {
            rect.anchorMin = new Vector2(0.5f, rect.anchorMin.y);
            rect.anchorMax = new Vector2(0.5f, rect.anchorMax.y);
            rect.SetSizeWithCurrentAnchors(RectTransform.Axis.Horizontal, _step);
            rect.SetSiblingIndex(siblingIndex);
        }

        private void LayoutSlots()
        {
            for (int i = 0; i < _slots.Count; i++)
            {
                RectTransform rect = _slots[i].Rect;
                Vector2 position = rect.anchoredPosition;
                position.x = _slots[i].Position * _step + _offset;
                rect.anchoredPosition = position;
            }
        }

        private void ResetAutoPlayTimer()
        {
            _autoPlayElapsed = 0f;
        }

        private static int Wrap(int value, int count)
        {
            return (value % count + count) % count;
        }
    }

    public sealed class UIInfiniteCarouselSlotClick : MonoBehaviour, IPointerClickHandler
    {
        internal UIInfiniteCarousel Owner { get; set; }
        internal int SlotIndex { get; set; }

        public void OnPointerClick(PointerEventData eventData)
        {
            Owner?.HandleSlotClick(this);
        }
    }
}
