using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FRFrame
{
    public sealed class UIDynamicVirtualList : MonoBehaviour
    {
        [SerializeField] private ScrollRect _scrollRect;
        [SerializeField] private RectTransform _viewport;
        [SerializeField] private RectTransform _content;
        [SerializeField] private GameObject _itemPrefab;
        [SerializeField] private float _spacing = 8f;
        [SerializeField] private float _paddingTop;
        [SerializeField] private float _paddingBottom;
        [SerializeField] private int _extraCount = 2;
        [SerializeField] private int _maxCacheCount = 64;
        [SerializeField] private UIVirtualListAnchorMode _anchorMode = UIVirtualListAnchorMode.PreserveFirstVisibleKey;
        [SerializeField] private UIDynamicSizeAnchorMode _sizeAnchorMode = UIDynamicSizeAnchorMode.PreserveFirstVisibleKey;
        [SerializeField] private float _minimumItemSize = 1f;

        public UIVirtualListRangeChangedEvent onVisibleRangeChanged = new UIVirtualListRangeChangedEvent();
        public UIVirtualListItemEvent onItemVisible = new UIVirtualListItemEvent();
        public UIVirtualListItemEvent onItemRecycled = new UIVirtualListItemEvent();
        public UnityEngine.Events.UnityEvent<UIItem> onItemCreated = new UnityEngine.Events.UnityEvent<UIItem>();

        private const string DefaultTemplateId = "default";
        private readonly List<object> _dataList = new List<object>();
        private readonly List<float> _itemSizes = new List<float>();
        private readonly Dictionary<int, UIItem> _visibleItems = new Dictionary<int, UIItem>();
        private readonly Dictionary<long, int> _keyToIndex = new Dictionary<long, int>();
        private readonly Dictionary<string, UIVirtualListTemplate> _templates = new Dictionary<string, UIVirtualListTemplate>();
        private readonly Dictionary<string, Stack<UIItem>> _pools = new Dictionary<string, Stack<UIItem>>();
        private readonly List<int> _removeIndices = new List<int>();
        private readonly List<KeyValuePair<int, UIItem>> _remappedItems = new List<KeyValuePair<int, UIItem>>();
        private readonly HashSet<string> _missingTemplateErrors = new HashSet<string>();
        private readonly FenwickTree _sizes = new FenwickTree();

        private UIForm _ownerForm;
        private Type _itemLogicType;
        private IUIVirtualListDataSource _dataSource;
        private Func<int, object, float> _sizeProvider;
        private IUIDynamicItemMeasurer _measurer;
        private int _currentStartIndex = -1;
        private int _currentEndIndex = -1;
        private int _sizeUpdateDepth;
        private bool _sizesDirty;
        private SizeAnchor _sizeUpdateAnchor;
        private UITweenHandle _scrollTween;
        private ScrollRect _boundScrollRect;
        private bool _destroyed;

        public UIVirtualListAnchorMode AnchorMode { get => _anchorMode; set => _anchorMode = value; }
        public UIDynamicSizeAnchorMode SizeAnchorMode { get => _sizeAnchorMode; set => _sizeAnchorMode = value; }
        private int Count => _dataSource != null ? Mathf.Max(0, _dataSource.Count) : _dataList.Count;

        public void Initialize(UIForm ownerForm, Type itemLogicType)
        {
            UnbindScrollRect();
            if (_itemLogicType != itemLogicType) { RecycleAllVisible(); DestroyCache(); }
            _ownerForm = ownerForm; _itemLogicType = itemLogicType; RebuildDefaultTemplate(); CacheComponents();
            if (isActiveAndEnabled) { BindScrollRect(); ForceRefresh(); }
        }

        public void Configure(ScrollRect scrollRect, RectTransform viewport, RectTransform content, GameObject itemPrefab, float spacing = 8f, float paddingTop = 0f, float paddingBottom = 0f, int extraCount = 2, int maxCacheCount = 64)
        {
            UnbindScrollRect(); RecycleAllVisible();
            if (_itemPrefab != itemPrefab) DestroyCache();
            _scrollRect = scrollRect; _viewport = viewport; _content = content; _itemPrefab = itemPrefab;
            _spacing = Mathf.Max(0f, spacing); _paddingTop = Mathf.Max(0f, paddingTop); _paddingBottom = Mathf.Max(0f, paddingBottom);
            _extraCount = Mathf.Max(0, extraCount); _maxCacheCount = Mathf.Max(0, maxCacheCount);
            RebuildDefaultTemplate(); TrimCaches(); RebuildSizeCache();
            if (isActiveAndEnabled) { BindScrollRect(); UpdateVisibleItems(true); }
        }

        public void ConfigureTemplates(IEnumerable<UIVirtualListTemplate> templates)
        {
            RecycleAllVisible(); DestroyCache(); _templates.Clear(); _missingTemplateErrors.Clear();
            if (templates != null)
            {
                foreach (UIVirtualListTemplate template in templates)
                {
                    if (template == null || string.IsNullOrEmpty(template.templateId) || template.prefab == null || template.itemLogicType == null || !typeof(UIItem).IsAssignableFrom(template.itemLogicType))
                    { FRLog.Error("UIDynamicVirtualList ignored an invalid template."); continue; }
                    if (_templates.ContainsKey(template.templateId)) { FRLog.Error($"UIDynamicVirtualList duplicate template id: {template.templateId}"); continue; }
                    template.maxCacheCount = Mathf.Max(0, template.maxCacheCount); _templates.Add(template.templateId, template); _pools[template.templateId] = new Stack<UIItem>();
                }
            }
            if (_templates.Count == 0) RebuildDefaultTemplate();
            ForceRefresh();
        }

        public void SetDataSource(IUIVirtualListDataSource dataSource)
        {
            ListAnchor anchor = CaptureListAnchor(_anchorMode); _dataSource = dataSource; RebuildKeys(); RebuildSizeCache(false); RestoreListAnchor(anchor, _anchorMode); ForceRefresh();
        }

        public void SetSizeProvider(Func<int, object, float> sizeProvider) { _sizeProvider = sizeProvider; RebuildSizeCache(); }
        public void SetItemMeasurer(IUIDynamicItemMeasurer measurer) { _measurer = measurer; }

        public void SetDataList<T>(IList<T> dataList)
        {
            ListAnchor anchor = CaptureListAnchor(_anchorMode); _dataSource = null; _dataList.Clear();
            if (dataList != null) for (int i = 0; i < dataList.Count; i++) _dataList.Add(dataList[i]);
            RebuildKeys(); RebuildSizeCache(false); RestoreListAnchor(anchor, _anchorMode); ForceRefresh();
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
            index = Mathf.Max(0, index); int end = index + count;
            NotifyStructureChanged(i => i < index ? i : (i >= end ? i - count : -1));
        }
        public void ReplaceRange(int index, int count)
        {
            if (count <= 0) return;
            index = Mathf.Max(0, index); int end = index + count;
            NotifyStructureChanged(i => i >= index && i < end ? -1 : i);
        }
        public void Move(int fromIndex, int toIndex, int count = 1)
        {
            if (count <= 0 || fromIndex < 0 || toIndex < 0 || fromIndex == toIndex) return;
            NotifyStructureChanged(i => RemapMovedIndex(i, fromIndex, toIndex, count));
        }
        private void NotifyStructureChanged(Func<int, int> remap)
        {
            ListAnchor anchor = CaptureListAnchor(_anchorMode);
            RemapVisibleItems(remap);
            RebuildKeys();
            RebuildSizeCache(false);
            RestoreListAnchor(anchor, _anchorMode);
            UpdateVisibleItems(true);
        }
        private void RemapVisibleItems(Func<int, int> remap)
        {
            _remappedItems.Clear();
            foreach (var pair in _visibleItems)
            {
                int newIndex = remap(pair.Key);
                if (newIndex < 0 || newIndex >= Count) { RecycleItem(pair.Key, pair.Value); continue; }
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
            UIVirtualListTemplate template = GetTemplate(DefaultTemplateId); if (template == null) return;
            Stack<UIItem> pool = GetPool(template.templateId); int target = Mathf.Min(Mathf.Max(0, count), template.maxCacheCount);
            while (pool.Count < target) { UIItem item = CreateItem(template); if (item == null) return; item.gameObject.SetActive(false); pool.Push(item); }
        }

        public void RefreshItem(int index) { if (_visibleItems.TryGetValue(index, out UIItem item) && index >= 0 && index < Count) item.RefreshItem(index, GetData(index)); }

        public void BeginSizeUpdate()
        {
            if (_sizeUpdateDepth++ == 0) { _sizeUpdateAnchor = CaptureSizeAnchor(_sizeAnchorMode); _sizesDirty = false; }
        }

        public void NotifyItemSizeChanged(int index) { if (index >= 0 && index < Count) NotifyItemSizeChanged(index, GetItemSize(index)); }
        public void NotifyItemSizeChanged(int index, float size)
        {
            if (index < 0 || index >= Count) return;
            bool ownBatch = _sizeUpdateDepth == 0; if (ownBatch) BeginSizeUpdate();
            SetItemSize(index, ValidateSize(size, index));
            if (ownBatch) EndSizeUpdate();
        }

        public void NotifyItemSizesChanged(int startIndex, int count)
        {
            BeginSizeUpdate(); int end = Mathf.Min(Count, Mathf.Max(0, startIndex) + Mathf.Max(0, count));
            for (int i = Mathf.Max(0, startIndex); i < end; i++) SetItemSize(i, GetItemSize(i));
            EndSizeUpdate();
        }

        public void NotifyItemSizesChanged() => InvalidateAllSizes();
        public void InvalidateSize(int index) => NotifyItemSizeChanged(index);
        public void InvalidateAllSizes() { BeginSizeUpdate(); for (int i = 0; i < Count; i++) SetItemSize(i, GetItemSize(i)); EndSizeUpdate(); }

        public void EndSizeUpdate()
        {
            if (_sizeUpdateDepth == 0) return;
            if (--_sizeUpdateDepth != 0) return;
            if (_sizesDirty) { RefreshContentSize(); RestoreSizeAnchor(_sizeUpdateAnchor, _sizeAnchorMode); ForceRefresh(); }
        }

        public int MeasureVisibleItems()
        {
            if (_measurer == null || _viewport == null) return 0;
            BeginSizeUpdate(); int measured = 0;
            foreach (var pair in _visibleItems)
            {
                float value = _measurer.MeasureItem(pair.Value, pair.Key, GetData(pair.Key), _viewport.rect.width);
                SetItemSize(pair.Key, ValidateSize(value, pair.Key)); measured++;
            }
            EndSizeUpdate(); return measured;
        }

        public void RebuildSizeCache() => RebuildSizeCache(true);
        private void RebuildSizeCache(bool refresh)
        {
            _itemSizes.Clear(); for (int i = 0; i < Count; i++) _itemSizes.Add(GetItemSize(i));
            _sizes.Build(_itemSizes, _spacing); RefreshContentSize(); if (refresh) ForceRefresh();
        }

        public void ScrollToIndex(int index, bool animated = true, float duration = 0.25f) => ScrollToIndex(index, UIVirtualListAlignment.Start, animated, duration);
        public void ScrollToIndex(int index, UIVirtualListAlignment alignment, bool animated = true, float duration = 0.25f)
        {
            if (_content == null || _viewport == null || Count == 0) return;
            index = Mathf.Clamp(index, 0, Count - 1); float top = GetItemTop(index); float size = _itemSizes[index]; float viewport = _viewport.rect.height; float current = GetScrollOffset(); float target = top;
            if (alignment == UIVirtualListAlignment.Center) target -= (viewport - size) * .5f;
            else if (alignment == UIVirtualListAlignment.End) target -= viewport - size;
            else if (alignment == UIVirtualListAlignment.Nearest)
            {
                if (top >= current && top + size <= current + viewport) target = current;
                else if (top + size > current + viewport) target = top + size - viewport;
            }
            SetScrollOffset(ClampOffset(target), animated, duration);
        }

        public bool ScrollToKey(long key, UIVirtualListAlignment alignment = UIVirtualListAlignment.Start, bool animated = true, float duration = .25f)
        { if (!_keyToIndex.TryGetValue(key, out int index)) return false; ScrollToIndex(index, alignment, animated, duration); return true; }
        public void ScrollToBottom(bool animated = true, float duration = .25f) { if (_content != null && _viewport != null) SetScrollOffset(ClampOffset(float.MaxValue), animated, duration); }
        public long? GetFirstVisibleKey() { if (Count == 0) return null; return GetKey(FindIndexByOffset(GetScrollOffset())); }
        public bool IsAtEnd(float tolerance = 1f) => _content != null && _viewport != null && ClampOffset(float.MaxValue) - GetScrollOffset() <= Mathf.Max(0f, tolerance);
        public bool TryGetVisibleItem(long key, out UIItem item) { item = null; return _keyToIndex.TryGetValue(key, out int index) && _visibleItems.TryGetValue(index, out item); }
        public void ForceRefresh() { RecycleAllVisible(); UpdateVisibleItems(true); }

        public void Clear()
        {
            KillTween(); _dataSource = null; _dataList.Clear(); _itemSizes.Clear(); _sizes.Clear(); _keyToIndex.Clear(); RecycleAllVisible(); DestroyCache(); RefreshContentSize(); ResetVisibleRange(true);
        }
        public void GetVisibleRange(out int start, out int end) { start = _currentStartIndex; end = _currentEndIndex; }

        private void OnEnable() { CacheComponents(); BindScrollRect(); RebuildKeys(); UpdateVisibleItems(true); }
        private void OnDisable() { UnbindScrollRect(); KillTween(); RecycleAllVisible(); ResetVisibleRange(); }
        private void OnDestroy() { if (_destroyed) return; _destroyed = true; UnbindScrollRect(); KillTween(); RecycleAllVisible(); DestroyCache(); }
        private void CacheComponents()
        {
            if (_scrollRect == null) _scrollRect = GetComponent<ScrollRect>();
            if (_scrollRect != null) { if (_viewport == null) _viewport = _scrollRect.viewport; if (_content == null) _content = _scrollRect.content; }
        }
        private void BindScrollRect() { if (_boundScrollRect == _scrollRect) return; UnbindScrollRect(); if (_scrollRect == null) return; _scrollRect.onValueChanged.AddListener(OnScrollChanged); _boundScrollRect = _scrollRect; }
        private void UnbindScrollRect() { if (_boundScrollRect == null) return; _boundScrollRect.onValueChanged.RemoveListener(OnScrollChanged); _boundScrollRect = null; }
        private void OnScrollChanged(Vector2 _) => UpdateVisibleItems(false);

        private void RebuildDefaultTemplate()
        {
            if (_templates.Count > 1 || (_templates.Count == 1 && !_templates.ContainsKey(DefaultTemplateId))) return;
            _templates.Clear();
            if (_itemPrefab != null && _itemLogicType != null) { _templates[DefaultTemplateId] = new UIVirtualListTemplate(DefaultTemplateId, _itemPrefab, _itemLogicType, _maxCacheCount); if (!_pools.ContainsKey(DefaultTemplateId)) _pools[DefaultTemplateId] = new Stack<UIItem>(); }
        }
        private void RebuildKeys()
        {
            _keyToIndex.Clear();
            for (int i = 0; i < Count; i++) { long key = GetKey(i); if (_keyToIndex.ContainsKey(key)) FRLog.Error($"UIDynamicVirtualList duplicate key {key} at index {i}; first occurrence is used."); else _keyToIndex.Add(key, i); }
        }
        private long GetKey(int index) => _dataSource != null ? _dataSource.GetKey(index) : index;
        private object GetData(int index) => _dataSource != null ? _dataSource.GetData(index) : _dataList[index];
        private string GetTemplateId(int index) { string id = _dataSource != null ? _dataSource.GetTemplateId(index) : DefaultTemplateId; return string.IsNullOrEmpty(id) ? DefaultTemplateId : id; }
        private float GetItemSize(int index) => ValidateSize(_sizeProvider != null ? _sizeProvider(index, GetData(index)) : 80f, index);
        private float ValidateSize(float value, int index)
        {
            if (float.IsNaN(value) || float.IsInfinity(value) || value <= 0f) { float valid = Mathf.Max(.001f, _minimumItemSize); FRLog.Error($"UIDynamicVirtualList invalid size {value} at index {index}; clamped to {valid}."); return valid; }
            return Mathf.Max(Mathf.Max(.001f, _minimumItemSize), value);
        }
        private void SetItemSize(int index, float value)
        {
            if (index < 0 || index >= _itemSizes.Count || Mathf.Approximately(_itemSizes[index], value)) return;
            float delta = value - _itemSizes[index]; _itemSizes[index] = value; _sizes.Add(index, delta); _sizesDirty = true;
            if (_visibleItems.TryGetValue(index, out UIItem item) && item.transform is RectTransform rect) rect.sizeDelta = new Vector2(0f, value);
        }
        private float GetItemTop(int index) => _paddingTop + _sizes.PrefixSum(index);
        private void RefreshContentSize() { CacheComponents(); if (_content != null) _content.sizeDelta = new Vector2(_content.sizeDelta.x, _paddingTop + _sizes.Total + _paddingBottom); }

        private void UpdateVisibleItems(bool force)
        {
            CacheComponents();
            if (Count == 0) { RecycleAllVisible(); ResetVisibleRange(); return; }
            if (_viewport == null || _content == null || _itemSizes.Count != Count) return;
            float top = GetScrollOffset(), bottom = top + _viewport.rect.height;
            int start = Mathf.Max(0, FindIndexByOffset(top) - _extraCount), end = Mathf.Min(Count - 1, FindIndexByOffset(bottom) + _extraCount);
            if (!force && start == _currentStartIndex && end == _currentEndIndex) return;
            bool changed = start != _currentStartIndex || end != _currentEndIndex; _currentStartIndex = start; _currentEndIndex = end; if (changed) onVisibleRangeChanged.Invoke(start, end);
            _removeIndices.Clear();
            foreach (var pair in _visibleItems) if (pair.Key < start || pair.Key > end || pair.Value.Key != GetKey(pair.Key) || pair.Value.TemplateId != ResolveTemplateId(pair.Key, out _)) _removeIndices.Add(pair.Key);
            for (int i = 0; i < _removeIndices.Count; i++) { int index = _removeIndices[i]; UIItem item = _visibleItems[index]; _visibleItems.Remove(index); RecycleItem(index, item); }
            for (int i = start; i <= end; i++)
            {
                if (_visibleItems.TryGetValue(i, out UIItem current)) { if (current.transform is RectTransform currentRect) { currentRect.sizeDelta = new Vector2(0f, _itemSizes[i]); currentRect.anchoredPosition = new Vector2(0f, -GetItemTop(i)); } continue; }
                string templateId = ResolveTemplateId(i, out UIVirtualListTemplate template); if (template == null) continue;
                UIItem item = GetOrCreateItem(template); if (item == null) continue; RectTransform rect = item.transform as RectTransform;
                if (rect == null) { FRLog.Error("UIDynamicVirtualList item requires a RectTransform."); DestroyItem(item); continue; }
                rect.SetParent(_content, false); rect.anchorMin = new Vector2(0f, 1f); rect.anchorMax = new Vector2(1f, 1f); rect.pivot = new Vector2(.5f, 1f); rect.sizeDelta = new Vector2(0f, _itemSizes[i]); rect.anchoredPosition = new Vector2(0f, -GetItemTop(i));
                item.gameObject.SetActive(true); item.InternalItemVisible(i, GetKey(i), templateId, GetData(i)); _visibleItems.Add(i, item); onItemVisible.Invoke(i, item);
            }
        }

        private int FindIndexByOffset(float offset)
        {
            if (Count == 0) return -1;
            float local = Mathf.Max(0f, offset - _paddingTop); return Mathf.Clamp(_sizes.LowerBound(local), 0, Count - 1);
        }
        private float GetScrollOffset() => _content == null ? 0f : Mathf.Max(0f, _content.anchoredPosition.y);
        private float ClampOffset(float value) => Mathf.Clamp(value, 0f, Mathf.Max(0f, (_content != null ? _content.sizeDelta.y : 0f) - (_viewport != null ? _viewport.rect.height : 0f)));
        private void SetScrollOffset(float offset, bool animated, float duration)
        {
            Vector2 target = _content.anchoredPosition; target.y = offset; KillTween();
            if (animated) _scrollTween = UITween.Move(_content, target, duration, UIAnimationEase.OutQuad); else _content.anchoredPosition = target; UpdateVisibleItems(true);
        }

        private ListAnchor CaptureListAnchor(UIVirtualListAnchorMode mode)
        {
            ListAnchor anchor = new ListAnchor { offset = GetScrollOffset(), atEnd = IsAtEnd() };
            int bestIndex = int.MaxValue; UIItem bestItem = null;
            foreach (var pair in _visibleItems)
            {
                float itemEnd = GetItemTop(pair.Key) + (pair.Key < _itemSizes.Count ? _itemSizes[pair.Key] : 0f);
                if (itemEnd > anchor.offset && pair.Key < bestIndex) { bestIndex = pair.Key; bestItem = pair.Value; }
            }
            if (bestItem != null) { anchor.hasKey = true; anchor.key = bestItem.Key; anchor.pixelOffset = GetItemTop(bestIndex) - anchor.offset; }
            return anchor;
        }
        private void RestoreListAnchor(ListAnchor anchor, UIVirtualListAnchorMode mode)
        {
            if (_content == null || _viewport == null) return; float offset = GetScrollOffset();
            if (mode == UIVirtualListAnchorMode.PreserveScrollOffset) offset = anchor.offset;
            else if (mode == UIVirtualListAnchorMode.PreserveFirstVisibleKey && anchor.hasKey && _keyToIndex.TryGetValue(anchor.key, out int index)) offset = GetItemTop(index) - anchor.pixelOffset;
            else if (mode == UIVirtualListAnchorMode.FollowEndIfAtEnd && anchor.atEnd) offset = float.MaxValue;
            Vector2 position = _content.anchoredPosition; position.y = ClampOffset(offset); _content.anchoredPosition = position;
        }
        private SizeAnchor CaptureSizeAnchor(UIDynamicSizeAnchorMode mode)
        {
            SizeAnchor anchor = new SizeAnchor { atEnd = IsAtEnd(), offset = GetScrollOffset() }; int index = Count > 0 ? FindIndexByOffset(anchor.offset) : -1;
            if (index >= 0) { anchor.hasKey = true; anchor.key = GetKey(index); anchor.pixelOffset = GetItemTop(index) - anchor.offset; } return anchor;
        }
        private void RestoreSizeAnchor(SizeAnchor anchor, UIDynamicSizeAnchorMode mode)
        {
            float offset = anchor.offset;
            if (mode == UIDynamicSizeAnchorMode.PreserveEnd && anchor.atEnd) offset = float.MaxValue;
            else if (mode == UIDynamicSizeAnchorMode.PreserveFirstVisibleKey && anchor.hasKey && _keyToIndex.TryGetValue(anchor.key, out int index)) offset = GetItemTop(index) - anchor.pixelOffset;
            Vector2 position = _content.anchoredPosition; position.y = ClampOffset(offset); _content.anchoredPosition = position;
        }

        private string ResolveTemplateId(int index, out UIVirtualListTemplate template)
        {
            string requestedId = GetTemplateId(index);
            if (_templates.TryGetValue(requestedId, out template)) return requestedId;
            if (_missingTemplateErrors.Add(requestedId)) FRLog.Error($"UIDynamicVirtualList template not found: {requestedId}; using the default template when available.");
            if (_templates.TryGetValue(DefaultTemplateId, out template)) return DefaultTemplateId;
            return requestedId;
        }
        private UIVirtualListTemplate GetTemplate(string id) { _templates.TryGetValue(id, out UIVirtualListTemplate template); return template; }
        private Stack<UIItem> GetPool(string id) { if (!_pools.TryGetValue(id, out Stack<UIItem> pool)) { pool = new Stack<UIItem>(); _pools[id] = pool; } return pool; }
        private UIItem GetOrCreateItem(UIVirtualListTemplate template) { Stack<UIItem> pool = GetPool(template.templateId); while (pool.Count > 0) { UIItem item = pool.Pop(); if (item != null) return item; } return CreateItem(template); }
        private UIItem CreateItem(UIVirtualListTemplate template)
        {
            GameObject instance = Instantiate(template.prefab); instance.name = template.prefab.name; Component component = instance.GetComponent(template.itemLogicType) ?? instance.AddComponent(template.itemLogicType); UIItem item = component as UIItem;
            if (item == null) { Destroy(instance); return null; } item.InternalInitialize(_ownerForm); onItemCreated.Invoke(item); return item;
        }
        private void RecycleItem(int index, UIItem item)
        {
            if (item == null) return; string templateId = item.TemplateId; onItemRecycled.Invoke(index, item); item.InternalItemRecycled(); item.gameObject.SetActive(false);
            UIVirtualListTemplate template = GetTemplate(templateId); Stack<UIItem> pool = template != null ? GetPool(templateId) : null;
            if (template != null && pool.Count < template.maxCacheCount) pool.Push(item); else DestroyItem(item);
        }
        private void RecycleAllVisible() { foreach (var pair in _visibleItems) RecycleItem(pair.Key, pair.Value); _visibleItems.Clear(); }
        private void TrimCaches() { foreach (var pair in _pools) { UIVirtualListTemplate template = GetTemplate(pair.Key); int max = template != null ? template.maxCacheCount : 0; while (pair.Value.Count > max) DestroyItem(pair.Value.Pop()); } }
        private void DestroyCache() { foreach (Stack<UIItem> pool in _pools.Values) while (pool.Count > 0) DestroyItem(pool.Pop()); _pools.Clear(); }
        private static void DestroyItem(UIItem item) { if (item == null) return; item.InternalDispose(); Destroy(item.gameObject); }
        private void KillTween() { _scrollTween?.Kill(); _scrollTween = null; }
        private void ResetVisibleRange(bool always = false) { bool changed = _currentStartIndex != -1 || _currentEndIndex != -1; _currentStartIndex = -1; _currentEndIndex = -1; if (changed || always) onVisibleRangeChanged.Invoke(-1, -1); }

        private struct ListAnchor { public float offset, pixelOffset; public bool atEnd, hasKey; public long key; }
        private struct SizeAnchor { public float offset, pixelOffset; public bool atEnd, hasKey; public long key; }

        private sealed class FenwickTree
        {
            private float[] _tree = new float[1];
            public int Count { get; private set; }
            public float Total => PrefixSum(Count);
            public void Clear() { _tree = new float[1]; Count = 0; }
            public void Build(List<float> values, float spacing)
            {
                Count = values.Count; _tree = new float[Count + 1];
                for (int i = 0; i < Count; i++) Add(i, values[i] + (i < Count - 1 ? spacing : 0f));
            }
            public void Add(int index, float delta) { for (int i = index + 1; i <= Count; i += i & -i) _tree[i] += delta; }
            public float PrefixSum(int count) { float sum = 0f; for (int i = Mathf.Clamp(count, 0, Count); i > 0; i -= i & -i) sum += _tree[i]; return sum; }
            public int LowerBound(float value)
            {
                int index = 0; float sum = 0f; int bit = 1; while ((bit << 1) <= Count) bit <<= 1;
                for (; bit != 0; bit >>= 1) { int next = index + bit; if (next <= Count && sum + _tree[next] <= value) { index = next; sum += _tree[next]; } }
                return Mathf.Min(index, Mathf.Max(0, Count - 1));
            }
        }
    }
}
