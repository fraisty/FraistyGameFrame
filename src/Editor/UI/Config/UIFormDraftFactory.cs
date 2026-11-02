using System;
using UnityEngine;

namespace FRFrame.Editor
{
    /// <summary>
    /// 负责创建新的 draft 行数据，避免窗口里到处手写默认值。
    /// </summary>
    public static class UIFormDraftFactory
    {
        /// <summary>
        /// 创建一个空的 UIFormConfig 默认配置。
        /// </summary>
        public static UIFormConfig CreateDefaultFormConfig(string groupName)
        {
            return new UIFormConfig
            {
                formId = string.Empty,
                logicType = string.Empty,
                assetPath = string.Empty,
                configGroup = string.IsNullOrWhiteSpace(groupName) ? "Default" : groupName,
                packageName = string.Empty,
                layer = UIGroupLayer.Normal,
                allowMultiInstance = true,
                fullScreen = false,
                blockingMode = UIBlockingMode.FullScreenOnly,
                disableGameObjectWhenHidden = false,
                pauseCovered = true,
                closeMode = UIFormCloseMode.Cache,
                coverVisualMode = UICoverVisualMode.HideByCanvasGroup,
                resident = false,
                bringToFrontOnOpen = true,
                allowBackClose = false,
                useMask = false,
                closeOnMaskClick = false,
                maskMode = UIMaskMode.None,
                maskColor = new Color(0f, 0f, 0f, 0.6f),
                openAnimation = UIAnimationPreset.None,
                closeAnimation = UIAnimationPreset.None,
                openAnimationDuration = 0.2f,
                closeAnimationDuration = 0.15f,
                openAnimationEase = UIAnimationEase.OutQuad,
                closeAnimationEase = UIAnimationEase.InQuad
            };
        }

        /// <summary>
        /// 基于数据创建一个新的 draft item。
        /// </summary>
        public static UIFormDraftItem CreateDraftItem(UIFormConfig config, bool isNew = false, bool isModified = false)
        {
            return new UIFormDraftItem(config, isNew, isModified);
        }

        /// <summary>
        /// 创建一个空白 draft item。
        /// </summary>
        public static UIFormDraftItem CreateEmptyDraftItem(string groupName)
        {
            UIFormConfig config = CreateDefaultFormConfig(groupName);
            return new UIFormDraftItem(config, true, true);
        }
    }
}
