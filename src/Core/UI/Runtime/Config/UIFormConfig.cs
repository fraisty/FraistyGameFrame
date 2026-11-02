using System;
using UnityEngine;

namespace FRFrame
{
    /// <summary>
    /// 单个UI窗口配置。
    /// </summary>
    [Serializable]
    public sealed class UIFormConfig
    {
        [Tooltip("窗口唯一ID，建议使用逻辑类名，如 LoginForm")]
        public string formId;

        [Tooltip("逻辑类全名，支持热更类型查找，如 Game.UI.LoginForm")]
        public string logicType;

        [Tooltip("UI资源路径，由 ResourceManager 直接加载；为空时将按 logicType 创建纯代码窗口")]
        public string assetPath;

        [Tooltip("配置所属组名，用于分组加载配置")]
        public string configGroup = "Default";

        [Tooltip("资源所属 package；为空时使用 UIConfigGroup.defaultPackageName，再回退到 configGroup")]
        public string packageName;

        [Tooltip("窗口所在UI层")]
        public UIGroupLayer layer = UIGroupLayer.Normal;

        [Tooltip("是否允许多实例")]
        public bool allowMultiInstance = true;

        [Tooltip("是否为全屏窗口")]
        public bool fullScreen = false;

        [Tooltip("窗口遮挡底层 UI 的规则")]
        public UIBlockingMode blockingMode = UIBlockingMode.FullScreenOnly;

        [Tooltip("隐藏时是否禁用 GameObject")]
        public bool disableGameObjectWhenHidden = false;

        [Tooltip("被更上层全屏窗口覆盖时是否暂停")]
        public bool pauseCovered = true;

        [Tooltip("关闭时策略：销毁或缓存")]
        public UIFormCloseMode closeMode = UIFormCloseMode.Cache;

        [Tooltip("被遮挡时的可见策略")]
        public UICoverVisualMode coverVisualMode = UICoverVisualMode.HideByCanvasGroup;

        [Tooltip("是否常驻，不允许普通Close销毁，仅隐藏")]
        public bool resident = false;

        [Tooltip("是否在打开时自动置顶同组窗口")]
        public bool bringToFrontOnOpen = true;

        [Tooltip("是否允许返回键/ESC关闭")]
        public bool allowBackClose = false;

        [Header("Mask")]
        [Tooltip("是否启用弹窗遮罩")]
        public bool useMask = false;

        [Tooltip("点击遮罩是否关闭窗口")]
        public bool closeOnMaskClick = false;

        [Tooltip("遮罩显示模式")]
        public UIMaskMode maskMode = UIMaskMode.None;

        [Tooltip("自定义遮罩颜色；模糊遮罩时作为叠加颜色")]
        public Color maskColor = new Color(0f, 0f, 0f, 0.6f);

        [Header("Animation")]
        [Tooltip("打开动画预设")]
        public UIAnimationPreset openAnimation = UIAnimationPreset.None;

        [Tooltip("关闭动画预设")]
        public UIAnimationPreset closeAnimation = UIAnimationPreset.None;

        [Tooltip("打开动画时长")]
        public float openAnimationDuration = 0.2f;

        [Tooltip("关闭动画时长")]
        public float closeAnimationDuration = 0.15f;

        [Tooltip("打开动画缓动")]
        public UIAnimationEase openAnimationEase = UIAnimationEase.OutQuad;

        [Tooltip("关闭动画缓动")]
        public UIAnimationEase closeAnimationEase = UIAnimationEase.InQuad;
    }
}
