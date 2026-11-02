using UnityEngine;
using UnityEngine.UI;

namespace FRFrame
{
    [RequireComponent(typeof(CanvasRenderer))]
    public sealed class UINoDrawingGraphic : Graphic
    {
        /// <summary>
        /// 阻止材质更新（因为不需要渲染，所以不需要计算材质变化）
        /// </summary>
        public override void SetMaterialDirty()
        {
            // 留空：切断 UGUI 的材质重建流程
        }
        
        /// <summary>
        /// 阻止顶点更新（因为不需要渲染，所以不需要计算尺寸或形状变化）
        /// </summary>
        public override void SetVerticesDirty()
        {
            // 留空：切断 UGUI 的网格重建流程
        }

        
        protected override void OnPopulateMesh(VertexHelper vh)
        {
            // 清空顶点助手，确保生成 0 个顶点和 0 个三角形
            vh.Clear();
        }
    }
}
