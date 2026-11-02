using System;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame.Editor
{
    /// <summary>
    /// UIConfigGroup 的 Draft 副本。
    /// 
    /// 编辑器中所有修改先作用在 Draft 上，
    /// 保存时再一次性回写到真正的 ScriptableObject 资产。
    /// 这样可以避免：
    /// 1. 未保存修改直接污染 asset
    /// 2. 删除/撤销/切换选中时状态混乱
    /// 3. UI Toolkit 绑定直接操作原始对象造成的副作用
    /// </summary>
    [Serializable]
    public sealed class UIConfigGroupDraft
    {
        /// <summary>
        /// 对应的原始资产。
        /// 不直接编辑它，只在保存时回写。
        /// </summary>
        public UIConfigGroup sourceAsset;

        /// <summary>
        /// 资产显示名（asset.name）。
        /// 仅用于界面显示。
        /// </summary>
        public string assetName;

        /// <summary>
        /// 资产路径。
        /// 仅用于界面显示、定位。
        /// </summary>
        public string assetPath;

        /// <summary>
        /// Group 配置字段。
        /// </summary>
        public string groupName;

        /// <summary>
        /// 是否初始化时加载。
        /// </summary>
        public bool loadOnInitialize;

        public string defaultPackageName;

        /// <summary>
        /// Draft 中的 forms 列表。
        /// </summary>
        public List<UIFormDraftItem> forms = new List<UIFormDraftItem>();

        /// <summary>
        /// 当前 Draft 是否存在未保存修改。
        /// </summary>
        public bool isDirty;

        /// <summary>
        /// 重新计算脏状态。
        /// 一般在行修改后可调用；也可直接由窗口层统一置脏。
        /// </summary>
        public void RecalculateDirty()
        {
            if (isDirty)
                return;

            foreach (UIFormDraftItem item in forms)
            {
                if (item == null)
                    continue;

                if (item.isNew || item.isModified)
                {
                    isDirty = true;
                    return;
                }
            }
        }
    }
}
