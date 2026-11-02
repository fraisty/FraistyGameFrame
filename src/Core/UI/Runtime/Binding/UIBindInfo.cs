using System;
using UnityEngine;

namespace FRFrame
{
    /// <summary>
    /// 单个绑定信息。
    /// </summary>
    [Serializable]
    public sealed class UIBindInfo
    {
        public string fieldName;
        public string typeName;
        public string path;
        public GameObject target;
        public Component component;
        public bool isWidget;
        public string widgetTypeName;
    }
}
