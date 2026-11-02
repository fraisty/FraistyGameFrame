using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.EventSystems;
using UnityEngine.UI;

namespace FRFrame
{
    public enum UISnapScrollDirection
    {
        Horizontal = 0,
        Vertical = 1
    }

    public enum UISnapScrollMode
    {
        Nearest = 0,
        NextOnThreshold = 1,
        Velocity = 2
    }

    public enum UIPageIndexMode
    {
        Clamp = 0,
        Wrap = 1
    }

    public enum UIPageAlignment
    {
        Start = 0,
        Center = 1,
        End = 2
    }

    [Serializable]
    public sealed class UISnapScrollPageEvent : UnityEvent<int>
    {
    }

    public sealed class UISnapScrollPage : MonoBehaviour, IBeginDragHandler, IDragHandler, IEndDragHandler
    {
        public ScrollRect scrollRect;
        public RectTransform content;
        public UISnapScrollDirection direction = UISnapScrollDirection.Horizontal;
        public UISnapScrollMode snapMode = UISnapScrollMode.Nearest;
        public UIPageIndexMode indexMode = UIPageIndexMode.Clamp;
        public UIPageAlignment alignment = UIPageAlignment.Center;
        public float snapDuration = 0.25f;
        public float swipeThreshold = 0.2f;
        public float velocityThreshold = 300f;
        public bool enableCenterScale;
        public float centerScale = 1.1f;
        public float sideScale = 0.9f;
        public UISnapScrollPageEvent onPageChanged = new UISnapScrollPageEvent();
        public UISnapScrollPageEvent onPageSettled = new UISnapScrollPageEvent();
        public UnityEvent onDragStarted = new UnityEvent();
        public UnityEvent onDragEnded = new UnityEvent();

        private readonly List<RectTransform> _pages = new List<RectTransform>();
        private readonly List<float> _targets = new List<float>();
        private int _selectedPage;
        private int _settledPage;
        private Vector2 _dragStartPosition;
        private UITweenHandle _moveTween;
        private int _moveVersion;

        public int SelectedPage => _selectedPage;
        public int SettledPage => _settledPage;
        public int CurrentPage => SelectedPage;
        public int TargetPage => _selectedPage;
        public int PageCount => _pages.Count;
        public bool IsSettling { get; private set; }

        [Obsolete("Use indexMode instead.")]
        public bool enableLoop
        {
            get => indexMode == UIPageIndexMode.Wrap;
            set => indexMode = value ? UIPageIndexMode.Wrap : UIPageIndexMode.Clamp;
        }

        private RectTransform Viewport
        {
            get
            {
                if (scrollRect != null && scrollRect.viewport != null)
                    return scrollRect.viewport;
                return transform as RectTransform;
            }
        }

        private void Awake()
        {
            CacheComponents();
            RefreshPages(false);
        }

        private void OnDisable()
        {
            StopMovement();
        }

        private void OnDestroy()
        {
            StopMovement();
        }

        private void Update()
        {
            if (IsSettling && (_moveTween == null || !_moveTween.IsActive))
            {
                if (_moveTween != null && _moveTween.IsCompleted)
                    CompleteSettlement(_moveVersion);
                else
                    CancelSettlement();
            }

            if (enableCenterScale)
                UpdateCenterScale();
        }

        public void RefreshPages()
        {
            RefreshPages(true);
        }

        public void RefreshPages(bool preserveSelection = true)
        {
            RectTransform selectedTransform = preserveSelection && _selectedPage >= 0 && _selectedPage < _pages.Count
                ? _pages[_selectedPage]
                : null;
            StopMovement();
            CacheComponents();
            _pages.Clear();
            _targets.Clear();

            if (content == null || Viewport == null)
            {
                _selectedPage = 0;
                _settledPage = 0;
                return;
            }

            Canvas.ForceUpdateCanvases();
            LayoutRebuilder.ForceRebuildLayoutImmediate(content);
            Canvas.ForceUpdateCanvases();

            for (int i = 0; i < content.childCount; i++)
            {
                RectTransform page = content.GetChild(i) as RectTransform;
                if (page != null && page.gameObject.activeSelf)
                    _pages.Add(page);
            }

            if (_pages.Count == 0)
            {
                _selectedPage = 0;
                _settledPage = 0;
                return;
            }

            int selected = preserveSelection ? _pages.IndexOf(selectedTransform) : 0;
            if (selected < 0)
                selected = preserveSelection ? Mathf.Clamp(_selectedPage, 0, _pages.Count - 1) : 0;

            CalculateTargets();
            _selectedPage = selected;
            _settledPage = selected;
            content.anchoredPosition = GetPageContentPosition(selected);
            if (scrollRect != null)
                scrollRect.StopMovement();
        }

