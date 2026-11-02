using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

namespace FRFrame.Editor
{
    /// <summary>
    /// Draft 校验器。
    /// 
    /// 注意：
    /// 这里校验的是 Draft，而不是资产对象本身。
    /// 这样可以在“保存前”就知道当前编辑状态是否有效。
    /// </summary>
    public static class UIConfigGroupDraftValidator
    {
        public enum ValidationSeverity
        {
            Info,
            Warning,
            Error
        }

        [Serializable]
        public sealed class ValidationMessage
        {
            public ValidationSeverity Severity;
            public string Message;
            public int FormIndex = -1;
            public string RowId;
            public string FormId;
            public string LogicType;
            public string AssetPath;
        }

        /// <summary>
        /// 校验整个 Draft。
        /// </summary>
        public static List<ValidationMessage> Validate(UIConfigGroupDraft draft)
        {
            List<ValidationMessage> messages = new List<ValidationMessage>();

            if (draft == null)
            {
                messages.Add(new ValidationMessage
                {
                    Severity = ValidationSeverity.Error,
                    Message = "Draft 为空。"
                });
                return messages;
            }

            if (draft.forms == null)
            {
                messages.Add(new ValidationMessage
                {
                    Severity = ValidationSeverity.Warning,
                    Message = "forms 列表为 null。"
                });
                return messages;
            }

            Dictionary<string, int> formIdMap = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            Dictionary<string, int> logicMap = new Dictionary<string, int>(StringComparer.Ordinal);

            for (int i = 0; i < draft.forms.Count; i++)
            {
                UIFormDraftItem item = draft.forms[i];
                if (item == null || item.data == null)
                {
                    messages.Add(new ValidationMessage
                    {
                        Severity = ValidationSeverity.Error,
                        FormIndex = i,
                        RowId = item != null ? item.rowId : null,
                        Message = $"第 {i} 项为空。"
                    });
                    continue;
                }

                UIFormConfig cfg = item.data;

                if (string.IsNullOrWhiteSpace(cfg.formId))
                {
                    messages.Add(new ValidationMessage
                    {
                        Severity = ValidationSeverity.Error,
                        FormIndex = i,
                        RowId = item.rowId,
                        FormId = cfg.formId,
                        LogicType = cfg.logicType,
                        AssetPath = cfg.assetPath,
                        Message = $"第 {i} 项 formId 为空。"
                    });
                }
                else if (formIdMap.TryGetValue(cfg.formId, out int existedIndex))
                {
                    messages.Add(new ValidationMessage
                    {
                        Severity = ValidationSeverity.Error,
                        FormIndex = i,
                        RowId = item.rowId,
                        FormId = cfg.formId,
                        LogicType = cfg.logicType,
                        AssetPath = cfg.assetPath,
                        Message = $"重复 formId: {cfg.formId}，与第 {existedIndex} 项重复。"
                    });
                }
                else
                {
                    formIdMap.Add(cfg.formId, i);
                }

                if (string.IsNullOrWhiteSpace(cfg.logicType))
                {
                    messages.Add(new ValidationMessage
                    {
                        Severity = ValidationSeverity.Warning,
                        FormIndex = i,
                        RowId = item.rowId,
                        FormId = cfg.formId,
                        LogicType = cfg.logicType,
                        AssetPath = cfg.assetPath,
                        Message = $"{SafeFormId(cfg)} 的 logicType 为空。"
                    });
                }
                else
                {
                    Type type = FindTypeByName(cfg.logicType);
                    if (type == null)
                    {
                        messages.Add(new ValidationMessage
                        {
                            Severity = ValidationSeverity.Error,
                            FormIndex = i,
                            RowId = item.rowId,
                            FormId = cfg.formId,
                            LogicType = cfg.logicType,
                            AssetPath = cfg.assetPath,
                            Message = $"{SafeFormId(cfg)} 的 logicType 不存在: {cfg.logicType}"
                        });
                    }
                    else
                    {
                        bool validUIType =
                            typeof(UIForm).IsAssignableFrom(type) ||
                            typeof(UIWidget).IsAssignableFrom(type) ||
                            typeof(UIItem).IsAssignableFrom(type);

                        if (!validUIType)
                        {
                            messages.Add(new ValidationMessage
                            {
                                Severity = ValidationSeverity.Warning,
                                FormIndex = i,
                                RowId = item.rowId,
                                FormId = cfg.formId,
                                LogicType = cfg.logicType,
                                AssetPath = cfg.assetPath,
                                Message = $"{SafeFormId(cfg)} 的 logicType 不是 UIForm/UIWidget/UIItem 派生类: {type.FullName}"
                            });
                        }
                    }

                    if (logicMap.TryGetValue(cfg.logicType, out int logicIndex))
                    {
                        messages.Add(new ValidationMessage
                        {
                            Severity = ValidationSeverity.Warning,
                            FormIndex = i,
                            RowId = item.rowId,
                            FormId = cfg.formId,
                            LogicType = cfg.logicType,
                            AssetPath = cfg.assetPath,
                            Message = $"{SafeFormId(cfg)} 的 logicType 与第 {logicIndex} 项重复: {cfg.logicType}"
                        });
                    }
                    else
                    {
                        logicMap.Add(cfg.logicType, i);
                    }
                }

                if (string.IsNullOrWhiteSpace(cfg.assetPath))
                {
                    messages.Add(new ValidationMessage
                    {
                        Severity = ValidationSeverity.Warning,
                        FormIndex = i,
                        RowId = item.rowId,
                        FormId = cfg.formId,
                        LogicType = cfg.logicType,
                        AssetPath = cfg.assetPath,
                        Message = $"{SafeFormId(cfg)} 的 assetPath 为空。"
                    });
                }
                else
                {
                    string assetPath = UIConfigEditorWindowHelper.TryLoadPrefabAtRuntimeStylePath(cfg.assetPath);
                    if (assetPath == null)
                    {
                        messages.Add(new ValidationMessage
                        {
                            Severity = ValidationSeverity.Error,
                            FormIndex = i,
                            RowId = item.rowId,
                            FormId = cfg.formId,
                            LogicType = cfg.logicType,
                            AssetPath = cfg.assetPath,
                            Message = $"{SafeFormId(cfg)} 的 prefab 不存在: {cfg.assetPath}"
                        });
                    }
                }

                if (string.IsNullOrWhiteSpace(cfg.configGroup))
                {
                    messages.Add(new ValidationMessage
                    {
                        Severity = ValidationSeverity.Warning,
                        FormIndex = i,
                        RowId = item.rowId,
                        FormId = cfg.formId,
                        LogicType = cfg.logicType,
                        AssetPath = cfg.assetPath,
                        Message = $"{SafeFormId(cfg)} 的 configGroup 为空。"
                    });
                }
                else if (!string.Equals(cfg.configGroup, draft.groupName, StringComparison.Ordinal))
                {
                    messages.Add(new ValidationMessage
                    {
                        Severity = ValidationSeverity.Warning,
                        FormIndex = i,
                        RowId = item.rowId,
                        FormId = cfg.formId,
                        LogicType = cfg.logicType,
                        AssetPath = cfg.assetPath,
                        Message = $"{SafeFormId(cfg)} 的 configGroup({cfg.configGroup}) 与 Group.groupName({draft.groupName}) 不一致。"
                    });
                }

                if (cfg.resident && cfg.closeMode == UIFormCloseMode.Destroy)
                {
                    messages.Add(new ValidationMessage
                    {
                        Severity = ValidationSeverity.Warning,
                        FormIndex = i,
                        RowId = item.rowId,
                        FormId = cfg.formId,
                        LogicType = cfg.logicType,
                        AssetPath = cfg.assetPath,
                        Message = $"{SafeFormId(cfg)} resident=true，但 closeMode=Destroy，语义可能冲突。"
                    });
                }

                if (cfg.fullScreen && cfg.allowMultiInstance)
                {
                    messages.Add(new ValidationMessage
                    {
                        Severity = ValidationSeverity.Warning,
                        FormIndex = i,
                        RowId = item.rowId,
                        FormId = cfg.formId,
                        LogicType = cfg.logicType,
                        AssetPath = cfg.assetPath,
                        Message = $"{SafeFormId(cfg)} 同时 fullScreen=true 且 allowMultiInstance=true，请确认是否合理。"
                    });
                }

                if (!cfg.fullScreen && cfg.pauseCovered)
                {
                    messages.Add(new ValidationMessage
                    {
                        Severity = ValidationSeverity.Info,
                        FormIndex = i,
                        RowId = item.rowId,
                        FormId = cfg.formId,
                        LogicType = cfg.logicType,
                        AssetPath = cfg.assetPath,
                        Message = $"{SafeFormId(cfg)} 非全屏但 pauseCovered=true，请确认这是否符合预期。"
                    });
                }

                if (cfg.coverVisualMode == UICoverVisualMode.SetInactive && cfg.resident)
                {
                    messages.Add(new ValidationMessage
                    {
                        Severity = ValidationSeverity.Warning,
                        FormIndex = i,
                        RowId = item.rowId,
                        FormId = cfg.formId,
                        LogicType = cfg.logicType,
                        AssetPath = cfg.assetPath,
                        Message = $"{SafeFormId(cfg)} resident=true 且 coverVisualMode=SetInactive，请确认隐藏恢复逻辑是否安全。"
                    });
                }

                if (cfg.blockingMode == UIBlockingMode.Always && cfg.coverVisualMode == UICoverVisualMode.KeepVisible)
                {
                    messages.Add(new ValidationMessage
                    {
                        Severity = ValidationSeverity.Info,
                        FormIndex = i,
                        RowId = item.rowId,
                        FormId = cfg.formId,
                        LogicType = cfg.logicType,
                        AssetPath = cfg.assetPath,
                        Message = $"{SafeFormId(cfg)} blockingMode=Always 但 coverVisualMode=KeepVisible，请确认底层 UI 是否只暂停不隐藏。"
                    });
                }

                if (cfg.disableGameObjectWhenHidden && cfg.coverVisualMode == UICoverVisualMode.SetInactive)
                {
                    messages.Add(new ValidationMessage
                    {
                        Severity = ValidationSeverity.Info,
                        FormIndex = i,
                        RowId = item.rowId,
                        FormId = cfg.formId,
                        LogicType = cfg.logicType,
                        AssetPath = cfg.assetPath,
                        Message = $"{SafeFormId(cfg)} 隐藏和遮挡都会 SetActive(false)，请确认 OnShow/OnReveal 能恢复状态。"
                    });
                }

                if (cfg.useMask && cfg.maskMode == UIMaskMode.None)
                {
                    messages.Add(new ValidationMessage
                    {
                        Severity = ValidationSeverity.Warning,
                        FormIndex = i,
                        RowId = item.rowId,
                        FormId = cfg.formId,
                        LogicType = cfg.logicType,
                        AssetPath = cfg.assetPath,
                        Message = $"{SafeFormId(cfg)} useMask=true，但 maskMode=None，运行时不会显示遮罩。"
                    });
                }

                if (!cfg.useMask && cfg.closeOnMaskClick)
                {
                    messages.Add(new ValidationMessage
                    {
                        Severity = ValidationSeverity.Info,
                        FormIndex = i,
                        RowId = item.rowId,
                        FormId = cfg.formId,
                        LogicType = cfg.logicType,
                        AssetPath = cfg.assetPath,
                        Message = $"{SafeFormId(cfg)} closeOnMaskClick=true，但 useMask=false，该配置不会生效。"
                    });
                }

                if (cfg.maskMode == UIMaskMode.CustomColor && cfg.maskColor.a <= 0f)
                {
                    messages.Add(new ValidationMessage
                    {
                        Severity = ValidationSeverity.Info,
                        FormIndex = i,
                        RowId = item.rowId,
                        FormId = cfg.formId,
                        LogicType = cfg.logicType,
                        AssetPath = cfg.assetPath,
                        Message = $"{SafeFormId(cfg)} 使用 CustomColor 遮罩但 alpha <= 0，遮罩不可见。"
                    });
                }

                if (cfg.openAnimation != UIAnimationPreset.None && cfg.openAnimationDuration <= 0f)
                {
                    messages.Add(new ValidationMessage
                    {
                        Severity = ValidationSeverity.Warning,
                        FormIndex = i,
                        RowId = item.rowId,
                        FormId = cfg.formId,
                        LogicType = cfg.logicType,
                        AssetPath = cfg.assetPath,
                        Message = $"{SafeFormId(cfg)} 配置了打开动画但 openAnimationDuration <= 0。"
                    });
                }

                if (cfg.closeAnimation != UIAnimationPreset.None && cfg.closeAnimationDuration <= 0f)
                {
                    messages.Add(new ValidationMessage
                    {
                        Severity = ValidationSeverity.Warning,
                        FormIndex = i,
                        RowId = item.rowId,
                        FormId = cfg.formId,
                        LogicType = cfg.logicType,
                        AssetPath = cfg.assetPath,
                        Message = $"{SafeFormId(cfg)} 配置了关闭动画但 closeAnimationDuration <= 0。"
                    });
                }
            }

            return messages;
        }

        private static string SafeFormId(UIFormConfig cfg)
        {
            if (cfg == null)
                return "(null)";
            return string.IsNullOrWhiteSpace(cfg.formId) ? "(UnnamedForm)" : cfg.formId;
        }

        private static Type FindTypeByName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            Type type = Type.GetType(typeName);
            if (type != null)
                return type;

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                try
                {
                    type = assembly.GetType(typeName);
                    if (type != null)
                        return type;
                }
                catch
                {
                }
            }

            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                Type[] types;
                try
                {
                    types = assembly.GetTypes();
                }
                catch
                {
                    continue;
                }

                foreach (Type t in types)
                {
                    if (t.Name == typeName)
                        return t;
                }
            }

            return null;
        }
    }
}