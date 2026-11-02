using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FRFrame.Editor
{
    /// <summary>
    /// 一些静态 helper。
    /// 
    /// 单独抽成 helper，便于：
    /// 1. Window / Validator / 其他工具类复用
    /// 2. 后续继续拆分时避免重复代码
    /// </summary>
    public static class UIConfigEditorWindowHelper
    {
        public static string ConvertAssetPathToResourcesRuntimePath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;

            return assetPath.Replace("\\", "/");
        }

        public static string TryLoadPrefabAtRuntimeStylePath(string runtimePath)
        {
            if (string.IsNullOrWhiteSpace(runtimePath))
                return null;

            string normalized = runtimePath.Replace("\\", "/");
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(normalized);
            return prefab != null ? normalized : null;
        }

        public static string FindBestRuntimeAssetPathByName(string prefabName)
        {
            if (string.IsNullOrWhiteSpace(prefabName))
                return null;

            string[] guids = AssetDatabase.FindAssets($"{prefabName} t:Prefab");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid).Replace("\\", "/");
                return path;
            }

            return null;
        }

        public static string GuessNameFromAssetPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;

            return Path.GetFileNameWithoutExtension(assetPath.Replace("\\", "/"));
        }

        public static string GetShortTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            int idx = typeName.LastIndexOf('.');
            return idx >= 0 ? typeName.Substring(idx + 1) : typeName;
        }

        public static Type FindTypeByName(string typeName)
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

        public static string TryFindTypeFullName(string shortTypeName)
        {
            Type type = FindTypeByName(shortTypeName);
            return type?.FullName;
        }

        public static bool ContainsIgnoreCase(string src, string key)
        {
            if (string.IsNullOrEmpty(src) || string.IsNullOrEmpty(key))
                return false;

            return src.IndexOf(key, StringComparison.OrdinalIgnoreCase) >= 0;
        }

        public static void EnsureFolderExists(string folderPath)
        {
            folderPath = folderPath.Replace("\\", "/");
            if (AssetDatabase.IsValidFolder(folderPath))
                return;

            string[] parts = folderPath.Split('/');
            string current = parts[0];
            for (int i = 1; i < parts.Length; i++)
            {
                string next = $"{current}/{parts[i]}";
                if (!AssetDatabase.IsValidFolder(next))
                {
                    AssetDatabase.CreateFolder(current, parts[i]);
                }

                current = next;
            }
        }
    }

    public sealed class UIConfigEditorWindow : EditorWindow
    {
        private const string MenuPath = "FRFrame/UI/UI Config Editor";
        private const string DefaultCreateFolder = "Assets/Resources/UIConfigs";

        private const string UxmlPath = "Assets/FRFrame/Editor/UI/Config/UIConfigEditorWindow.uxml";
        private const string UssPath = "Assets/FRFrame/Editor/UI/Config/UIConfigEditorWindow.uss";

        private const string ScriptPickerControlId = "UIConfigEditorWindow_ScriptPicker";
        private const string PrefabPickerControlId = "UIConfigEditorWindow_PrefabPicker";
        
        private enum SortField
        {
            FormId,
            LogicType,
            AssetPath,
            ConfigGroup,
            PackageName,
            Layer,
            AllowMultiInstance,
            FullScreen,
            PauseCovered,
            BlockingMode,
            DisableGameObjectWhenHidden,
            CloseMode,
            CoverVisualMode,
            Resident,
            BringToFrontOnOpen,
            AllowBackClose,
            UseMask,
            MaskMode,
            OpenAnimation,
            CloseAnimation
        }

        private sealed class GroupListItem
        {
            public UIConfigGroup Group;
            public string Title;
            public string AssetPath;
        }

        // 数据缓存
        private readonly List<UIConfigGroup> _groups = new List<UIConfigGroup>();
        private readonly List<Type> _unconfiguredUITypes = new List<Type>();
        // 未配置 UI 类型选择 / 筛选状态
        private readonly List<Type> _selectedUnconfiguredTypes = new List<Type>();
        private bool _unconfiguredOnlyWithPrefab = false;
        private Toggle _unconfiguredOnlyWithPrefabToggle;
        private Button _addSelectedUnconfiguredButton;
        
        private List<UIConfigGroupDraftValidator.ValidationMessage> _validationMessages = new List<UIConfigGroupDraftValidator.ValidationMessage>();

        // 当前编辑状态
        private UIConfigGroupDraft _currentDraft;
        private UIConfigGroup _selectedGroupAsset;
        private UIFormDraftItem _selectedFormItem;
        private readonly List<UIFormDraftItem> _selectedFormItems = new List<UIFormDraftItem>();
        // 仅保存可跨 Form 复用的运行时参数；FormID、LogicType、AssetPath 不会进入剪贴板。
        private UIFormConfig _formSettingsClipboard;

        // 查询 / 排序 / 过滤
        private string _groupSearch = string.Empty;
        private string _formSearch = string.Empty;
        private string _unconfiguredSearch = string.Empty;
        private bool _showOnlyInvalid = false;
        private bool _sortAscending = true;
        private SortField _sortField = SortField.FormId;

        // 新建 group 配置
        private string _newGroupName = "UIConfigGroup_Default";
        private string _newGroupDisplayName = "Default";
        private string _createFolder = DefaultCreateFolder;

        // 批量工具配置
        private bool _batchAllowMultiInstance = true;
        private bool _batchFullScreen = false;
        private bool _batchPauseCovered = true;
        private bool _batchDisableGameObjectWhenHidden = false;
        private bool _batchResident = false;
        private string _batchPackageName = string.Empty;
        private bool _batchBringToFrontOnOpen = true;
        private bool _batchAllowBackClose = false;
        private bool _batchUseMask = false;
        private bool _batchCloseOnMaskClick = false;
        private UIGroupLayer _batchLayer = UIGroupLayer.Normal;
        private UIBlockingMode _batchBlockingMode = UIBlockingMode.FullScreenOnly;
        private UIFormCloseMode _batchCloseMode = UIFormCloseMode.Cache;
        private UICoverVisualMode _batchCoverVisualMode = UICoverVisualMode.HideByCanvasGroup;
        private UIMaskMode _batchMaskMode = UIMaskMode.None;
        private UIAnimationPreset _batchOpenAnimation = UIAnimationPreset.None;
        private UIAnimationPreset _batchCloseAnimation = UIAnimationPreset.None;

        private ListView _groupListView;
        private ListView _formListView;
        private ListView _validationListView;
        private ListView _unconfiguredListView;

        private TextField _groupSearchField;
        private TextField _formSearchField;
        private TextField _unconfiguredSearchField;

        private TextField _newGroupAssetNameField;
        private TextField _newGroupDisplayNameField;
        private TextField _createFolderField;

        private Label _groupHeaderLabel;
        private Label _groupPathLabel;
        private Label _formListSummaryLabel;
        private Label _detailTitleLabel;
        private Label _statusLabel;

        private TextField _draftGroupNameField;
        private TextField _draftDefaultPackageNameField;
        private Toggle _draftLoadOnInitializeToggle;

        private TextField _formIdField;
        private TextField _logicTypeField;
        private TextField _assetPathField;
        private TextField _configGroupField;
        private TextField _packageNameField;
        private EnumField _layerField;
        private Toggle _allowMultiInstanceField;
        private Toggle _fullScreenField;
        private Toggle _pauseCoveredField;
        private EnumField _blockingModeField;
        private Toggle _disableGameObjectWhenHiddenField;
        private EnumField _closeModeField;
        private EnumField _coverVisualModeField;
        private Toggle _residentField;
        private Toggle _bringToFrontOnOpenField;
        private Toggle _allowBackCloseField;
        private Toggle _useMaskField;
        private Toggle _closeOnMaskClickField;
        private EnumField _maskModeField;
        private ColorField _maskColorField;
        private EnumField _openAnimationField;
        private EnumField _closeAnimationField;
        private FloatField _openAnimationDurationField;
        private FloatField _closeAnimationDurationField;
        private EnumField _openAnimationEaseField;
        private EnumField _closeAnimationEaseField;

        private Toggle _showOnlyInvalidToggle;
        private EnumField _sortFieldEnum;
        private Toggle _sortAscendingToggle;

        private EnumField _batchLayerField;
        private TextField _batchPackageNameField;
        private EnumField _batchBlockingModeField;
        private EnumField _batchCloseModeField;
        private EnumField _batchCoverVisualModeField;
        private Toggle _batchAllowMultiInstanceToggle;
        private Toggle _batchFullScreenToggle;
        private Toggle _batchPauseCoveredToggle;
        private Toggle _batchDisableGameObjectWhenHiddenToggle;
        private Toggle _batchResidentToggle;
        private Toggle _batchBringTopToggle;
        private Toggle _batchAllowBackCloseToggle;
        private Toggle _batchUseMaskToggle;
        private Toggle _batchCloseOnMaskClickToggle;
        private EnumField _batchMaskModeField;
        private EnumField _batchOpenAnimationField;
        private EnumField _batchCloseAnimationField;
        private UIFormDraftItem _lastPrimarySelectedFormItem;
        private string _lastPrimarySelectedFormId;
        private bool _isDragHovering;
        private UIConfigGroup _lastSelectedGroupAsset;
        private VisualElement _formListPanelRoot;
        private List<GroupListItem> _visibleGroupItems = new List<GroupListItem>();
        private List<UIFormDraftItem> _visibleFormItems = new List<UIFormDraftItem>();
        private List<Type> _visibleUnconfiguredTypes = new List<Type>();
        
        [MenuItem(MenuPath)]
        public static void Open()
        {
            UIConfigEditorWindow window = GetWindow<UIConfigEditorWindow>("UI Config Editor (UI 配置编辑器)");
            window.minSize = new Vector2(1500f, 760f);
            window.Show();
        }

        private void OnEnable()
        {
            RefreshGroups();
        }

        public void CreateGUI()
        {
            rootVisualElement.Clear();

            VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
            StyleSheet uss = AssetDatabase.LoadAssetAtPath<StyleSheet>(UssPath);

            if (uxml == null)
            {
                rootVisualElement.Add(new Label($"未找到 UXML: {UxmlPath}"));
                return;
            }

            uxml.CloneTree(rootVisualElement);

            if (uss != null)
            {
                rootVisualElement.styleSheets.Add(uss);
            }

            QueryElements();
            EnsureUnconfiguredToolbar();
            BindToolbarButtons();
            BindInputs();
            ConfigureListViews();
            
            RegisterFormListInteractions();

            RefreshAllViews();
        }

        #region Query / Init

        private void QueryElements()
        {
            _groupSearchField = rootVisualElement.Q<TextField>("group-search-field");
            _formSearchField = rootVisualElement.Q<TextField>("form-search-field");
            _unconfiguredSearchField = rootVisualElement.Q<TextField>("unconfigured-search-field");

            _newGroupAssetNameField = rootVisualElement.Q<TextField>("new-group-asset-name-field");
            _newGroupDisplayNameField = rootVisualElement.Q<TextField>("new-group-display-name-field");
            _createFolderField = rootVisualElement.Q<TextField>("create-folder-field");

            _groupListView = rootVisualElement.Q<ListView>("group-list-view");
            _formListView = rootVisualElement.Q<ListView>("form-list-view");
            _validationListView = rootVisualElement.Q<ListView>("validation-list-view");
            _unconfiguredListView = rootVisualElement.Q<ListView>("unconfigured-list-view");

            _groupHeaderLabel = rootVisualElement.Q<Label>("group-header-label");
            _groupPathLabel = rootVisualElement.Q<Label>("group-path-label");
            _formListSummaryLabel = rootVisualElement.Q<Label>("form-list-summary-label");
            _detailTitleLabel = rootVisualElement.Q<Label>("detail-title-label");
            _statusLabel = rootVisualElement.Q<Label>("status-label");

            _draftGroupNameField = rootVisualElement.Q<TextField>("draft-group-name-field");
            _draftDefaultPackageNameField = rootVisualElement.Q<TextField>("draft-default-package-name-field");
            _draftLoadOnInitializeToggle = rootVisualElement.Q<Toggle>("draft-load-on-initialize-toggle");

            _formIdField = rootVisualElement.Q<TextField>("field-form-id");
            _logicTypeField = rootVisualElement.Q<TextField>("field-logic-type");
            _assetPathField = rootVisualElement.Q<TextField>("field-asset-path");
            _configGroupField = rootVisualElement.Q<TextField>("field-config-group");
            _packageNameField = rootVisualElement.Q<TextField>("field-package-name");
            _layerField = rootVisualElement.Q<EnumField>("field-layer");
            _allowMultiInstanceField = rootVisualElement.Q<Toggle>("field-allow-multi-instance");
            _fullScreenField = rootVisualElement.Q<Toggle>("field-full-screen");
            _pauseCoveredField = rootVisualElement.Q<Toggle>("field-pause-covered");
            _blockingModeField = rootVisualElement.Q<EnumField>("field-blocking-mode");
            _disableGameObjectWhenHiddenField = rootVisualElement.Q<Toggle>("field-disable-game-object-when-hidden");
            _closeModeField = rootVisualElement.Q<EnumField>("field-close-mode");
            _coverVisualModeField = rootVisualElement.Q<EnumField>("field-cover-visual-mode");
            _residentField = rootVisualElement.Q<Toggle>("field-resident");
            _bringToFrontOnOpenField = rootVisualElement.Q<Toggle>("field-bring-top");
            _allowBackCloseField = rootVisualElement.Q<Toggle>("field-allow-back-close");
            _useMaskField = rootVisualElement.Q<Toggle>("field-use-mask");
            _closeOnMaskClickField = rootVisualElement.Q<Toggle>("field-close-on-mask-click");
            _maskModeField = rootVisualElement.Q<EnumField>("field-mask-mode");
            _maskColorField = rootVisualElement.Q<ColorField>("field-mask-color");
            _openAnimationField = rootVisualElement.Q<EnumField>("field-open-animation");
            _closeAnimationField = rootVisualElement.Q<EnumField>("field-close-animation");
            _openAnimationDurationField = rootVisualElement.Q<FloatField>("field-open-animation-duration");
            _closeAnimationDurationField = rootVisualElement.Q<FloatField>("field-close-animation-duration");
            _openAnimationEaseField = rootVisualElement.Q<EnumField>("field-open-animation-ease");
            _closeAnimationEaseField = rootVisualElement.Q<EnumField>("field-close-animation-ease");

            _showOnlyInvalidToggle = rootVisualElement.Q<Toggle>("toggle-show-only-invalid");
            _sortFieldEnum = rootVisualElement.Q<EnumField>("sort-field-enum");
            _sortAscendingToggle = rootVisualElement.Q<Toggle>("toggle-sort-ascending");

            _batchLayerField = rootVisualElement.Q<EnumField>("batch-layer-field");
            _batchPackageNameField = rootVisualElement.Q<TextField>("batch-package-name-field");
            _batchBlockingModeField = rootVisualElement.Q<EnumField>("batch-blocking-mode-field");
            _batchCloseModeField = rootVisualElement.Q<EnumField>("batch-close-mode-field");
            _batchCoverVisualModeField = rootVisualElement.Q<EnumField>("batch-cover-visual-mode-field");
            _batchAllowMultiInstanceToggle = rootVisualElement.Q<Toggle>("batch-allow-multi-instance-toggle");
            _batchFullScreenToggle = rootVisualElement.Q<Toggle>("batch-full-screen-toggle");
            _batchPauseCoveredToggle = rootVisualElement.Q<Toggle>("batch-pause-covered-toggle");
            _batchDisableGameObjectWhenHiddenToggle = rootVisualElement.Q<Toggle>("batch-disable-game-object-when-hidden-toggle");
            _batchResidentToggle = rootVisualElement.Q<Toggle>("batch-resident-toggle");
            _batchBringTopToggle = rootVisualElement.Q<Toggle>("batch-bring-top-toggle");
            _batchAllowBackCloseToggle = rootVisualElement.Q<Toggle>("batch-allow-back-close-toggle");
            _batchUseMaskToggle = rootVisualElement.Q<Toggle>("batch-use-mask-toggle");
            _batchCloseOnMaskClickToggle = rootVisualElement.Q<Toggle>("batch-close-on-mask-click-toggle");
            _batchMaskModeField = rootVisualElement.Q<EnumField>("batch-mask-mode-field");
            _batchOpenAnimationField = rootVisualElement.Q<EnumField>("batch-open-animation-field");
            _batchCloseAnimationField = rootVisualElement.Q<EnumField>("batch-close-animation-field");

            _newGroupAssetNameField?.SetValueWithoutNotify(_newGroupName);
            _newGroupDisplayNameField?.SetValueWithoutNotify(_newGroupDisplayName);
            _createFolderField?.SetValueWithoutNotify(_createFolder);

            _sortFieldEnum?.Init(_sortField);
            _sortAscendingToggle?.SetValueWithoutNotify(_sortAscending);
            _showOnlyInvalidToggle?.SetValueWithoutNotify(_showOnlyInvalid);

            _layerField?.Init(UIGroupLayer.Normal);
            _closeModeField?.Init(UIFormCloseMode.Cache);
            _blockingModeField?.Init(UIBlockingMode.FullScreenOnly);
            _coverVisualModeField?.Init(UICoverVisualMode.HideByCanvasGroup);
            _maskModeField?.Init(UIMaskMode.None);
            _openAnimationField?.Init(UIAnimationPreset.None);
            _closeAnimationField?.Init(UIAnimationPreset.None);
            _openAnimationEaseField?.Init(UIAnimationEase.OutQuad);
            _closeAnimationEaseField?.Init(UIAnimationEase.InQuad);

            _batchLayerField?.Init(_batchLayer);
            _batchBlockingModeField?.Init(_batchBlockingMode);
            _batchCloseModeField?.Init(_batchCloseMode);
            _batchCoverVisualModeField?.Init(_batchCoverVisualMode);
            _batchMaskModeField?.Init(_batchMaskMode);
            _batchOpenAnimationField?.Init(_batchOpenAnimation);
            _batchCloseAnimationField?.Init(_batchCloseAnimation);
            _batchPackageNameField?.SetValueWithoutNotify(_batchPackageName);

            _batchAllowMultiInstanceToggle?.SetValueWithoutNotify(_batchAllowMultiInstance);
            _batchFullScreenToggle?.SetValueWithoutNotify(_batchFullScreen);
            _batchPauseCoveredToggle?.SetValueWithoutNotify(_batchPauseCovered);
            _batchDisableGameObjectWhenHiddenToggle?.SetValueWithoutNotify(_batchDisableGameObjectWhenHidden);
            _batchResidentToggle?.SetValueWithoutNotify(_batchResident);
            _batchBringTopToggle?.SetValueWithoutNotify(_batchBringToFrontOnOpen);
            _batchAllowBackCloseToggle?.SetValueWithoutNotify(_batchAllowBackClose);
            _batchUseMaskToggle?.SetValueWithoutNotify(_batchUseMask);
            _batchCloseOnMaskClickToggle?.SetValueWithoutNotify(_batchCloseOnMaskClick);
            _formListPanelRoot = rootVisualElement.Q<VisualElement>("form-list-panel") ?? (_formListView as VisualElement);
        }

        private void EnsureUnconfiguredToolbar()
        {
            if (_unconfiguredListView == null || _unconfiguredListView.parent == null)
                return;

            VisualElement parent = _unconfiguredListView.parent;

            if (parent.Q<VisualElement>("unconfigured-extra-toolbar") != null)
                return;

            VisualElement toolbar = new VisualElement
            {
                name = "unconfigured-extra-toolbar"
            };
            toolbar.style.flexDirection = FlexDirection.Row;
            toolbar.style.alignItems = Align.Center;
            toolbar.style.marginTop = 4;
            toolbar.style.marginBottom = 4;

            _unconfiguredOnlyWithPrefabToggle = new Toggle("仅显示有同名Prefab")
            {
                value = _unconfiguredOnlyWithPrefab
            };
            _unconfiguredOnlyWithPrefabToggle.RegisterValueChangedCallback(evt =>
            {
                _unconfiguredOnlyWithPrefab = evt.newValue;
                RefreshUnconfiguredListView();
            });

            _addSelectedUnconfiguredButton = new Button(AddSelectedUnconfiguredToCurrentGroup)
            {
                text = "加入选中项"
            };
            _addSelectedUnconfiguredButton.style.marginLeft = 8;

            Button clearSelectionButton = new Button(() =>
                {
                    _selectedUnconfiguredTypes.Clear();
                    _unconfiguredListView?.ClearSelection();
                    RefreshStatusLabel();
                })
                {
                    text = "清空选择"
                };
            clearSelectionButton.style.marginLeft = 4;

            toolbar.Add(_unconfiguredOnlyWithPrefabToggle);
            toolbar.Add(_addSelectedUnconfiguredButton);
            toolbar.Add(clearSelectionButton);

            int listIndex = parent.IndexOf(_unconfiguredListView);
            if (listIndex >= 0)
                parent.Insert(listIndex, toolbar);
            else
                parent.Add(toolbar);
        }
        
        private void BindToolbarButtons()
        {
            BindButton("btn-refresh", () =>
            {
                CacheGroupSelection();
                RefreshGroups();
                RefreshAllViews();
            });

            BindButton("btn-save-current", () => SaveCurrentDraft());
            BindButton("btn-revert-current", RevertCurrentDraft);
            BindButton("btn-validate-current", ValidateCurrentDraft);
            BindButton("btn-validate-all", ValidateAllAssets);
            BindButton("btn-ping-folder", PingDefaultFolder);

            BindButton("btn-ping-group", PingSelectedGroupAsset);
            BindButton("btn-delete-group", DeleteSelectedGroup);

            BindButton("btn-select-folder", SelectCreateFolder);
            BindButton("btn-create-group", CreateNewGroup);
            BindButton("btn-scan-groups", () =>
            {
                CacheGroupSelection();
                RefreshGroups();
                RefreshAllViews();
            });

            BindButton("btn-use-asset-name", UseAssetNameOverwriteGroupName);
            BindButton("btn-ping-group-right", PingSelectedGroupAsset);
            BindButton("btn-copy-group-path", CopySelectedGroupPath);

            BindButton("btn-add-empty-form", AddEmptyForm);
            BindButton("btn-add-from-selection", AddFormsFromSelection);
            BindButton("btn-duplicate-selected-forms", DuplicateSelectedForms);
            BindButton("btn-delete-selected-forms", DeleteSelectedForms);

            BindButton("btn-move-up", () => MoveSelectedPrimaryForm(-1));
            BindButton("btn-move-down", () => MoveSelectedPrimaryForm(1));
            BindButton("btn-auto-fix-selected", AutoFixSelectedForms);
            BindButton("btn-sort-forms", SortCurrentForms);

            BindButton("btn-pick-script", PickScriptForSelectedFormLogicType);
            BindButton("btn-pick-prefab", PickPrefabForSelectedFormPath);

            BindButton("btn-auto-fix-current", AutoFixSelectedPrimaryForm);
            BindButton("btn-duplicate-current", DuplicateSelectedPrimaryForm);
            BindButton("btn-delete-current", DeleteSelectedPrimaryForm);

            BindButton("btn-apply-batch-layer", ApplyBatchLayerToSelected);
            BindButton("btn-apply-batch-package-name", ApplyBatchPackageNameToSelected);
            BindButton("btn-apply-batch-blocking-mode", ApplyBatchBlockingModeToSelected);
            BindButton("btn-apply-batch-close-mode", ApplyBatchCloseModeToSelected);
            BindButton("btn-apply-batch-cover-visual", ApplyBatchCoverVisualToSelected);
            BindButton("btn-apply-batch-mask", ApplyBatchMaskToSelected);
            BindButton("btn-apply-batch-animation", ApplyBatchAnimationToSelected);
            BindButton("btn-apply-batch-bools", ApplyBatchBoolsToSelected);
            BindButton("btn-apply-config-group-current", ApplyBatchConfigGroupToSelected);

            BindButton("btn-scan-unconfigured", ScanUnconfiguredUITypes);
            BindButton("btn-add-all-unconfigured", AddAllUnconfiguredToCurrentGroup);
        }

        private void BindInputs()
        {
            if (_groupSearchField != null)
            {
                _groupSearchField.RegisterValueChangedCallback(evt =>
                {
                    _groupSearch = evt.newValue ?? string.Empty;
                    RefreshGroupListView();
                });
            }

            if (_formSearchField != null)
            {
                _formSearchField.RegisterValueChangedCallback(evt =>
                {
                    _formSearch = evt.newValue ?? string.Empty;
                    RefreshFormListView();
                });
            }

            if (_unconfiguredSearchField != null)
            {
                _unconfiguredSearchField.RegisterValueChangedCallback(evt =>
                {
                    _unconfiguredSearch = evt.newValue ?? string.Empty;
                    RefreshUnconfiguredListView();
                });
            }

            if (_newGroupAssetNameField != null)
                _newGroupAssetNameField.RegisterValueChangedCallback(evt => _newGroupName = evt.newValue ?? string.Empty);

            if (_newGroupDisplayNameField != null)
                _newGroupDisplayNameField.RegisterValueChangedCallback(evt => _newGroupDisplayName = evt.newValue ?? string.Empty);

            if (_createFolderField != null)
                _createFolderField.RegisterValueChangedCallback(evt => _createFolder = evt.newValue ?? string.Empty);

            if (_showOnlyInvalidToggle != null)
            {
                _showOnlyInvalidToggle.RegisterValueChangedCallback(evt =>
                {
                    _showOnlyInvalid = evt.newValue;
                    RefreshFormListView();
                });
            }

            if (_sortFieldEnum != null)
            {
                _sortFieldEnum.RegisterValueChangedCallback(evt =>
                {
                    _sortField = (SortField)evt.newValue;
                    RefreshFormListView();
                });
            }

            if (_sortAscendingToggle != null)
            {
                _sortAscendingToggle.RegisterValueChangedCallback(evt =>
                {
                    _sortAscending = evt.newValue;
                    RefreshFormListView();
                });
            }

            if (_draftGroupNameField != null)
            {
                _draftGroupNameField.RegisterValueChangedCallback(evt =>
                {
                    if (_currentDraft == null) return;
                    _currentDraft.groupName = evt.newValue ?? string.Empty;
                    MarkCurrentDraftDirty();
                    RefreshFormDetailFromSelection();
                    RefreshFormListView();
                });
            }

            if (_draftDefaultPackageNameField != null)
            {
                _draftDefaultPackageNameField.RegisterValueChangedCallback(evt =>
                {
                    if (_currentDraft == null) return;
                    _currentDraft.defaultPackageName = evt.newValue ?? string.Empty;
                    MarkCurrentDraftDirty();
                });
            }

            if (_draftLoadOnInitializeToggle != null)
            {
                _draftLoadOnInitializeToggle.RegisterValueChangedCallback(evt =>
                {
                    if (_currentDraft == null) return;
                    _currentDraft.loadOnInitialize = evt.newValue;
                    MarkCurrentDraftDirty();
                });
            }

            if (_formIdField != null)
                _formIdField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.formId = evt.newValue ?? string.Empty));
            if (_logicTypeField != null)
                _logicTypeField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.logicType = evt.newValue ?? string.Empty));
            if (_assetPathField != null)
                _assetPathField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.assetPath = evt.newValue ?? string.Empty));
            if (_configGroupField != null)
                _configGroupField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.configGroup = evt.newValue ?? string.Empty));
            if (_packageNameField != null)
                _packageNameField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.packageName = evt.newValue ?? string.Empty));
            if (_layerField != null)
                _layerField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.layer = (UIGroupLayer)evt.newValue));
            if (_allowMultiInstanceField != null)
                _allowMultiInstanceField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.allowMultiInstance = evt.newValue));
            if (_fullScreenField != null)
                _fullScreenField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.fullScreen = evt.newValue));
            if (_pauseCoveredField != null)
                _pauseCoveredField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.pauseCovered = evt.newValue));
            if (_blockingModeField != null)
                _blockingModeField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.blockingMode = (UIBlockingMode)evt.newValue));
            if (_disableGameObjectWhenHiddenField != null)
                _disableGameObjectWhenHiddenField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.disableGameObjectWhenHidden = evt.newValue));
            if (_closeModeField != null)
                _closeModeField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.closeMode = (UIFormCloseMode)evt.newValue));
            if (_coverVisualModeField != null)
                _coverVisualModeField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.coverVisualMode = (UICoverVisualMode)evt.newValue));
            if (_residentField != null)
                _residentField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.resident = evt.newValue));
            if (_bringToFrontOnOpenField != null)
                _bringToFrontOnOpenField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.bringToFrontOnOpen = evt.newValue));
            if (_allowBackCloseField != null)
                _allowBackCloseField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.allowBackClose = evt.newValue));
            if (_useMaskField != null)
                _useMaskField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.useMask = evt.newValue));
            if (_closeOnMaskClickField != null)
                _closeOnMaskClickField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.closeOnMaskClick = evt.newValue));
            if (_maskModeField != null)
                _maskModeField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.maskMode = (UIMaskMode)evt.newValue));
            if (_maskColorField != null)
                _maskColorField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.maskColor = evt.newValue));
            if (_openAnimationField != null)
                _openAnimationField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.openAnimation = (UIAnimationPreset)evt.newValue));
            if (_closeAnimationField != null)
                _closeAnimationField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.closeAnimation = (UIAnimationPreset)evt.newValue));
            if (_openAnimationDurationField != null)
                _openAnimationDurationField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.openAnimationDuration = Mathf.Max(0f, evt.newValue)));
            if (_closeAnimationDurationField != null)
                _closeAnimationDurationField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.closeAnimationDuration = Mathf.Max(0f, evt.newValue)));
            if (_openAnimationEaseField != null)
                _openAnimationEaseField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.openAnimationEase = (UIAnimationEase)evt.newValue));
            if (_closeAnimationEaseField != null)
                _closeAnimationEaseField.RegisterValueChangedCallback(evt => UpdateSelectedFormField(cfg => cfg.closeAnimationEase = (UIAnimationEase)evt.newValue));

            if (_batchLayerField != null)
                _batchLayerField.RegisterValueChangedCallback(evt => _batchLayer = (UIGroupLayer)evt.newValue);
            if (_batchPackageNameField != null)
                _batchPackageNameField.RegisterValueChangedCallback(evt => _batchPackageName = evt.newValue ?? string.Empty);
            if (_batchBlockingModeField != null)
                _batchBlockingModeField.RegisterValueChangedCallback(evt => _batchBlockingMode = (UIBlockingMode)evt.newValue);
            if (_batchCloseModeField != null)
                _batchCloseModeField.RegisterValueChangedCallback(evt => _batchCloseMode = (UIFormCloseMode)evt.newValue);
            if (_batchCoverVisualModeField != null)
                _batchCoverVisualModeField.RegisterValueChangedCallback(evt => _batchCoverVisualMode = (UICoverVisualMode)evt.newValue);

            if (_batchAllowMultiInstanceToggle != null)
                _batchAllowMultiInstanceToggle.RegisterValueChangedCallback(evt => _batchAllowMultiInstance = evt.newValue);
            if (_batchFullScreenToggle != null)
                _batchFullScreenToggle.RegisterValueChangedCallback(evt => _batchFullScreen = evt.newValue);
            if (_batchPauseCoveredToggle != null)
                _batchPauseCoveredToggle.RegisterValueChangedCallback(evt => _batchPauseCovered = evt.newValue);
            if (_batchDisableGameObjectWhenHiddenToggle != null)
                _batchDisableGameObjectWhenHiddenToggle.RegisterValueChangedCallback(evt => _batchDisableGameObjectWhenHidden = evt.newValue);
            if (_batchResidentToggle != null)
                _batchResidentToggle.RegisterValueChangedCallback(evt => _batchResident = evt.newValue);
            if (_batchBringTopToggle != null)
                _batchBringTopToggle.RegisterValueChangedCallback(evt => _batchBringToFrontOnOpen = evt.newValue);
            if (_batchAllowBackCloseToggle != null)
                _batchAllowBackCloseToggle.RegisterValueChangedCallback(evt => _batchAllowBackClose = evt.newValue);
            if (_batchUseMaskToggle != null)
                _batchUseMaskToggle.RegisterValueChangedCallback(evt => _batchUseMask = evt.newValue);
            if (_batchCloseOnMaskClickToggle != null)
                _batchCloseOnMaskClickToggle.RegisterValueChangedCallback(evt => _batchCloseOnMaskClick = evt.newValue);
            if (_batchMaskModeField != null)
                _batchMaskModeField.RegisterValueChangedCallback(evt => _batchMaskMode = (UIMaskMode)evt.newValue);
            if (_batchOpenAnimationField != null)
                _batchOpenAnimationField.RegisterValueChangedCallback(evt => _batchOpenAnimation = (UIAnimationPreset)evt.newValue);
            if (_batchCloseAnimationField != null)
                _batchCloseAnimationField.RegisterValueChangedCallback(evt => _batchCloseAnimation = (UIAnimationPreset)evt.newValue);
        }

        private void ConfigureListViews()
        {
            if (_groupListView != null)
            {
                _groupListView.selectionType = SelectionType.Single;
                _groupListView.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
                _groupListView.fixedItemHeight = 58;
                _groupListView.makeItem = MakeGroupItem;
                _groupListView.bindItem = BindGroupItem;
                _groupListView.selectionChanged += OnGroupSelectionChanged;
            }

            if (_formListView != null)
            {
                _formListView.selectionType = SelectionType.Multiple;
                _formListView.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
                _formListView.fixedItemHeight = 92;
                _formListView.makeItem = MakeFormItem;
                _formListView.bindItem = BindFormItem;
                _formListView.selectionChanged += OnFormSelectionChanged;
                _formListView.RegisterCallback<KeyDownEvent>(OnFormListKeyDown);
            }

            if (_validationListView != null)
            {
                _validationListView.selectionType = SelectionType.Single;

                // 校验信息可能比较长，需要自动换行，所以保留 DynamicHeight。
                _validationListView.virtualizationMethod = CollectionVirtualizationMethod.DynamicHeight;

                _validationListView.makeItem = MakeValidationItem;
                _validationListView.bindItem = BindValidationItem;
            }

            if (_unconfiguredListView != null)
            {
                _unconfiguredListView.selectionType = SelectionType.Multiple;
                _unconfiguredListView.virtualizationMethod = CollectionVirtualizationMethod.FixedHeight;
                _unconfiguredListView.fixedItemHeight = 88;
                _unconfiguredListView.makeItem = MakeUnconfiguredItem;
                _unconfiguredListView.bindItem = BindUnconfiguredItem;
                _unconfiguredListView.selectionChanged += OnUnconfiguredSelectionChanged;
            }
        }

        private void OnUnconfiguredSelectionChanged(IEnumerable<object> selected)
        {
            _selectedUnconfiguredTypes.Clear();

            if (selected != null)
            {
                foreach (object obj in selected)
                {
                    if (obj is Type type)
                        _selectedUnconfiguredTypes.Add(type);
                }
            }

            RefreshStatusLabel();
        }
        
        private void BindButton(string name, Action action)
        {
            Button btn = rootVisualElement.Q<Button>(name);
            if (btn != null)
            {
                btn.clicked += () => action?.Invoke();
            }
        }

        private void RegisterFormListInteractions()
        {
            VisualElement target = _formListPanelRoot ?? (_formListView as VisualElement);
            if (target != null)
            {
                RegisterDragAndDrop(target);
                target.focusable = true;
                target.RegisterCallback<KeyDownEvent>(OnFormListKeyDown);
            }
        }

        private void OnFormListKeyDown(KeyDownEvent evt)
        {
            if (evt == null)
                return;

            if (evt.keyCode == KeyCode.Delete || evt.keyCode == KeyCode.Backspace)
            {
                if (_selectedFormItems.Count > 0)
                {
                    DeleteSelectedForms();
                    evt.StopPropagation();
                }
            }
        }
        
        private void OnGUI()
        {
            Event e = Event.current;
            if (e == null)
                return;

            if (e.commandName == "ObjectSelectorUpdated" || e.commandName == "ObjectSelectorClosed")
            {
                UnityEngine.Object picked = EditorGUIUtility.GetObjectPickerObject();
                if (picked == null || _selectedFormItem == null)
                    return;

                if (picked is MonoScript script)
                {
                    Type type = script.GetClass();
                    UpdateSelectedFormField(cfg => cfg.logicType = type != null ? type.FullName : script.name);
                    RefreshFormDetailFromSelection();
                    Repaint();
                }
                else if (picked is GameObject prefab)
                {
                    string assetPath = AssetDatabase.GetAssetPath(prefab);
                    if (!string.IsNullOrWhiteSpace(assetPath))
                    {
                        UpdateSelectedFormField(cfg => cfg.assetPath = assetPath.Replace("\\", "/"));
                        RefreshFormDetailFromSelection();
                        Repaint();
                    }
                }
            }
        }
        
        #endregion

        #region Make / Bind Item

        private VisualElement MakeGroupItem()
        {
            VisualElement root = new VisualElement();
            root.AddToClassList("list-item-card");
            root.RegisterCallback<MouseDownEvent>(OnGroupItemMouseDown);

            Label title = new Label { name = "title" };
            title.AddToClassList("list-item-title");
            root.Add(title);

            Label path = new Label { name = "path" };
            path.AddToClassList("list-item-meta");
            root.Add(path);

            return root;
        }

        private void BindGroupItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _visibleGroupItems.Count)
                return;

            GroupListItem item = _visibleGroupItems[index];
            element.Q<Label>("title").text = item.Title;
            element.Q<Label>("path").text = item.AssetPath;
            element.userData = item;

            element.RemoveFromClassList("status-selected");
            if (item.Group == _selectedGroupAsset)
            {
                element.AddToClassList("status-selected");
            }
        }

        private void OnGroupItemMouseDown(MouseDownEvent evt)
        {
            if (evt == null || evt.button != 0 || evt.clickCount != 2)
                return;

            VisualElement ve = evt.currentTarget as VisualElement;
            GroupListItem item = ve?.userData as GroupListItem;
            if (item?.Group == null)
                return;

            Selection.activeObject = item.Group;
            EditorGUIUtility.PingObject(item.Group);
            evt.StopPropagation();
        }
        
        private VisualElement MakeFormItem()
        {
            VisualElement root = new VisualElement();
            root.AddToClassList("list-item-card");
            root.AddToClassList("list-item-row");
            root.RegisterCallback<MouseDownEvent>(OnFormItemMouseDown);
            root.RegisterCallback<ContextClickEvent>(OnFormItemContextClick);

            VisualElement main = new VisualElement();
            main.name = "main";
            main.AddToClassList("list-item-main");

            Label line1 = new Label { name = "line1" };
            line1.AddToClassList("list-item-title");
            main.Add(line1);

            Label line2 = new Label { name = "line2" };
            line2.AddToClassList("list-item-sub");
            main.Add(line2);

            Label line3 = new Label { name = "line3" };
            line3.AddToClassList("list-item-meta");
            main.Add(line3);

            VisualElement actions = new VisualElement();
            actions.name = "actions";
            actions.AddToClassList("list-item-actions");

            Button autoBtn = new Button { name = "autoBtn", text = "修复(Auto)" };
            autoBtn.AddToClassList("inline-btn");
            autoBtn.AddToClassList("inline-btn-auto");
            autoBtn.clickable = new Clickable(OnFormAutoButtonClicked);
            actions.Add(autoBtn);

            Button copyBtn = new Button { name = "copyBtn", text = "复制参数" };
            copyBtn.AddToClassList("inline-btn");
            copyBtn.AddToClassList("inline-btn-copy");
            copyBtn.clickable = new Clickable(OnFormCopyButtonClicked);
            actions.Add(copyBtn);

            Button pasteBtn = new Button { name = "pasteBtn", text = "粘贴参数" };
            pasteBtn.AddToClassList("inline-btn");
            pasteBtn.AddToClassList("inline-btn-paste");
            pasteBtn.clickable = new Clickable(OnFormPasteButtonClicked);
            actions.Add(pasteBtn);

            Button deleteBtn = new Button { name = "deleteBtn", text = "删(Del)" };
            deleteBtn.AddToClassList("inline-btn");
            deleteBtn.AddToClassList("inline-btn-delete");
            deleteBtn.clickable = new Clickable(OnFormDeleteButtonClicked);
            actions.Add(deleteBtn);

            root.Add(main);
            root.Add(actions);            
            
            return root;
        }

        private void BindFormItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _visibleFormItems.Count)
                return;

            UIFormDraftItem item = _visibleFormItems[index];
            UIFormConfig cfg = item?.data;
            element.userData = item;

            Button autoBtn = element.Q<Button>("autoBtn");
            Button copyBtn = element.Q<Button>("copyBtn");
            Button pasteBtn = element.Q<Button>("pasteBtn");
            Button deleteBtn = element.Q<Button>("deleteBtn");
            if (autoBtn != null) autoBtn.userData = item;
            if (copyBtn != null) copyBtn.userData = item;
            if (pasteBtn != null)
            {
                pasteBtn.userData = item;
                pasteBtn.SetEnabled(_formSettingsClipboard != null);
            }
            if (deleteBtn != null) deleteBtn.userData = item;
            
            element.RemoveFromClassList("status-ok");
            element.RemoveFromClassList("status-warn");
            element.RemoveFromClassList("status-error");
            element.RemoveFromClassList("status-dirty");
            element.RemoveFromClassList("status-new");
            element.RemoveFromClassList("status-selected");
            element.RemoveFromClassList("status-invalid-soft");
            element.RemoveFromClassList("status-missing-script");
            element.RemoveFromClassList("status-missing-prefab");
            element.RemoveFromClassList("status-duplicate-id");

            string status = GetRowStatus(item);
            switch (status)
            {
                case "Error":
                    element.AddToClassList("status-error");
                    break;
                case "Warn":
                    element.AddToClassList("status-warn");
                    break;
                default:
                    element.AddToClassList("status-ok");
                    break;
            }

            if (item != null && item.isModified)
            {
                element.AddToClassList("status-dirty");
            }

            if (item != null && item.isNew)
            {
                element.AddToClassList("status-new");
            }

            if (item != null && item == _selectedFormItem)
            {
                element.AddToClassList("status-selected");
            }

            bool missingScript = cfg == null || string.IsNullOrWhiteSpace(cfg.logicType) || UIConfigEditorWindowHelper.FindTypeByName(cfg.logicType) == null;
            bool missingPrefab = cfg == null || string.IsNullOrWhiteSpace(cfg.assetPath) || UIConfigEditorWindowHelper.TryLoadPrefabAtRuntimeStylePath(cfg.assetPath) == null;
            bool duplicateId = cfg != null && HasDuplicateFormIdInDraft(cfg.formId, item);
            bool invalid = IsInvalid(item);

            if (invalid) element.AddToClassList("status-invalid-soft");
            if (missingScript) element.AddToClassList("status-missing-script");
            if (missingPrefab) element.AddToClassList("status-missing-prefab");
            if (duplicateId) element.AddToClassList("status-duplicate-id");

            string displayStatus = GetStatusDisplayName(status);
            string maskSummary = cfg != null && cfg.useMask ? $"Mask: {cfg.maskMode}" : "Mask: Off";
            string animationSummary = cfg != null ? $"Anim: {cfg.openAnimation}/{cfg.closeAnimation}" : "Anim: (null)";

            element.Q<Label>("line1").text = $"{index}. {SafeFormId(cfg)}";
            element.Q<Label>("line2").text = $"脚本(Logic): {cfg?.logicType ?? "(null)"}";
            element.Q<Label>("line3").text = $"预制体Asset: {cfg?.assetPath ?? "(null)"} | Group: {cfg?.configGroup ?? "(null)"} | {maskSummary} | {animationSummary} | [{displayStatus}]";
        }

        private void OnFormItemMouseDown(MouseDownEvent evt)
        {
            if (evt == null || evt.button != 0 || evt.clickCount != 2)
                return;

            VisualElement ve = evt.currentTarget as VisualElement;
            UIFormDraftItem item = ve?.userData as UIFormDraftItem;
            UIFormConfig cfg = item?.data;
            if (cfg == null)
                return;

            bool pinged = false;

            if (!string.IsNullOrWhiteSpace(cfg.assetPath))
            {
                GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(cfg.assetPath);
                if (prefab != null)
                {
                    Selection.activeObject = prefab;
                    EditorGUIUtility.PingObject(prefab);
                    pinged = true;
                }
            }

            if (!pinged && !string.IsNullOrWhiteSpace(cfg.logicType))
            {
                MonoScript script = FindMonoScriptByTypeName(cfg.logicType);
                if (script != null)
                {
                    Selection.activeObject = script;
                    EditorGUIUtility.PingObject(script);
                    pinged = true;
                }
            }

            if (pinged)
            {
                evt.StopPropagation();
            }
        }
        
        private void OnFormItemContextClick(ContextClickEvent evt)
        {
            VisualElement ve = evt.currentTarget as VisualElement;
            UIFormDraftItem item = ve?.userData as UIFormDraftItem;
            UIFormConfig cfg = item?.data;
            if (cfg == null)
                return;
        
            SelectSingleFormItem(item);
        
            GenericMenu menu = new GenericMenu();
        
            if (!string.IsNullOrWhiteSpace(cfg.assetPath) && AssetDatabase.LoadAssetAtPath<GameObject>(cfg.assetPath) != null)
            {
                menu.AddItem(new GUIContent("定位 Prefab"), false, () => PingPrefabByPath(cfg.assetPath));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("定位 Prefab"));
            }
        
            if (!string.IsNullOrWhiteSpace(cfg.logicType) && FindMonoScriptByTypeName(cfg.logicType) != null)
            {
                menu.AddItem(new GUIContent("定位 Script"), false, () => PingScriptByTypeName(cfg.logicType));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("定位 Script"));
            }
        
            menu.AddSeparator(string.Empty);
        
            menu.AddItem(new GUIContent("自动修复"), false, () =>
            {
                if (_currentDraft == null || item == null)
                    return;
        
                SelectSingleFormItem(item);
                AutoFixForm(item);
                MarkCurrentDraftDirty();
                RefreshAllViewsPreserveFormSelection();
            });
        
            menu.AddItem(new GUIContent("复制参数"), false, () =>
            {
                if (_currentDraft == null || item == null || item.data == null)
                    return;

                SelectSingleFormItem(item);

                CopyFormSettingsToClipboard(item);
            });

            if (_formSettingsClipboard != null)
            {
                menu.AddItem(new GUIContent("粘贴参数"), false, () => PasteFormSettings(item));
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("粘贴参数"));
            }
        
            menu.AddItem(new GUIContent("删除"), false, () =>
            {
                if (_currentDraft == null || item == null)
                    return;
        
                DeleteSingleFormItemWithConfirm(item);
            });
        
            menu.AddSeparator(string.Empty);
        
            if (!string.IsNullOrWhiteSpace(cfg.formId))
            {
                menu.AddItem(new GUIContent("复制 FormId"), false, () => EditorGUIUtility.systemCopyBuffer = cfg.formId);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("复制 FormId"));
            }
        
            if (!string.IsNullOrWhiteSpace(cfg.assetPath))
            {
                menu.AddItem(new GUIContent("复制 AssetPath"), false, () => EditorGUIUtility.systemCopyBuffer = cfg.assetPath);
            }
            else
            {
                menu.AddDisabledItem(new GUIContent("复制 AssetPath"));
            }
        
            menu.ShowAsContext();
            evt.StopPropagation();
        }
        
        private void OnFormAutoButtonClicked()
        {
            UIFormDraftItem item = GetCurrentButtonBoundFormItem();
            if (_currentDraft == null || item == null)
                return;

            SelectSingleFormItem(item);
            AutoFixForm(item);
            MarkCurrentDraftDirty();
            RefreshAllViewsPreserveFormSelection();
        }

        private void OnFormCopyButtonClicked()
        {
            UIFormDraftItem item = GetCurrentButtonBoundFormItem();
            if (_currentDraft == null || item == null || item.data == null)
                return;

            SelectSingleFormItem(item);

            CopyFormSettingsToClipboard(item);
        }

        private void OnFormPasteButtonClicked()
        {
            UIFormDraftItem item = GetCurrentButtonBoundFormItem();
            if (_currentDraft == null || item == null || item.data == null || _formSettingsClipboard == null)
                return;

            SelectSingleFormItem(item);
            PasteFormSettings(item);
        }

        private void CopyFormSettingsToClipboard(UIFormDraftItem item)
        {
            if (item == null || item.data == null)
                return;

            _formSettingsClipboard = UIConfigGroupDraftMapper.CloneReusableFormSettings(item.data);
            SetStatusText($"已复制可复用参数：{SafeFormId(item.data)}。请选择目标 Form 粘贴。");
            RefreshFormListView();
        }

        private void PasteFormSettings(UIFormDraftItem item)
        {
            if (_currentDraft == null || item == null || item.data == null || _formSettingsClipboard == null)
                return;

            UIConfigGroupDraftMapper.CopyReusableFormSettings(_formSettingsClipboard, item.data);
            item.isModified = true;
            SelectSingleFormItem(item);
            MarkCurrentDraftDirty();
            RefreshAllViewsPreserveFormSelection();
            SetStatusText($"已将可复用参数粘贴到：{SafeFormId(item.data)}。FormID、LogicType、AssetPath 保持不变。");
        }

        private void OnFormDeleteButtonClicked()
        {
            UIFormDraftItem item = GetCurrentButtonBoundFormItem();
            if (_currentDraft == null || item == null)
                return;

            DeleteSingleFormItemWithConfirm(item);
        }

        private UIFormDraftItem GetCurrentButtonBoundFormItem()
        {
            VisualElement focused = rootVisualElement?.panel?.focusController?.focusedElement as VisualElement;
            if (focused is Button btn && btn.userData is UIFormDraftItem directItem)
                return directItem;

            return null;
        }
        
        private VisualElement MakeUnconfiguredItem()
        {
            VisualElement root = new VisualElement();
            root.AddToClassList("list-item-card");

            Label typeLabel = new Label { name = "type" };
            typeLabel.AddToClassList("list-item-title");
            root.Add(typeLabel);

            Label pathLabel = new Label { name = "path" };
            pathLabel.AddToClassList("list-item-meta");
            root.Add(pathLabel);

            Button addBtn = new Button { name = "addBtn", text = "加入当前Group" };
            addBtn.RegisterCallback<ClickEvent>(OnUnconfiguredAddButtonClicked);
            root.Add(addBtn);

            return root;
        }

        private void BindUnconfiguredItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _visibleUnconfiguredTypes.Count)
                return;

            Type type = _visibleUnconfiguredTypes[index];
            string guessedPath = UIConfigEditorWindowHelper.FindBestRuntimeAssetPathByName(type.Name);
            element.userData = type;
            element.Q<Label>("type").text = type.FullName ?? type.Name;
            element.Q<Label>("path").text = string.IsNullOrWhiteSpace(guessedPath) ? "(未找到同名Prefab)" : guessedPath;

            Button btn = element.Q<Button>("addBtn");
            if (btn != null)
            {
                btn.userData = type;
            }
        }

        private void OnUnconfiguredAddButtonClicked(ClickEvent evt)
        {
            Button btn = evt?.currentTarget as Button;
            Type type = btn?.userData as Type;

            if (type == null)
                return;

            AddTypeToCurrentGroup(type);
            evt.StopPropagation();
        }
        
        private VisualElement MakeValidationItem()
        {
            Label label = new Label();
            label.AddToClassList("validation-item");
            label.AddToClassList("validation-item-clickable");
            label.style.whiteSpace = WhiteSpace.Normal;
            label.RegisterCallback<MouseUpEvent>(OnValidationItemMouseUp);
            return label;
        }

        private void OnValidationItemMouseUp(MouseUpEvent evt)
        {
            if (evt.button != 0)
                return;

            Label label = evt.currentTarget as Label;
            if (label?.userData is UIConfigGroupDraftValidator.ValidationMessage msg)
            {
                LocateFormFromValidationMessage(msg);
                evt.StopPropagation();
            }
        }
        
        private void BindValidationItem(VisualElement element, int index)
        {
            if (index < 0 || index >= _validationMessages.Count)
                return;

            Label label = element as Label;
            if (label == null)
                return;

            UIConfigGroupDraftValidator.ValidationMessage msg = _validationMessages[index];

            label.RemoveFromClassList("validation-info");
            label.RemoveFromClassList("validation-warn");
            label.RemoveFromClassList("validation-error");

            string prefix = msg.Severity == UIConfigGroupDraftValidator.ValidationSeverity.Error
                ? "[错误]"
                : msg.Severity == UIConfigGroupDraftValidator.ValidationSeverity.Warning
                    ? "[警告]"
                    : "[信息]";

            label.text = $"{prefix} {msg.Message}";

            switch (msg.Severity)
            {
                case UIConfigGroupDraftValidator.ValidationSeverity.Error:
                    label.AddToClassList("validation-error");
                    break;
                case UIConfigGroupDraftValidator.ValidationSeverity.Warning:
                    label.AddToClassList("validation-warn");
                    break;
                default:
                    label.AddToClassList("validation-info");
                    break;
            }

            label.userData = msg;
        }

        #endregion

        #region Refresh / Selection

        private void RefreshAllViews()
        {
            RefreshGroupListView();
            RestoreGroupSelection();
            RefreshDraftBindings();
            RefreshFormListView();
            RefreshFormDetailFromSelection();
            RefreshValidationListView();
            RefreshUnconfiguredListView();
            RefreshStatusLabel();
        }

        private void RefreshAllViewsPreserveFormSelection()
        {
            CachePrimarySelection();
            RefreshAllViews();
            RestorePrimarySelection();
        }

        private void CachePrimarySelection()
        {
            _lastPrimarySelectedFormItem = _selectedFormItem;
            _lastPrimarySelectedFormId = _selectedFormItem?.data?.formId;
        }

        private void RestorePrimarySelection()
        {
            if (_currentDraft == null || _currentDraft.forms == null || _currentDraft.forms.Count == 0)
            {
                ClearFormSelectionState();
                RefreshFormDetailFromSelection();
                RefreshStatusLabel();
                return;
            }

            UIFormDraftItem restored = null;

            if (_lastPrimarySelectedFormItem != null && _currentDraft.forms.Contains(_lastPrimarySelectedFormItem))
            {
                restored = _lastPrimarySelectedFormItem;
            }
            else if (!string.IsNullOrWhiteSpace(_lastPrimarySelectedFormId))
            {
                restored = _currentDraft.forms.FirstOrDefault(x =>
                    x != null &&
                    x.data != null &&
                    string.Equals(x.data.formId, _lastPrimarySelectedFormId, StringComparison.OrdinalIgnoreCase));
            }

            if (restored == null)
            {
                restored = _currentDraft.forms.FirstOrDefault();
            }

            if (restored != null)
            {
                SelectSingleFormItem(restored, false);
                ScrollFormListToItem(restored);
            }
            else
            {
                ClearFormSelectionState();
            }

            RefreshFormDetailFromSelection();
            RefreshStatusLabel();
            RefreshFormListView();
        }

        private void SelectSingleFormItem(UIFormDraftItem item, bool syncListView = true)
        {
            _selectedFormItems.Clear();
            _selectedFormItem = item;

            if (item != null)
            {
                _selectedFormItems.Add(item);
                _lastPrimarySelectedFormItem = item;
                _lastPrimarySelectedFormId = item.data?.formId;
            }
            else
            {
                _lastPrimarySelectedFormItem = null;
                _lastPrimarySelectedFormId = null;
            }

            if (syncListView && _formListView != null)
            {
                if (item != null)
                {
                    int visibleIndex = _visibleFormItems.IndexOf(item);
                    if (visibleIndex >= 0)
                    {
                        _formListView.SetSelectionWithoutNotify(new[] { visibleIndex });
                        _formListView.ScrollToItem(visibleIndex);
                    }
                    else
                    {
                        _formListView.ClearSelection();
                    }
                }
                else
                {
                    _formListView.ClearSelection();
                }
            }
        }

        private void ClearFormSelectionState()
        {
            _selectedFormItem = null;
            _selectedFormItems.Clear();
            _lastPrimarySelectedFormItem = null;
            _lastPrimarySelectedFormId = null;
        }

        private void ScrollFormListToItem(UIFormDraftItem item)
        {
            if (_formListView == null || item == null)
                return;

            int visibleIndex = _visibleFormItems.IndexOf(item);
            if (visibleIndex >= 0)
            {
                _formListView.ScrollToItem(visibleIndex);
            }
        }
        
        private void CacheGroupSelection()
        {
            _lastSelectedGroupAsset = _selectedGroupAsset;
        }

        private void RestoreGroupSelection()
        {
            if (_groupListView == null)
                return;
            
            if (_selectedGroupAsset == null)
            {
                _groupListView.ClearSelection();
                return;
            }

            int index = _visibleGroupItems.FindIndex(x => x != null && x.Group == _selectedGroupAsset);
            if (index >= 0)
            {
                _groupListView.SetSelectionWithoutNotify(new[] { index });
                _groupListView.ScrollToItem(index);
            }
            else
            {
                _groupListView.ClearSelection();
            }
        }
        
        private void RefreshGroups()
        {
            CacheGroupSelection();
            _groups.Clear();

            string[] guids = AssetDatabase.FindAssets("t:UIConfigGroup");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                UIConfigGroup group = AssetDatabase.LoadAssetAtPath<UIConfigGroup>(path);
                if (group != null)
                {
                    _groups.Add(group);
                }
            }

            _groups.Sort((a, b) => string.Compare(a != null ? a.name : string.Empty, b != null ? b.name : string.Empty, StringComparison.OrdinalIgnoreCase));

            if (_selectedGroupAsset != null && !_groups.Contains(_selectedGroupAsset))
            {
                _selectedGroupAsset = null;
                _currentDraft = null;
                ClearFormSelectionState();
            }
        }

        private void RefreshGroupListView()
        {
            if (_groupListView == null)
                return;

            _visibleGroupItems = BuildVisibleGroupItems();
            _groupListView.itemsSource = _visibleGroupItems;
            _groupListView.Rebuild();
            RestoreGroupSelection();
        }

        private void RefreshFormListView()
        {
            if (_formListView == null)
                return;

            _visibleFormItems = BuildVisibleFormItems();
            _formListView.itemsSource = _visibleFormItems;
            _formListView.Rebuild();
            // (_formListPanelRoot ?? (_formListView as VisualElement))?.Focus();

            if (_selectedFormItem != null && !_visibleFormItems.Contains(_selectedFormItem))
            {
                _formListView.ClearSelection();
            }
            else if (_selectedFormItem != null)
            {
                int selectedIndex = _visibleFormItems.IndexOf(_selectedFormItem);
                if (selectedIndex >= 0)
                {
                    _formListView.SetSelectionWithoutNotify(new[] { selectedIndex });
                }
            }
            if (_formListSummaryLabel != null)
            {
                _formListSummaryLabel.text = $"显示 {_visibleFormItems.Count} items";
            }
        }

        private void RefreshValidationListView()
        {
            if (_validationListView == null)
                return;

            _validationListView.itemsSource = _validationMessages;
            _validationListView.Rebuild();
        }

        private void RefreshUnconfiguredListView()
        {
            if (_unconfiguredListView == null)
                return;

            _visibleUnconfiguredTypes = BuildVisibleUnconfiguredTypes();
            _unconfiguredListView.itemsSource = _visibleUnconfiguredTypes;
            _unconfiguredListView.Rebuild();
        }

        private void RefreshDraftBindings()
        {
            if (_currentDraft == null)
            {
                if (_groupHeaderLabel != null) _groupHeaderLabel.text = "当前 Group: (未选择)";
                if (_groupPathLabel != null) _groupPathLabel.text = string.Empty;
                _draftGroupNameField?.SetValueWithoutNotify(string.Empty);
                _draftDefaultPackageNameField?.SetValueWithoutNotify(string.Empty);
                _draftLoadOnInitializeToggle?.SetValueWithoutNotify(false);
                return;
            }

            if (_groupHeaderLabel != null)
                _groupHeaderLabel.text = $"当前 Group: {_currentDraft.assetName} [{_currentDraft.groupName}]";
            if (_groupPathLabel != null)
                _groupPathLabel.text = _currentDraft.assetPath ?? string.Empty;

            _draftGroupNameField?.SetValueWithoutNotify(_currentDraft.groupName ?? string.Empty);
            _draftDefaultPackageNameField?.SetValueWithoutNotify(_currentDraft.defaultPackageName ?? string.Empty);
            _draftLoadOnInitializeToggle?.SetValueWithoutNotify(_currentDraft.loadOnInitialize);
        }

        private void RefreshFormDetailFromSelection()
        {
            if (_selectedFormItem == null || _selectedFormItem.data == null)
            {
                if (_detailTitleLabel != null)
                    _detailTitleLabel.text = "未选中 Form";

                _formIdField?.SetValueWithoutNotify(string.Empty);
                _logicTypeField?.SetValueWithoutNotify(string.Empty);
                _assetPathField?.SetValueWithoutNotify(string.Empty);
                _configGroupField?.SetValueWithoutNotify(string.Empty);
                _packageNameField?.SetValueWithoutNotify(string.Empty);

                _layerField?.SetValueWithoutNotify(UIGroupLayer.Normal);
                _allowMultiInstanceField?.SetValueWithoutNotify(false);
                _fullScreenField?.SetValueWithoutNotify(false);
                _pauseCoveredField?.SetValueWithoutNotify(false);
                _blockingModeField?.SetValueWithoutNotify(UIBlockingMode.FullScreenOnly);
                _disableGameObjectWhenHiddenField?.SetValueWithoutNotify(false);
                _closeModeField?.SetValueWithoutNotify(UIFormCloseMode.Cache);
                _coverVisualModeField?.SetValueWithoutNotify(UICoverVisualMode.HideByCanvasGroup);
                _residentField?.SetValueWithoutNotify(false);
                _bringToFrontOnOpenField?.SetValueWithoutNotify(false);
                _allowBackCloseField?.SetValueWithoutNotify(false);
                _useMaskField?.SetValueWithoutNotify(false);
                _closeOnMaskClickField?.SetValueWithoutNotify(false);
                _maskModeField?.SetValueWithoutNotify(UIMaskMode.None);
                _maskColorField?.SetValueWithoutNotify(new Color(0f, 0f, 0f, 0.6f));
                _openAnimationField?.SetValueWithoutNotify(UIAnimationPreset.None);
                _closeAnimationField?.SetValueWithoutNotify(UIAnimationPreset.None);
                _openAnimationDurationField?.SetValueWithoutNotify(0.2f);
                _closeAnimationDurationField?.SetValueWithoutNotify(0.15f);
                _openAnimationEaseField?.SetValueWithoutNotify(UIAnimationEase.OutQuad);
                _closeAnimationEaseField?.SetValueWithoutNotify(UIAnimationEase.InQuad);
                return;
            }

            UIFormConfig cfg = _selectedFormItem.data;

            if (_detailTitleLabel != null)
                _detailTitleLabel.text = $"当前 Form: {SafeFormId(cfg)}";

            _formIdField?.SetValueWithoutNotify(cfg.formId ?? string.Empty);
            _logicTypeField?.SetValueWithoutNotify(cfg.logicType ?? string.Empty);
            _assetPathField?.SetValueWithoutNotify(cfg.assetPath ?? string.Empty);
            _configGroupField?.SetValueWithoutNotify(cfg.configGroup ?? string.Empty);
            _packageNameField?.SetValueWithoutNotify(cfg.packageName ?? string.Empty);

            _layerField?.SetValueWithoutNotify(cfg.layer);
            _allowMultiInstanceField?.SetValueWithoutNotify(cfg.allowMultiInstance);
            _fullScreenField?.SetValueWithoutNotify(cfg.fullScreen);
            _pauseCoveredField?.SetValueWithoutNotify(cfg.pauseCovered);
            _blockingModeField?.SetValueWithoutNotify(cfg.blockingMode);
            _disableGameObjectWhenHiddenField?.SetValueWithoutNotify(cfg.disableGameObjectWhenHidden);
            _closeModeField?.SetValueWithoutNotify(cfg.closeMode);
            _coverVisualModeField?.SetValueWithoutNotify(cfg.coverVisualMode);
            _residentField?.SetValueWithoutNotify(cfg.resident);
            _bringToFrontOnOpenField?.SetValueWithoutNotify(cfg.bringToFrontOnOpen);
            _allowBackCloseField?.SetValueWithoutNotify(cfg.allowBackClose);
            _useMaskField?.SetValueWithoutNotify(cfg.useMask);
            _closeOnMaskClickField?.SetValueWithoutNotify(cfg.closeOnMaskClick);
            _maskModeField?.SetValueWithoutNotify(cfg.maskMode);
            _maskColorField?.SetValueWithoutNotify(cfg.maskColor);
            _openAnimationField?.SetValueWithoutNotify(cfg.openAnimation);
            _closeAnimationField?.SetValueWithoutNotify(cfg.closeAnimation);
            _openAnimationDurationField?.SetValueWithoutNotify(cfg.openAnimationDuration);
            _closeAnimationDurationField?.SetValueWithoutNotify(cfg.closeAnimationDuration);
            _openAnimationEaseField?.SetValueWithoutNotify(cfg.openAnimationEase);
            _closeAnimationEaseField?.SetValueWithoutNotify(cfg.closeAnimationEase);
        }

        private void RefreshStatusLabel()
        {
            if (_statusLabel == null)
                return;

            if (_currentDraft == null)
            {
                _statusLabel.text = "未选择 Group";
                return;
            }

            string dirty = _currentDraft.isDirty ? "有未保存修改" : "无修改";
            _statusLabel.text = $"Group: {_currentDraft.assetName} | Forms: {_currentDraft.forms.Count} | 已选: {_selectedFormItems.Count} | {dirty}";
        }

        private void OnGroupSelectionChanged(IEnumerable<object> selected)
        {
            GroupListItem item = selected?.FirstOrDefault() as GroupListItem;
            UIConfigGroup nextGroup = item?.Group;

            if (nextGroup == _selectedGroupAsset)
                return;

            _lastSelectedGroupAsset = nextGroup;
            
            if (!TrySwitchGroup(nextGroup))
            {
                RefreshGroupListView();
                return;
            }

            RefreshAllViews();
        }

        private bool TrySwitchGroup(UIConfigGroup nextGroup)
        {
            if (_currentDraft != null && _currentDraft.isDirty)
            {
                int result = EditorUtility.DisplayDialogComplex(
                    "未保存修改",
                    $"当前 Group [{_currentDraft.assetName}] 存在未保存修改，是否先保存？",
                    "保存",
                    "取消",
                    "放弃修改");

                if (result == 0)
                {
                    if (!SaveCurrentDraft())
                        return false;
                }
                else if (result == 1)
                {
                    return false;
                }
            }

            _selectedGroupAsset = nextGroup;
            _lastSelectedGroupAsset = nextGroup;
            
            ClearFormSelectionState();
            _validationMessages.Clear();

            _currentDraft = nextGroup != null
                ? UIConfigGroupDraftMapper.CreateDraft(nextGroup)
                : null;

            return true;
        }

        private void OnFormSelectionChanged(IEnumerable<object> selected)
        {
            _selectedFormItems.Clear();

            if (selected != null)
            {
                foreach (object obj in selected)
                {
                    if (obj is UIFormDraftItem item)
                    {
                        _selectedFormItems.Add(item);
                    }
                }
            }

            _selectedFormItem = _selectedFormItems.FirstOrDefault();
            _lastPrimarySelectedFormItem = _selectedFormItem;
            _lastPrimarySelectedFormId = _selectedFormItem?.data?.formId;
            
            RefreshFormDetailFromSelection();
            RefreshFormListView();
            RefreshStatusLabel();
        }

        #endregion

        #region Group Operations

        private void CreateNewGroup()
        {
            if (string.IsNullOrWhiteSpace(_newGroupName))
            {
                EditorUtility.DisplayDialog("提示", "Asset Name 不能为空。", "确定");
                return;
            }

            if (string.IsNullOrWhiteSpace(_createFolder))
            {
                EditorUtility.DisplayDialog("提示", "Create Folder 不能为空。", "确定");
                return;
            }

            UIConfigEditorWindowHelper.EnsureFolderExists(_createFolder);

            UIConfigGroup asset = CreateInstance<UIConfigGroup>();
            asset.groupName = string.IsNullOrWhiteSpace(_newGroupDisplayName) ? "Default" : _newGroupDisplayName;
            asset.loadOnInitialize = true;
            asset.forms = new List<UIFormConfig>();

            string path = Path.Combine(_createFolder, _newGroupName + ".asset").Replace("\\", "/");
            path = AssetDatabase.GenerateUniqueAssetPath(path);

            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            RefreshGroups();
            TrySwitchGroup(asset);
            RefreshAllViews();

            Selection.activeObject = asset;
            EditorGUIUtility.PingObject(asset);
        }

        private void DeleteSelectedGroup()
        {
            if (_selectedGroupAsset == null)
                return;

            string path = AssetDatabase.GetAssetPath(_selectedGroupAsset);
            bool ok = EditorUtility.DisplayDialog(
                "删除Group",
                $"确定删除以下 UIConfigGroup 吗？\n\n{_selectedGroupAsset.name}\n{path}",
                "删除",
                "取消");

            if (!ok)
                return;

            AssetDatabase.DeleteAsset(path);
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            _selectedGroupAsset = null;
            _currentDraft = null;
            ClearFormSelectionState();
            _validationMessages.Clear();

            RefreshGroups();
            RefreshAllViews();
        }

        private void PingSelectedGroupAsset()
        {
            if (_selectedGroupAsset == null)
                return;

            Selection.activeObject = _selectedGroupAsset;
            EditorGUIUtility.PingObject(_selectedGroupAsset);
        }

        private void CopySelectedGroupPath()
        {
            if (_selectedGroupAsset == null)
                return;

            EditorGUIUtility.systemCopyBuffer = AssetDatabase.GetAssetPath(_selectedGroupAsset);
        }

        private void UseAssetNameOverwriteGroupName()
        {
            if (_currentDraft == null)
                return;

            string assetName = _currentDraft.assetName;
            const string prefix = "UIConfigGroup_";
            if (!string.IsNullOrWhiteSpace(assetName) &&
                assetName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                assetName = assetName.Substring(prefix.Length);
            }

            _currentDraft.groupName = assetName ?? string.Empty;
            MarkCurrentDraftDirty();
            RefreshDraftBindings();
            RefreshFormListView();
        }

        private void SelectCreateFolder()
        {
            string abs = EditorUtility.OpenFolderPanel("选择UIConfigGroup创建目录", Application.dataPath, "");
            if (string.IsNullOrEmpty(abs))
                return;

            string projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);
            if (abs.StartsWith(projectRoot))
            {
                _createFolder = abs.Substring(projectRoot.Length).Replace("\\", "/");
                _createFolderField?.SetValueWithoutNotify(_createFolder);
            }
            else
            {
                EditorUtility.DisplayDialog("提示", "请选择项目 Assets 目录下的路径。", "确定");
            }
        }

        #endregion

        #region Save / Revert / Validate

        private void SaveAll()
        {
            SaveCurrentDraft();
        }

        private bool SaveCurrentDraft()
        {
            if (_currentDraft == null)
                return false;

            _validationMessages = UIConfigGroupDraftValidator.Validate(_currentDraft);
            RefreshValidationListView();

            bool hasError = _validationMessages.Any(m => m.Severity == UIConfigGroupDraftValidator.ValidationSeverity.Error);
            if (hasError)
            {
                EditorUtility.DisplayDialog("保存失败", "当前 Draft 存在 Error 级别校验问题，请先修复。", "确定");
                return false;
            }

            CachePrimarySelection();
            UIConfigGroupDraftMapper.ApplyDraftToAsset(_currentDraft);

            _currentDraft = UIConfigGroupDraftMapper.CreateDraft(_selectedGroupAsset);

            RefreshGroups();
            RefreshAllViewsPreserveFormSelection();
            return true;
        }

        private void RevertCurrentDraft()
        {
            if (_selectedGroupAsset == null)
                return;

            bool ok = EditorUtility.DisplayDialog("放弃修改", "确定放弃当前未保存修改吗？", "放弃", "取消");
            if (!ok)
                return;

            CachePrimarySelection();
            _currentDraft = UIConfigGroupDraftMapper.CreateDraft(_selectedGroupAsset);
            _validationMessages.Clear();

            RefreshAllViewsPreserveFormSelection();
        }

        private void ValidateCurrentDraft()
        {
            if (_currentDraft == null)
                return;

            _validationMessages = UIConfigGroupDraftValidator.Validate(_currentDraft);
            if (_validationMessages.Count == 0)
            {
                _validationMessages.Add(new UIConfigGroupDraftValidator.ValidationMessage
                {
                    Severity = UIConfigGroupDraftValidator.ValidationSeverity.Info,
                    Message = $"Group {_currentDraft.assetName} 校验通过。"
                });
            }

            RefreshValidationListView();
        }

        private void ValidateAllAssets()
        {
            List<UIConfigGroupDraftValidator.ValidationMessage> all = new List<UIConfigGroupDraftValidator.ValidationMessage>();

            foreach (UIConfigGroup group in _groups)
            {
                if (group == null)
                    continue;

                UIConfigGroupDraft draft = UIConfigGroupDraftMapper.CreateDraft(group);
                List<UIConfigGroupDraftValidator.ValidationMessage> result = UIConfigGroupDraftValidator.Validate(draft);
                all.AddRange(result.Select(m =>
                {
                    m.Message = $"[{group.name}] {m.Message}";
                    return m;
                }));
            }

            if (all.Count == 0)
            {
                all.Add(new UIConfigGroupDraftValidator.ValidationMessage
                {
                    Severity = UIConfigGroupDraftValidator.ValidationSeverity.Info,
                    Message = "全部Group校验通过，没有发现异常。"
                });
            }

            _validationMessages = all;
            RefreshValidationListView();
        }

        #endregion

        #region Form Operations

        private void AddEmptyForm()
        {
            if (_currentDraft == null)
                return;

            UIFormDraftItem item = UIFormDraftFactory.CreateEmptyDraftItem(_currentDraft.groupName);
            _currentDraft.forms.Add(item);
            
            SelectSingleFormItem(item, false);
            MarkCurrentDraftDirty();

            RefreshAllViewsPreserveFormSelection();
        }

        private void AddFormsFromSelection()
        {
            if (_currentDraft == null)
                return;

            UnityEngine.Object[] selected = Selection.objects;
            if (selected == null || selected.Length == 0)
                return;

            UIFormDraftItem lastAdded = null;
            int addedCount = 0;
            
            foreach (UnityEngine.Object obj in selected)
            {
                if (obj is GameObject go)
                {
                    UIFormDraftItem item = TryAddPrefabObjectToCurrentDraft(go);
                    if (item != null)
                    {
                        lastAdded = item;
                        addedCount++;
                    }
                }
                else if (obj is MonoScript script)
                {
                    UIFormDraftItem item = TryAddScriptObjectToCurrentDraft(script);
                    if (item != null)
                    {
                        lastAdded = item;
                        addedCount++;
                    }
                }
            }
            
            if (addedCount <= 0)
                return;
            
            if (lastAdded != null)
            {
                SelectSingleFormItem(lastAdded, false);
            }
            
            MarkCurrentDraftDirty();
            RefreshAllViewsPreserveFormSelection();
        }

        private void DeleteSelectedForms()
        {
            if (_currentDraft == null || _selectedFormItems.Count == 0)
                return;

            CachePrimarySelection();
            int fallbackIndex = _selectedFormItem != null ? _currentDraft.forms.IndexOf(_selectedFormItem) : -1;
            
            foreach (UIFormDraftItem item in _selectedFormItems.ToList())
            {
                _currentDraft.forms.Remove(item);
            }

            if (_currentDraft.forms.Count > 0)
            {
                int nextIndex = Mathf.Clamp(fallbackIndex, 0, _currentDraft.forms.Count - 1);
                SelectSingleFormItem(_currentDraft.forms[nextIndex], false);
            }
            else
            {
                ClearFormSelectionState();
            }
            
            MarkCurrentDraftDirty();
            RefreshAllViewsPreserveFormSelection();
        }

        private void DuplicateSelectedForms()
        {
            if (_currentDraft == null || _selectedFormItems.Count == 0)
                return;

            List<UIFormDraftItem> copies = new List<UIFormDraftItem>();

            foreach (UIFormDraftItem item in _selectedFormItems)
            {
                if (item == null || item.data == null)
                    continue;

                UIFormConfig src = item.data;
                UIFormConfig copy = UIConfigGroupDraftMapper.CloneFormConfig(src);
                copy.formId = MakeUniqueFormId(_currentDraft, src.formId + "_Copy");
                copies.Add(UIFormDraftFactory.CreateDraftItem(copy, true, true));
            }

            _currentDraft.forms.AddRange(copies);
            if (copies.Count > 0)
            {
                SelectSingleFormItem(copies[0], false);
            }
            
            MarkCurrentDraftDirty();
            RefreshAllViewsPreserveFormSelection();
        }

        private void MoveSelectedPrimaryForm(int delta)
        {
            if (_currentDraft == null || _selectedFormItem == null)
                return;

            int index = _currentDraft.forms.IndexOf(_selectedFormItem);
            if (index < 0)
                return;

            int newIndex = index + delta;
            if (newIndex < 0 || newIndex >= _currentDraft.forms.Count)
                return;

            UIFormDraftItem temp = _currentDraft.forms[index];
            _currentDraft.forms[index] = _currentDraft.forms[newIndex];
            _currentDraft.forms[newIndex] = temp;

            MarkCurrentDraftDirty();
            RefreshAllViewsPreserveFormSelection();
        }

        private void DuplicateSelectedPrimaryForm()
        {
            if (_selectedFormItem == null || _currentDraft == null)
                return;

            UIFormConfig src = _selectedFormItem.data;
            UIFormConfig copy = UIConfigGroupDraftMapper.CloneFormConfig(src);
            copy.formId = MakeUniqueFormId(_currentDraft, src.formId + "_Copy");

            int index = _currentDraft.forms.IndexOf(_selectedFormItem);
            UIFormDraftItem newItem = UIFormDraftFactory.CreateDraftItem(copy, true, true);
            _currentDraft.forms.Insert(index + 1, newItem);
            SelectSingleFormItem(newItem, false);
            
            MarkCurrentDraftDirty();
            RefreshAllViewsPreserveFormSelection();
        }

        private void DeleteSelectedPrimaryForm()
        {
            if (_selectedFormItem == null || _currentDraft == null)
                return;

            int rawIndex = _currentDraft.forms.IndexOf(_selectedFormItem);
            _currentDraft.forms.Remove(_selectedFormItem);
            _selectedFormItems.Remove(_selectedFormItem);
            if (_currentDraft.forms.Count > 0)
            {
                int nextIndex = Mathf.Clamp(rawIndex, 0, _currentDraft.forms.Count - 1);
                SelectSingleFormItem(_currentDraft.forms[nextIndex], false);
            }
            else
            {
                ClearFormSelectionState();
            }
            
            MarkCurrentDraftDirty();

            RefreshAllViewsPreserveFormSelection();
        }

        private void AutoFixSelectedPrimaryForm()
        {
            if (_selectedFormItem == null || _currentDraft == null)
                return;

            AutoFixForm(_selectedFormItem);
            MarkCurrentDraftDirty();

            RefreshAllViewsPreserveFormSelection();
        }

        private void AutoFixSelectedForms()
        {
            if (_currentDraft == null || _selectedFormItems.Count == 0)
                return;

            foreach (UIFormDraftItem item in _selectedFormItems)
            {
                AutoFixForm(item);
            }

            MarkCurrentDraftDirty();
            RefreshAllViewsPreserveFormSelection();
        }

        private void SortCurrentForms()
        {
            if (_currentDraft == null)
                return;

            CachePrimarySelection();
            
            IEnumerable<UIFormDraftItem> sorted = ApplySort(_currentDraft.forms);
            _currentDraft.forms = sorted.ToList();
            MarkCurrentDraftDirty();

            RefreshAllViewsPreserveFormSelection();
        }

        private void UpdateSelectedFormField(Action<UIFormConfig> apply)
        {
            if (_selectedFormItem == null || _selectedFormItem.data == null || apply == null)
                return;

            apply(_selectedFormItem.data);
            _selectedFormItem.isModified = true;
            MarkCurrentDraftDirty();

            RefreshAllViewsPreserveFormSelection();
        }

        private void PickScriptForSelectedFormLogicTypeOld()
        {
            if (_selectedFormItem == null)
                return;

            string path = EditorUtility.OpenFilePanel("选择脚本", Application.dataPath, "cs");
            if (string.IsNullOrWhiteSpace(path))
                return;

            string projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);
            if (!path.StartsWith(projectRoot))
            {
                EditorUtility.DisplayDialog("提示", "请选择项目内脚本。", "确定");
                return;
            }

            string relative = path.Substring(projectRoot.Length).Replace("\\", "/");
            MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(relative);
            if (script == null)
                return;

            Type type = script.GetClass();
            UpdateSelectedFormField(cfg => cfg.logicType = type != null ? type.FullName : script.name);
            RefreshFormDetailFromSelection();
        }

        private void PickScriptForSelectedFormLogicType()
        {
            if (_selectedFormItem == null)
                return;

            EditorGUIUtility.ShowObjectPicker<MonoScript>(null, false, string.Empty, 0);
        }
        
        private void PickPrefabForSelectedFormPathOld()
        {
            if (_selectedFormItem == null)
                return;

            string path = EditorUtility.OpenFilePanel("选择Prefab", Application.dataPath, "prefab");
            if (string.IsNullOrWhiteSpace(path))
                return;

            string projectRoot = Application.dataPath.Substring(0, Application.dataPath.Length - "Assets".Length);
            if (!path.StartsWith(projectRoot))
            {
                EditorUtility.DisplayDialog("提示", "请选择项目内Prefab。", "确定");
                return;
            }

            string relative = path.Substring(projectRoot.Length).Replace("\\", "/");
            UpdateSelectedFormField(cfg => cfg.assetPath = relative);
            RefreshFormDetailFromSelection();
        }

        private void PickPrefabForSelectedFormPath()
        {
            if (_selectedFormItem == null)
                return;

            EditorGUIUtility.ShowObjectPicker<GameObject>(null, false, "t:Prefab", 0);
        }
        
        #endregion

        #region Batch Operations

        private void ApplyBatchLayerToSelected()
        {
            ApplyBatchToSelected(cfg => cfg.layer = _batchLayer);
        }

        private void ApplyBatchPackageNameToSelected()
        {
            ApplyBatchToSelected(cfg => cfg.packageName = _batchPackageName ?? string.Empty);
        }

        private void ApplyBatchBlockingModeToSelected()
        {
            ApplyBatchToSelected(cfg => cfg.blockingMode = _batchBlockingMode);
        }

        private void ApplyBatchCloseModeToSelected()
        {
            ApplyBatchToSelected(cfg => cfg.closeMode = _batchCloseMode);
        }

        private void ApplyBatchCoverVisualToSelected()
        {
            ApplyBatchToSelected(cfg => cfg.coverVisualMode = _batchCoverVisualMode);
        }

        private void ApplyBatchMaskToSelected()
        {
            ApplyBatchToSelected(cfg =>
            {
                cfg.useMask = _batchUseMask;
                cfg.closeOnMaskClick = _batchCloseOnMaskClick;
                cfg.maskMode = _batchMaskMode;
            });
        }

        private void ApplyBatchAnimationToSelected()
        {
            ApplyBatchToSelected(cfg =>
            {
                cfg.openAnimation = _batchOpenAnimation;
                cfg.closeAnimation = _batchCloseAnimation;
            });
        }

        private void ApplyBatchBoolsToSelected()
        {
            ApplyBatchToSelected(cfg =>
            {
                cfg.allowMultiInstance = _batchAllowMultiInstance;
                cfg.fullScreen = _batchFullScreen;
                cfg.pauseCovered = _batchPauseCovered;
                cfg.disableGameObjectWhenHidden = _batchDisableGameObjectWhenHidden;
                cfg.resident = _batchResident;
                cfg.bringToFrontOnOpen = _batchBringToFrontOnOpen;
                cfg.allowBackClose = _batchAllowBackClose;
            });
        }

        private void ApplyBatchConfigGroupToSelected()
        {
            if (_currentDraft == null)
                return;

            string groupName = string.IsNullOrWhiteSpace(_currentDraft.groupName) ? "Default" : _currentDraft.groupName;
            ApplyBatchToSelected(cfg => cfg.configGroup = groupName);
        }

        private void ApplyBatchToSelected(Action<UIFormConfig> apply)
        {
            if (_currentDraft == null || _selectedFormItems.Count == 0 || apply == null)
                return;

            foreach (UIFormDraftItem item in _selectedFormItems)
            {
                if (item?.data == null)
                    continue;

                apply(item.data);
                item.isModified = true;
            }

            MarkCurrentDraftDirty();
            RefreshAllViewsPreserveFormSelection();
        }

        #endregion

        #region Unconfigured Scan

        private void ScanUnconfiguredUITypes()
        {
            _unconfiguredUITypes.Clear();

            HashSet<string> configuredTypes = new HashSet<string>(StringComparer.Ordinal);
            foreach (UIConfigGroup group in _groups)
            {
                if (group == null || group.forms == null)
                    continue;

                foreach (UIFormConfig cfg in group.forms)
                {
                    if (cfg == null || string.IsNullOrWhiteSpace(cfg.logicType))
                        continue;

                    configuredTypes.Add(cfg.logicType);
                    configuredTypes.Add(UIConfigEditorWindowHelper.GetShortTypeName(cfg.logicType));
                }
            }

            List<Type> allUITypes = new List<Type>();
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

                foreach (Type type in types)
                {
                    if (type == null || !type.IsClass || type.IsAbstract)
                        continue;

                    if (typeof(UIForm).IsAssignableFrom(type))
                        //typeof(UIWidget).IsAssignableFrom(type) ||
                        //typeof(UIItem).IsAssignableFrom(type))
                    {
                        allUITypes.Add(type);
                    }
                }
            }

            foreach (Type type in allUITypes.OrderBy(t => t.FullName))
            {
                if (configuredTypes.Contains(type.FullName) || configuredTypes.Contains(type.Name))
                    continue;

                _unconfiguredUITypes.Add(type);
            }

            RefreshUnconfiguredListView();
        }

        private UIFormDraftItem TryCreateDraftItemFromType(Type type)
        {
            if (_currentDraft == null || type == null)
                return null;

            string shortName = type.Name;

            bool exists = _currentDraft.forms.Any(f =>
                f != null &&
                f.data != null &&
                !string.IsNullOrWhiteSpace(f.data.logicType) &&
                (string.Equals(f.data.logicType, type.FullName, StringComparison.Ordinal) ||
                 string.Equals(f.data.logicType, shortName, StringComparison.Ordinal)));

            if (exists)
                return null;

            string path = UIConfigEditorWindowHelper.FindBestRuntimeAssetPathByName(shortName);

            UIFormConfig config = UIFormDraftFactory.CreateDefaultFormConfig(_currentDraft.groupName);
            config.formId = MakeUniqueFormId(_currentDraft, shortName);
            config.logicType = type.FullName;
            config.assetPath = path ?? string.Empty;

            return UIFormDraftFactory.CreateDraftItem(config, true, true);
        }
        
        private void AddTypeToCurrentGroup(Type type)
        {
            if (_currentDraft == null)
            {
                EditorUtility.DisplayDialog("提示", "请先选择一个 UIConfigGroup。", "确定");
                return;
            }

            if (type == null)
                return;

            UIFormDraftItem newItem = TryCreateDraftItemFromType(type);
            if (newItem == null)
            {
                SetStatusText($"未加入：{type.Name} 可能已经存在于当前 Group。");
                return;
            }

            _currentDraft.forms.Add(newItem);
            SelectSingleFormItem(newItem, false);
            MarkCurrentDraftDirty();

            ScanUnconfiguredUITypes();
            RefreshAllViewsPreserveFormSelection();

            SetStatusText($"已加入 UI 类型：{type.FullName}");
        }

        private void AddAllUnconfiguredToCurrentGroup()
        {
            if (_currentDraft == null)
                return;


            List<Type> visibleTypes = BuildVisibleUnconfiguredTypes();
            UIFormDraftItem lastAdded = null;
            int addedCount = 0;

            foreach (Type type in visibleTypes)
            {
                UIFormDraftItem item = TryCreateDraftItemFromType(type);
                if (item == null)
                    continue;

                _currentDraft.forms.Add(item);
                lastAdded = item;
                addedCount++;
            }
            if (addedCount <= 0)
            {
                RefreshUnconfiguredListView();
                return;
            }

            if (lastAdded != null)
            {
                SelectSingleFormItem(lastAdded, false);
            }

            MarkCurrentDraftDirty();

            _selectedUnconfiguredTypes.Clear();
            _unconfiguredListView?.ClearSelection();

            ScanUnconfiguredUITypes();
            RefreshAllViewsPreserveFormSelection();

            SetStatusText($"已加入 {addedCount} 个 UI 类型到当前 Group。");
        }

        #endregion

        #region Helpers
        private void SetStatusText(string text)
        {
            if (_statusLabel != null)
                _statusLabel.text = text ?? string.Empty;

            Repaint();
        }
        
        private string GetStatusDisplayName(string status)
        {
            switch (status)
            {
                case "Error":
                    return "错误";
                case "Warn":
                    return "警告";
                case "Ok":
                    return "正常";
                default:
                    return status;
            }
        }
        
        private void DeleteSingleFormItemWithConfirm(UIFormDraftItem item)
        {
            if (_currentDraft == null || item == null)
                return;

            string formId = SafeFormId(item.data);
            bool ok = EditorUtility.DisplayDialog("删除 Form", $"确定删除 Form：{formId} 吗？", "删除", "取消");
            if (!ok)
                return;

            bool wasPrimary = item == _selectedFormItem;
            int rawIndex = _currentDraft.forms.IndexOf(item);

            _currentDraft.forms.Remove(item);
            _selectedFormItems.Remove(item);

            if (_lastPrimarySelectedFormItem == item)
            {
                _lastPrimarySelectedFormItem = null;
                _lastPrimarySelectedFormId = null;
            }

            if (wasPrimary)
            {
                UIFormDraftItem fallback = null;
                if (_currentDraft.forms.Count > 0)
                {
                    int nextIndex = Mathf.Clamp(rawIndex, 0, _currentDraft.forms.Count - 1);
                    fallback = _currentDraft.forms[nextIndex];
                }

                if (fallback != null)
                    SelectSingleFormItem(fallback);
                else
                    ClearFormSelectionState();
            }

            MarkCurrentDraftDirty();
            RefreshAllViewsPreserveFormSelection();
        }
        
        private MonoScript FindMonoScriptByTypeName(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName))
                return null;

            Type targetType = UIConfigEditorWindowHelper.FindTypeByName(typeName);
            string shortName = UIConfigEditorWindowHelper.GetShortTypeName(typeName) ?? typeName;

            string[] guids = AssetDatabase.FindAssets($"{shortName} t:MonoScript");
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                MonoScript script = AssetDatabase.LoadAssetAtPath<MonoScript>(path);
                if (script == null)
                    continue;

                Type scriptType = script.GetClass();
                if (targetType != null)
                {
                    if (scriptType == targetType)
                        return script;
                }
                else if (string.Equals(script.name, shortName, StringComparison.Ordinal))
                {
                    return script;
                }
            }

            return null;
        }
        
        private void PingPrefabByPath(string assetPath)
        {
            if (string.IsNullOrWhiteSpace(assetPath))
                return;

            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>(assetPath);
            if (prefab == null)
                return;

            Selection.activeObject = prefab;
            EditorGUIUtility.PingObject(prefab);
        }

        private void PingScriptByTypeName(string typeName)
        {
            MonoScript script = FindMonoScriptByTypeName(typeName);
            if (script == null)
                return;

            Selection.activeObject = script;
            EditorGUIUtility.PingObject(script);
        }
        
        private void RegisterDragAndDrop(VisualElement root)
        {
            if (root == null)
                return;

            root.RegisterCallback<DragUpdatedEvent>(evt =>
            {
                bool supported = _currentDraft != null && HasSupportedDraggedObjects();
                DragAndDrop.visualMode = supported ? DragAndDropVisualMode.Copy : DragAndDropVisualMode.Rejected;

                if (supported && !_isDragHovering)
                {
                    _isDragHovering = true;
                    root.AddToClassList("drop-target");
                }
                else if (!supported && _isDragHovering)
                {
                    _isDragHovering = false;
                    root.RemoveFromClassList("drop-target");
                }

                evt.StopPropagation();
            });

            root.RegisterCallback<DragLeaveEvent>(evt =>
            {
                _isDragHovering = false;
                root.RemoveFromClassList("drop-target");
            });

            root.RegisterCallback<DragPerformEvent>(evt =>
            {
                _isDragHovering = false;
                root.RemoveFromClassList("drop-target");

                if (_currentDraft == null || !HasSupportedDraggedObjects())
                    return;

                DragAndDrop.AcceptDrag();

                int addedCount = 0;
                UIFormDraftItem lastAdded = null;

                foreach (UnityEngine.Object obj in DragAndDrop.objectReferences)
                {
                    if (obj is GameObject go)
                    {
                        UIFormDraftItem item = TryAddPrefabObjectToCurrentDraft(go);
                        if (item != null)
                        {
                            addedCount++;
                            lastAdded = item;
                        }
                    }
                    else if (obj is MonoScript script)
                    {
                        UIFormDraftItem item = TryAddScriptObjectToCurrentDraft(script);
                        if (item != null)
                        {
                            addedCount++;
                            lastAdded = item;
                        }
                    }
                }

                if (addedCount > 0)
                {
                    if (lastAdded != null)
                        SelectSingleFormItem(lastAdded, false);

                    MarkCurrentDraftDirty();
                    RefreshAllViewsPreserveFormSelection();
                }

                evt.StopPropagation();
            });
        }

        private bool HasSupportedDraggedObjects()
        {
            if (DragAndDrop.objectReferences == null || DragAndDrop.objectReferences.Length == 0)
                return false;

            foreach (UnityEngine.Object obj in DragAndDrop.objectReferences)
            {
                if (obj is MonoScript)
                    return true;

                if (obj is GameObject go && IsPrefabAssetObject(go))
                    return true;
            }

            return false;
        }

        private bool IsPrefabAssetObject(GameObject go)
        {
            if (go == null)
                return false;

            string path = AssetDatabase.GetAssetPath(go);
            if (string.IsNullOrWhiteSpace(path))
                return false;

            PrefabAssetType assetType = PrefabUtility.GetPrefabAssetType(go);
            return assetType != PrefabAssetType.NotAPrefab;
        }
        
        private UIFormDraftItem TryAddPrefabObjectToCurrentDraft(GameObject go)
        {
            if (_currentDraft == null || go == null)
                return null;

            if (!IsPrefabAssetObject(go))
                return null;
            
            string path = AssetDatabase.GetAssetPath(go);
            if (string.IsNullOrEmpty(path))
                return null;

            string runtimePath = UIConfigEditorWindowHelper.ConvertAssetPathToResourcesRuntimePath(path);
            if (string.IsNullOrWhiteSpace(runtimePath))
                return null;

            string prefabName = go.name;
            string logicType = UIConfigEditorWindowHelper.TryFindTypeFullName(prefabName);
            string formId = MakeUniqueFormId(_currentDraft, prefabName);

            UIFormConfig config = UIFormDraftFactory.CreateDefaultFormConfig(_currentDraft.groupName);
            config.formId = formId;
            config.logicType = logicType ?? prefabName;
            config.assetPath = runtimePath;

            UIFormDraftItem item = UIFormDraftFactory.CreateDraftItem(config, true, true);
            _currentDraft.forms.Add(item);
            return item;
        }

        private UIFormDraftItem TryAddScriptObjectToCurrentDraft(MonoScript script)
        {
            if (_currentDraft == null || script == null)
                return null;

            Type type = script.GetClass();
            string shortName = script.name;
            string fullName = type != null ? type.FullName : shortName;
            string guessedPath = UIConfigEditorWindowHelper.FindBestRuntimeAssetPathByName(shortName);

            UIFormConfig config = UIFormDraftFactory.CreateDefaultFormConfig(_currentDraft.groupName);
            config.formId = MakeUniqueFormId(_currentDraft, shortName);
            config.logicType = fullName;
            config.assetPath = guessedPath ?? string.Empty;

            UIFormDraftItem item = UIFormDraftFactory.CreateDraftItem(config, true, true);
            _currentDraft.forms.Add(item);
            return item;
        }

        private void LocateFormFromValidationMessage(UIConfigGroupDraftValidator.ValidationMessage msg)
        {
            if (_currentDraft == null || msg == null)
                return;

            UIFormDraftItem target = TryResolveFormFromValidationMessageStructured(msg);

            if (target == null && !string.IsNullOrWhiteSpace(msg.Message))
            {
                foreach (UIFormDraftItem item in BuildVisibleFormItems())
                {
                    string formId = item?.data?.formId;
                    if (string.IsNullOrWhiteSpace(formId))
                        continue;

                    if (msg.Message.IndexOf(formId, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        target = item;
                        break;
                    }
                }
            }

            if (target == null && !string.IsNullOrWhiteSpace(msg.Message))
            {
                foreach (UIFormDraftItem item in _currentDraft.forms)
                {
                    string formId = item?.data?.formId;
                    if (string.IsNullOrWhiteSpace(formId))
                        continue;

                    if (msg.Message.IndexOf(formId, StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        target = item;
                        break;
                    }
                }
            }

            if (target == null)
                return;

            SelectSingleFormItem(target);
            RefreshFormDetailFromSelection();
            RefreshFormListView();
            RefreshStatusLabel();
        }
        
        private UIFormDraftItem TryResolveFormFromValidationMessageStructured(UIConfigGroupDraftValidator.ValidationMessage msg)
        {
            if (msg == null || _currentDraft == null || _currentDraft.forms == null)
                return null;
            
            if (!string.IsNullOrWhiteSpace(msg.RowId))
            {
                UIFormDraftItem byRowId = _currentDraft.forms.FirstOrDefault(x =>
                    x != null &&
                    string.Equals(x.rowId, msg.RowId, StringComparison.Ordinal));

                if (byRowId != null)
                    return byRowId;
            }

            if (!string.IsNullOrWhiteSpace(msg.FormId))
            {
                UIFormDraftItem byFormId = _currentDraft.forms.FirstOrDefault(x =>
                    x != null &&
                    x.data != null &&
                    string.Equals(x.data.formId, msg.FormId, StringComparison.OrdinalIgnoreCase));

                if (byFormId != null)
                    return byFormId;
            }

            if (msg.FormIndex >= 0 && msg.FormIndex < _currentDraft.forms.Count)
            {
                UIFormDraftItem byIndex = _currentDraft.forms[msg.FormIndex];
                if (byIndex != null)
                    return byIndex;
            }

            if (!string.IsNullOrWhiteSpace(msg.LogicType))
            {
                UIFormDraftItem byLogicType = _currentDraft.forms.FirstOrDefault(x =>
                    x != null &&
                    x.data != null &&
                    string.Equals(x.data.logicType, msg.LogicType, StringComparison.Ordinal));

                if (byLogicType != null)
                    return byLogicType;
            }

            if (!string.IsNullOrWhiteSpace(msg.AssetPath))
            {
                UIFormDraftItem byAssetPath = _currentDraft.forms.FirstOrDefault(x =>
                    x != null &&
                    x.data != null &&
                    string.Equals(x.data.assetPath, msg.AssetPath, StringComparison.OrdinalIgnoreCase));

                if (byAssetPath != null)
                    return byAssetPath;
            }

            return null;
        }
        
        private void MarkCurrentDraftDirty()
        {
            if (_currentDraft == null)
                return;

            _currentDraft.isDirty = true;
            RefreshDraftBindings();
            RefreshStatusLabel();
        }

        private List<GroupListItem> BuildVisibleGroupItems()
        {
            List<GroupListItem> items = new List<GroupListItem>();

            foreach (UIConfigGroup group in _groups)
            {
                if (group == null)
                    continue;

                string title = GetGroupDisplayTitle(group);
                if (!string.IsNullOrWhiteSpace(_groupSearch) &&
                    title.IndexOf(_groupSearch, StringComparison.OrdinalIgnoreCase) < 0)
                {
                    continue;
                }

                items.Add(new GroupListItem
                {
                    Group = group,
                    Title = title,
                    AssetPath = AssetDatabase.GetAssetPath(group)
                });
            }

            return items;
        }

        private List<UIFormDraftItem> BuildVisibleFormItems()
        {
            List<UIFormDraftItem> result = new List<UIFormDraftItem>();
            if (_currentDraft == null || _currentDraft.forms == null)
                return result;

            IEnumerable<UIFormDraftItem> items = ApplySort(_currentDraft.forms);

            foreach (UIFormDraftItem item in items)
            {
                UIFormConfig cfg = item?.data;
                if (cfg == null)
                {
                    result.Add(item);
                    continue;
                }

                bool matchSearch = string.IsNullOrWhiteSpace(_formSearch) ||
                                   UIConfigEditorWindowHelper.ContainsIgnoreCase(cfg.formId, _formSearch) ||
                                   UIConfigEditorWindowHelper.ContainsIgnoreCase(cfg.logicType, _formSearch) ||
                                   UIConfigEditorWindowHelper.ContainsIgnoreCase(cfg.assetPath, _formSearch) ||
                                   UIConfigEditorWindowHelper.ContainsIgnoreCase(cfg.configGroup, _formSearch);

                bool invalid = IsInvalid(item);

                if (_showOnlyInvalid && !invalid)
                    continue;

                if (!matchSearch)
                    continue;

                result.Add(item);
            }

            return result;
        }

        private List<Type> BuildVisibleUnconfiguredTypes()
        {
            IEnumerable<Type> query = _unconfiguredUITypes;

            if (_unconfiguredOnlyWithPrefab)
            {
                query = query.Where(type =>
                {
                    if (type == null)
                        return false;

                    string prefabPath = UIConfigEditorWindowHelper.FindBestRuntimeAssetPathByName(type.Name);
                    return !string.IsNullOrWhiteSpace(prefabPath);
                });
            }

            if (!string.IsNullOrWhiteSpace(_unconfiguredSearch))
            {
                query = query.Where(type =>
                {
                    if (type == null)
                        return false;

                    string fullName = type.FullName ?? type.Name;
                    string shortName = type.Name;
                    string prefabPath = UIConfigEditorWindowHelper.FindBestRuntimeAssetPathByName(type.Name) ?? string.Empty;

                    return fullName.IndexOf(_unconfiguredSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        shortName.IndexOf(_unconfiguredSearch, StringComparison.OrdinalIgnoreCase) >= 0 ||
                        prefabPath.IndexOf(_unconfiguredSearch, StringComparison.OrdinalIgnoreCase) >= 0;
                });
            }

            return query
                .Where(type => type != null)
                .OrderBy(type => type.FullName ?? type.Name)
                .ToList();
        }
        
        private void AddSelectedUnconfiguredToCurrentGroup()
        {
            if (_currentDraft == null)
            {
                EditorUtility.DisplayDialog("提示", "请先选择一个 UIConfigGroup。", "确定");
                return;
            }

            List<Type> selectedTypes = _selectedUnconfiguredTypes
                .Where(type => type != null)
                .Distinct()
                .ToList();

            if (selectedTypes.Count == 0)
            {
                EditorUtility.DisplayDialog("提示", "请先在未配置 UI 类型列表中选择一个或多个类型。", "确定");
                return;
            }

            UIFormDraftItem lastAdded = null;
            int addedCount = 0;

            foreach (Type type in selectedTypes)
            {
                UIFormDraftItem item = TryCreateDraftItemFromType(type);
                if (item == null)
                    continue;

                _currentDraft.forms.Add(item);
                lastAdded = item;
                addedCount++;
            }

            if (addedCount <= 0)
            {
                SetStatusText("选中的类型没有可加入项，可能已经存在于当前 Group。");
                RefreshUnconfiguredListView();
                return;
            }

            if (lastAdded != null)
                SelectSingleFormItem(lastAdded, false);

            _selectedUnconfiguredTypes.Clear();
            _unconfiguredListView?.ClearSelection();

            MarkCurrentDraftDirty();
            ScanUnconfiguredUITypes();
            RefreshAllViewsPreserveFormSelection();

            SetStatusText($"已加入 {addedCount} 个 UI 类型到当前 Group。");
        }
        
        private IEnumerable<UIFormDraftItem> ApplySort(IEnumerable<UIFormDraftItem> items)
        {
            Func<UIFormDraftItem, object> selector = item => item?.data?.formId;

            switch (_sortField)
            {
                case SortField.FormId: selector = item => item?.data?.formId; break;
                case SortField.LogicType: selector = item => item?.data?.logicType; break;
                case SortField.AssetPath: selector = item => item?.data?.assetPath; break;
                case SortField.ConfigGroup: selector = item => item?.data?.configGroup; break;
                case SortField.Layer: selector = item => item != null && item.data != null ? item.data.layer : default; break;
                case SortField.AllowMultiInstance: selector = item => item != null && item.data != null && item.data.allowMultiInstance; break;
                case SortField.FullScreen: selector = item => item != null && item.data != null && item.data.fullScreen; break;
                case SortField.PauseCovered: selector = item => item != null && item.data != null && item.data.pauseCovered; break;
                case SortField.CloseMode: selector = item => item != null && item.data != null ? item.data.closeMode : default; break;
                case SortField.CoverVisualMode: selector = item => item != null && item.data != null ? item.data.coverVisualMode : default; break;
                case SortField.Resident: selector = item => item != null && item.data != null && item.data.resident; break;
                case SortField.BringToFrontOnOpen: selector = item => item != null && item.data != null && item.data.bringToFrontOnOpen; break;
                case SortField.AllowBackClose: selector = item => item != null && item.data != null && item.data.allowBackClose; break;
                case SortField.UseMask: selector = item => item != null && item.data != null && item.data.useMask; break;
                case SortField.MaskMode: selector = item => item != null && item.data != null ? item.data.maskMode : default; break;
                case SortField.OpenAnimation: selector = item => item != null && item.data != null ? item.data.openAnimation : default; break;
                case SortField.CloseAnimation: selector = item => item != null && item.data != null ? item.data.closeAnimation : default; break;
            }

            return _sortAscending ? items.OrderBy(selector) : items.OrderByDescending(selector);
        }

        private bool IsInvalid(UIFormDraftItem item)
        {
            UIFormConfig cfg = item?.data;
            if (cfg == null)
                return true;

            if (string.IsNullOrWhiteSpace(cfg.formId))
                return true;

            if (HasDuplicateFormIdInDraft(cfg.formId, item))
                return true;

            if (string.IsNullOrWhiteSpace(cfg.logicType))
                return true;

            if (string.IsNullOrWhiteSpace(cfg.assetPath))
                return true;

            if (string.IsNullOrWhiteSpace(cfg.configGroup))
                return true;

            if (UIConfigEditorWindowHelper.FindTypeByName(cfg.logicType) == null)
                return true;

            if (UIConfigEditorWindowHelper.TryLoadPrefabAtRuntimeStylePath(cfg.assetPath) == null)
                return true;

            if (cfg.useMask && cfg.maskMode == UIMaskMode.None)
                return true;

            if (cfg.openAnimation != UIAnimationPreset.None && cfg.openAnimationDuration <= 0f)
                return true;

            if (cfg.closeAnimation != UIAnimationPreset.None && cfg.closeAnimationDuration <= 0f)
                return true;

            return false;
        }

        private bool HasDuplicateFormIdInDraft(string formId, UIFormDraftItem self)
        {
            if (_currentDraft == null || _currentDraft.forms == null || string.IsNullOrWhiteSpace(formId))
                return false;

            foreach (UIFormDraftItem item in _currentDraft.forms)
            {
                if (item == null || item == self || item.data == null || string.IsNullOrWhiteSpace(item.data.formId))
                    continue;

                if (string.Equals(item.data.formId, formId, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        private string SafeFormId(UIFormConfig cfg)
        {
            if (cfg == null)
                return "(null)";
            return string.IsNullOrWhiteSpace(cfg.formId) ? "(UnnamedForm)" : cfg.formId;
        }

        private string GetGroupDisplayTitle(UIConfigGroup group)
        {
            if (group == null)
                return "(null)";

            string g = string.IsNullOrWhiteSpace(group.groupName) ? string.Empty : group.groupName;
            return string.IsNullOrWhiteSpace(g) ? group.name : $"{group.name} [{g}]";
        }

        private string GetRowStatus(UIFormDraftItem item)
        {
            UIFormConfig cfg = item?.data;
            if (cfg == null)
                return "Error";

            bool hasError = false;
            bool hasWarn = false;

            if (string.IsNullOrWhiteSpace(cfg.formId))
                hasError = true;

            if (string.IsNullOrWhiteSpace(cfg.logicType))
            {
                hasWarn = true;
            }
            else if (UIConfigEditorWindowHelper.FindTypeByName(cfg.logicType) == null)
            {
                hasError = true;
            }

            if (string.IsNullOrWhiteSpace(cfg.assetPath))
            {
                hasWarn = true;
            }
            else if (UIConfigEditorWindowHelper.TryLoadPrefabAtRuntimeStylePath(cfg.assetPath) == null)
            {
                hasError = true;
            }

            if (string.IsNullOrWhiteSpace(cfg.configGroup))
            {
                hasWarn = true;
            }
            else if (_currentDraft != null &&
                     !string.Equals(cfg.configGroup, _currentDraft.groupName, StringComparison.Ordinal))
            {
                hasWarn = true;
            }

            if (HasDuplicateFormIdInDraft(cfg.formId, item))
                hasError = true;

            if (cfg.useMask && cfg.maskMode == UIMaskMode.None)
                hasWarn = true;

            if (cfg.openAnimation != UIAnimationPreset.None && cfg.openAnimationDuration <= 0f)
                hasWarn = true;

            if (cfg.closeAnimation != UIAnimationPreset.None && cfg.closeAnimationDuration <= 0f)
                hasWarn = true;

            if (hasError) return "Error";
            if (hasWarn) return "Warn";
            return "Ok";
        }

        private void AutoFixForm(UIFormDraftItem item)
        {
            if (_currentDraft == null || item?.data == null)
                return;

            UIFormConfig cfg = item.data;
            string guessedName = null;

            if (!string.IsNullOrWhiteSpace(cfg.assetPath))
            {
                guessedName = UIConfigEditorWindowHelper.GuessNameFromAssetPath(cfg.assetPath);
            }

            if (string.IsNullOrWhiteSpace(guessedName) && !string.IsNullOrWhiteSpace(cfg.logicType))
            {
                guessedName = UIConfigEditorWindowHelper.GetShortTypeName(cfg.logicType);
            }

            if (string.IsNullOrWhiteSpace(cfg.formId))
            {
                cfg.formId = !string.IsNullOrWhiteSpace(guessedName) ? guessedName : "NewForm";
                cfg.formId = MakeUniqueFormId(_currentDraft, cfg.formId, item);
            }

            if (string.IsNullOrWhiteSpace(cfg.logicType) && !string.IsNullOrWhiteSpace(guessedName))
            {
                Type type = UIConfigEditorWindowHelper.FindTypeByName(guessedName);
                cfg.logicType = type != null ? type.FullName : guessedName;
            }

            if (string.IsNullOrWhiteSpace(cfg.assetPath) && !string.IsNullOrWhiteSpace(guessedName))
            {
                string path = UIConfigEditorWindowHelper.FindBestRuntimeAssetPathByName(guessedName);
                if (!string.IsNullOrWhiteSpace(path))
                {
                    cfg.assetPath = path;
                }
            }

            if (string.IsNullOrWhiteSpace(cfg.configGroup))
            {
                cfg.configGroup = string.IsNullOrWhiteSpace(_currentDraft.groupName) ? "Default" : _currentDraft.groupName;
            }

            item.isModified = true;
        }

        private string MakeUniqueFormId(UIConfigGroupDraft draft, string baseId, UIFormDraftItem ignore = null)
        {
            if (draft == null)
                return string.IsNullOrWhiteSpace(baseId) ? "NewForm" : baseId;

            if (string.IsNullOrWhiteSpace(baseId))
                baseId = "NewForm";

            HashSet<string> used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (UIFormDraftItem item in draft.forms)
            {
                if (item == null || item == ignore || item.data == null || string.IsNullOrWhiteSpace(item.data.formId))
                    continue;

                used.Add(item.data.formId);
            }

            string result = baseId;
            int suffix = 1;
            while (used.Contains(result))
            {
                result = $"{baseId}_{suffix++}";
            }

            return result;
        }

        private void PingDefaultFolder()
        {
            UnityEngine.Object obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(DefaultCreateFolder);
            if (obj != null)
            {
                Selection.activeObject = obj;
                EditorGUIUtility.PingObject(obj);
            }
            else
            {
                EditorUtility.DisplayDialog("提示", $"目录不存在：{DefaultCreateFolder}", "确定");
            }
        }

        #endregion
    }
}
