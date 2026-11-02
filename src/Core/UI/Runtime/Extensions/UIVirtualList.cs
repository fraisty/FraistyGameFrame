using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FRFrame
{
    public sealed class UIVirtualList : MonoBehaviour
    {
        [Header("Required")]
        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private RectTransform _viewport;
        [SerializeField] private RectTransform _content;
        [SerializeField] private GameObject _itemPrefab;

        [Header("Layout")]
        [SerializeField] private UIVirtualListLayoutMode _layoutMode = UIVirtualListLayoutMode.Vertical;
        [SerializeField] private Vector2 _itemSize = new Vector2(100f, 100f);
        [SerializeField] private Vector2 _spacing = Vector2.zero;
        [SerializeField] private UIVirtualListPadding _padding;
        [SerializeField] private int _constraintCount = 1;
        [SerializeField] private int _extraCount = 2;
        [SerializeField] private int _maxCacheCount = 64;
        [SerializeField] private UIVirtualListAnchorMode _anchorMode = UIVirtualListAnchorMode.PreserveFirstVisibleKey;

        [Header("Events")]
        public UIVirtualListItemEvent onItemVisible = new UIVirtualListItemEvent();
        public UIVirtualListItemEvent onItemRecycled = new UIVirtualListItemEvent();
        public UnityEngine.Events.UnityEvent<UIItem> onItemCreated = new UnityEngine.Events.UnityEvent<UIItem>();
        public UIVirtualListRangeChangedEvent onVisibleRangeChanged = new UIVirtualListRangeChangedEvent();

        private const string DefaultTemplateId = "default";
        private readonly List<object> _dataList = new List<object>();
        private readonly Dictionary<int, UIItem> _visibleItems = new Dictionary<int, UIItem>();
        private readonly Dictionary<string, UIVirtualListTemplate> _templates = new Dictionary<string, UIVirtualListTemplate>();
        private readonly Dictionary<string, Stack<UIItem>> _pools = new Dictionary<string, Stack<UIItem>>();
        private readonly Dictionary<long, int> _keyToIndex = new Dictionary<long, int>();
        private readonly List<int> _removeIndices = new List<int>();
        private readonly List<KeyValuePair<int, UIItem>> _remappedItems = new List<KeyValuePair<int, UIItem>>();
        private readonly HashSet<string> _missingTemplateErrors = new HashSet<string>();

        private UIForm _ownerForm;
        private Type _itemLogicType;
        private IUIVirtualListDataSource _dataSource;
        private int _currentStartIndex = -1;
        private int _currentEndIndex = -1;
        private UITweenHandle _scrollTween;
        private ScrollRect _boundScrollRect;
        private bool _destroyed;

        public UIVirtualListAnchorMode AnchorMode { get => _anchorMode; set => _anchorMode = value; }
        private int Count => _dataSource != null ? Mathf.Max(0, _dataSource.Count) : _dataList.Count;

        public void Initialize(UIForm ownerForm, Type itemLogicType)
        {
            UnbindScrollRect();
            if (_itemLogicType != itemLogicType)
            {
                RecycleAllVisible();
                DestroyCache();
            }
            _ownerForm = ownerForm;
            _itemLogicType = itemLogicType;
            RebuildDefaultTemplate();
            CacheComponents();
            if (isActiveAndEnabled) { BindScrollRect(); ForceRefresh(); }
        }

        public void Configure(ScrollRect scrollRect, RectTransform viewport, RectTransform content, GameObject itemPrefab, UIVirtualListLayoutMode layoutMode, Vector2 itemSize, Vector2 spacing, UIVirtualListPadding padding, int constraintCount = 1, int extraCount = 2, int maxCacheCount = 64)
        {
            UnbindScrollRect();
            RecycleAllVisible();
            bool templateChanged = _itemPrefab != itemPrefab;
            _scrollRect = scrollRect;
            _viewport = viewport;
            _content = content;
            _itemPrefab = itemPrefab;
            _layoutMode = layoutMode;
            _itemSize = itemSize;
            _spacing = spacing;
            _padding = padding;
            _constraintCount = Mathf.Max(1, constraintCount);
            _extraCount = Mathf.Max(0, extraCount);
            _maxCacheCount = Mathf.Max(0, maxCacheCount);
            if (templateChanged) DestroyCache();
            RebuildDefaultTemplate();
            TrimCaches();
            RefreshContentSize();
            if (isActiveAndEnabled) { BindScrollRect(); UpdateVisibleItems(true); }
        }

        public void ConfigureTemplates(IEnumerable<UIVirtualListTemplate> templates)
        {
            RecycleAllVisible();
            DestroyCache();
            _templates.Clear();
            _missingTemplateErrors.Clear();
            if (templates != null)
            {
                foreach (UIVirtualListTemplate template in templates)
                {
                    if (template == null || string.IsNullOrEmpty(template.templateId) || template.prefab == null || template.itemLogicType == null)
                    {
                        FRLog.Error("UIVirtualList ignored an invalid template.");
                        continue;
                    }
                    if (!typeof(UIItem).IsAssignableFrom(template.itemLogicType))
                    {
                        FRLog.Error($"UIVirtualList template type is not UIItem: {template.itemLogicType.FullName}");
                        continue;
                    }
                    if (_templates.ContainsKey(template.templateId))
                    {
                        FRLog.Error($"UIVirtualList duplicate template id: {template.templateId}");
                        continue;
                    }
                    template.maxCacheCount = Mathf.Max(0, template.maxCacheCount);
                    _templates.Add(template.templateId, template);
                    _pools[template.templateId] = new Stack<UIItem>();
                }
            }
            if (_templates.Count == 0) RebuildDefaultTemplate();
            ForceRefresh();
        }

        public void SetDataSource(IUIVirtualListDataSource dataSource)
        {
            AnchorState anchor = CaptureAnchor(_anchorMode);
            _dataSource = dataSource;
            RebuildKeys();
            RefreshContentSize();
            RestoreAnchor(anchor, _anchorMode);
            ForceRefresh();
        }

        public void SetItemLogic(Type itemLogicType)
        {
            if (_itemLogicType == itemLogicType) return;
            RecycleAllVisible();
            DestroyCache();
            _itemLogicType = itemLogicType;
            RebuildDefaultTemplate();
            UpdateVisibleItems(true);
        }

        public void SetDataList<T>(IList<T> dataList)
        {
            AnchorState anchor = CaptureAnchor(_anchorMode);
            _dataSource = null;
            _dataList.Clear();
            if (dataList != null)
                for (int i = 0; i < dataList.Count; i++) _dataList.Add(dataList[i]);
            RebuildKeys();
            RefreshContentSize();
            RestoreAnchor(anchor, _anchorMode);
            ForceRefresh();
        }

        public void InsertRange(int index, int count)
        {
            if (count <= 0) return;
            index = Mathf.Clamp(index, 0, Count);
            NotifyStructureChanged(i => i >= index ? i + count : i);
        }

        public void RemoveRange(int index, int count)
        {
            if (count <= 0) return;
            index = Mathf.Max(0, index);
            int end = index + count;
            NotifyStructureChanged(i => i < index ? i : (i >= end ? i - count : -1));
        }

        public void ReplaceRange(int index, int count)
        {
            if (count <= 0) return;
            index = Mathf.Max(0, index);
            int end = index + count;
            NotifyStructureChanged(i => i >= index && i < end ? -1 : i);
        }

        public void Move(int fromIndex, int toIndex, int count = 1)
        {
            if (count <= 0 || fromIndex < 0 || toIndex < 0 || fromIndex == toIndex) return;
            NotifyStructureChanged(i => RemapMovedIndex(i, fromIndex, toIndex, count));
        }

        private void NotifyStructureChanged(Func<int, int> remap)
        {
            AnchorState anchor = CaptureAnchor(_anchorMode);
            RemapVisibleItems(remap);
            RebuildKeys();
            RefreshContentSize();
            RestoreAnchor(anchor, _anchorMode);
            UpdateVisibleItems(true);
        }

        private void RemapVisibleItems(Func<int, int> remap)
        {
            _remappedItems.Clear();
            foreach (var pair in _visibleItems)
            {
                int newIndex = remap(pair.Key);
                if (newIndex < 0 || newIndex >= Count)
                {
                    RecycleItem(pair.Key, pair.Value);
                    continue;
                }
                pair.Value.InternalSetIndex(newIndex);
                _remappedItems.Add(new KeyValuePair<int, UIItem>(newIndex, pair.Value));
            }
            _visibleItems.Clear();
            for (int i = 0; i < _remappedItems.Count; i++)
            {
                KeyValuePair<int, UIItem> pair = _remappedItems[i];
                if (_visibleItems.ContainsKey(pair.Key)) RecycleItem(pair.Key, pair.Value);
                else _visibleItems.Add(pair.Key, pair.Value);
            }
        }

        private static int RemapMovedIndex(int index, int fromIndex, int toIndex, int count)
        {
            int fromEnd = fromIndex + count;
            if (index >= fromIndex && index < fromEnd) return toIndex + index - fromIndex;
            if (fromIndex < toIndex && index >= fromEnd && index < toIndex + count) return index - count;
            if (toIndex < fromIndex && index >= toIndex && index < fromIndex) return index + count;
            return index;
        }

        public void Prewarm(int count)
        {
            UIVirtualListTemplate template = GetTemplate(DefaultTemplateId);
            if (template == null) return;
            Stack<UIItem> pool = GetPool(template.templateId);
            int target = Mathf.Min(Mathf.Max(0, count), template.maxCacheCount);
            while (pool.Count < target)
            {
                UIItem item = CreateItem(template);
                if (item == null) return;
                item.gameObject.SetActive(false);
                pool.Push(item);
            }
        }

        public void ScrollToIndex(int index, bool animated = true, float duration = 0.25f) => ScrollToIndex(index, UIVirtualListAlignment.Start, animated, duration);

        public void ScrollToIndex(int index, UIVirtualListAlignment alignment, bool animated = true, float duration = 0.25f)
        {
            if (_content == null || _viewport == null || Count == 0) return;
            index = Mathf.Clamp(index, 0, Count - 1);
            float itemStart = GetMainPosition(index);
            float itemLength = IsHorizontal ? _itemSize.x : _itemSize.y;
            float viewportLength = IsHorizontal ? _viewport.rect.width : _viewport.rect.height;
            float current = GetScrollOffset();
            float targetOffset = itemStart;
            if (alignment == UIVirtualListAlignment.Center) targetOffset -= (viewportLength - itemLength) * 0.5f;
            else if (alignment == UIVirtualListAlignment.End) targetOffset -= viewportLength - itemLength;
            else if (alignment == UIVirtualListAlignment.Nearest)
            {
                if (itemStart >= current && itemStart + itemLength <= current + viewportLength) targetOffset = current;
                else if (itemStart < current) targetOffset = itemStart;
                else targetOffset = itemStart + itemLength - viewportLength;
            }
            SetScrollOffset(ClampOffset(targetOffset), animated, duration);
        }

        public bool ScrollToKey(long key, UIVirtualListAlignment alignment = UIVirtualListAlignment.Start, bool animated = true, float duration = 0.25f)
        {
            if (!_keyToIndex.TryGetValue(key, out int index)) return false;
            ScrollToIndex(index, alignment, animated, duration);
            return true;
        }

        public bool TryGetVisibleItem(long key, out UIItem item)
        {
            item = null;
            return _keyToIndex.TryGetValue(key, out int index) && _visibleItems.TryGetValue(index, out item);
        }

        public void RefreshItem(int index)
        {
            if (_visibleItems.TryGetValue(index, out UIItem item) && index >= 0 && index < Count)
                item.RefreshItem(index, GetData(index));
        }

        public void ForceRefresh() { RecycleAllVisible(); UpdateVisibleItems(true); }

        public void Clear()
        {
            KillTween();
            _dataSource = null;
            _dataList.Clear();
            _keyToIndex.Clear();
            RecycleAllVisible();
            DestroyCache();
            RefreshContentSize();
            ResetVisibleRange(true);
        }

        public void GetVisibleRange(out int start, out int end) { start = _currentStartIndex; end = _currentEndIndex; }

        private void OnEnable() { CacheComponents(); BindScrollRect(); RebuildKeys(); UpdateVisibleItems(true); }
        private void OnDisable() { UnbindScrollRect(); KillTween(); RecycleAllVisible(); ResetVisibleRange(); }
        private void OnDestroy()
        {
            if (_destroyed) return;
            _destroyed = true;
            UnbindScrollRect(); KillTween(); RecycleAllVisible(); DestroyCache();
        }

        private void CacheComponents()
        {
            if (_scrollRect == null) _scrollRect = GetComponent<ScrollRect>();
            if (_scrollRect != null)
            {
                if (_viewport == null) _viewport = _scrollRect.viewport;
                if (_content == null) _content = _scrollRect.content;
            }
        }

        private void BindScrollRect()
        {
            if (_boundScrollRect == _scrollRect) return;
            UnbindScrollRect();
            if (_scrollRect == null) return;
            _scrollRect.onValueChanged.AddListener(OnScrollChanged);
            _boundScrollRect = _scrollRect;
        }

        private void UnbindScrollRect()
        {
            if (_boundScrollRect == null) return;
            _boundScrollRect.onValueChanged.RemoveListener(OnScrollChanged);
            _boundScrollRect = null;
        }

        private void RebuildDefaultTemplate()
        {
            if (_templates.Count > 1 || (_templates.Count == 1 && !_templates.ContainsKey(DefaultTemplateId))) return;
            _templates.Clear();
            if (_itemPrefab != null && _itemLogicType != null)
            {
                _templates[DefaultTemplateId] = new UIVirtualListTemplate(DefaultTemplateId, _itemPrefab, _itemLogicType, _maxCacheCount);
                if (!_pools.ContainsKey(DefaultTemplateId)) _pools[DefaultTemplateId] = new Stack<UIItem>();
            }
        }

        private void RebuildKeys()
        {
            _keyToIndex.Clear();
            for (int i = 0; i < Count; i++)
            {
                long key = GetKey(i);
                if (_keyToIndex.ContainsKey(key)) FRLog.Error($"UIVirtualList duplicate key {key} at index {i}; first occurrence is used.");
                else _keyToIndex.Add(key, i);
            }
        }

        private long GetKey(int index) => _dataSource != null ? _dataSource.GetKey(index) : index;
        private object GetData(int index) => _dataSource != null ? _dataSource.GetData(index) : _dataList[index];
        private string GetTemplateId(int index)
        {
            string id = _dataSource != null ? _dataSource.GetTemplateId(index) : DefaultTemplateId;
            return string.IsNullOrEmpty(id) ? DefaultTemplateId : id;
        }

        private void RefreshContentSize()
        {
            CacheComponents();
            if (_content == null) return;
            int constraint = Mathf.Max(1, _constraintCount);
            int lines = Mathf.CeilToInt(Count / (float)constraint);
            Vector2 size = _content.sizeDelta;
            if (IsHorizontal)
            {
                size.x = _padding.left + _padding.right + lines * _itemSize.x + Mathf.Max(0, lines - 1) * _spacing.x;
                size.y = _layoutMode == UIVirtualListLayoutMode.Horizontal
                    ? 0f
                    : _padding.top + _padding.bottom + constraint * _itemSize.y + Mathf.Max(0, constraint - 1) * _spacing.y;
            }
            else
            {
                size.x = _layoutMode == UIVirtualListLayoutMode.Vertical
                    ? 0f
                    : _padding.left + _padding.right + constraint * _itemSize.x + Mathf.Max(0, constraint - 1) * _spacing.x;
                size.y = _padding.top + _padding.bottom + lines * _itemSize.y + Mathf.Max(0, lines - 1) * _spacing.y;
            }
            _content.sizeDelta = size;
        }

        private void OnScrollChanged(Vector2 _) => UpdateVisibleItems(false);

        private void UpdateVisibleItems(bool force)
        {
            CacheComponents();
            if (Count == 0) { RecycleAllVisible(); ResetVisibleRange(); return; }
            if (_viewport == null || _content == null) return;
            CalculateVisibleRange(out int start, out int end);
            if (!force && start == _currentStartIndex && end == _currentEndIndex) return;
            bool changed = start != _currentStartIndex || end != _currentEndIndex;
            _currentStartIndex = start; _currentEndIndex = end;
            if (changed) onVisibleRangeChanged.Invoke(start, end);

            _removeIndices.Clear();
            foreach (var pair in _visibleItems)
            {
                if (pair.Key < start || pair.Key > end || pair.Value.Key != GetKey(pair.Key) || pair.Value.TemplateId != ResolveTemplateId(pair.Key, out _))
                    _removeIndices.Add(pair.Key);
            }
            for (int i = 0; i < _removeIndices.Count; i++)
            {
                int index = _removeIndices[i]; UIItem item = _visibleItems[index]; _visibleItems.Remove(index); RecycleItem(index, item);
            }

            for (int i = start; i <= end; i++)
            {
                if (_visibleItems.TryGetValue(i, out UIItem current))
                {
                    if (current.transform is RectTransform currentRect) ApplyItemLayout(currentRect, i);
                    continue;
                }
                string templateId = ResolveTemplateId(i, out UIVirtualListTemplate template);
                if (template == null) continue;
                UIItem item = GetOrCreateItem(template);
                if (item == null) continue;
                RectTransform rect = item.transform as RectTransform;
                if (rect == null) { FRLog.Error("UIVirtualList item requires a RectTransform."); DestroyItem(item); continue; }
                rect.SetParent(_content, false);
                ApplyItemLayout(rect, i);
                item.gameObject.SetActive(true); item.InternalItemVisible(i, GetKey(i), templateId, GetData(i));
                _visibleItems.Add(i, item); onItemVisible.Invoke(i, item);
            }
        }

        private void ApplyItemLayout(RectTransform rect, int index)
        {
            if (_layoutMode == UIVirtualListLayoutMode.Vertical)
            {
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = Vector2.one;
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(-_padding.left - _padding.right, _itemSize.y);
            }
            else if (_layoutMode == UIVirtualListLayoutMode.Horizontal)
            {
                rect.anchorMin = Vector2.zero;
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = new Vector2(_itemSize.x, -_padding.top - _padding.bottom);
            }
            else
            {
                rect.anchorMin = new Vector2(0f, 1f);
                rect.anchorMax = new Vector2(0f, 1f);
                rect.pivot = new Vector2(0f, 1f);
                rect.sizeDelta = _itemSize;
            }
            rect.anchoredPosition = GetItemPosition(index);
        }

        private void CalculateVisibleRange(out int start, out int end)
        {
            int constraint = Mathf.Max(1, _constraintCount);
            float step = Mathf.Max(1f, IsHorizontal ? _itemSize.x + _spacing.x : _itemSize.y + _spacing.y);
            float padding = IsHorizontal ? _padding.left : _padding.top;
            int startLine = Mathf.Max(0, Mathf.FloorToInt((GetScrollOffset() - padding) / step) - _extraCount);
            float viewport = IsHorizontal ? _viewport.rect.width : _viewport.rect.height;
            int lines = Mathf.CeilToInt(viewport / step) + _extraCount * 2 + 1;
            start = Mathf.Clamp(startLine * constraint, 0, Count - 1);
            end = Mathf.Clamp((startLine + lines + 1) * constraint - 1, 0, Count - 1);
        }

        private Vector2 GetItemPosition(int index)
        {
            int constraint = Mathf.Max(1, _constraintCount); int line = index / constraint; int cross = index % constraint;
            return IsHorizontal
                ? new Vector2(_padding.left + line * (_itemSize.x + _spacing.x), -_padding.top - cross * (_itemSize.y + _spacing.y))
                : new Vector2(_padding.left + cross * (_itemSize.x + _spacing.x), -_padding.top - line * (_itemSize.y + _spacing.y));
        }

        private float GetMainPosition(int index) => IsHorizontal ? GetItemPosition(index).x : -GetItemPosition(index).y;
        private float GetScrollOffset() => _content == null ? 0f : (IsHorizontal ? Mathf.Max(0f, -_content.anchoredPosition.x) : Mathf.Max(0f, _content.anchoredPosition.y));
        private float ClampOffset(float value)
        {
            float content = IsHorizontal ? _content.sizeDelta.x : _content.sizeDelta.y;
            float viewport = IsHorizontal ? _viewport.rect.width : _viewport.rect.height;
            return Mathf.Clamp(value, 0f, Mathf.Max(0f, content - viewport));
        }

        private void SetScrollOffset(float offset, bool animated, float duration)
        {
            Vector2 target = _content.anchoredPosition;
            if (IsHorizontal) target.x = -offset; else target.y = offset;
            KillTween();
            if (animated) _scrollTween = UITween.Move(_content, target, duration, UIAnimationEase.OutQuad);
            else _content.anchoredPosition = target;
            UpdateVisibleItems(true);
        }

        private AnchorState CaptureAnchor(UIVirtualListAnchorMode mode)
        {
            AnchorState state = new AnchorState { offset = GetScrollOffset(), wasAtEnd = IsAtEnd() };
            int bestIndex = int.MaxValue;
            UIItem bestItem = null;
            foreach (var pair in _visibleItems)
            {
                float itemEnd = GetMainPosition(pair.Key) + (IsHorizontal ? _itemSize.x : _itemSize.y);
                if (itemEnd > state.offset && pair.Key < bestIndex)
                {
                    bestIndex = pair.Key;
                    bestItem = pair.Value;
                }
            }
            if (bestItem != null)
            {
                state.hasKey = true;
                state.key = bestItem.Key;
                state.pixelOffset = GetMainPosition(bestIndex) - state.offset;
            }
            return state;
        }

        private void RestoreAnchor(AnchorState state, UIVirtualListAnchorMode mode)
        {
            if (_content == null || _viewport == null) return;
            float offset = GetScrollOffset();
            if (mode == UIVirtualListAnchorMode.PreserveScrollOffset) offset = state.offset;
            else if (mode == UIVirtualListAnchorMode.PreserveFirstVisibleKey && state.hasKey && _keyToIndex.TryGetValue(state.key, out int index)) offset = GetMainPosition(index) - state.pixelOffset;
            else if (mode == UIVirtualListAnchorMode.FollowEndIfAtEnd && state.wasAtEnd) offset = float.MaxValue;
            Vector2 position = _content.anchoredPosition;
            float clamped = ClampOffset(offset);
            if (IsHorizontal) position.x = -clamped; else position.y = clamped;
            _content.anchoredPosition = position;
        }

        public bool IsAtEnd(float tolerance = 1f) => _content != null && _viewport != null && ClampOffset(float.MaxValue) - GetScrollOffset() <= Mathf.Max(0f, tolerance);

        private string ResolveTemplateId(int index, out UIVirtualListTemplate template)
        {
            string requestedId = GetTemplateId(index);
            if (_templates.TryGetValue(requestedId, out template)) return requestedId;
            if (_missingTemplateErrors.Add(requestedId))
                FRLog.Error($"UIVirtualList template not found: {requestedId}; using the default template when available.");
            if (_templates.TryGetValue(DefaultTemplateId, out template)) return DefaultTemplateId;
            return requestedId;
        }

        private UIVirtualListTemplate GetTemplate(string id)
        {
            _templates.TryGetValue(id, out UIVirtualListTemplate template);
            return template;
        }

        private Stack<UIItem> GetPool(string id)
        {
            if (!_pools.TryGetValue(id, out Stack<UIItem> pool)) { pool = new Stack<UIItem>(); _pools[id] = pool; }
            return pool;
        }

        private UIItem GetOrCreateItem(UIVirtualListTemplate template)
        {
            Stack<UIItem> pool = GetPool(template.templateId);
            while (pool.Count > 0) { UIItem item = pool.Pop(); if (item != null) return item; }
            return CreateItem(template);
        }

        private UIItem CreateItem(UIVirtualListTemplate template)
        {
            if (template == null || template.prefab == null || template.itemLogicType == null || !typeof(UIItem).IsAssignableFrom(template.itemLogicType)) return null;
            GameObject instance = Instantiate(template.prefab); instance.name = template.prefab.name;
            Component component = instance.GetComponent(template.itemLogicType) ?? instance.AddComponent(template.itemLogicType);
            UIItem item = component as UIItem;
            if (item == null) { Destroy(instance); return null; }
            item.InternalInitialize(_ownerForm); onItemCreated.Invoke(item); return item;
        }

        private void RecycleItem(int index, UIItem item)
        {
            if (item == null) return;
            string templateId = item.TemplateId;
            onItemRecycled.Invoke(index, item); item.InternalItemRecycled(); item.gameObject.SetActive(false);
            UIVirtualListTemplate template = GetTemplate(templateId);
            Stack<UIItem> pool = template != null ? GetPool(templateId) : null;
            if (template != null && pool.Count < template.maxCacheCount) pool.Push(item); else DestroyItem(item);
        }

        private void RecycleAllVisible() { foreach (var pair in _visibleItems) RecycleItem(pair.Key, pair.Value); _visibleItems.Clear(); }
        private void TrimCaches()
        {
            foreach (var pair in _pools)
            {
                UIVirtualListTemplate template = GetTemplate(pair.Key); int max = template != null ? template.maxCacheCount : 0;
                while (pair.Value.Count > max) DestroyItem(pair.Value.Pop());
            }
        }
        private void DestroyCache() { foreach (Stack<UIItem> pool in _pools.Values) while (pool.Count > 0) DestroyItem(pool.Pop()); _pools.Clear(); }
        private static void DestroyItem(UIItem item) { if (item == null) return; item.InternalDispose(); Destroy(item.gameObject); }
        private void KillTween() { _scrollTween?.Kill(); _scrollTween = null; }
        private void ResetVisibleRange(bool always = false)
        {
            bool changed = _currentStartIndex != -1 || _currentEndIndex != -1; _currentStartIndex = -1; _currentEndIndex = -1;
            if (changed || always) onVisibleRangeChanged.Invoke(-1, -1);
        }
        private bool IsHorizontal => _layoutMode == UIVirtualListLayoutMode.Horizontal || _layoutMode == UIVirtualListLayoutMode.GridHorizontal;

        private struct AnchorState { public float offset; public bool wasAtEnd; public bool hasKey; public long key; public float pixelOffset; }
    }
}
