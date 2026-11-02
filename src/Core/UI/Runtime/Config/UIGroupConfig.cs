using System;
using UnityEngine;

namespace FRFrame
{
    /// <summary>
    /// UI层组配置。
    /// </summary>
    [Serializable]
    public sealed class UIGroupConfig
    {
        public UIGroupLayer layer;
        public string displayName;
        public int sortingOrderBase;
    }
}
