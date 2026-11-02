using System;
using UnityEngine;

namespace FRFrame.Editor
{
    /// <summary>
    /// Draft 层中的单条 UIForm 行数据。
    /// 
    /// 这里不直接复用原始 UIFormConfig 引用，
    /// 而是做一层包装，方便：
    /// 1. 给每行一个稳定 rowId，便于 ListView 跟踪
    /// 2. 标记 isNew / isModified / isDeleted 等运行时状态
    /// 3. 后续如果需要扩展行级状态（如错误缓存、展开状态）会更方便
    /// </summary>
    [Serializable]
    public sealed class UIFormDraftItem
    {
        /// <summary>
        /// 运行时唯一行标识。
        /// 仅用于编辑器 UI 层，不参与保存到资产。
        /// </summary>
        public string rowId;

        /// <summary>
        /// 真正的表单配置数据副本。
        /// </summary>
        public UIFormConfig data;

        /// <summary>
        /// 是否为本次编辑中新建项。
        /// </summary>
        public bool isNew;

        /// <summary>
        /// 是否已被修改。
        /// </summary>
        public bool isModified;

        /// <summary>
        /// 默认构造。
        /// </summary>
        public UIFormDraftItem()
        {
        }

        /// <summary>
        /// 便捷构造。
        /// </summary>
        public UIFormDraftItem(UIFormConfig data, bool isNew = false, bool isModified = false)
        {
            rowId = Guid.NewGuid().ToString("N");
            this.data = data;
            this.isNew = isNew;
            this.isModified = isModified;
        }
    }
}
