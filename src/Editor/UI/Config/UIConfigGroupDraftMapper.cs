using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Editor
{
    /// <summary>
    /// 负责资产对象 <-> Draft 对象之间的映射。
    /// </summary>
    public static class UIConfigGroupDraftMapper
    {
        /// <summary>
        /// 从真实 UIConfigGroup 资产创建 Draft。
        /// 这里会做深拷贝，避免编辑 Draft 时直接改到原始资产。
        /// </summary>
        public static UIConfigGroupDraft CreateDraft(UIConfigGroup source)
        {
            UIConfigGroupDraft draft = new UIConfigGroupDraft
            {
                sourceAsset = source,
                assetName = source != null ? source.name : string.Empty,
                assetPath = source != null ? AssetDatabase.GetAssetPath(source) : string.Empty,
                groupName = source != null ? source.groupName : string.Empty,
                loadOnInitialize = source != null && source.loadOnInitialize,
                defaultPackageName = source != null ? source.defaultPackageName : string.Empty,
                forms = new List<UIFormDraftItem>(),
                isDirty = false
            };

            if (source != null && source.forms != null)
            {
                foreach (UIFormConfig form in source.forms)
                {
                    UIFormConfig copy = CloneFormConfig(form);
                    draft.forms.Add(UIFormDraftFactory.CreateDraftItem(copy, false, false));
                }
            }

            return draft;
        }

        /// <summary>
        /// 将 Draft 内容回写到真实资产。
        /// 采用整表覆盖方式，逻辑简单且一致性更高。
        /// </summary>
        public static void ApplyDraftToAsset(UIConfigGroupDraft draft)
        {
            if (draft == null || draft.sourceAsset == null)
                return;

            UIConfigGroup asset = draft.sourceAsset;

            Undo.RecordObject(asset, "Apply UIConfigGroup Draft");

            asset.groupName = draft.groupName;
            asset.loadOnInitialize = draft.loadOnInitialize;
            asset.defaultPackageName = draft.defaultPackageName;

            if (asset.forms == null)
            {
                asset.forms = new List<UIFormConfig>();
            }
            else
            {
                asset.forms.Clear();
            }

            foreach (UIFormDraftItem item in draft.forms)
            {
                if (item == null || item.data == null)
                    continue;

                asset.forms.Add(CloneFormConfig(item.data));
            }

            EditorUtility.SetDirty(asset);
            AssetDatabase.SaveAssets();
        }

        /// <summary>
        /// 深拷贝 UIFormConfig。
        /// </summary>
        public static UIFormConfig CloneFormConfig(UIFormConfig src)
        {
            if (src == null)
            {
                return UIFormDraftFactory.CreateDefaultFormConfig("Default");
            }

            return new UIFormConfig
            {
                formId = src.formId,
                logicType = src.logicType,
                assetPath = src.assetPath,
                configGroup = src.configGroup,
                packageName = src.packageName,
                layer = src.layer,
                allowMultiInstance = src.allowMultiInstance,
                fullScreen = src.fullScreen,
                blockingMode = src.blockingMode,
                disableGameObjectWhenHidden = src.disableGameObjectWhenHidden,
                pauseCovered = src.pauseCovered,
                closeMode = src.closeMode,
                coverVisualMode = src.coverVisualMode,
                resident = src.resident,
                bringToFrontOnOpen = src.bringToFrontOnOpen,
                allowBackClose = src.allowBackClose,
                useMask = src.useMask,
                closeOnMaskClick = src.closeOnMaskClick,
                maskMode = src.maskMode,
                maskColor = src.maskColor,
                openAnimation = src.openAnimation,
                closeAnimation = src.closeAnimation,
                openAnimationDuration = src.openAnimationDuration,
                closeAnimationDuration = src.closeAnimationDuration,
                openAnimationEase = src.openAnimationEase,
                closeAnimationEase = src.closeAnimationEase
            };
        }

        /// <summary>
        /// 将适合在不同 Form 间复用的运行时参数复制到目标配置。
        /// formId、logicType 和 assetPath 是每个 Form 独有的标识/资源字段，刻意保留目标值。
        /// </summary>
        public static void CopyReusableFormSettings(UIFormConfig source, UIFormConfig target)
        {
            if (source == null || target == null)
                return;

            target.configGroup = source.configGroup;
            target.packageName = source.packageName;
            target.layer = source.layer;
            target.allowMultiInstance = source.allowMultiInstance;
            target.fullScreen = source.fullScreen;
            target.blockingMode = source.blockingMode;
            target.disableGameObjectWhenHidden = source.disableGameObjectWhenHidden;
            target.pauseCovered = source.pauseCovered;
            target.closeMode = source.closeMode;
            target.coverVisualMode = source.coverVisualMode;
            target.resident = source.resident;
            target.bringToFrontOnOpen = source.bringToFrontOnOpen;
            target.allowBackClose = source.allowBackClose;
            target.useMask = source.useMask;
            target.closeOnMaskClick = source.closeOnMaskClick;
            target.maskMode = source.maskMode;
            target.maskColor = source.maskColor;
            target.openAnimation = source.openAnimation;
            target.closeAnimation = source.closeAnimation;
            target.openAnimationDuration = source.openAnimationDuration;
            target.closeAnimationDuration = source.closeAnimationDuration;
            target.openAnimationEase = source.openAnimationEase;
            target.closeAnimationEase = source.closeAnimationEase;
        }

        /// <summary>
        /// 创建只包含可复用参数的配置副本，供编辑器参数剪贴板使用。
        /// </summary>
        public static UIFormConfig CloneReusableFormSettings(UIFormConfig source)
        {
            if (source == null)
                return null;

            UIFormConfig copy = UIFormDraftFactory.CreateDefaultFormConfig(source.configGroup);
            CopyReusableFormSettings(source, copy);
            return copy;
        }
    }
}
