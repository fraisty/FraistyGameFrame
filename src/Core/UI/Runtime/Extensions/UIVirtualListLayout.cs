using System;
using UnityEngine;
using UnityEngine.Events;

namespace FRFrame
{
    public enum UIVirtualListLayoutMode
    {
        Vertical = 0,
        Horizontal = 1,
        GridVertical = 2,
        GridHorizontal = 3
    }

    public enum UIVirtualListAnchorMode
    {
        None,
        PreserveScrollOffset,
        PreserveFirstVisibleKey,
        FollowEndIfAtEnd
    }

    public enum UIVirtualListAlignment
    {
        Start,
        Center,
        End,
        Nearest
    }

    public enum UIDynamicSizeAnchorMode
    {
        None,
        PreserveFirstVisibleKey,
        PreserveEnd
    }

    public interface IUIVirtualListDataSource
    {
        int Count { get; }
        long GetKey(int index);
        object GetData(int index);
        string GetTemplateId(int index);
    }

    public interface IUIDynamicItemMeasurer
    {
        float MeasureItem(UIItem item, int index, object data, float availableWidth);
    }

    [Serializable]
    public sealed class UIVirtualListTemplate
    {
        public string templateId;
        public GameObject prefab;
        public Type itemLogicType;
        public int maxCacheCount = 64;

        public UIVirtualListTemplate() { }

        public UIVirtualListTemplate(string templateId, GameObject prefab, Type itemLogicType, int maxCacheCount = 64)
        {
            this.templateId = templateId;
            this.prefab = prefab;
            this.itemLogicType = itemLogicType;
            this.maxCacheCount = Mathf.Max(0, maxCacheCount);
        }
    }

    [Serializable]
    public sealed class UIVirtualListRangeChangedEvent : UnityEvent<int, int> { }

    [Serializable]
    public sealed class UIVirtualListItemEvent : UnityEvent<int, UIItem> { }

    [Serializable]
    public struct UIVirtualListPadding
    {
        public float left;
        public float right;
        public float top;
        public float bottom;
    }
}
