using System.Collections.Generic;
using UnityEngine;

namespace FRFrame
{
    /// <summary>
    /// UI配置组。
    /// 支持默认组初始化加载，其它组按需加载。
    /// </summary>
    [CreateAssetMenu(menuName = "FRFrame/UI/UIConfigGroup", fileName = "UIConfigGroup_Default")]
    public sealed class UIConfigGroup : ScriptableObject
    {
        [Tooltip("配置组名，如 Default/Battle/Lobby")]
        public string groupName = "Default";

        [Tooltip("是否作为UI框架初始化时自动加载的默认组")]
        public bool loadOnInitialize = true;

        [Tooltip("该组 UI 资源默认 package；单个 UIFormConfig.packageName 可覆盖")]
        public string defaultPackageName;

        [Tooltip("该组下所有UI配置")]
        public List<UIFormConfig> forms = new List<UIFormConfig>();
    }
}