        public void MoveToPage(int index, bool animated = true)
        {
            if (content == null || PageCount == 0)
                return;

            index = ResolveIndex(index);
            StopMovement();
            _selectedPage = index;
            Vector2 target = GetPageContentPosition(index);
            onPageChanged.Invoke(index);

            if (!animated || snapDuration <= 0f || (target - content.anchoredPosition).sqrMagnitude < 0.0001f)
            {
                content.anchoredPosition = target;
                _settledPage = index;
                onPageSettled.Invoke(index);
                return;
            }

            int version = ++_moveVersion;
            _moveTween = UITween.Move(content, target, snapDuration, UIAnimationEase.OutQuad);
            IsSettling = true;
            _moveVersion = version;
        }

        public void StopMovement()
        {
            CancelSettlement();
            if (scrollRect != null)
                scrollRect.StopMovement();
        }

        public void Next(bool animated = true)
        {
            MoveToPage(SelectedPage + 1, animated);
        }

        public void Previous(bool animated = true)
        {
            MoveToPage(SelectedPage - 1, animated);
        }

        public void OnBeginDrag(PointerEventData eventData)
        {
            StopMovement();
            _dragStartPosition = content != null ? content.anchoredPosition : Vector2.zero;
            onDragStarted.Invoke();
        }

        public void OnDrag(PointerEventData eventData)
        {
        }

        public void OnEndDrag(PointerEventData eventData)
        {
            onDragEnded.Invoke();
            if (content != null && PageCount > 0)
                MoveToPage(FindTargetPage(), true);
        }

        private void CompleteSettlement(int version)
        {
            if (!IsSettling || version != _moveVersion)
                return;

            IsSettling = false;
            _moveTween = null;
            if (content != null && _selectedPage < _targets.Count)
                content.anchoredPosition = GetPageContentPosition(_selectedPage);
            _settledPage = _selectedPage;
            onPageSettled.Invoke(_settledPage);
        }

        private void CancelSettlement()
        {
            ++_moveVersion;
            IsSettling = false;
            _moveTween?.Kill();
            _moveTween = null;
        }

        private void CacheComponents()
        {
            if (scrollRect == null)
                scrollRect = GetComponent<ScrollRect>();
            if (scrollRect != null && content == null)
                content = scrollRect.content;
        }

        private void CalculateTargets()
        {
            RectTransform viewport = Viewport;
            Bounds contentBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, content);
            Rect viewportRect = viewport.rect;
            float current = Axis(content.anchoredPosition);
            float minShift;
            float maxShift;

            if (direction == UISnapScrollDirection.Horizontal)
            {
                minShift = viewportRect.xMax - contentBounds.max.x;
                maxShift = viewportRect.xMin - contentBounds.min.x;
            }
            else
            {
                minShift = viewportRect.yMax - contentBounds.max.y;
                maxShift = viewportRect.yMin - contentBounds.min.y;
            }

            bool contentFits = minShift > maxShift;
            float fitShift = direction == UISnapScrollDirection.Horizontal
                ? viewportRect.center.x - contentBounds.center.x
                : viewportRect.center.y - contentBounds.center.y;

