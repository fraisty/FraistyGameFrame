using System.Collections.Generic;
using UnityEngine;

namespace FRFrame
{
    /// <summary>
    /// UI 绑定列表的持久化排序规则。
    /// 代码生成器按相同规则重排数据，确保生成索引与运行时列表一致。
    /// </summary>
    public enum UIBindSortMode
    {
        /// <summary>
        /// 按节点名称路径、组件类型优先级、字段名依次排序。
        /// </summary>
        Path = 0,

        /// <summary>
        /// 按 Hierarchy 深度优先顺序、组件类型优先级、字段名依次排序。
        /// </summary>
        Hierarchy = 1
    }

    /// <summary>
    /// UI绑定数据容器。
    /// 挂在UI prefab 根节点上，运行时按索引或字段名获取。
    /// </summary>
    public sealed class UIBindData : MonoBehaviour
    {
        [SerializeField] private MonoScriptReference _targetScript;
        [SerializeField] private UIBindSortMode _sortMode;
        [SerializeField] private List<UIBindInfo> _bindInfos = new List<UIBindInfo>();

        public MonoScriptReference TargetScript => _targetScript;
        public UIBindSortMode SortMode => _sortMode;
        public IReadOnlyList<UIBindInfo> BindInfos => _bindInfos;

        public void SetTargetScript(MonoScriptReference scriptRef)
        {
            _targetScript = scriptRef;
        }

        public void SetSortMode(UIBindSortMode sortMode)
        {
            _sortMode = sortMode;
        }

        public void SetBindInfos(List<UIBindInfo> infos)
        {
            _bindInfos = infos ?? new List<UIBindInfo>();
        }

        public int Count => _bindInfos != null ? _bindInfos.Count : 0;

        public T GetComponentByIndex<T>(int index) where T : Component
        {
            if (_bindInfos == null || index < 0 || index >= _bindInfos.Count)
            {
                FRLog.Error($"UIBindData.GetComponentByIndex failed, index out of range : {index}");
                return null;
            }

            var info = _bindInfos[index];
            if (info == null)
                return null;

            if (info.component == null && info.target != null)
            {
                if (typeof(T) == typeof(Transform))
                    return info.target.transform as T;

                if (typeof(T) == typeof(RectTransform))
                    return info.target.transform as T;

                return info.target.GetComponent<T>();
            }

            return info.component as T;
        }

        public GameObject GetGameObjectByIndex(int index)
        {
            if (_bindInfos == null || index < 0 || index >= _bindInfos.Count)
            {
                FRLog.Error($"UIBindData.GetGameObjectByIndex failed, index out of range : {index}");
                return null;
            }

            return _bindInfos[index]?.target;
        }

        public bool TryGetBindInfo(string fieldName, out UIBindInfo info)
        {
            info = null;
            if (_bindInfos == null || string.IsNullOrEmpty(fieldName))
                return false;

            for (int i = 0; i < _bindInfos.Count; i++)
            {
                var item = _bindInfos[i];
                if (item != null && item.fieldName == fieldName)
                {
                    info = item;
                    return true;
                }
            }

            return false;
        }
    }
}