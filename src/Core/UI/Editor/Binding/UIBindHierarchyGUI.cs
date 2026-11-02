#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Editor
{
    /// <summary>
    /// Hierarchy 面板上的 UI 绑定辅助绘制。
    /// 
    /// 功能：
    /// 1. 在带有 UIBindData 的根节点显示 B 标识；
    /// 2. 在可绑定节点后显示 [+] 添加绑定按钮；
    /// 3. 在已绑定节点后显示绑定状态按钮；
    /// 4. 点击绑定状态按钮后可 Ping Target、Ping Info、删除绑定、替换绑定类型。
    /// </summary>
    [InitializeOnLoad]
    public static class UIBindHierarchyGUI
    {
        private static readonly Color[] BindDataColors =
        {
            new Color(0.95f, 0.58f, 0.18f),
            new Color(0.25f, 0.62f, 0.95f),
            new Color(0.62f, 0.42f, 0.95f),
            new Color(0.25f, 0.72f, 0.42f),
            new Color(0.9f, 0.35f, 0.58f),
            new Color(0.82f, 0.72f, 0.2f),
        };

        /// <summary>
        /// 每个 UIBindData 根节点对应一个颜色索引，用于在 Hierarchy 里区分不同绑定根。
        /// </summary>
        private static readonly Dictionary<int, int> BindDataColorIndex = new Dictionary<int, int>();

        private static int _nextColorIndex;

        static UIBindHierarchyGUI()
        {
            // 域重载（脚本重编译）后类型可能变化，清一次 FindType 缓存，避免缓存陈旧。
            UIBindEditorUtility.ClearTypeCache();

            EditorApplication.hierarchyWindowItemOnGUI += OnHierarchyGUI;
            ObjectFactory.componentWasAdded += OnComponentAdded;
            EditorApplication.hierarchyChanged += OnHierarchyChanged;
        }

        /// <summary>
        /// Hierarchy 变化时清理已经失效的颜色缓存。
        /// </summary>
        private static void OnHierarchyChanged()
        {
            List<int> removed = null;
            foreach (var pair in BindDataColorIndex)
            {
                if (EditorUtility.InstanceIDToObject(pair.Key) == null)
                {
                    removed ??= new List<int>();
                    removed.Add(pair.Key);
                }
            }

            if (removed == null)
                return;

            for (int i = 0; i < removed.Count; i++)
            {
                BindDataColorIndex.Remove(removed[i]);
            }
        }

        /// <summary>
        /// 当新增 UIBindData 组件时，如果它位于另一个 UIBindData 的子节点下，
        /// 则把父级绑定数据中属于该新根节点子树的绑定迁移到新 UIBindData 上。
        /// </summary>
        private static void OnComponentAdded(Component component)
        {
            if (component is not UIBindData newBindData)
                return;

            UIBindData parentBindData = FindOwnerBindData(component.transform.parent);
            if (parentBindData == null)
                return;

            List<UIBindInfo> parentInfos = new List<UIBindInfo>(parentBindData.BindInfos);
            List<UIBindInfo> newInfos = new List<UIBindInfo>(newBindData.BindInfos);

            int movedCount = 0;
            for (int i = parentInfos.Count - 1; i >= 0; i--)
            {
                UIBindInfo info = parentInfos[i];
                if (info == null || info.target == null)
                    continue;

                if (!IsChildOf(info.target.transform, newBindData.transform))
                    continue;

                parentInfos.RemoveAt(i);
                info.path = UIBindEditorUtility.GetTransformPath(newBindData.transform, info.target.transform);
                newInfos.Add(info);
                movedCount++;
            }

            if (movedCount <= 0)
                return;

            Undo.RecordObject(parentBindData, "Move UI Bind Infos");
            Undo.RecordObject(newBindData, "Move UI Bind Infos");

            parentBindData.SetBindInfos(parentInfos);
            newBindData.SetBindInfos(newInfos);

            EditorUtility.SetDirty(parentBindData);
            EditorUtility.SetDirty(newBindData);

            Debug.Log($"[UIBind] 已将 {movedCount} 个绑定从 {parentBindData.name} 移动到 {newBindData.name}");
        }

        /// <summary>
        /// Hierarchy 每一行的 GUI 绘制入口。
        /// </summary>
        private static void OnHierarchyGUI(int instanceId, Rect selectionRect)
        {
            GameObject go = EditorUtility.InstanceIDToObject(instanceId) as GameObject;
            if (go == null)
                return;

            UIBindData selfBindData = go.GetComponent<UIBindData>();
            UIBindData ownerBindData = FindOwnerBindData(go.transform);
            if (selfBindData == null && ownerBindData == null)
                return;

            float buttonWidth = 22f;
            float spacing = 2f;
            float x = selectionRect.xMax - 5f - buttonWidth;

            Color oldBackground = GUI.backgroundColor;
            Color oldContent = GUI.contentColor;

            // 当前节点本身就是绑定根节点时，显示 B 标识。
            if (selfBindData != null)
            {
                DrawBindRootMark(selectionRect, selfBindData, ref x, spacing);
            }

            // 当前节点位于某个 UIBindData 根节点下时，显示添加和绑定状态按钮。
            if (ownerBindData != null)
            {
                Color bindColor = GetBindDataColor(ownerBindData);
                GUI.backgroundColor = bindColor;

                Rect addRect = new Rect(x, selectionRect.y, buttonWidth, selectionRect.height);
                if (GUI.Button(addRect, "+", EditorStyles.miniButton))
                {
                    ShowAddComponentMenu(ownerBindData, go);
                }

                x -= buttonWidth + spacing;
                GUI.backgroundColor = oldBackground;

                DrawBindStateButton(selectionRect, ownerBindData, go, bindColor, ref x, spacing);
            }

            GUI.backgroundColor = oldBackground;
            GUI.contentColor = oldContent;
        }

        /// <summary>
        /// 绘制 UIBindData 根节点的 B 标识。
        /// </summary>
        private static void DrawBindRootMark(Rect selectionRect, UIBindData bindData, ref float x, float spacing)
        {
            Color color = GetBindDataColor(bindData);
            Rect rect = new Rect(x, selectionRect.y, 22f, selectionRect.height);

            Color oldColor = GUI.contentColor;
            GUI.contentColor = color;

            GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                fontSize = 11
            };

            GUI.Label(rect, "B", style);

            GUI.contentColor = oldColor;
            x -= 22f + spacing;
        }

        /// <summary>
        /// 绘制已绑定状态按钮。
        /// 
        /// 单个绑定时显示组件类型名；
        /// 多个绑定时显示 Bind 数量。
        /// </summary>
        private static void DrawBindStateButton(Rect selectionRect, UIBindData bindData, GameObject go, Color color, ref float x, float spacing)
        {
            List<UIBindInfo> infos = UIBindEditorUtility.GetBindInfosOnTarget(bindData, go);
            if (infos.Count == 0)
                return;

            string label;
            float width;

            if (infos.Count == 1)
            {
                label = GetBindInfoDisplayName(infos[0]);
                width = Mathf.Clamp(GUI.skin.button.CalcSize(new GUIContent(label)).x + 8f, 38f, 82f);
            }
            else
            {
                label = "Bind " + infos.Count;
                width = 56f;
            }

            GUI.backgroundColor = GetBindStateColor(infos, color);

            Rect rect = new Rect(x - width + 22f, selectionRect.y, width, selectionRect.height);
            if (GUI.Button(rect, label, EditorStyles.miniButton))
            {
                ShowBindStateMenu(bindData, go, infos);
            }

            x -= width + spacing;
        }

        /// <summary>
        /// 根据绑定类型决定状态按钮颜色。
        /// </summary>
        private static Color GetBindStateColor(List<UIBindInfo> infos, Color ownerColor)
        {
            if (infos == null || infos.Count == 0)
                return ownerColor;

            if (infos.Count > 1)
                return new Color(0.78f, 0.5f, 0.16f);

            UIBindInfo info = infos[0];
            if (info == null)
                return ownerColor;

            if (info.isWidget)
                return new Color(0.62f, 0.42f, 0.95f);

            Type type = UIBindEditorUtility.FindType(info.typeName);
            return GetTypeColor(type, ownerColor);
        }

        private static Color GetTypeColor(Type type, Color fallback)
        {
            if (type == typeof(GameObject)) return new Color(0.35f, 0.55f, 0.9f);
            if (type == typeof(RectTransform) || type == typeof(Transform)) return new Color(0.45f, 0.65f, 0.75f);
            if (type == typeof(UnityEngine.UI.Button)) return new Color(0.25f, 0.55f, 0.95f);
            if (type == typeof(UnityEngine.UI.Image) || type == typeof(UnityEngine.UI.RawImage)) return new Color(0.25f, 0.7f, 0.45f);
            if (type == typeof(TMPro.TextMeshProUGUI) || type == typeof(UnityEngine.UI.Text)) return new Color(0.6f, 0.45f, 0.95f);
            if (type == typeof(TMPro.TMP_InputField) || type == typeof(UnityEngine.UI.InputField)) return new Color(0.75f, 0.45f, 0.65f);
            if (type == typeof(UnityEngine.UI.ScrollRect) || type == typeof(UnityEngine.UI.Scrollbar)) return new Color(0.45f, 0.65f, 0.9f);
            if (type == typeof(UnityEngine.UI.Toggle) || type == typeof(UnityEngine.UI.Slider)) return new Color(0.9f, 0.6f, 0.25f);
            if (type == typeof(CanvasGroup)) return new Color(0.45f, 0.7f, 0.7f);

            return fallback;
        }

        /// <summary>
        /// 显示添加绑定菜单。
        /// </summary>
        private static void ShowAddComponentMenu(UIBindData bindData, GameObject go)
        {
            GenericMenu menu = new GenericMenu();
            List<Type> types = UIBindEditorUtility.GetBindableTypes(go);

            bool hasAvailable = false;
            for (int i = 0; i < types.Count; i++)
            {
                Type type = types[i];
                bool alreadyBound = UIBindEditorUtility.IsAlreadyBound(bindData, go, type);
                string label = UIBindEditorUtility.GetCompactTypeName(type);

                if (alreadyBound)
                {
                    menu.AddDisabledItem(new GUIContent(label + " (已绑定)"));
                    continue;
                }

                hasAvailable = true;
                Type capturedType = type;
                menu.AddItem(new GUIContent(label), false, () =>
                {
                    UIBindDataEditor.AddBinding(bindData, go, capturedType);
                });
            }

            if (types.Count > 0)
                menu.AddSeparator("");

            menu.AddItem(new GUIContent("Bind All On This Node"), false, () =>
            {
                UIBindDataEditor.AddAllBindingsOnTarget(bindData, go);
            });

            menu.AddItem(new GUIContent("Bind All In Children"), false, () =>
            {
                UIBindDataEditor.AddAllBindingsInChildren(bindData, go);
            });

            if (!hasAvailable && types.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("没有可绑定组件"));
            }

            menu.ShowAsContext();
        }

        /// <summary>
        /// 显示已绑定状态菜单。
        /// 
        /// 每个已绑定组件下提供：
        /// 1. Ping Target：选中并 Ping 实际绑定目标节点；
        /// 2. Ping Info：选中绑定根节点，并在 UIBindData Inspector 中高亮对应 BindInfo；
        /// 3. Remove Binding：删除该绑定；
        /// 4. Replace With：替换绑定类型。
        /// </summary>
        private static void ShowBindStateMenu(UIBindData bindData, GameObject go, List<UIBindInfo> infos)
        {
            GenericMenu menu = new GenericMenu();

            for (int i = 0; i < infos.Count; i++)
            {
                UIBindInfo info = infos[i];
                if (info == null)
                    continue;

                string displayName = GetBindInfoDisplayName(info);
                UIBindInfo capturedInfo = info;

                menu.AddItem(new GUIContent(displayName + "/Ping Target"), false, () =>
                {
                    if (capturedInfo.target != null)
                    {
                        EditorGUIUtility.PingObject(capturedInfo.target);
                        Selection.activeGameObject = capturedInfo.target;
                    }
                });

                menu.AddItem(new GUIContent(displayName + "/Ping Info"), false, () =>
                {
                    /*
                     * 新增功能：
                     * 点击后跳转到绑定根节点，并在 UIBindData 的 Bind Infos 列表中高亮对应项。
                     */
                    UIBindDataEditor.PingBindInfo(bindData, capturedInfo);
                });

                menu.AddItem(new GUIContent(displayName + "/Remove Binding"), false, () =>
                {
                    UIBindDataEditor.RemoveBinding(bindData, capturedInfo);
                });

                AddReplaceMenus(menu, bindData, go, capturedInfo, displayName);
            }

            menu.AddSeparator("");

            menu.AddItem(new GUIContent("Remove All On This Node"), false, () =>
            {
                UIBindDataEditor.RemoveBindingsOnTarget(bindData, go);
            });

            menu.ShowAsContext();
        }

        /// <summary>
        /// 添加替换绑定类型菜单。
        /// </summary>
        private static void AddReplaceMenus(GenericMenu menu, UIBindData bindData, GameObject go, UIBindInfo info, string displayName)
        {
            if (info == null || info.isWidget)
                return;

            List<Type> types = UIBindEditorUtility.GetBindableTypes(go);
            Type currentType = UIBindEditorUtility.FindType(info.typeName);

            for (int i = 0; i < types.Count; i++)
            {
                Type type = types[i];
                if (type == currentType)
                    continue;

                if (UIBindEditorUtility.IsAlreadyBound(bindData, go, type))
                    continue;

                Type capturedType = type;
                menu.AddItem(new GUIContent(displayName + "/Replace With/" + UIBindEditorUtility.GetCompactTypeName(type)), false, () =>
                {
                    UIBindDataEditor.ReplaceBinding(bindData, info, capturedType);
                });
            }
        }

        /// <summary>
        /// 获取绑定信息在菜单和按钮上的显示名称。
        /// </summary>
        private static string GetBindInfoDisplayName(UIBindInfo info)
        {
            if (info == null)
                return "Missing";

            if (info.isWidget)
            {
                Type widgetType = UIBindEditorUtility.FindType(info.widgetTypeName);
                return widgetType != null ? widgetType.Name : "Widget";
            }

            Type type = UIBindEditorUtility.FindType(info.typeName);
            return UIBindEditorUtility.GetCompactTypeName(type);
        }

        /// <summary>
        /// 从当前节点向父级查找最近的 UIBindData。
        /// </summary>
        private static UIBindData FindOwnerBindData(Transform current)
        {
            while (current != null)
            {
                UIBindData bindData = current.GetComponent<UIBindData>();
                if (bindData != null)
                    return bindData;

                current = current.parent;
            }

            return null;
        }

        /// <summary>
        /// 判断 target 是否位于 parent 子树下。
        /// </summary>
        private static bool IsChildOf(Transform target, Transform parent)
        {
            Transform current = target;
            while (current != null)
            {
                if (current == parent)
                    return true;

                current = current.parent;
            }

            return false;
        }

        /// <summary>
        /// 获取某个 UIBindData 根节点在 Hierarchy 中使用的颜色。
        /// </summary>
        private static Color GetBindDataColor(UIBindData bindData)
        {
            int instanceId = bindData.GetInstanceID();
            if (!BindDataColorIndex.TryGetValue(instanceId, out int index))
            {
                index = _nextColorIndex % BindDataColors.Length;
                BindDataColorIndex.Add(instanceId, index);
                _nextColorIndex++;
            }

            return BindDataColors[index];
        }
    }
}
#endif