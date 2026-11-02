#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FRFrame.Editor
{
    /// <summary>
    /// UI 绑定独立管理窗口。
    /// 
    /// 设计目标：
    /// 1. Inspector 适合少量快速查看；
    /// 2. 独立窗口适合大量绑定的浏览、搜索、筛选、批量操作；
    /// 3. 使用 UI Toolkit ListView，支持虚拟化，绑定数量较多时比 IMGUI 全量绘制更合适。
    /// </summary>
    public sealed class UIBindManagerWindow : EditorWindow
    {
        private const string WindowTitle = "UI Bind Manager";

        private enum GroupMode
        {
            All = 0,
            PathArea = 1,
            Type = 2,
            WidgetOrNormal = 3
        }

        private enum DisplayFilter
        {
            All = 0,
            ErrorOnly = 1,
            WidgetOnly = 2,
            NormalOnly = 3
        }

        private enum SortColumn
        {
            Index = 0,
            FieldName = 1,
            Type = 2,
            Path = 3,
            Target = 4,
            Status = 5
        }

        private sealed class GroupView
        {
            public string name;
            public int count;
            public int errorCount;

            public string DisplayName
            {
                get
                {
                    if (errorCount > 0)
                        return $"{name} ({count})  异常:{errorCount}";

                    return $"{name} ({count})";
                }
            }
        }

        private sealed class RowView
        {
            public int index;
            public UIBindInfo info;
            public string groupName;
            public string fieldName;
            public string typeName;
            public string path;
            public string targetName;
            public bool hasError;
            public string errorText;
        }

        private UIBindData _bindData;

        private ObjectField _bindDataField;
        private TextField _searchField;
        private EnumField _groupModeField;
        private EnumField _displayFilterField;

        private ListView _groupListView;
        private ListView _bindListView;
        private IMGUIContainer _detailIMGUI;

        private Label _summaryLabel;
        private Label _selectedLabel;

        private readonly List<GroupView> _groups = new List<GroupView>();
        private readonly List<RowView> _allRows = new List<RowView>();
        private readonly List<RowView> _visibleRows = new List<RowView>();

        private string _searchText = string.Empty;
        private string _selectedGroupName = "全部";

        private GroupMode _groupMode = GroupMode.PathArea;
        private DisplayFilter _displayFilter = DisplayFilter.All;

        private SortColumn _sortColumn = SortColumn.Index;
        private bool _sortAscending = true;

        private SerializedObject _detailSerializedObject;

        // 详情面板绑定的对象缓存，避免每帧 new SerializedObject 造成的焦点/性能问题。
        private UIBindData _detailBoundData;

        #region 打开窗口入口

        [MenuItem("Tools/UI Bind/UI Bind Manager")]
        public static void OpenFromMenu()
        {
            UIBindData bindData = null;

            if (Selection.activeGameObject != null)
            {
                bindData = FindOwnerBindData(Selection.activeGameObject.transform);
            }

            Open(bindData);
        }

        /// <summary>
        /// 从外部打开窗口。
        /// 可以在 Inspector 里加按钮调用：
        /// UIBindManagerWindow.Open(_bindData);
        /// </summary>
        public static void Open(UIBindData bindData)
        {
            UIBindManagerWindow window = GetWindow<UIBindManagerWindow>();
            window.titleContent = new GUIContent(WindowTitle);
            window.minSize = new Vector2(720f, 420f);
            window.SetBindData(bindData);
            window.Show();
        }

        #endregion

        private void OnEnable()
        {
            titleContent = new GUIContent(WindowTitle);
            minSize = new Vector2(900f, 520f);

            BuildUI();

            if (_bindData == null && Selection.activeGameObject != null)
            {
                _bindData = FindOwnerBindData(Selection.activeGameObject.transform);
            }

            RefreshBindDataField();
            RebuildAll();
        }

        private void OnSelectionChange()
        {
            /*
             * 不自动切换 Root，避免用户正在管理 A 预制体时，随手选中其他节点导致窗口内容跳变。
             * 如果确实需要切换，请使用窗口顶部的 Root 字段。
             */
            Repaint();
        }

        private void SetBindData(UIBindData bindData)
        {
            _bindData = bindData;
            RefreshBindDataField();
            RebuildAll();
        }

        #region UI 构建

        private void BuildUI()
        {
            rootVisualElement.Clear();

            VisualElement root = rootVisualElement;
            root.style.flexDirection = FlexDirection.Column;
            root.style.paddingLeft = 6;
            root.style.paddingRight = 6;
            root.style.paddingTop = 6;
            root.style.paddingBottom = 6;

            BuildTopBar(root);
            BuildToolbar(root);
            BuildMainArea(root);
        }

        /// <summary>
        /// 顶部 Root 选择区域。
        /// </summary>
        private void BuildTopBar(VisualElement root)
        {
            VisualElement top = new VisualElement();
            top.style.flexDirection = FlexDirection.Row;
            top.style.marginBottom = 4;
            root.Add(top);

            _bindDataField = new ObjectField("Root");
            _bindDataField.objectType = typeof(UIBindData);
            _bindDataField.allowSceneObjects = true;
            _bindDataField.style.flexGrow = 1;
            _bindDataField.RegisterValueChangedCallback(evt =>
            {
                _bindData = evt.newValue as UIBindData;
                _selectedGroupName = "全部";
                RebuildAll();
            });
            top.Add(_bindDataField);

            Button useSelectionButton = new Button(() =>
            {
                if (Selection.activeGameObject == null)
                    return;

                UIBindData selectedBindData = FindOwnerBindData(Selection.activeGameObject.transform);
                SetBindData(selectedBindData);
            })
            {
                text = "使用当前选择"
            };
            useSelectionButton.style.width = 110;
            top.Add(useSelectionButton);

            Button pingRootButton = new Button(() =>
            {
                if (_bindData == null)
                    return;

                Selection.activeGameObject = _bindData.gameObject;
                EditorGUIUtility.PingObject(_bindData.gameObject);
            })
            {
                text = "Ping Root"
            };
            pingRootButton.style.width = 90;
            top.Add(pingRootButton);
        }

        /// <summary>
        /// 搜索、分组、过滤和操作按钮区域。
        /// </summary>
        private void BuildToolbar(VisualElement root)
        {
            VisualElement toolbar = new VisualElement();
            toolbar.style.flexDirection = FlexDirection.Column;
            toolbar.style.marginBottom = 5;
            toolbar.style.paddingLeft = 4;
            toolbar.style.paddingRight = 4;
            toolbar.style.paddingTop = 4;
            toolbar.style.paddingBottom = 4;
            toolbar.style.borderBottomWidth = 1;
            toolbar.style.borderTopWidth = 1;
            toolbar.style.borderLeftWidth = 1;
            toolbar.style.borderRightWidth = 1;
            toolbar.style.borderBottomColor = new Color(0.22f, 0.22f, 0.22f);
            toolbar.style.borderTopColor = new Color(0.22f, 0.22f, 0.22f);
            toolbar.style.borderLeftColor = new Color(0.22f, 0.22f, 0.22f);
            toolbar.style.borderRightColor = new Color(0.22f, 0.22f, 0.22f);
            root.Add(toolbar);

            VisualElement line1 = new VisualElement();
            line1.style.flexDirection = FlexDirection.Row;
            line1.style.flexShrink = 0;
            toolbar.Add(line1);

            _searchField = new TextField("搜索");
            _searchField.style.flexGrow = 1;
            _searchField.RegisterValueChangedCallback(evt =>
            {
                _searchText = evt.newValue ?? string.Empty;
                _selectedGroupName = "全部";
                RebuildAll();
            });
            line1.Add(_searchField);

            Button clearSearchButton = new Button(() =>
            {
                _searchText = string.Empty;
                _searchField.SetValueWithoutNotify(string.Empty);
                _selectedGroupName = "全部";
                RebuildAll();
            })
            {
                text = "清空"
            };
            clearSearchButton.style.width = 60;
            line1.Add(clearSearchButton);

            VisualElement filterLine = new VisualElement();
            filterLine.style.flexDirection = FlexDirection.Row;
            filterLine.style.flexShrink = 0;
            filterLine.style.marginTop = 4;
            toolbar.Add(filterLine);

            _groupModeField = new EnumField("分组", _groupMode);
            _groupModeField.style.flexGrow = 1;
            _groupModeField.style.minWidth = 180;
            _groupModeField.RegisterValueChangedCallback(evt =>
            {
                _groupMode = (GroupMode)evt.newValue;
                _selectedGroupName = "全部";
                RebuildAll();
            });
            filterLine.Add(_groupModeField);

            _displayFilterField = new EnumField("显示", _displayFilter);
            _displayFilterField.style.flexGrow = 1;
            _displayFilterField.style.minWidth = 180;
            _displayFilterField.RegisterValueChangedCallback(evt =>
            {
                _displayFilter = (DisplayFilter)evt.newValue;
                _selectedGroupName = "全部";
                RebuildAll();
            });
            filterLine.Add(_displayFilterField);

            VisualElement actionLine = new VisualElement();
            actionLine.style.flexDirection = FlexDirection.Row;
            actionLine.style.flexWrap = Wrap.Wrap;
            actionLine.style.flexShrink = 0;
            actionLine.style.marginTop = 4;
            toolbar.Add(actionLine);

            Button refreshButton = new Button(RefreshBindInfos)
            {
                text = "刷新/清理"
            };
            refreshButton.style.width = 90;
            actionLine.Add(refreshButton);

            Button validateButton = new Button(() => ValidateBindInfos(true))
            {
                text = "校验"
            };
            validateButton.style.width = 70;
            actionLine.Add(validateButton);

            Button sortButton = new Button(ShowSortMenu)
            {
                text = "排序方式"
            };
            sortButton.tooltip = "选择路径排序或 Hierarchy 顺序排序；代码生成器会使用相同规则。";
            sortButton.style.width = 80;
            actionLine.Add(sortButton);

            Button generateButton = new Button(GenerateCode)
            {
                text = "生成绑定代码"
            };
            generateButton.style.width = 110;
            actionLine.Add(generateButton);

            Button deleteButton = new Button(DeleteSelectedRows)
            {
                text = "删除选中"
            };
            deleteButton.style.width = 90;
            actionLine.Add(deleteButton);

            Button pingTargetButton = new Button(PingSelectedTarget)
            {
                text = "Ping Target"
            };
            pingTargetButton.style.width = 90;
            actionLine.Add(pingTargetButton);

            Button pingInfoButton = new Button(PingSelectedInfo)
            {
                text = "Ping Info"
            };
            pingInfoButton.style.width = 80;
            actionLine.Add(pingInfoButton);

            Button batchRenameButton = new Button(OpenBatchRename)
            {
                text = "批量重命名"
            };
            batchRenameButton.tooltip = "对选中的多条绑定统一加前缀/后缀、查找替换、正则替换，或按节点名+类型规则重建字段名。";
            batchRenameButton.style.width = 90;
            actionLine.Add(batchRenameButton);

            Button dedupButton = new Button(DeduplicateFieldNames)
            {
                text = "消歧重名"
            };
            dedupButton.tooltip = "为重复的字段名自动追加序号，快速消除重名异常。";
            dedupButton.style.width = 80;
            actionLine.Add(dedupButton);

            Button regenButton = new Button(RegenerateSelectedOrAllNames)
            {
                text = "重生成名称"
            };
            regenButton.tooltip = "按多层节点名+类型规则重新生成字段名。选中若干行则只重建选中项，未选中则重建全部。";
            regenButton.style.width = 90;
            actionLine.Add(regenButton);
        }

        /// <summary>
        /// 主区域：左侧分组列表 + 右侧绑定表格 + 下方详情面板。
        /// </summary>
        private void BuildMainArea(VisualElement root)
        {
            VisualElement main = new VisualElement();
            main.style.flexDirection = FlexDirection.Row;
            main.style.flexGrow = 1;
            main.style.minHeight = 180;
            root.Add(main);

            BuildGroupPanel(main);
            BuildRightPanel(main);
        }

        private void BuildGroupPanel(VisualElement main)
        {
            VisualElement left = new VisualElement();
            left.style.width = 180;
            left.style.minWidth = 160;
            left.style.marginRight = 5;
            left.style.flexDirection = FlexDirection.Column;
            main.Add(left);

            Label title = new Label("分组");
            title.style.unityFontStyleAndWeight = FontStyle.Bold;
            title.style.marginBottom = 3;
            left.Add(title);

            _groupListView = new ListView();
            _groupListView.itemsSource = _groups;
            _groupListView.fixedItemHeight = 24;
            _groupListView.selectionType = SelectionType.Single;
            _groupListView.style.flexGrow = 1;
            _groupListView.makeItem = MakeGroupItem;
            _groupListView.bindItem = BindGroupItem;
            _groupListView.onSelectionChange += OnGroupSelectionChanged;
            left.Add(_groupListView);
        }

        private VisualElement MakeGroupItem()
        {
            Label label = new Label();
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.paddingLeft = 4;
            return label;
        }

        private void BindGroupItem(VisualElement element, int index)
        {
            Label label = element as Label;
            if (label == null)
                return;

            if (index < 0 || index >= _groups.Count)
            {
                label.text = string.Empty;
                return;
            }

            GroupView group = _groups[index];
            label.text = group.DisplayName;

            if (group.errorCount > 0)
                label.style.color = new Color(1f, 0.45f, 0.35f);
            else
                label.style.color = Color.white;
        }

        private void BuildRightPanel(VisualElement main)
        {
            VisualElement right = new VisualElement();
            right.style.flexGrow = 1;
            right.style.minWidth = 0;
            right.style.flexDirection = FlexDirection.Column;
            main.Add(right);

            ScrollView tableScrollView = new ScrollView(ScrollViewMode.Horizontal);
            tableScrollView.style.flexGrow = 1;
            tableScrollView.style.minHeight = 90;
            right.Add(tableScrollView);

            VisualElement table = new VisualElement();
            table.style.width = 824;
            table.style.flexDirection = FlexDirection.Column;
            table.style.flexGrow = 1;
            tableScrollView.Add(table);

            BuildTableHeader(table);

            _bindListView = new ListView();
            _bindListView.itemsSource = _visibleRows;
            _bindListView.fixedItemHeight = 24;
            _bindListView.selectionType = SelectionType.Multiple;
            _bindListView.style.flexGrow = 1;
            _bindListView.style.minHeight = 90;
            _bindListView.makeItem = MakeRowItem;
            _bindListView.bindItem = BindRowItem;
            _bindListView.onSelectionChange += _ => UpdateDetailPanel();
            table.Add(_bindListView);

            BuildBottomBar(right);

            Label detailTitle = new Label("详情");
            detailTitle.style.unityFontStyleAndWeight = FontStyle.Bold;
            detailTitle.style.marginTop = 5;
            right.Add(detailTitle);

            // 详情区用垂直 ScrollView 包裹 IMGUIContainer。
            // 之前 IMGUIContainer 是固定高度且无滚动，内容变多（状态行/前缀行/全称预览/Target/Path/组件等）
            // 时底部会被直接裁掉，导致"详情显示不完整"。
            // 这里：ScrollView 固定视口高度（可滚动），IMGUIContainer 不设固定高度、按内容自适应撑开。
            ScrollView detailScroll = new ScrollView(ScrollViewMode.Vertical);
            detailScroll.style.height = 200;
            detailScroll.style.minHeight = 120;
            detailScroll.style.flexShrink = 0;
            detailScroll.style.borderBottomWidth = 1;
            detailScroll.style.borderTopWidth = 1;
            detailScroll.style.borderLeftWidth = 1;
            detailScroll.style.borderRightWidth = 1;
            detailScroll.style.borderBottomColor = new Color(0.22f, 0.22f, 0.22f);
            detailScroll.style.borderTopColor = new Color(0.22f, 0.22f, 0.22f);
            detailScroll.style.borderLeftColor = new Color(0.22f, 0.22f, 0.22f);
            detailScroll.style.borderRightColor = new Color(0.22f, 0.22f, 0.22f);
            right.Add(detailScroll);

            _detailIMGUI = new IMGUIContainer(DrawSelectedDetailIMGUI);
            _detailIMGUI.style.paddingLeft = 4;
            _detailIMGUI.style.paddingRight = 4;
            _detailIMGUI.style.paddingTop = 4;
            _detailIMGUI.style.paddingBottom = 4;
            detailScroll.Add(_detailIMGUI);
        }

        private void BuildTableHeader(VisualElement parent)
        {
            VisualElement header = new VisualElement();
            header.style.flexDirection = FlexDirection.Row;
            header.style.height = 24;
            header.style.marginBottom = 2;
            header.style.backgroundColor = new Color(0.16f, 0.16f, 0.16f);
            parent.Add(header);

            AddHeaderButton(header, "#", 44, SortColumn.Index);
            AddHeaderButton(header, "Field Name", 200, SortColumn.FieldName);
            AddHeaderButton(header, "Type", 130, SortColumn.Type);
            AddHeaderButton(header, "Path", 260, SortColumn.Path);
            AddHeaderButton(header, "Target", 160, SortColumn.Target);
            AddHeaderButton(header, "Status", 100, SortColumn.Status);
        }

        private void AddHeaderButton(VisualElement parent, string text, float width, SortColumn column)
        {
            Button button = new Button(() =>
            {
                if (_sortColumn == column)
                    _sortAscending = !_sortAscending;
                else
                {
                    _sortColumn = column;
                    _sortAscending = true;
                }

                RebuildAll(false);
            })
            {
                text = GetHeaderText(text, column)
            };

            button.style.width = width;
            button.style.height = 22;

            // Button 默认带左右 3px、上下 1px 的 margin，会让表头逐列相对数据行错位。
            // 这里把 margin 归零，使 Button 实际占位宽度严格等于 width，与下方数据行对齐。
            button.style.marginLeft = 0;
            button.style.marginRight = 0;
            button.style.marginTop = 0;
            button.style.marginBottom = 0;
            // 文本左对齐 + 左内边距，和数据行 cell 的 paddingLeft = 4 保持一致。
            button.style.paddingLeft = 4;
            button.style.paddingRight = 0;
            button.style.unityTextAlign = TextAnchor.MiddleLeft;

            parent.Add(button);
        }

        private string GetHeaderText(string text, SortColumn column)
        {
            if (_sortColumn != column)
                return text;

            return _sortAscending ? text + " ▲" : text + " ▼";
        }

        private VisualElement MakeRowItem()
        {
            VisualElement row = new VisualElement();
            row.style.flexDirection = FlexDirection.Row;
            row.style.height = 24;

            row.Add(MakeCell("index", 44));

            // Field Name 列拆成两块：只读的类型前缀 + 可编辑的语义名。
            // 自动命名是 前缀 + 语义（如 _goBg_fleet = _go + Bg_fleet），
            // 大多数改名只需要改语义部分，前缀锁定不动，避免误改破坏类型前缀约定。
            // 整块宽度仍为 200，和表头 Field Name 列对齐。
            VisualElement fieldCell = new VisualElement();
            fieldCell.name = "fieldCell";
            fieldCell.style.width = 200;
            fieldCell.style.flexDirection = FlexDirection.Row;
            fieldCell.style.alignItems = Align.Center;

            // 只读前缀标签。灰色显示，鼠标悬停能看到"类型前缀（只读）"提示。
            Label prefixLabel = new Label();
            prefixLabel.name = "fieldPrefix";
            prefixLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            prefixLabel.style.color = new Color(0.55f, 0.55f, 0.55f);
            prefixLabel.style.flexShrink = 0;
            prefixLabel.style.paddingLeft = 4;
            prefixLabel.tooltip = "类型前缀（只读，随绑定类型自动决定）";
            fieldCell.Add(prefixLabel);

            // 语义名输入框。isDelayed：仅回车或失焦提交，可从上到下 Tab 连续改名。
            TextField fieldInput = new TextField();
            fieldInput.name = "field";
            fieldInput.isDelayed = true;
            fieldInput.style.flexGrow = 1;
            fieldInput.style.flexShrink = 1;
            fieldInput.style.marginLeft = 0;
            fieldInput.style.marginRight = 0;
            fieldInput.style.marginTop = 1;
            fieldInput.style.marginBottom = 1;
            fieldInput.RegisterValueChangedCallback(OnRowFieldNameChanged);
            fieldCell.Add(fieldInput);

            row.Add(fieldCell);

            row.Add(MakeCell("type", 130));
            row.Add(MakeCell("path", 260));

            // Target 列做成可点击：点击后在工程/层级里定位（Ping）并选中目标节点，
            // 和详情面板里的行为一致，省去先选中行再点 Ping Target 的两步操作。
            Label targetCell = MakeCell("target", 160);
            targetCell.style.color = new Color(0.45f, 0.7f, 1f); // 用链接色提示可点击。
            targetCell.tooltip = "点击定位到该绑定的目标节点";
            targetCell.RegisterCallback<PointerDownEvent>(OnTargetCellClicked);
            row.Add(targetCell);
            row.Add(MakeCell("status", 100));

            return row;
        }

        private Label MakeCell(string name, float width)
        {
            Label label = new Label();
            label.name = name;
            label.style.width = width;
            label.style.unityTextAlign = TextAnchor.MiddleLeft;
            label.style.paddingLeft = 4;
            label.style.overflow = Overflow.Hidden;
            return label;
        }

        private void BindRowItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _visibleRows.Count)
                return;

            RowView row = _visibleRows[index];

            Label indexLabel = element.Q<Label>("index");
            Label prefixLabel = element.Q<Label>("fieldPrefix");
            TextField fieldInput = element.Q<TextField>("field");
            Label typeLabel = element.Q<Label>("type");
            Label pathLabel = element.Q<Label>("path");
            Label targetLabel = element.Q<Label>("target");
            Label statusLabel = element.Q<Label>("status");

            if (indexLabel != null) indexLabel.text = row.index.ToString();

            // 拆分前缀与语义名：前缀只读展示，语义名可编辑。
            string prefix = UIBindEditorUtility.GetFieldNamePrefix(row.info);
            string remainder = UIBindEditorUtility.GetFieldNameRemainder(row.info);

            if (prefixLabel != null)
                prefixLabel.text = prefix;

            if (fieldInput != null)
            {
                // 记录索引与前缀，供提交时定位数据并把前缀拼回。
                fieldInput.userData = new RowFieldEditContext { index = row.index, prefix = prefix };
                // SetValueWithoutNotify：绑定复用行时刷新文本不触发提交回调。
                fieldInput.SetValueWithoutNotify(remainder ?? string.Empty);
                // 悬停显示完整字段名，保证全称可见。
                fieldInput.tooltip = "完整字段名: " + (row.fieldName ?? string.Empty);
            }

            if (typeLabel != null) typeLabel.text = row.typeName;
            if (pathLabel != null) pathLabel.text = string.IsNullOrEmpty(row.path) ? "(root)" : row.path;

            if (targetLabel != null)
            {
                targetLabel.text = row.targetName;
                // 存目标节点引用，供点击定位使用（行会复用，必须每次刷新）。
                targetLabel.userData = row.info != null ? row.info.target : null;
            }

            if (statusLabel != null) statusLabel.text = row.hasError ? "异常" : "OK";

            Color normalColor = Color.white;
            Color errorColor = new Color(1f, 0.45f, 0.35f);
            Color linkColor = new Color(0.45f, 0.7f, 1f);

            if (fieldInput != null) fieldInput.style.color = row.hasError ? errorColor : normalColor;
            if (typeLabel != null) typeLabel.style.color = row.hasError ? errorColor : normalColor;
            if (pathLabel != null) pathLabel.style.color = row.hasError ? errorColor : normalColor;
            // Target 保持链接色提示可点击；异常时用错误色。
            if (targetLabel != null) targetLabel.style.color = row.hasError ? errorColor : linkColor;
            if (statusLabel != null) statusLabel.style.color = row.hasError ? errorColor : new Color(0.55f, 0.9f, 0.55f);
        }

        /// <summary>
        /// 点击 Target 列：定位并选中目标节点。
        /// </summary>
        private void OnTargetCellClicked(PointerDownEvent evt)
        {
            if (evt.target is not Label cell)
                return;

            GameObject target = cell.userData as GameObject;
            if (target == null)
                return;

            Selection.activeGameObject = target;
            EditorGUIUtility.PingObject(target);
        }

        /// <summary>
        /// 行内语义名输入框的编辑上下文。
        /// 记录该输入框对应的绑定索引，以及提交时需要拼回的类型前缀。
        /// </summary>
        private sealed class RowFieldEditContext
        {
            public int index;
            public string prefix;
        }

        /// <summary>
        /// 行内 Field Name 输入框提交回调（isDelayed，仅回车或失焦触发）。
        /// 输入框里只是语义名，这里把只读前缀拼回，组成完整字段名。
        /// </summary>
        private void OnRowFieldNameChanged(ChangeEvent<string> evt)
        {
            if (_bindData == null)
                return;

            if (evt.target is not TextField field || field.userData is not RowFieldEditContext ctx)
                return;

            IReadOnlyList<UIBindInfo> infos = _bindData.BindInfos;
            if (infos == null || ctx.index < 0 || ctx.index >= infos.Count)
                return;

            UIBindInfo info = infos[ctx.index];
            if (info == null)
                return;

            // 语义名 + 前缀组合成完整字段名。
            string remainder = evt.newValue ?? string.Empty;
            string newName = UIBindEditorUtility.ComposeFieldName(ctx.prefix, remainder);

            if (info.fieldName == newName)
                return;

            Undo.RecordObject(_bindData, "Rename UI Bind Field");
            info.fieldName = newName;
            EditorUtility.SetDirty(_bindData);

            // 延迟重建：避免在当前值变更事件处理过程中重建 ListView。
            // 重建只为刷新异常状态与分组统计，不影响输入焦点（提交已完成）。
            EditorApplication.delayCall += () =>
            {
                if (this != null)
                    RebuildAll(false);
            };
        }

        private void BuildBottomBar(VisualElement root)
        {
            VisualElement bottom = new VisualElement();
            bottom.style.flexDirection = FlexDirection.Row;
            bottom.style.height = 22;
            bottom.style.flexShrink = 0;
            bottom.style.marginTop = 4;
            bottom.style.marginBottom = 2;
            root.Add(bottom);

            _summaryLabel = new Label();
            _summaryLabel.style.flexGrow = 1;
            _summaryLabel.style.unityTextAlign = TextAnchor.MiddleLeft;
            _summaryLabel.style.overflow = Overflow.Hidden;
            bottom.Add(_summaryLabel);

            _selectedLabel = new Label();
            _selectedLabel.style.width = 100;
            _selectedLabel.style.unityTextAlign = TextAnchor.MiddleRight;
            _selectedLabel.style.flexShrink = 0;
            bottom.Add(_selectedLabel);
        }

        #endregion

        #region 数据构建与刷新

        private void RefreshBindDataField()
        {
            if (_bindDataField != null)
                _bindDataField.SetValueWithoutNotify(_bindData);
        }

        private void RebuildAll(bool clearSelection = true)
        {
            _groups.Clear();
            _allRows.Clear();
            _visibleRows.Clear();

            if (_bindData == null)
            {
                RefreshListViews();
                UpdateSummary();
                UpdateDetailPanel();
                return;
            }

            IReadOnlyList<UIBindInfo> infos = _bindData.BindInfos;
            if (infos == null)
            {
                RefreshListViews();
                UpdateSummary();
                UpdateDetailPanel();
                return;
            }

            Dictionary<string, int> fieldNameCounts = BuildFieldNameCountMap(infos);
            Dictionary<string, int> bindingKeyCounts = BuildBindingKeyCountMap(infos);

            string search = string.IsNullOrEmpty(_searchText) ? string.Empty : _searchText.Trim().ToLowerInvariant();

            for (int i = 0; i < infos.Count; i++)
            {
                UIBindInfo info = infos[i];

                string errorText = GetBindInfoErrorText(i, info, fieldNameCounts, bindingKeyCounts);
                bool hasError = !string.IsNullOrEmpty(errorText);

                if (!PassDisplayFilter(info, hasError))
                    continue;

                if (!IsMatchSearch(info, search))
                    continue;

                RowView row = CreateRowView(i, info, hasError, errorText);
                _allRows.Add(row);
            }

            BuildGroupsFromRows();

            if (!HasGroup(_selectedGroupName))
                _selectedGroupName = "全部";

            for (int i = 0; i < _allRows.Count; i++)
            {
                RowView row = _allRows[i];
                if (_selectedGroupName == "全部" || row.groupName == _selectedGroupName)
                    _visibleRows.Add(row);
            }

            SortVisibleRows();

            RefreshListViews();

            if (clearSelection && _bindListView != null)
                _bindListView.ClearSelection();

            SelectCurrentGroupInList();

            UpdateSummary();
            UpdateDetailPanel();
        }

        private RowView CreateRowView(int index, UIBindInfo info, bool hasError, string errorText)
        {
            string fieldName = info != null ? info.fieldName : string.Empty;
            string path = info != null ? info.path : string.Empty;
            string targetName = info != null && info.target != null ? info.target.name : "<Missing>";
            string typeName = GetBindInfoTypeDisplayName(info);
            string groupName = GetGroupName(info);

            return new RowView
            {
                index = index,
                info = info,
                groupName = groupName,
                fieldName = fieldName,
                typeName = typeName,
                path = path,
                targetName = targetName,
                hasError = hasError,
                errorText = errorText
            };
        }

        private void BuildGroupsFromRows()
        {
            Dictionary<string, GroupView> map = new Dictionary<string, GroupView>();

            GroupView all = new GroupView
            {
                name = "全部",
                count = _allRows.Count,
                errorCount = 0
            };

            for (int i = 0; i < _allRows.Count; i++)
            {
                RowView row = _allRows[i];

                if (row.hasError)
                    all.errorCount++;

                string groupName = string.IsNullOrEmpty(row.groupName) ? "(未分组)" : row.groupName;

                if (!map.TryGetValue(groupName, out GroupView group))
                {
                    group = new GroupView
                    {
                        name = groupName,
                        count = 0,
                        errorCount = 0
                    };

                    map.Add(groupName, group);
                }

                group.count++;

                if (row.hasError)
                    group.errorCount++;
            }

            _groups.Add(all);

            List<GroupView> sortedGroups = new List<GroupView>(map.Values);
            sortedGroups.Sort((a, b) => string.Compare(a.name, b.name, StringComparison.Ordinal));

            _groups.AddRange(sortedGroups);
        }

        private void SortVisibleRows()
        {
            _visibleRows.Sort((a, b) =>
            {
                int result;

                switch (_sortColumn)
                {
                    case SortColumn.Index:
                        result = a.index.CompareTo(b.index);
                        break;

                    case SortColumn.FieldName:
                        result = string.Compare(a.fieldName, b.fieldName, StringComparison.OrdinalIgnoreCase);
                        break;

                    case SortColumn.Type:
                        result = string.Compare(a.typeName, b.typeName, StringComparison.OrdinalIgnoreCase);
                        break;

                    case SortColumn.Path:
                        result = string.Compare(a.path, b.path, StringComparison.OrdinalIgnoreCase);
                        break;

                    case SortColumn.Target:
                        result = string.Compare(a.targetName, b.targetName, StringComparison.OrdinalIgnoreCase);
                        break;

                    case SortColumn.Status:
                        result = a.hasError.CompareTo(b.hasError);
                        break;

                    default:
                        result = a.index.CompareTo(b.index);
                        break;
                }

                return _sortAscending ? result : -result;
            });
        }

        private void RefreshListViews()
        {
            RefreshListView(_groupListView);
            RefreshListView(_bindListView);
        }

        private static void RefreshListView(ListView listView)
        {
            if (listView == null)
                return;

#if UNITY_2021_2_OR_NEWER
            listView.RefreshItems();
#else
            listView.Refresh();
#endif
        }

        private void UpdateSummary()
        {
            if (_summaryLabel == null)
                return;

            if (_bindData == null)
            {
                _summaryLabel.text = "未选择 UIBindData。";
                return;
            }

            int total = _bindData.BindInfos != null ? _bindData.BindInfos.Count : 0;
            int errorCount = 0;

            for (int i = 0; i < _allRows.Count; i++)
            {
                if (_allRows[i].hasError)
                    errorCount++;
            }

            _summaryLabel.text = $"Root: {_bindData.name}    总数: {total}    当前显示: {_visibleRows.Count}    当前筛选异常: {errorCount}";
        }

        private void UpdateDetailPanel()
        {
            if (_selectedLabel != null)
                _selectedLabel.text = $"选中: {GetSelectedRows().Count}";

            if (_detailIMGUI != null)
                _detailIMGUI.MarkDirtyRepaint();
        }

        #endregion

        #region 分组选择

        private void OnGroupSelectionChanged(IEnumerable<object> selectedObjects)
        {
            foreach (object obj in selectedObjects)
            {
                GroupView group = obj as GroupView;
                if (group == null)
                    continue;

                _selectedGroupName = group.name;
                RebuildAll();
                break;
            }
        }

        private bool HasGroup(string groupName)
        {
            for (int i = 0; i < _groups.Count; i++)
            {
                if (_groups[i].name == groupName)
                    return true;
            }

            return false;
        }

        private void SelectCurrentGroupInList()
        {
            if (_groupListView == null)
                return;

            int index = -1;
            for (int i = 0; i < _groups.Count; i++)
            {
                if (_groups[i].name == _selectedGroupName)
                {
                    index = i;
                    break;
                }
            }

            if (index >= 0)
                _groupListView.SetSelectionWithoutNotify(new[] { index });
            else
                _groupListView.ClearSelection();
        }

        #endregion

        #region 详情编辑

        /// <summary>
        /// 详情区域仍使用 IMGUI 绘制 SerializedProperty。
        /// 
        /// 原因：
        /// 1. 你的 UIBindInfo 是序列化数据；
        /// 2. IMGUI 对 SerializedProperty 的兼容性更直接；
        /// 3. 主列表已经使用 UI Toolkit ListView 虚拟化，性能瓶颈已解决。
        /// </summary>
        private void DrawSelectedDetailIMGUI()
        {
            if (_bindData == null)
            {
                EditorGUILayout.HelpBox("请选择一个 UIBindData。", MessageType.Info);
                return;
            }

            List<RowView> selectedRows = GetSelectedRows();

            if (selectedRows.Count == 0)
            {
                EditorGUILayout.HelpBox("请选择一条绑定查看详情。支持多选后批量删除。", MessageType.Info);
                return;
            }

            if (selectedRows.Count > 1)
            {
                EditorGUILayout.HelpBox($"当前选中 {selectedRows.Count} 条绑定。详情面板仅支持单条编辑，批量操作请使用上方按钮。", MessageType.Info);
                return;
            }

            RowView row = selectedRows[0];
            if (row == null || row.index < 0)
                return;

            // 复用同一个 SerializedObject，只有目标对象变化时才重建。
            // 每帧 new SerializedObject 会打断输入焦点，也会造成不必要的开销。
            if (_detailSerializedObject == null || _detailBoundData != _bindData)
            {
                _detailSerializedObject = new SerializedObject(_bindData);
                _detailBoundData = _bindData;
            }

            _detailSerializedObject.Update();

            SerializedProperty bindInfosProp = _detailSerializedObject.FindProperty("_bindInfos");

            if (bindInfosProp == null)
            {
                EditorGUILayout.HelpBox("未找到序列化字段 _bindInfos。", MessageType.Warning);
                return;
            }

            if (row.index >= bindInfosProp.arraySize)
            {
                EditorGUILayout.HelpBox("选中项索引已失效，请刷新。", MessageType.Warning);
                return;
            }

            SerializedProperty element = bindInfosProp.GetArrayElementAtIndex(row.index);

            SerializedProperty fieldName = element.FindPropertyRelative("fieldName");
            SerializedProperty typeName = element.FindPropertyRelative("typeName");
            SerializedProperty path = element.FindPropertyRelative("path");
            SerializedProperty targetProp = element.FindPropertyRelative("target");
            SerializedProperty componentProp = element.FindPropertyRelative("component");
            SerializedProperty isWidget = element.FindPropertyRelative("isWidget");
            SerializedProperty widgetTypeName = element.FindPropertyRelative("widgetTypeName");

            EditorGUI.BeginChangeCheck();

            EditorGUILayout.LabelField($"[{row.index}] {row.fieldName}", EditorStyles.boldLabel);

            // 错误提示改为固定占位的一行标签，不再用"出现即插入 HelpBox"的方式改变布局。
            // 这样每帧控件数量保持一致，IMGUI 控件 ID 序列稳定，输入 Field Name 不会中途丢焦点。
            DrawFixedStatusLine(row);

            // Field Name 拆成 前缀（只读）+ 语义名（可编辑）。
            // 前缀随类型自动决定、不允许手改；用户只编辑语义名，提交时前缀拼回。
            // DelayedTextField：仅回车或失焦提交。
            // 注意：这里用值比较赋值，不能再嵌套 BeginChangeCheck/EndChangeCheck，
            // 否则内层 EndChangeCheck 会消费 GUI.changed，导致外层负责 ApplyModifiedProperties
            // 的 EndChangeCheck 捕获不到本次改名，字段名写不回去。
            if (fieldName != null)
            {
                string prefix = UIBindEditorUtility.GetFieldNamePrefix(row.info);
                string remainder = UIBindEditorUtility.GetFieldNameRemainder(row.info);

                EditorGUILayout.BeginHorizontal();

                // 前缀只读展示。
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

                // 完整字段名预览：可直接选中复制，并提供一键复制按钮。
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

            if (isWidget != null)
            {
                EditorGUILayout.PropertyField(isWidget);

                if (isWidget.boolValue)
                {
                    if (widgetTypeName != null)
                        EditorGUILayout.PropertyField(widgetTypeName, new GUIContent("Widget Type Full Name"));
                }
                else
                {
                    GameObject targetGo = targetProp != null ? targetProp.objectReferenceValue as GameObject : null;
                    Type currentType = typeName != null ? UIBindEditorUtility.FindType(typeName.stringValue) : null;
                    string typeLabel = currentType != null ? UIBindEditorUtility.GetCompactTypeName(currentType) : GetShortTypeName(typeName != null ? typeName.stringValue : string.Empty);

                    EditorGUILayout.BeginHorizontal();

                    if (GUILayout.Button($"绑定类型: {typeLabel}", GUILayout.Height(22)))
                    {
                        ShowTypeSelectMenu(typeName, componentProp, fieldName, targetGo);
                    }

                    if (GUILayout.Button("修复组件引用", GUILayout.Width(100), GUILayout.Height(22)))
                    {
                        if (targetGo != null && currentType != null && componentProp != null)
                        {
                            componentProp.objectReferenceValue = UIBindEditorUtility.ResolveComponent(targetGo, currentType);
                        }
                    }

                    EditorGUILayout.EndHorizontal();

                    if (componentProp != null)
                        EditorGUILayout.PropertyField(componentProp);
                }
            }

            if (EditorGUI.EndChangeCheck())
            {
                _detailSerializedObject.ApplyModifiedProperties();
                EditorUtility.SetDirty(_bindData);

                /*
                 * 延迟刷新，避免在 IMGUI 当前绘制过程中直接重建 UI Toolkit ListView。
                 */
                EditorApplication.delayCall += () =>
                {
                    if (this != null)
                        RebuildAll(false);
                };
            }
        }

        /// <summary>
        /// 绘制固定占位的状态行。
        /// 无论有没有错误都占一行，只切换文字和颜色，
        /// 保证详情面板每帧控件数量恒定，避免编辑字段名时 IMGUI 控件 ID 漂移导致丢焦点。
        /// </summary>
        private void DrawFixedStatusLine(RowView row)
        {
            Color old = GUI.contentColor;

            if (row != null && row.hasError)
            {
                GUI.contentColor = new Color(1f, 0.45f, 0.35f);
                EditorGUILayout.LabelField("异常: " + row.errorText, EditorStyles.miniLabel);
            }
            else
            {
                GUI.contentColor = new Color(0.55f, 0.9f, 0.55f);
                EditorGUILayout.LabelField("状态: OK", EditorStyles.miniLabel);
            }

            GUI.contentColor = old;
        }

        /// <summary>
        /// 绘制可复制的完整字段名：只读可选中文本框 + 一键复制按钮。
        /// </summary>
        private void DrawCopyableFullName(string fullName)
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
                            componentProp.objectReferenceValue = UIBindEditorUtility.ResolveComponent(targetGo, capturedType);

                        if (fieldName != null && string.IsNullOrEmpty(fieldName.stringValue))
                            fieldName.stringValue = UIBindEditorUtility.BuildUniqueFieldName(targetGo, capturedType, _bindData.BindInfos, _bindData.transform);
                    }

                    _detailSerializedObject.ApplyModifiedProperties();
                    EditorUtility.SetDirty(_bindData);
                    RebuildAll(false);
                });
            }

            menu.ShowAsContext();
        }

        #endregion

        #region 按钮操作

        private void GenerateCode()
        {
            if (_bindData == null)
            {
                EditorUtility.DisplayDialog("提示", "请先选择 UIBindData。", "确定");
                return;
            }

            if (!ValidateBindInfos(true))
                return;

            UIBindScriptHolder holder = _bindData.GetComponent<UIBindScriptHolder>();
            MonoScript script = holder != null ? holder.targetScript : null;

            UICodeGenerator.Generate(_bindData, script);
        }

        private void ShowSortMenu()
        {
            if (_bindData == null)
                return;

            GenericMenu menu = new GenericMenu();
            menu.AddItem(new GUIContent("路径（名称路径）"), _bindData.SortMode == UIBindSortMode.Path, () => ApplySortMode(UIBindSortMode.Path));
            menu.AddItem(new GUIContent("Hierarchy（层级顺序）"), _bindData.SortMode == UIBindSortMode.Hierarchy, () => ApplySortMode(UIBindSortMode.Hierarchy));
            menu.ShowAsContext();
        }

        private void ApplySortMode(UIBindSortMode sortMode)
        {
            Undo.RecordObject(_bindData, "Change UI Bind Sort Mode");
            _bindData.SetSortMode(sortMode);
            SortBindInfos();
        }

        private void SortBindInfos()
        {
            if (_bindData == null)
                return;

            List<UIBindInfo> list = new List<UIBindInfo>(_bindData.BindInfos);
            UIBindEditorUtility.SortBindInfos(list, _bindData);

            Undo.RecordObject(_bindData, "Sort UI Bind Infos");
            _bindData.SetBindInfos(list);
            EditorUtility.SetDirty(_bindData);

            RebuildAll();
        }

        private void RefreshBindInfos()
        {
            if (_bindData == null)
                return;

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

            RebuildAll();
        }

        private void DeleteSelectedRows()
        {
            if (_bindData == null)
                return;

            List<RowView> selectedRows = GetSelectedRows();
            if (selectedRows.Count == 0)
                return;

            if (!EditorUtility.DisplayDialog("确认删除", $"确定删除选中的 {selectedRows.Count} 条绑定？", "确定", "取消"))
                return;

            HashSet<UIBindInfo> removeSet = new HashSet<UIBindInfo>();
            for (int i = 0; i < selectedRows.Count; i++)
            {
                if (selectedRows[i] != null && selectedRows[i].info != null)
                    removeSet.Add(selectedRows[i].info);
            }

            Undo.RecordObject(_bindData, "Delete Selected UI Bind Infos");

            List<UIBindInfo> list = new List<UIBindInfo>(_bindData.BindInfos);
            list.RemoveAll(info => info != null && removeSet.Contains(info));

            _bindData.SetBindInfos(list);
            EditorUtility.SetDirty(_bindData);

            RebuildAll();
        }

        private void PingSelectedTarget()
        {
            RowView row = GetFirstSelectedRow();
            if (row == null || row.info == null || row.info.target == null)
                return;

            Selection.activeGameObject = row.info.target;
            EditorGUIUtility.PingObject(row.info.target);
        }

        private void PingSelectedInfo()
        {
            RowView row = GetFirstSelectedRow();
            if (_bindData == null || row == null || row.info == null)
                return;

            UIBindDataEditor.PingBindInfo(_bindData, row.info);
        }

        /// <summary>
        /// 打开批量重命名弹窗，对当前选中的绑定进行统一改名。
        /// </summary>
        private void OpenBatchRename()
        {
            if (_bindData == null)
                return;

            List<RowView> selectedRows = GetSelectedRows();
            if (selectedRows.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先在列表中选择要批量重命名的绑定。", "确定");
                return;
            }

            // 收集选中项作为重命名目标。
            List<UIBindBatchRenameWindow.RenameTarget> targets = new List<UIBindBatchRenameWindow.RenameTarget>();
            HashSet<UIBindInfo> selectedSet = new HashSet<UIBindInfo>();

            for (int i = 0; i < selectedRows.Count; i++)
            {
                RowView r = selectedRows[i];
                if (r == null || r.info == null)
                    continue;

                selectedSet.Add(r.info);
                targets.Add(new UIBindBatchRenameWindow.RenameTarget
                {
                    info = r.info,
                    originalName = r.info.fieldName ?? string.Empty
                });
            }

            // 收集"未选中项"已占用的字段名，供批量结果跨项判重。
            HashSet<string> outsideNames = new HashSet<string>();
            IReadOnlyList<UIBindInfo> allInfos = _bindData.BindInfos;
            if (allInfos != null)
            {
                for (int i = 0; i < allInfos.Count; i++)
                {
                    UIBindInfo info = allInfos[i];
                    if (info == null || selectedSet.Contains(info) || string.IsNullOrEmpty(info.fieldName))
                        continue;

                    outsideNames.Add(info.fieldName);
                }
            }

            UIBindBatchRenameWindow.Open(targets, outsideNames, OnBatchRenameApplied);
        }

        /// <summary>
        /// 批量重命名弹窗应用后的回调：统一记录 Undo、标脏并刷新。
        /// </summary>
        private void OnBatchRenameApplied(List<UIBindBatchRenameWindow.RenameTarget> targets)
        {
            if (_bindData == null || targets == null || targets.Count == 0)
                return;

            // 目标名字已经由弹窗写入 info.fieldName，这里只需要登记 Undo 与刷新。
            Undo.RecordObject(_bindData, "Batch Rename UI Bind Fields");
            EditorUtility.SetDirty(_bindData);

            RebuildAll(false);
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
            RebuildAll(false);

            Debug.Log($"[UIBind] 已为 {renamed} 个重复字段名追加序号消歧。");
        }

        /// <summary>
        /// 按规则重生成字段名。选中若干行时只重建选中项，未选中时重建全部。
        /// </summary>
        private void RegenerateSelectedOrAllNames()
        {
            if (_bindData == null)
                return;

            IReadOnlyList<UIBindInfo> allInfos = _bindData.BindInfos;
            if (allInfos == null || allInfos.Count == 0)
                return;

            // 收集重建目标：优先用选中行，没选中则全部。
            List<UIBindInfo> targets = new List<UIBindInfo>();
            List<RowView> selectedRows = GetSelectedRows();

            if (selectedRows.Count > 0)
            {
                for (int i = 0; i < selectedRows.Count; i++)
                {
                    if (selectedRows[i] != null && selectedRows[i].info != null)
                        targets.Add(selectedRows[i].info);
                }
            }
            else
            {
                string scopeTip = $"未选中任何行，将按规则重建全部 {allInfos.Count} 个字段名。是否继续？";
                if (!EditorUtility.DisplayDialog("重生成名称", scopeTip, "全部重建", "取消"))
                    return;

                for (int i = 0; i < allInfos.Count; i++)
                {
                    if (allInfos[i] != null)
                        targets.Add(allInfos[i]);
                }
            }

            if (targets.Count == 0)
                return;

            Undo.RecordObject(_bindData, "Regenerate UI Bind Field Names");

            int renamed = UIBindEditorUtility.RegenerateFieldNames(targets, allInfos, _bindData.transform);

            if (renamed <= 0)
            {
                EditorUtility.DisplayDialog("提示", "字段名已符合规则，无需修改。", "确定");
                return;
            }

            EditorUtility.SetDirty(_bindData);
            RebuildAll(false);

            Debug.Log($"[UIBind] 已按规则重生成 {renamed} 个字段名。");
        }

        #endregion

        #region 选中项辅助

        private List<RowView> GetSelectedRows()
        {
            List<RowView> result = new List<RowView>();

            if (_bindListView == null)
                return result;

            foreach (int selectedIndex in _bindListView.selectedIndices)
            {
                if (selectedIndex >= 0 && selectedIndex < _visibleRows.Count)
                    result.Add(_visibleRows[selectedIndex]);
            }

            return result;
        }

        private RowView GetFirstSelectedRow()
        {
            List<RowView> rows = GetSelectedRows();
            if (rows.Count == 0)
                return null;

            return rows[0];
        }

        #endregion

        #region 校验与异常信息

        private bool ValidateBindInfos(bool showDialog)
        {
            if (_bindData == null)
            {
                if (showDialog)
                    EditorUtility.DisplayDialog("提示", "请先选择 UIBindData。", "确定");

                return false;
            }

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

        private static string GetBindInfoErrorText(
            int index,
            UIBindInfo info,
            Dictionary<string, int> fieldNameCounts,
            Dictionary<string, int> bindingKeyCounts)
        {
            if (info == null)
                return $"[{index}] 绑定数据为空。";

            List<string> errors = new List<string>();

            if (string.IsNullOrEmpty(info.fieldName))
            {
                errors.Add("字段名为空");
            }
            else if (fieldNameCounts != null &&
                     fieldNameCounts.TryGetValue(info.fieldName, out int fieldNameCount) &&
                     fieldNameCount > 1)
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
                            errors.Add("Target 上找不到组件: " + type.Name);
                    }
                }
            }

            string bindKey = BuildBindingKey(info);
            if (!string.IsNullOrEmpty(bindKey) &&
                bindingKeyCounts != null &&
                bindingKeyCounts.TryGetValue(bindKey, out int bindCount) &&
                bindCount > 1)
            {
                errors.Add("重复绑定: " + bindKey);
            }

            if (errors.Count == 0)
                return string.Empty;

            return $"[{index}] " + string.Join("；", errors);
        }

        private static Dictionary<string, int> BuildFieldNameCountMap(IReadOnlyList<UIBindInfo> infos)
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

        private static Dictionary<string, int> BuildBindingKeyCountMap(IReadOnlyList<UIBindInfo> infos)
        {
            Dictionary<string, int> map = new Dictionary<string, int>();

            if (infos == null)
                return map;

            for (int i = 0; i < infos.Count; i++)
            {
                UIBindInfo info = infos[i];
                string key = BuildBindingKey(info);

                if (string.IsNullOrEmpty(key))
                    continue;

                if (!map.ContainsKey(key))
                    map.Add(key, 0);

                map[key]++;
            }

            return map;
        }

        #endregion

        #region 过滤、搜索、分组

        private bool PassDisplayFilter(UIBindInfo info, bool hasError)
        {
            switch (_displayFilter)
            {
                case DisplayFilter.All:
                    return true;

                case DisplayFilter.ErrorOnly:
                    return hasError;

                case DisplayFilter.WidgetOnly:
                    return info != null && info.isWidget;

                case DisplayFilter.NormalOnly:
                    return info != null && !info.isWidget;

                default:
                    return true;
            }
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

        private string GetGroupName(UIBindInfo info)
        {
            if (info == null)
                return "Missing";

            switch (_groupMode)
            {
                case GroupMode.All:
                    return "全部";

                case GroupMode.PathArea:
                    return GetPathAreaGroupName(info.path);

                case GroupMode.Type:
                    return GetBindInfoTypeDisplayName(info);

                case GroupMode.WidgetOrNormal:
                    return info.isWidget ? "Widgets" : "Normal Components";

                default:
                    return "全部";
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

        private static bool ContainsIgnoreCase(string value, string lowerSearch)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            return value.ToLowerInvariant().Contains(lowerSearch);
        }

        #endregion

        #region 类型和排序辅助

        private static string GetBindInfoTypeDisplayName(UIBindInfo info)
        {
            if (info == null)
                return "Missing";

            if (info.isWidget)
            {
                string shortName = GetShortTypeName(info.widgetTypeName);
                return string.IsNullOrEmpty(shortName) ? "Widget" : shortName;
            }

            Type type = UIBindEditorUtility.FindType(info.typeName);
            if (type != null)
                return UIBindEditorUtility.GetCompactTypeName(type);

            return GetShortTypeName(info.typeName);
        }

        private static string GetShortTypeName(string fullName)
        {
            if (string.IsNullOrEmpty(fullName))
                return string.Empty;

            int index = fullName.LastIndexOf('.');
            return index >= 0 ? fullName.Substring(index + 1) : fullName;
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

        #region 查找绑定根

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

        #endregion
    }
}
#endif