            for (int i = 0; i < _pages.Count; i++)
            {
                Bounds pageBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, _pages[i]);
                float pagePoint = GetAlignedPoint(pageBounds);
                float viewportPoint = GetAlignedPoint(viewportRect);
                float shift = viewportPoint - pagePoint;
                if (contentFits)
                    shift = fitShift;
                else
                    shift = Mathf.Clamp(shift, minShift, maxShift);
                _targets.Add(current + shift);
            }
        }

        private int FindTargetPage()
        {
            float position = Axis(content.anchoredPosition);
            int nearest = FindNearest(position);
            if (snapMode == UISnapScrollMode.Nearest)
                return nearest;

            float dragDelta = position - Axis(_dragStartPosition);
            int directionalPage = FindAdjacentPage(_selectedPage, dragDelta);
            if (snapMode == UISnapScrollMode.NextOnThreshold)
            {
                float distance = Mathf.Abs(_targets[directionalPage] - _targets[_selectedPage]);
                if (directionalPage != _selectedPage && Mathf.Abs(dragDelta) >= distance * Mathf.Clamp01(swipeThreshold))
                    return directionalPage;
                return _selectedPage;
            }

            float velocity = 0f;
            if (scrollRect != null)
                velocity = direction == UISnapScrollDirection.Horizontal ? scrollRect.velocity.x : scrollRect.velocity.y;
            if (Mathf.Abs(velocity) >= velocityThreshold)
                return FindAdjacentPage(nearest, velocity);
            return nearest;
        }

        private int FindNearest(float position)
        {
            int nearest = 0;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < _targets.Count; i++)
            {
                float distance = Mathf.Abs(position - _targets[i]);
                if (distance < bestDistance)
                {
                    nearest = i;
                    bestDistance = distance;
                }
            }
            return nearest;
        }

        private int FindAdjacentPage(int from, float movement)
        {
            if (Mathf.Approximately(movement, 0f))
                return from;

            float currentTarget = _targets[from];
            int result = from;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < _targets.Count; i++)
            {
                float delta = _targets[i] - currentTarget;
                if (delta * movement <= 0f)
                    continue;
                float distance = Mathf.Abs(delta);
                if (distance < bestDistance)
                {
                    result = i;
                    bestDistance = distance;
                }
            }

            if (result == from && indexMode == UIPageIndexMode.Wrap)
                result = movement > 0f ? PageCount - 1 : 0;
            return result;
        }

        private int ResolveIndex(int index)
        {
            if (indexMode == UIPageIndexMode.Wrap)
                return (index % PageCount + PageCount) % PageCount;
            return Mathf.Clamp(index, 0, PageCount - 1);
        }

        private Vector2 GetPageContentPosition(int index)
        {
            Vector2 position = content.anchoredPosition;
            if (direction == UISnapScrollDirection.Horizontal)
                position.x = _targets[index];
            else
                position.y = _targets[index];
            return position;
        }

        private float Axis(Vector2 value)
        {
            return direction == UISnapScrollDirection.Horizontal ? value.x : value.y;
        }

        private float GetAlignedPoint(Bounds bounds)
        {
            if (direction == UISnapScrollDirection.Horizontal)
                return alignment == UIPageAlignment.Start ? bounds.min.x : alignment == UIPageAlignment.End ? bounds.max.x : bounds.center.x;
            return alignment == UIPageAlignment.Start ? bounds.max.y : alignment == UIPageAlignment.End ? bounds.min.y : bounds.center.y;
        }

        private float GetAlignedPoint(Rect rect)
        {
            if (direction == UISnapScrollDirection.Horizontal)
                return alignment == UIPageAlignment.Start ? rect.xMin : alignment == UIPageAlignment.End ? rect.xMax : rect.center.x;
            return alignment == UIPageAlignment.Start ? rect.yMax : alignment == UIPageAlignment.End ? rect.yMin : rect.center.y;
        }

        private void UpdateCenterScale()
        {
            RectTransform viewport = Viewport;
            if (viewport == null || PageCount == 0)
                return;

            float viewportCenter = direction == UISnapScrollDirection.Horizontal ? viewport.rect.center.x : viewport.rect.center.y;
            float range = Mathf.Max(1f, direction == UISnapScrollDirection.Horizontal ? viewport.rect.width : viewport.rect.height);
            for (int i = 0; i < _pages.Count; i++)
            {
                Bounds bounds = RectTransformUtility.CalculateRelativeRectTransformBounds(viewport, _pages[i]);
                float center = direction == UISnapScrollDirection.Horizontal ? bounds.center.x : bounds.center.y;
                float distance = Mathf.Abs(center - viewportCenter) / range;
                float scale = Mathf.Lerp(centerScale, sideScale, Mathf.Clamp01(distance));
                _pages[i].localScale = Vector3.one * scale;
            }
        }
    }
}
