#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Editor
{
    [CustomEditor(typeof(UIBindData))]
    public sealed class UIBindDataEditor : UnityEditor.Editor
    {
        private UIBindData _bindData;
        private UIBindScriptHolder _scriptHolder;

        #region Inspector 显示设置

        /// <summary>
        /// Bind Infos 的分组方式。
        /// </summary>
        private enum BindGroupMode
        {
            None = 0,
            PathArea = 1,
            Type = 2,
            WidgetOrNormal = 3
        }

        /// <summary>
        /// Bind Infos 的显示过滤方式。
        /// </summary>
        private enum BindDisplayFilter
        {
            All = 0,
            ErrorOnly = 1
        }

        /// <summary>
        /// 当前 Inspector 搜索关键字。
        /// </summary>
        private string _searchText = string.Empty;

        /// <summary>
        /// 当前分组模式。
        /// 默认按路径区域分组，比较符合 UI 预制体层级习惯。
        /// </summary>
        private BindGroupMode _groupMode = BindGroupMode.PathArea;

        /// <summary>
        /// 当前过滤模式。
        /// </summary>
        private BindDisplayFilter _displayFilter = BindDisplayFilter.All;

        /// <summary>
        /// 分组折叠状态缓存。
        /// 
        /// key 包含 UIBindData InstanceID + 分组模式 + 分组名，
        /// 避免不同预制体之间折叠状态互相影响。
        /// </summary>
        private static readonly Dictionary<string, bool> GroupFoldoutStates = new Dictionary<string, bool>();

        /// <summary>
        /// 单条 BindInfo 折叠状态缓存。
        /// 
        /// key 包含 UIBindData InstanceID + BindInfo 稳定 key。
        /// </summary>
        private static readonly Dictionary<string, bool> ItemFoldoutStates = new Dictionary<string, bool>();

        /// <summary>
        /// Inspector 中用于绘制的一条绑定视图数据。
        /// 
        /// 注意：
        /// 这里不直接承担数据编辑，只用于分组、过滤、显示状态。
        /// 真正编辑仍然通过 SerializedProperty 完成。
        /// </summary>
        private sealed class BindInfoView
        {
            public int index;
            public string groupName;
            public UIBindInfo info;
            public bool hasError;
            public string errorText;
        }

        #endregion

        #region Ping Bind Info 定位功能

        /// <summary>
        /// 当前需要在 Inspector 中定位的 UIBindData 实例 ID。
        /// </summary>
        private static int _pendingPingBindDataInstanceId;

        /// <summary>
        /// 当前需要定位的 BindInfo 在列表里的索引。
        /// 索引用于优先快速匹配，key 用于兜底。
        /// </summary>
        private static int _pendingPingBindInfoIndex = -1;

        /// <summary>
        /// 当前需要定位的 BindInfo 稳定标识。
        /// </summary>
        private static string _pendingPingBindInfoKey;

        /// <summary>
        /// 从 Hierarchy 的绑定菜单跳转到 UIBindData Inspector 中对应的 BindInfo。
        /// </summary>
        public static void PingBindInfo(UIBindData bindData, UIBindInfo info)
        {
            if (bindData == null || info == null)
                return;

            int bindDataInstanceId = bindData.GetInstanceID();

            _pendingPingBindDataInstanceId = bindDataInstanceId;
            _pendingPingBindInfoIndex = FindBindInfoIndex(bindData, info);
            _pendingPingBindInfoKey = MakeBindInfoKey(info);

            /*
             * 必须延迟一帧切换 Selection。
             * 
             * GenericMenu 回调发生在当前 GUI 事件中，
             * 如果当帧直接切 Selection，有概率打断 Inspector 的 GUILayout 状态，
             * 导致 Bind Infos 不显示或 Inspector 空白。
             */
            EditorApplication.delayCall += () =>
            {
                UIBindData targetBindData = EditorUtility.InstanceIDToObject(bindDataInstanceId) as UIBindData;
                if (targetBindData == null)
                    return;

                Selection.activeGameObject = targetBindData.gameObject;
                EditorGUIUtility.PingObject(targetBindData.gameObject);

                RepaintEditorViews();
            };
        }

        /// <summary>
        /// 查找 BindInfo 在 UIBindData.BindInfos 中的索引。
        /// </summary>
        private static int FindBindInfoIndex(UIBindData bindData, UIBindInfo info)
        {
            if (bindData == null || info == null)
                return -1;

            IReadOnlyList<UIBindInfo> infos = bindData.BindInfos;
            if (infos == null)
                return -1;

            for (int i = 0; i < infos.Count; i++)
            {
                if (ReferenceEquals(infos[i], info))
                    return i;
            }

            string key = MakeBindInfoKey(info);
            if (string.IsNullOrEmpty(key))
                return -1;

            for (int i = 0; i < infos.Count; i++)
            {
                if (MakeBindInfoKey(infos[i]) == key)
                    return i;
            }

            return -1;
        }

        /// <summary>
        /// 生成 BindInfo 稳定标识。
        /// 
        /// 不长期保存 UIBindInfo 引用，是为了避免 Inspector 刷新、序列化重建后引用不稳定。
        /// </summary>
        private static string MakeBindInfoKey(UIBindInfo info)
        {
            if (info == null)
                return string.Empty;

            int targetId = info.target != null ? info.target.GetInstanceID() : 0;

            return string.Format(
                "{0}|{1}|{2}|{3}|{4}|{5}",
                targetId,
                info.fieldName ?? string.Empty,
                info.typeName ?? string.Empty,
                info.path ?? string.Empty,
                info.isWidget,
                info.widgetTypeName ?? string.Empty
            );
        }

        /// <summary>
        /// 判断指定 index 的 BindInfo 是否是当前 Ping Info 定位目标。
        /// </summary>
        private bool IsPingTarget(int index)
        {
            if (_bindData == null)
                return false;

            if (_pendingPingBindDataInstanceId == 0)
                return false;

            if (_bindData.GetInstanceID() != _pendingPingBindDataInstanceId)
                return false;

            if (string.IsNullOrEmpty(_pendingPingBindInfoKey))
                return false;

            IReadOnlyList<UIBindInfo> infos = _bindData.BindInfos;
            if (infos == null || index < 0 || index >= infos.Count)
                return false;

            UIBindInfo info = infos[index];
            if (info == null)
                return false;

            string currentKey = MakeBindInfoKey(info);

            if (_pendingPingBindInfoIndex == index && currentKey == _pendingPingBindInfoKey)
                return true;

            return currentKey == _pendingPingBindInfoKey;
        }

        /// <summary>
        /// 清除当前 Ping Info 定位状态。
        /// </summary>
        private static void ClearPingBindInfo()
        {
            _pendingPingBindDataInstanceId = 0;
            _pendingPingBindInfoIndex = -1;
            _pendingPingBindInfoKey = null;

            RepaintEditorViews();
        }

        /// <summary>
        /// 如果删除的 BindInfo 正好是当前定位目标，则清理定位状态。
        /// </summary>
        private static void ClearPingBindInfoIfMatch(UIBindData bindData, UIBindInfo info)
        {
            if (bindData == null || info == null)
                return;

            if (_pendingPingBindDataInstanceId == 0)
                return;

            if (bindData.GetInstanceID() != _pendingPingBindDataInstanceId)
                return;

            string key = MakeBindInfoKey(info);
            if (key == _pendingPingBindInfoKey)
            {
                ClearPingBindInfo();
            }
        }

        #endregion

        private void OnEnable()
        {
            _bindData = target as UIBindData;
            if (_bindData != null)
            {
                _scriptHolder = _bindData.GetComponent<UIBindScriptHolder>();
            }
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            if (_bindData == null)
            {
                serializedObject.ApplyModifiedProperties();
                return;
            }

            DrawScriptSelectArea();
            EditorGUILayout.Space();

            DrawOperationButtons();
            EditorGUILayout.Space();

            DrawBindList();

            serializedObject.ApplyModifiedProperties();
        }

        #region 顶部脚本选择区域

        private void DrawScriptSelectArea()
        {
            EditorGUILayout.LabelField("Target Script", EditorStyles.boldLabel);

            if (_scriptHolder == null)
            {
                EditorGUILayout.HelpBox("当前节点缺少 UIBindScriptHolder。", MessageType.Info);

                if (GUILayout.Button("添加 Script Holder"))
                {
                    _scriptHolder = _bindData.gameObject.GetComponent<UIBindScriptHolder>();
                    if (_scriptHolder == null)
                    {
                        _scriptHolder = Undo.AddComponent<UIBindScriptHolder>(_bindData.gameObject);
                    }
                }

                return;
            }

            EditorGUI.BeginChangeCheck();
            MonoScript newScript = (MonoScript)EditorGUILayout.ObjectField("Target Script", _scriptHolder.targetScript, typeof(MonoScript), false);
            if (EditorGUI.EndChangeCheck())
            {
                Undo.RecordObject(_scriptHolder, "Set UI Target Script");
                _scriptHolder.targetScript = newScript;
                EditorUtility.SetDirty(_scriptHolder);
                SyncTargetScriptToRuntimeData();
            }

            if (GUILayout.Button("同步脚本信息到运行时数据"))
            {
                SyncTargetScriptToRuntimeData();
            }
        }

        private void SyncTargetScriptToRuntimeData()
        {
            if (_bindData == null || _scriptHolder == null || _scriptHolder.targetScript == null)
                return;

            Type type = _scriptHolder.targetScript.GetClass();
            if (type == null)
            {
                EditorUtility.DisplayDialog("错误", "所选脚本没有有效类型。", "确定");
                return;
            }

            MonoScriptReference scriptRef = new MonoScriptReference
            {
                className = type.Name,
                namespaceName = type.Namespace,
                assemblyName = type.Assembly.GetName().Name
            };

            Undo.RecordObject(_bindData, "Sync UI Target Script");
            _bindData.SetTargetScript(scriptRef);
            EditorUtility.SetDirty(_bindData);
        }

        #endregion

        #region 操作按钮

        private void DrawOperationButtons()
        {
            EditorGUILayout.LabelField("操作", EditorStyles.boldLabel);

            EditorGUILayout.BeginHorizontal();
            
            if (GUILayout.Button("生成绑定代码", GUILayout.Height(30)))
            {
                if (ValidateBindInfos(true))
                {
                    UICodeGenerator.Generate(_bindData, _scriptHolder != null ? _scriptHolder.targetScript : null);
                }
            }

            if (GUILayout.Button("刷新/清理", GUILayout.Height(30)))
            {
                RefreshBindInfos();
            }

            if (GUILayout.Button("校验", GUILayout.Height(30)))
            {
                ValidateBindInfos(true);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.PrefixLabel(new GUIContent(
                "排序规则",
                "路径：按节点名称路径排序；Hierarchy：按节点在层级面板中的先后顺序排序。同一节点上的多个绑定继续按组件类型和字段名排序。"));
            UIBindSortMode sortMode = (UIBindSortMode)EditorGUILayout.EnumPopup(
                _bindData.SortMode,
                GUILayout.MinWidth(100),
                GUILayout.ExpandWidth(true));
            if (sortMode != _bindData.SortMode)
            {
                Undo.RecordObject(_bindData, "Change UI Bind Sort Mode");
                _bindData.SetSortMode(sortMode);
                EditorUtility.SetDirty(_bindData);
            }

            if (GUILayout.Button(
                    new GUIContent("应用排序", "按当前排序规则重排绑定列表；生成绑定代码时也会自动应用同一规则。"),
                    GUILayout.Width(90),
                    GUILayout.Height(26)))
            {
                SortBindInfos();
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("打开管理窗口", GUILayout.Height(26)))
            {
                UIBindManagerWindow.Open(_bindData);
            }
            
            if (GUILayout.Button("清除定位", GUILayout.Height(26)))
            {
                ClearPingBindInfo();
            }

            if (GUILayout.Button("清空绑定", GUILayout.Height(26)))
            {
                if (EditorUtility.DisplayDialog("确认", "确定清空全部绑定？", "确定", "取消"))
                {
                    Undo.RecordObject(_bindData, "Clear UI Bind Infos");
                    _bindData.SetBindInfos(new List<UIBindInfo>());
                    EditorUtility.SetDirty(_bindData);
                    ClearPingBindInfo();
                }
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button(
                    new GUIContent("消歧重名", "为重复的字段名自动追加序号，快速消除重名异常。"),
                    GUILayout.Height(26)))
            {
                DeduplicateFieldNames();
            }

            if (GUILayout.Button(
                    new GUIContent("重生成名称", "按多层节点名+类型规则重新生成全部字段名（如 GridFrame/main_2/shadow → _imgMain_2Shadow）。"),
                    GUILayout.Height(26)))
            {
                RegenerateAllNames();
            }

            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 按规则重生成全部字段名。
        /// </summary>
        private void RegenerateAllNames()
        {
            if (_bindData == null)
                return;

            IReadOnlyList<UIBindInfo> allInfos = _bindData.BindInfos;
            if (allInfos == null || allInfos.Count == 0)
                return;

            if (!EditorUtility.DisplayDialog(
                    "重生成名称",
                    $"将按规则重建全部 {allInfos.Count} 个字段名。是否继续？",
                    "全部重建",
                    "取消"))
                return;

            List<UIBindInfo> targets = new List<UIBindInfo>();
            for (int i = 0; i < allInfos.Count; i++)
            {
                if (allInfos[i] != null)
                    targets.Add(allInfos[i]);
            }

            Undo.RecordObject(_bindData, "Regenerate UI Bind Field Names");

            int renamed = UIBindEditorUtility.RegenerateFieldNames(targets, allInfos, _bindData.transform);

            if (renamed <= 0)
            {
                EditorUtility.DisplayDialog("提示", "字段名已符合规则，无需修改。", "确定");
                return;
            }

            EditorUtility.SetDirty(_bindData);
            Repaint();

            Debug.Log($"[UIBind] 已按规则重生成 {renamed} 个字段名。");
        }

        /// <summary>
        /// 一键消除重复字段名。
        /// </summary>
        private void DeduplicateFieldNames()
        {
            if (_bindData == null)
                return;

            Undo.RecordObject(_bindData, "Deduplicate UI Bind Field Names");

            int renamed = UIBindEditorUtility.DeduplicateFieldNames(_bindData.BindInfos);

            if (renamed <= 0)
            {
                EditorUtility.DisplayDialog("提示", "没有发现重复字段名。", "确定");
                return;
            }

            EditorUtility.SetDirty(_bindData);
            Repaint();

            Debug.Log($"[UIBind] 已为 {renamed} 个重复字段名追加序号消歧。");
        }

        #endregion

        #region Bind Infos 列表绘制

        private void DrawBindList()
        {
            SerializedProperty bindInfosProp = serializedObject.FindProperty("_bindInfos");
            if (bindInfosProp == null)
            {
                EditorGUILayout.HelpBox("未找到序列化字段 _bindInfos，请确认 UIBindData 中字段名是否一致。", MessageType.Warning);
                return;
            }

            List<BindInfoView> views = BuildBindInfoViews();
            int totalCount = bindInfosProp.arraySize;
            int visibleCount = views.Count;
            int errorCount = CountErrorViews(views);

            EditorGUILayout.LabelField($"Bind Infos ({totalCount})", EditorStyles.boldLabel);

            DrawBindListToolbar(totalCount, visibleCount, errorCount, views);

            if (totalCount == 0)
            {
                EditorGUILayout.HelpBox("暂无绑定。请在 Hierarchy 面板中点击节点后的 [+] 添加绑定。", MessageType.None);
                return;
            }

            if (visibleCount == 0)
            {
                EditorGUILayout.HelpBox("没有符合当前搜索或过滤条件的绑定。", MessageType.Info);
                return;
            }

            DrawGroupedBindViews(bindInfosProp, views);
        }

        /// <summary>
        /// 绘制 Bind Infos 的搜索、分组、过滤、展开折叠工具栏。
        /// </summary>
        private void DrawBindListToolbar(int totalCount, int visibleCount, int errorCount, List<BindInfoView> views)
        {
            EditorGUILayout.BeginVertical("box");

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField("搜索", GUILayout.Width(34f));

            EditorGUI.BeginChangeCheck();
            _searchText = EditorGUILayout.TextField(_searchText);
            if (EditorGUI.EndChangeCheck())
            {
                Repaint();
            }

            if (GUILayout.Button("清空", GUILayout.Width(48f)))
            {
                _searchText = string.Empty;
                GUI.FocusControl(null);
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField("分组", GUILayout.Width(34f));
            _groupMode = (BindGroupMode)EditorGUILayout.EnumPopup(_groupMode);

            EditorGUILayout.LabelField("显示", GUILayout.Width(34f));
            _displayFilter = (BindDisplayFilter)EditorGUILayout.EnumPopup(_displayFilter);

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.BeginHorizontal();

            if (GUILayout.Button("全部展开", GUILayout.Height(24)))
            {
                SetAllFoldoutStates(views, true);
            }

            if (GUILayout.Button("全部折叠", GUILayout.Height(24)))
            {
                SetAllFoldoutStates(views, false);
            }

            if (GUILayout.Button("仅异常", GUILayout.Height(24)))
            {
                _displayFilter = BindDisplayFilter.ErrorOnly;
            }

            if (GUILayout.Button("显示全部", GUILayout.Height(24)))
            {
                _displayFilter = BindDisplayFilter.All;
            }

            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField($"当前显示: {visibleCount}/{totalCount}    异常: {errorCount}", EditorStyles.miniLabel);

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 根据当前搜索、分组、过滤设置构建 Inspector 绘制数据。
        /// </summary>
        private List<BindInfoView> BuildBindInfoViews()
        {
            List<BindInfoView> views = new List<BindInfoView>();

            IReadOnlyList<UIBindInfo> infos = _bindData.BindInfos;
            if (infos == null)
                return views;

            Dictionary<string, int> fieldNameCounts = BuildFieldNameCountMap(infos);

            string search = string.IsNullOrEmpty(_searchText) ? string.Empty : _searchText.Trim().ToLowerInvariant();

            for (int i = 0; i < infos.Count; i++)
            {
                UIBindInfo info = infos[i];
                string errorText = GetBindInfoErrorText(i, info, fieldNameCounts);
                bool hasError = !string.IsNullOrEmpty(errorText);

                if (_displayFilter == BindDisplayFilter.ErrorOnly && !hasError)
                    continue;

                if (!IsMatchSearch(info, search))
                    continue;

                BindInfoView view = new BindInfoView
                {
                    index = i,
                    groupName = GetGroupName(info),
                    info = info,
                    hasError = hasError,
                    errorText = errorText
                };

                views.Add(view);
            }

            return views;
        }

        /// <summary>
        /// 绘制分组后的 BindInfo 列表。
        /// </summary>
        private void DrawGroupedBindViews(SerializedProperty bindInfosProp, List<BindInfoView> views)
        {
            if (_groupMode == BindGroupMode.None)
            {
                for (int i = 0; i < views.Count; i++)
                {
                    DrawBindElementByView(bindInfosProp, views[i]);
                }

                return;
            }

            Dictionary<string, List<BindInfoView>> groups = new Dictionary<string, List<BindInfoView>>();
            List<string> groupOrder = new List<string>();

            for (int i = 0; i < views.Count; i++)
            {
                BindInfoView view = views[i];
                string groupName = string.IsNullOrEmpty(view.groupName) ? "(未分组)" : view.groupName;

                if (!groups.TryGetValue(groupName, out List<BindInfoView> list))
                {
                    list = new List<BindInfoView>();
                    groups.Add(groupName, list);
                    groupOrder.Add(groupName);
                }

                list.Add(view);
            }

            for (int i = 0; i < groupOrder.Count; i++)
            {
                string groupName = groupOrder[i];
                List<BindInfoView> groupViews = groups[groupName];

                bool groupHasPingTarget = ContainsPingTarget(groupViews);
                bool groupHasError = ContainsError(groupViews);

                string groupKey = GetGroupFoldoutKey(groupName);

                bool expanded = GetGroupFoldout(groupKey, true);

                /*
                 * 如果 Ping Info 定位到当前分组，需要自动展开分组。
                 */
                if (groupHasPingTarget)
                {
                    expanded = true;
                    GroupFoldoutStates[groupKey] = true;
                }

                DrawGroupHeader(groupName, groupViews.Count, groupHasError, groupKey, expanded);

                if (!GetGroupFoldout(groupKey, true))
                    continue;

                EditorGUI.indentLevel++;

                for (int j = 0; j < groupViews.Count; j++)
                {
                    DrawBindElementByView(bindInfosProp, groupViews[j]);
                }

                EditorGUI.indentLevel--;
            }
        }

        /// <summary>
        /// 绘制分组标题。
        /// </summary>
        private void DrawGroupHeader(string groupName, int count, bool hasError, string groupKey, bool expanded)
        {
            EditorGUILayout.BeginHorizontal("box");

            Color oldContent = GUI.contentColor;
            if (hasError)
            {
                GUI.contentColor = new Color(1f, 0.45f, 0.35f);
            }

            bool newExpanded = EditorGUILayout.Foldout(expanded, $"{groupName} ({count})", true, EditorStyles.foldoutHeader);
            GUI.contentColor = oldContent;

            if (newExpanded != expanded)
            {
                GroupFoldoutStates[groupKey] = newExpanded;
            }

            EditorGUILayout.EndHorizontal();
        }

        private void DrawBindElementByView(SerializedProperty bindInfosProp, BindInfoView view)
        {
            if (view == null)
                return;

            if (view.index < 0 || view.index >= bindInfosProp.arraySize)
                return;

            SerializedProperty element = bindInfosProp.GetArrayElementAtIndex(view.index);
            DrawBindElement(bindInfosProp, element, view);
        }

        /// <summary>
        /// 绘制单个 BindInfo。
        /// 默认显示为简略行，点击折叠箭头后显示完整编辑内容。
        /// </summary>
        private void DrawBindElement(SerializedProperty listProp, SerializedProperty element, BindInfoView view)
        {
            int index = view.index;

            SerializedProperty fieldName = element.FindPropertyRelative("fieldName");
            SerializedProperty typeName = element.FindPropertyRelative("typeName");
            SerializedProperty path = element.FindPropertyRelative("path");
            SerializedProperty targetProp = element.FindPropertyRelative("target");
            SerializedProperty componentProp = element.FindPropertyRelative("component");
            SerializedProperty isWidget = element.FindPropertyRelative("isWidget");
            SerializedProperty widgetTypeName = element.FindPropertyRelative("widgetTypeName");

            GameObject targetGo = targetProp != null ? targetProp.objectReferenceValue as GameObject : null;

            bool widget = isWidget != null && isWidget.boolValue;

            Type currentType = null;
            if (!widget && typeName != null)
            {
                currentType = UIBindEditorUtility.FindType(typeName.stringValue);
            }

            string typeLabel = widget
                ? GetShortTypeName(widgetTypeName != null ? widgetTypeName.stringValue : string.Empty)
                : GetTypeDisplayName(currentType, typeName != null ? typeName.stringValue : string.Empty);

            string pathValue = path != null ? path.stringValue : string.Empty;
            string fieldValue = fieldName != null ? fieldName.stringValue : string.Empty;

            bool isPingTarget = IsPingTarget(index);
            string itemKey = GetItemFoldoutKey(index);

            bool expanded = GetItemFoldout(itemKey, false);

            /*
             * 如果 Ping Info 定位到当前条目，自动展开该条目。
             */
            if (isPingTarget)
            {
                expanded = true;
                ItemFoldoutStates[itemKey] = true;
            }

            Color oldBackgroundColor = GUI.backgroundColor;

            if (isPingTarget)
            {
                GUI.backgroundColor = new Color(1f, 0.72f, 0.25f);
            }
            else if (view.hasError)
            {
                GUI.backgroundColor = new Color(1f, 0.45f, 0.35f);
            }

            EditorGUILayout.BeginVertical("box");
            GUI.backgroundColor = oldBackgroundColor;

            if (isPingTarget)
            {
                EditorGUILayout.HelpBox("当前从 Hierarchy 的 Ping Info 定位到此绑定。", MessageType.Info);
            }

            if (view.hasError)
            {
                EditorGUILayout.HelpBox(view.errorText, MessageType.Warning);
            }

            DrawBindElementHeader(
                listProp,
                index,
                itemKey,
                expanded,
                fieldValue,
                typeLabel,
                pathValue,
                targetGo,
                view.hasError
            );

            if (GetItemFoldout(itemKey, false))
            {
                DrawBindElementExpandedContent(
                    fieldName,
                    typeName,
                    path,
                    targetProp,
                    componentProp,
                    isWidget,
                    widgetTypeName,
                    targetGo,
                    typeLabel
                );
            }

            EditorGUILayout.EndVertical();
        }

        /// <summary>
        /// 绘制 BindInfo 简略行。
        /// 
        /// 这一版把折叠箭头、字段名、类型、状态、按钮分成独立区域，
        /// 避免字段名过长时，类型 Badge 挡住字段名。
        /// </summary>
        private void DrawBindElementHeader(
            SerializedProperty listProp,
            int index,
            string itemKey,
            bool expanded,
            string fieldValue,
            string typeLabel,
            string pathValue,
            GameObject targetGo,
            bool hasError)
        {
            EditorGUILayout.BeginHorizontal();
        
            // 单独绘制折叠箭头。
            // 注意：
            // 某些 Unity 版本的 EditorGUILayout.Foldout 不支持 GUILayout.Width 参数，
            // 所以这里使用 GetControlRect + EditorGUI.Foldout。
            Rect foldoutRect = EditorGUILayout.GetControlRect(
                false,
                EditorGUIUtility.singleLineHeight,
                GUILayout.Width(16f)
            );
        
            bool newExpanded = EditorGUI.Foldout(
                foldoutRect,
                expanded,
                GUIContent.none,
                true
            );
        
            if (newExpanded != expanded)
            {
                ItemFoldoutStates[itemKey] = newExpanded;
            }
        
            // 字段名显示区域。
            // 使用独立 LabelField，Unity 会自动裁剪过长文字，不会和类型 Badge 重叠。
            string title = $"[{index}] {fieldValue}";
            if (string.IsNullOrEmpty(fieldValue))
                title = $"[{index}] <字段名为空>";
        
            GUIStyle fieldStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                clipping = TextClipping.Clip
            };
        
            // tooltip 里放完整字段名，鼠标悬停时可以看到完整内容。
            GUIContent titleContent = new GUIContent(title, title);
        
            EditorGUILayout.LabelField(
                titleContent,
                fieldStyle,
                GUILayout.MinWidth(120f)
            );
        
            // 中间加弹性空白，把类型、状态、按钮固定推到右侧。
            GUILayout.FlexibleSpace();
        
            // 类型标签宽度稍微加大一点，避免 TextMeshProUGUI 等长类型显示不全。
            string safeTypeLabel = string.IsNullOrEmpty(typeLabel) ? "Unknown" : typeLabel;
            GUIContent typeContent = new GUIContent(safeTypeLabel, safeTypeLabel);
        
            GUILayout.Label(
                typeContent,
                GetBadgeStyle(),
                GUILayout.Width(120f),
                GUILayout.Height(20f)
            );
        
            // 状态显示。
            Color oldContent = GUI.contentColor;
            GUI.contentColor = hasError ? new Color(1f, 0.35f, 0.25f) : Color.gray;
        
            GUILayout.Label(
                hasError ? "异常" : "OK",
                GUILayout.Width(36f)
            );
        
            GUI.contentColor = oldContent;
        
            if (GUILayout.Button("Ping", GUILayout.Width(44)))
            {
                if (targetGo != null)
                {
                    EditorGUIUtility.PingObject(targetGo);
                    Selection.activeGameObject = targetGo;
                }
            }
        
            if (GUILayout.Button("上移", GUILayout.Width(44)))
            {
                if (index > 0)
                {
                    listProp.MoveArrayElement(index, index - 1);
                    serializedObject.ApplyModifiedProperties();
                    GUIUtility.ExitGUI();
                }
            }
        
            if (GUILayout.Button("下移", GUILayout.Width(44)))
            {
                if (index < listProp.arraySize - 1)
                {
                    listProp.MoveArrayElement(index, index + 1);
                    serializedObject.ApplyModifiedProperties();
                    GUIUtility.ExitGUI();
                }
            }
        
            if (GUILayout.Button("删除", GUILayout.Width(44)))
            {
                if (EditorUtility.DisplayDialog("确认删除", $"确定删除绑定 [{index}] {fieldValue}？", "确定", "取消"))
                {
                    if (IsPingTarget(index))
                    {
                        ClearPingBindInfo();
                    }
        
                    listProp.DeleteArrayElementAtIndex(index);
                    serializedObject.ApplyModifiedProperties();
                    GUIUtility.ExitGUI();
                }
            }
        
            EditorGUILayout.EndHorizontal();
        
            // 第二行显示路径，和第一行错开一点，让列表更清晰。
            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(22f);
        
            string displayPath = string.IsNullOrEmpty(pathValue) ? "(root)" : pathValue;
        
            EditorGUILayout.LabelField(
                new GUIContent(displayPath, displayPath),
                EditorStyles.miniLabel
            );
        
            EditorGUILayout.EndHorizontal();
        }

        /// <summary>
        /// 绘制 BindInfo 展开后的完整编辑内容。
        /// </summary>
        private void DrawBindElementExpandedContent(
            SerializedProperty fieldName,
            SerializedProperty typeName,
            SerializedProperty path,
            SerializedProperty targetProp,
            SerializedProperty componentProp,
            SerializedProperty isWidget,
            SerializedProperty widgetTypeName,
            GameObject targetGo,
            string typeLabel)
        {
            EditorGUILayout.Space(2f);

            // Field Name 拆成 前缀（只读）+ 语义名（可编辑）。
            // 前缀随类型自动决定，用户只改语义部分（如 _goBg_fleet 只改 Bg_fleet），提交时前缀拼回。
            // DelayedTextField：仅回车或失焦提交，避免逐字符触发重名检测/布局变化打断输入。
            // 用值比较赋值，赋值后由 OnInspectorGUI 末尾的 ApplyModifiedProperties 统一提交。
            if (fieldName != null)
            {
                bool widgetForPrefix = isWidget != null && isWidget.boolValue;
                string typeForPrefix = widgetForPrefix
                    ? (widgetTypeName != null ? widgetTypeName.stringValue : string.Empty)
                    : (typeName != null ? typeName.stringValue : string.Empty);

                string prefix = UIBindEditorUtility.GetFieldNamePrefix(fieldName.stringValue, widgetForPrefix, typeForPrefix);
                string remainder = UIBindEditorUtility.GetFieldNameRemainder(fieldName.stringValue, widgetForPrefix, typeForPrefix);

                EditorGUILayout.BeginHorizontal();

                EditorGUILayout.LabelField("Field Name", GUILayout.Width(EditorGUIUtility.labelWidth));
                if (!string.IsNullOrEmpty(prefix))
                {
                    Color oldColor = GUI.color;
                    GUI.color = new Color(0.7f, 0.7f, 0.7f);
                    EditorGUILayout.LabelField(prefix, GUILayout.Width(Mathf.Max(30f, prefix.Length * 9f)));
                    GUI.color = oldColor;
                }

                string newRemainder = EditorGUILayout.DelayedTextField(remainder);
                if (newRemainder != remainder)
                    fieldName.stringValue = UIBindEditorUtility.ComposeFieldName(prefix, newRemainder);

                EditorGUILayout.EndHorizontal();

                // 完整字段名：可直接选中复制，并提供一键复制按钮。
                DrawCopyableFullName(fieldName.stringValue);
            }

            if (targetProp != null)
                EditorGUILayout.PropertyField(targetProp);

            if (path != null)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.PropertyField(path);
                EditorGUI.EndDisabledGroup();
            }

            if (isWidget == null)
                return;

            EditorGUI.BeginChangeCheck();
            bool widgetToggle = EditorGUILayout.Toggle("Is Widget", isWidget.boolValue);
            if (EditorGUI.EndChangeCheck())
            {
                isWidget.boolValue = widgetToggle;
            }

            if (isWidget.boolValue)
            {
                if (widgetTypeName != null)
                {
                    EditorGUILayout.PropertyField(widgetTypeName, new GUIContent("Widget Type Full Name"));
                }
            }
            else
            {
                if (GUILayout.Button($"绑定类型: {typeLabel}"))
                {
                    ShowTypeSelectMenu(typeName, componentProp, fieldName, targetGo);
                }

                if (componentProp != null)
                    EditorGUILayout.PropertyField(componentProp);
            }
        }

        #endregion

        #region 分组、搜索、折叠辅助

        private Dictionary<string, int> BuildFieldNameCountMap(IReadOnlyList<UIBindInfo> infos)
        {
            Dictionary<string, int> map = new Dictionary<string, int>();

            if (infos == null)
                return map;

            for (int i = 0; i < infos.Count; i++)
            {
                UIBindInfo info = infos[i];
                if (info == null || string.IsNullOrEmpty(info.fieldName))
                    continue;

                if (!map.ContainsKey(info.fieldName))
                    map.Add(info.fieldName, 0);

                map[info.fieldName]++;
            }

            return map;
        }

        private string GetGroupName(UIBindInfo info)
        {
            if (info == null)
                return "Missing";

            switch (_groupMode)
            {
                case BindGroupMode.None:
                    return string.Empty;

                case BindGroupMode.PathArea:
                    return GetPathAreaGroupName(info.path);

                case BindGroupMode.Type:
                    return GetBindInfoTypeDisplayName(info);

                case BindGroupMode.WidgetOrNormal:
                    return info.isWidget ? "Widgets" : "Normal Components";

                default:
                    return string.Empty;
            }
        }

        private static string GetPathAreaGroupName(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "(Root)";

            int index = path.IndexOf('/');
            if (index < 0)
                return path;

            return path.Substring(0, index);
        }

        private static string GetBindInfoTypeDisplayName(UIBindInfo info)
        {
            if (info == null)
                return "Missing";

            if (info.isWidget)
                return GetShortTypeName(info.widgetTypeName);

            Type type = UIBindEditorUtility.FindType(info.typeName);
            return GetTypeDisplayName(type, info.typeName);
        }

        private static bool IsMatchSearch(UIBindInfo info, string search)
        {
            if (string.IsNullOrEmpty(search))
                return true;

            if (info == null)
                return false;

            if (ContainsIgnoreCase(info.fieldName, search))
                return true;

            if (ContainsIgnoreCase(info.path, search))
                return true;

            if (ContainsIgnoreCase(info.typeName, search))
                return true;

            if (ContainsIgnoreCase(info.widgetTypeName, search))
                return true;

            if (info.target != null && ContainsIgnoreCase(info.target.name, search))
                return true;

            if (info.component != null && ContainsIgnoreCase(info.component.GetType().Name, search))
                return true;

            return false;
        }

        private static bool ContainsIgnoreCase(string value, string lowerSearch)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            return value.ToLowerInvariant().Contains(lowerSearch);
        }

        private string GetGroupFoldoutKey(string groupName)
        {
            int bindDataId = _bindData != null ? _bindData.GetInstanceID() : 0;
            return bindDataId + "|Group|" + _groupMode + "|" + groupName;
        }

        /// <summary>
        /// 获取单条 BindInfo 的折叠状态 key。
        /// 
        /// 注意：
        /// 这里不要使用 fieldName/typeName/path 等可编辑字段生成 key。
        /// 否则用户编辑 Field Name 时，每输入一个字符 key 都会变化，
        /// 导致条目立刻折叠，并且 Inspector 滚动位置跳到顶部。
        /// 
        /// 使用 index 的好处是：
        /// 1. 编辑字段名时不会改变折叠状态；
        /// 2. 编辑类型时不会改变折叠状态；
        /// 3. 编辑 target 时也不会改变折叠状态。
        /// 
        /// 缺点是排序或删除后，折叠状态会跟随位置而不是跟随具体数据。
        /// 但这个影响远小于编辑时频繁折叠的问题。
        /// </summary>
        private string GetItemFoldoutKey(int index)
        {
            int bindDataId = _bindData != null ? _bindData.GetInstanceID() : 0;
            return bindDataId + "|ItemIndex|" + index;
        }

        private static bool GetGroupFoldout(string key, bool defaultValue)
        {
            if (string.IsNullOrEmpty(key))
                return defaultValue;

            if (GroupFoldoutStates.TryGetValue(key, out bool value))
                return value;

            GroupFoldoutStates[key] = defaultValue;
            return defaultValue;
        }

        private static bool GetItemFoldout(string key, bool defaultValue)
        {
            if (string.IsNullOrEmpty(key))
                return defaultValue;

            if (ItemFoldoutStates.TryGetValue(key, out bool value))
                return value;

            ItemFoldoutStates[key] = defaultValue;
            return defaultValue;
        }

        private void SetAllFoldoutStates(List<BindInfoView> views, bool expanded)
        {
            if (views == null)
                return;

            for (int i = 0; i < views.Count; i++)
            {
                BindInfoView view = views[i];
                if (view == null)
                    continue;

                string groupKey = GetGroupFoldoutKey(view.groupName);
                GroupFoldoutStates[groupKey] = expanded;

                string itemKey = GetItemFoldoutKey(view.index);
                ItemFoldoutStates[itemKey] = expanded;
            }

            Repaint();
        }

        private bool ContainsPingTarget(List<BindInfoView> views)
        {
            if (views == null)
                return false;

            for (int i = 0; i < views.Count; i++)
            {
                if (views[i] != null && IsPingTarget(views[i].index))
                    return true;
            }

            return false;
        }

        private static bool ContainsError(List<BindInfoView> views)
        {
            if (views == null)
                return false;

            for (int i = 0; i < views.Count; i++)
            {
                if (views[i] != null && views[i].hasError)
                    return true;
            }

            return false;
        }

        private static int CountErrorViews(List<BindInfoView> views)
        {
            int count = 0;

            if (views == null)
                return count;

            for (int i = 0; i < views.Count; i++)
            {
                if (views[i] != null && views[i].hasError)
                    count++;
            }

            return count;
        }

        #endregion

        #region 异常检测

        /// <summary>
        /// 获取单条 BindInfo 的异常信息。
        /// 返回空字符串表示没有异常。
        /// </summary>
        private static string GetBindInfoErrorText(int index, UIBindInfo info, Dictionary<string, int> fieldNameCounts)
        {
            if (info == null)
                return $"[{index}] 绑定数据为空。";

            List<string> errors = new List<string>();

            if (string.IsNullOrEmpty(info.fieldName))
            {
                errors.Add("字段名为空");
            }
            else if (fieldNameCounts != null &&
                     fieldNameCounts.TryGetValue(info.fieldName, out int count) &&
                     count > 1)
            {
                errors.Add("字段名重复: " + info.fieldName);
            }

            if (info.target == null)
            {
                errors.Add("Target 为空");
            }

            if (info.isWidget)
            {
                Type widgetType = UIBindEditorUtility.FindType(info.widgetTypeName);
                if (widgetType == null)
                {
                    errors.Add("Widget 类型找不到: " + info.widgetTypeName);
                }
                else if (!UIBindEditorUtility.IsWidgetType(widgetType))
                {
                    errors.Add("Widget 类型不是 UIWidget: " + info.widgetTypeName);
                }
            }
            else
            {
                Type type = UIBindEditorUtility.FindType(info.typeName);
                if (type == null)
                {
                    errors.Add("绑定类型找不到: " + info.typeName);
                }
                else if (type != typeof(GameObject))
                {
                    if (info.target != null)
                    {
                        Component component = UIBindEditorUtility.ResolveComponent(info.target, type);
                        if (component == null)
                        {
                            errors.Add("Target 上找不到组件: " + type.Name);
                        }
                    }
                }
            }

            if (errors.Count == 0)
                return string.Empty;

            return $"[{index}] " + string.Join("；", errors);
        }

        #endregion

        #region 类型选择和显示

        private void ShowTypeSelectMenu(SerializedProperty typeName, SerializedProperty componentProp, SerializedProperty fieldName, GameObject targetGo)
        {
            if (typeName == null)
                return;

            GenericMenu menu = new GenericMenu();

            List<Type> types = targetGo != null
                ? UIBindEditorUtility.GetBindableTypes(targetGo)
                : new List<Type>(UIBindEditorUtility.DefaultBindableTypes);

            Type currentType = UIBindEditorUtility.FindType(typeName.stringValue);

            for (int i = 0; i < types.Count; i++)
            {
                Type type = types[i];
                Type capturedType = type;

                menu.AddItem(new GUIContent(UIBindEditorUtility.GetCompactTypeName(type)), currentType == type, () =>
                {
                    typeName.stringValue = capturedType.FullName;

                    if (targetGo != null)
                    {
                        if (componentProp != null)
                        {
                            componentProp.objectReferenceValue = UIBindEditorUtility.ResolveComponent(targetGo, capturedType);
                        }

                        if (fieldName != null && string.IsNullOrEmpty(fieldName.stringValue))
                        {
                            fieldName.stringValue = UIBindEditorUtility.BuildUniqueFieldName(targetGo, capturedType, _bindData.BindInfos, _bindData.transform);
                        }
                    }

                    serializedObject.ApplyModifiedProperties();
                });
            }

            menu.ShowAsContext();
        }

        private static GUIStyle GetBadgeStyle()
        {
            GUIStyle style = new GUIStyle(EditorStyles.miniButton)
            {
                alignment = TextAnchor.MiddleCenter,
                fontStyle = FontStyle.Bold,
                clipping = TextClipping.Clip,
                fixedHeight = 20f
            };

            return style;
        }

        private static string GetTypeDisplayName(Type type, string fallbackFullName)
        {
            if (type != null)
                return UIBindEditorUtility.GetCompactTypeName(type);

            if (!string.IsNullOrEmpty(fallbackFullName))
                return GetShortTypeName(fallbackFullName);

            return "Unknown";
        }

        private static string GetShortTypeName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return string.Empty;

            int index = fullName.LastIndexOf('.');
            return index >= 0 ? fullName.Substring(index + 1) : fullName;
        }

        /// <summary>
        /// 绘制可复制的完整字段名：只读可选中文本框 + 一键复制按钮。
        /// </summary>
        private static void DrawCopyableFullName(string fullName)
        {
            EditorGUILayout.BeginHorizontal();

            EditorGUILayout.LabelField("完整", GUILayout.Width(EditorGUIUtility.labelWidth));

            // SelectableLabel 支持鼠标选中复制。
            EditorGUILayout.SelectableLabel(
                fullName,
                EditorStyles.textField,
                GUILayout.Height(EditorGUIUtility.singleLineHeight));

            if (GUILayout.Button("复制", GUILayout.Width(48f)))
            {
                EditorGUIUtility.systemCopyBuffer = fullName ?? string.Empty;
            }

            EditorGUILayout.EndHorizontal();
        }

        #endregion

        #region 数据操作

        private void SortBindInfos()
        {
            List<UIBindInfo> list = new List<UIBindInfo>(_bindData.BindInfos);
            UIBindEditorUtility.SortBindInfos(list, _bindData);

            Undo.RecordObject(_bindData, "Sort UI Bind Infos");
            _bindData.SetBindInfos(list);
            EditorUtility.SetDirty(_bindData);
        }

        private void RefreshBindInfos()
        {
            List<UIBindInfo> list = new List<UIBindInfo>(_bindData.BindInfos);
            int removed = 0;
            int repaired = 0;

            Undo.RecordObject(_bindData, "Refresh UI Bind Infos");

            for (int i = list.Count - 1; i >= 0; i--)
            {
                UIBindInfo info = list[i];
                if (info == null || info.target == null)
                {
                    list.RemoveAt(i);
                    removed++;
                    continue;
                }

                info.path = UIBindEditorUtility.GetTransformPath(_bindData.transform, info.target.transform);

                if (!info.isWidget)
                {
                    Type type = UIBindEditorUtility.FindType(info.typeName);
                    Component component = UIBindEditorUtility.ResolveComponent(info.target, type);
                    if (info.component != component)
                    {
                        info.component = component;
                        repaired++;
                    }
                }
            }

            _bindData.SetBindInfos(list);
            EditorUtility.SetDirty(_bindData);

            Debug.Log($"[UIBind] 刷新完成，移除无效绑定 {removed} 个，修复组件引用 {repaired} 个。");
        }

        private bool ValidateBindInfos(bool showDialog)
        {
            List<string> errors = new List<string>();
            HashSet<string> fieldNames = new HashSet<string>();
            HashSet<string> bindingKeys = new HashSet<string>();

            IReadOnlyList<UIBindInfo> infos = _bindData.BindInfos;
            for (int i = 0; i < infos.Count; i++)
            {
                UIBindInfo info = infos[i];
                if (info == null)
                {
                    errors.Add($"[{i}] 绑定数据为空。");
                    continue;
                }

                if (string.IsNullOrEmpty(info.fieldName))
                    errors.Add($"[{i}] 字段名为空。");
                else if (!fieldNames.Add(info.fieldName))
                    errors.Add($"[{i}] 字段名重复: {info.fieldName}");

                if (info.target == null)
                    errors.Add($"[{i}] Target 为空。");

                if (info.isWidget)
                {
                    Type widgetType = UIBindEditorUtility.FindType(info.widgetTypeName);
                    if (widgetType == null)
                        errors.Add($"[{i}] Widget 类型找不到: {info.widgetTypeName}");
                    else if (!UIBindEditorUtility.IsWidgetType(widgetType))
                        errors.Add($"[{i}] Widget 类型不是 UIWidget: {info.widgetTypeName}");
                }
                else
                {
                    Type type = UIBindEditorUtility.FindType(info.typeName);
                    if (type == null)
                    {
                        errors.Add($"[{i}] 绑定类型找不到: {info.typeName}");
                    }
                    else if (type != typeof(GameObject))
                    {
                        Component component = UIBindEditorUtility.ResolveComponent(info.target, type);
                        if (component == null)
                            errors.Add($"[{i}] Target 上找不到组件: {type.Name}");
                    }
                }

                string key = BuildBindingKey(info);
                if (!string.IsNullOrEmpty(key) && !bindingKeys.Add(key))
                    errors.Add($"[{i}] 重复绑定: {key}");
            }

            if (errors.Count == 0)
            {
                if (showDialog)
                    EditorUtility.DisplayDialog("校验通过", "UI 绑定数据没有发现问题。", "确定");

                return true;
            }

            string message = string.Join("\n", errors);
            if (showDialog)
                EditorUtility.DisplayDialog("校验失败", message, "确定");

            Debug.LogError("[UIBind] 校验失败:\n" + message);
            return false;
        }

        private static string BuildBindingKey(UIBindInfo info)
        {
            if (info == null || info.target == null)
                return string.Empty;

            if (info.isWidget)
                return info.target.GetInstanceID() + "|Widget|" + info.widgetTypeName;

            return info.target.GetInstanceID() + "|" + info.typeName;
        }

        #endregion

        #region 对外接口：Hierarchy 菜单调用

        public static void AddBinding(UIBindData bindData, GameObject target)
        {
            Type bindType = UIBindEditorUtility.GuessBindableType(target);
            AddBinding(bindData, target, bindType);
        }

        public static void AddBinding(UIBindData bindData, GameObject target, Type bindType)
        {
            if (bindData == null || target == null || bindType == null)
                return;

            if (!UIBindEditorUtility.CanBindType(bindType))
                return;

            if (UIBindEditorUtility.IsAlreadyBound(bindData, target, bindType))
            {
                Debug.Log($"[UIBind] 已存在绑定: {target.name}.{bindType.Name}");
                return;
            }

            Undo.RecordObject(bindData, "Add UI Binding");

            IReadOnlyList<UIBindInfo> readonlyInfos = bindData.BindInfos;
            List<UIBindInfo> infos = new List<UIBindInfo>(readonlyInfos != null ? readonlyInfos : Array.Empty<UIBindInfo>());

            AppendBinding(infos, bindData, target, bindType);

            bindData.SetBindInfos(infos);

            EditorUtility.SetDirty(bindData);
        }

        /// <summary>
        /// 向 infos 列表追加一条绑定（如已存在则跳过），不做保存。
        /// 供批量添加复用，避免每条都 SetBindInfos / SaveAssets。
        /// 返回是否实际新增。
        /// </summary>
        private static bool AppendBinding(List<UIBindInfo> infos, UIBindData bindData, GameObject target, Type bindType)
        {
            if (infos == null || target == null || bindType == null)
                return false;

            if (!UIBindEditorUtility.CanBindType(bindType))
                return false;

            // 在已收集的列表里判重，不依赖 bindData 当前数据（批量过程中 bindData 尚未写回）。
            string typeName = bindType.FullName;
            for (int i = 0; i < infos.Count; i++)
            {
                UIBindInfo exist = infos[i];
                if (exist != null && !exist.isWidget && exist.target == target && exist.typeName == typeName)
                    return false;
            }

            UIBindInfo info = new UIBindInfo
            {
                fieldName = UIBindEditorUtility.BuildUniqueFieldName(target, bindType, infos, bindData.transform),
                typeName = typeName,
                path = UIBindEditorUtility.GetTransformPath(bindData.transform, target.transform),
                target = target,
                component = UIBindEditorUtility.ResolveComponent(target, bindType),
                isWidget = false,
                widgetTypeName = string.Empty
            };

            infos.Add(info);
            return true;
        }

        public static void AddAllBindingsOnTarget(UIBindData bindData, GameObject target)
        {
            if (bindData == null || target == null)
                return;

            Undo.RecordObject(bindData, "Add UI Bindings On Target");

            List<UIBindInfo> infos = new List<UIBindInfo>(bindData.BindInfos);
            int added = 0;

            List<Type> types = UIBindEditorUtility.GetBindableTypes(target);
            for (int i = 0; i < types.Count; i++)
            {
                if (AppendBinding(infos, bindData, target, types[i]))
                    added++;
            }

            if (added <= 0)
                return;

            bindData.SetBindInfos(infos);
            EditorUtility.SetDirty(bindData);
        }

        public static void AddAllBindingsInChildren(UIBindData bindData, GameObject root)
        {
            if (bindData == null || root == null)
                return;

            Undo.RecordObject(bindData, "Add UI Bindings In Children");

            // 一次性收集所有子节点的可绑定类型，循环结束后只写回并保存一次。
            List<UIBindInfo> infos = new List<UIBindInfo>(bindData.BindInfos);
            int added = 0;

            Transform[] children = root.GetComponentsInChildren<Transform>(true);
            for (int i = 0; i < children.Length; i++)
            {
                GameObject go = children[i].gameObject;
                List<Type> types = UIBindEditorUtility.GetBindableTypes(go);
                for (int t = 0; t < types.Count; t++)
                {
                    if (AppendBinding(infos, bindData, go, types[t]))
                        added++;
                }
            }

            if (added <= 0)
            {
                Debug.Log("[UIBind] 没有新增绑定。");
                return;
            }

            bindData.SetBindInfos(infos);
            EditorUtility.SetDirty(bindData);

            Debug.Log($"[UIBind] 批量新增绑定 {added} 个。");
        }

        public static void RemoveBinding(UIBindData bindData, UIBindInfo removeInfo)
        {
            if (bindData == null || removeInfo == null)
                return;

            ClearPingBindInfoIfMatch(bindData, removeInfo);

            Undo.RecordObject(bindData, "Remove UI Binding");

            List<UIBindInfo> infos = new List<UIBindInfo>(bindData.BindInfos);
            infos.Remove(removeInfo);
            bindData.SetBindInfos(infos);

            EditorUtility.SetDirty(bindData);
        }

        public static void RemoveBindingsOnTarget(UIBindData bindData, GameObject target)
        {
            if (bindData == null || target == null)
                return;

            Undo.RecordObject(bindData, "Remove UI Bindings On Target");

            List<UIBindInfo> infos = new List<UIBindInfo>(bindData.BindInfos);

            bool removedPingTarget = false;
            infos.RemoveAll(info =>
            {
                bool shouldRemove = info != null && info.target == target;
                if (shouldRemove && bindData.GetInstanceID() == _pendingPingBindDataInstanceId)
                {
                    string key = MakeBindInfoKey(info);
                    if (key == _pendingPingBindInfoKey)
                        removedPingTarget = true;
                }

                return shouldRemove;
            });

            bindData.SetBindInfos(infos);

            if (removedPingTarget)
            {
                ClearPingBindInfo();
            }

            EditorUtility.SetDirty(bindData);
        }

        public static void ReplaceBinding(UIBindData bindData, UIBindInfo info, Type newType)
        {
            if (bindData == null || info == null || info.target == null || newType == null)
                return;

            if (UIBindEditorUtility.IsAlreadyBound(bindData, info.target, newType))
                return;

            Undo.RecordObject(bindData, "Replace UI Binding");

            info.isWidget = false;
            info.widgetTypeName = string.Empty;
            info.typeName = newType.FullName;
            info.component = UIBindEditorUtility.ResolveComponent(info.target, newType);
            info.fieldName = UIBindEditorUtility.BuildUniqueFieldName(info.target, newType, bindData.BindInfos, bindData.transform);
            info.path = UIBindEditorUtility.GetTransformPath(bindData.transform, info.target.transform);

            // 替换类型后定位 key 会变化，清理旧定位，避免高亮错位。
            ClearPingBindInfo();

            EditorUtility.SetDirty(bindData);
        }

        #endregion

        #region 编辑器刷新辅助

        /// <summary>
        /// 尽量安全地刷新编辑器相关视图。
        /// 
        /// 不依赖 UnityEditorInternal.InternalEditorUtility.RepaintAllViews，
        /// 避免部分 Unity 版本 API 不可用。
        /// </summary>
        private static void RepaintEditorViews()
        {
            SceneView.RepaintAll();
            EditorApplication.QueuePlayerLoopUpdate();

            Type inspectorType = typeof(UnityEditor.Editor).Assembly.GetType("UnityEditor.InspectorWindow");
            if (inspectorType == null)
                return;

            UnityEngine.Object[] windows = Resources.FindObjectsOfTypeAll(inspectorType);
            for (int i = 0; i < windows.Length; i++)
            {
                if (windows[i] is EditorWindow window)
                    window.Repaint();
            }
        }

        #endregion
    }
}
#endif