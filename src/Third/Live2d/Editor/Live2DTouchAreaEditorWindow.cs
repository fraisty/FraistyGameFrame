#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEditorInternal;
using UnityEngine;
using UnityEngine.UI;

namespace FRFrame.Live2D.Editor
{
    public class Live2DTouchAreaEditorWindow : EditorWindow
    {
        private enum EditHandle
        {
            None,
            Move,
            Left,
            Right,
            Top,
            Bottom,
            TopLeft,
            TopRight,
            BottomLeft,
            BottomRight
        }

        private Live2DInteractionProfile _profile;
        private Live2DMotionLibrary _library;
        private Live2DMotionController _controller;
        private Live2DRawImageInputRuntime _inputRuntime;
        private RawImage _rawImage;
        private int _layoutIndex;
        private int _selectedAreaIndex = -1;
        private int _selectedBindingIndex = -1;
        private int _selectedActionIndex = -1;
        private bool _recordByDrag;
        private bool _editRectMode = true;
        private bool _showAreaList = true;
        private bool _showBindingEditor = true;
        private bool _showMotionControls = true;
        private bool _showTransformTools = true;
        private bool _showEmbeddedCanvas = true;
        private bool _drawSnapshotBackground = true;
        private Texture2D _gameViewSnapshot;
        private Vector2 _scroll;
        private Vector2 _dragStart;
        private Vector2 _dragCurrent;
        private Rect _editStartRect;
        private bool _draggingCreate;
        private bool _draggingEdit;
        private EditHandle _activeHandle;
        private string _motionSearch = string.Empty;
        private string _newLayoutId = "Layout";
        private string _areaPrefix = "Area";
        private Dictionary<string, string> _parameterDisplayNames;
        private string _parameterDisplayNameSourcePath;
        private const float MinRectSize = 0.01f;
        private const float HandleSize = 10f;

        private static Live2DTouchAreaEditorWindow _mainWindow;
        private static Live2DTouchAreaCanvasWindow _canvasWindow;

        [MenuItem("Tools/Live2D/New Runtime/Touch Area Editor")]
        public static void Open()
        {
            _mainWindow = GetWindow<Live2DTouchAreaEditorWindow>("Live2D Touch Areas");
        }

        [MenuItem("Tools/Live2D/New Runtime/Touch Area Editor Canvas")]
        public static void OpenCanvas()
        {
            if (_mainWindow == null) _mainWindow = GetWindow<Live2DTouchAreaEditorWindow>("Live2D Touch Areas");
            if (_canvasWindow == null) _canvasWindow = CreateWindow<Live2DTouchAreaCanvasWindow>("Live2D Touch Canvas");
            _canvasWindow.Owner = _mainWindow;
            _canvasWindow.minSize = new Vector2(720, 520);
            _canvasWindow.Show();
            _canvasWindow.Focus();
        }

        private Live2DInteractionLayout CurrentLayout => _profile != null && _profile.layouts != null && _layoutIndex >= 0 && _layoutIndex < _profile.layouts.Count ? _profile.layouts[_layoutIndex] : null;
        private Live2DTouchArea SelectedArea => CurrentLayout != null && CurrentLayout.areas != null && _selectedAreaIndex >= 0 && _selectedAreaIndex < CurrentLayout.areas.Count ? CurrentLayout.areas[_selectedAreaIndex] : null;
        private Live2DInteractionBinding SelectedBinding => SelectedArea != null && SelectedArea.bindings != null && _selectedBindingIndex >= 0 && _selectedBindingIndex < SelectedArea.bindings.Count ? SelectedArea.bindings[_selectedBindingIndex] : null;
        private Live2DInteractionAction SelectedAction => SelectedBinding != null && SelectedBinding.actions != null && _selectedActionIndex >= 0 && _selectedActionIndex < SelectedBinding.actions.Count ? SelectedBinding.actions[_selectedActionIndex] : null;

        private void OnEnable()
        {
            _mainWindow = this;
            AutoLinkFromScene(false);
        }

        private void OnGUI()
        {
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            DrawReferenceBlock();
            EditorGUILayout.Space(12);
            DrawLayoutBlock();
            EditorGUILayout.Space(12);
            DrawCanvasBlock();
            EditorGUILayout.Space(12);
            DrawAreaBlock();
            EditorGUILayout.Space(12);
            DrawMotionBlock();
            EditorGUILayout.Space(12);
            DrawTransformBlock();
            EditorGUILayout.Space(12);
            DrawSaveBlock();
            EditorGUILayout.EndScrollView();
        }

        private void DrawReferenceBlock()
        {
            EditorGUILayout.LabelField("场景与资产", EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("自动关联场景对象", GUILayout.Height(26))) AutoLinkFromScene(true);
                if (GUILayout.Button("从 Selection 关联", GUILayout.Height(26))) LinkFromSelection();
            }

            EditorGUI.BeginChangeCheck();
            _controller = (Live2DMotionController)EditorGUILayout.ObjectField("Motion Controller", _controller, typeof(Live2DMotionController), true);
            _inputRuntime = (Live2DRawImageInputRuntime)EditorGUILayout.ObjectField("RawImage Input Runtime", _inputRuntime, typeof(Live2DRawImageInputRuntime), true);
            _rawImage = (RawImage)EditorGUILayout.ObjectField("RawImage", _rawImage, typeof(RawImage), true);
            _profile = (Live2DInteractionProfile)EditorGUILayout.ObjectField("Interaction Profile", _profile, typeof(Live2DInteractionProfile), false);
            _library = (Live2DMotionLibrary)EditorGUILayout.ObjectField("Motion Library", _library, typeof(Live2DMotionLibrary), false);
            if (EditorGUI.EndChangeCheck())
            {
                SyncReferences();
                ClampSelection();
            }

            if (_controller != null)
            {
                EditorGUILayout.LabelField("当前 Base", string.IsNullOrEmpty(_controller.CurrentBaseClipId) ? "(None)" : _controller.CurrentBaseClipId);
                EditorGUILayout.LabelField("当前 Overlay", string.IsNullOrEmpty(_controller.CurrentOverlayClipId) ? "(None)" : _controller.CurrentOverlayClipId);
            }
        }

        private void DrawLayoutBlock()
        {
            if (_profile == null)
            {
                EditorGUILayout.HelpBox("请先选择 Interaction Profile，或点击自动关联场景对象。", MessageType.Info);
                return;
            }

            if (_profile.layouts == null) _profile.layouts = new List<Live2DInteractionLayout>();
            EditorGUILayout.LabelField("Layout 编辑", EditorStyles.boldLabel);

            var names = BuildLayoutNames();
            if (names.Length > 0)
            {
                EditorGUI.BeginChangeCheck();
                _layoutIndex = EditorGUILayout.Popup("当前 Layout", Mathf.Clamp(_layoutIndex, 0, names.Length - 1), names);
                if (EditorGUI.EndChangeCheck()) ResetAreaSelection();
            }
            else
            {
                EditorGUILayout.LabelField("当前 Layout", "(None)");
            }

            using (new EditorGUILayout.HorizontalScope())
            {
                _newLayoutId = EditorGUILayout.TextField(_newLayoutId);
                if (GUILayout.Button("新建 Layout", GUILayout.Width(110))) CreateLayout(_newLayoutId);
                using (new EditorGUI.DisabledScope(CurrentLayout == null))
                {
                    if (GUILayout.Button("复制", GUILayout.Width(70))) CopyCurrentLayout();
                    if (GUILayout.Button("删除", GUILayout.Width(70))) DeleteCurrentLayout();
                }
            }

            var layout = CurrentLayout;
            if (layout == null) return;

            EditorGUI.BeginChangeCheck();
            layout.layoutId = EditorGUILayout.TextField("Layout Id", layout.layoutId);
            layout.displayName = EditorGUILayout.TextField("Display Name", layout.displayName);
            if (GUILayout.Button("设为默认 Layout")) _profile.defaultLayoutId = layout.layoutId;
            _profile.defaultLayoutId = EditorGUILayout.TextField("Default Layout Id", _profile.defaultLayoutId);
            if (EditorGUI.EndChangeCheck()) MarkChanged();

            DrawLayoutBindingBlock(layout);
        }

        private void DrawLayoutBindingBlock(Live2DInteractionLayout layout)
        {
            EditorGUILayout.LabelField("Base Motion -> Layout 绑定", EditorStyles.boldLabel);
            if (_profile.layoutBindings == null) _profile.layoutBindings = new List<Live2DLayoutBinding>();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_controller == null || string.IsNullOrEmpty(_controller.CurrentBaseClipId)))
                {
                    if (GUILayout.Button("绑定当前 Base Motion 到当前 Layout"))
                        BindMotionToLayout(_controller.CurrentBaseClipId, layout.layoutId);
                }

                if (GUILayout.Button("新增空绑定", GUILayout.Width(100)))
                {
                    _profile.layoutBindings.Add(new Live2DLayoutBinding { layoutId = layout.layoutId });
                    MarkChanged();
                }
            }

            for (int i = 0; i < _profile.layoutBindings.Count; i++)
            {
                var binding = _profile.layoutBindings[i];
                if (binding == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    EditorGUI.BeginChangeCheck();
                    binding.baseMotionId = DrawMotionIdPopup(binding.baseMotionId, GUILayout.MinWidth(160));
                    binding.baseGroupId = EditorGUILayout.TextField(binding.baseGroupId, GUILayout.MinWidth(100));
                    binding.layoutId = DrawLayoutIdPopup(binding.layoutId, GUILayout.MinWidth(130));
                    if (EditorGUI.EndChangeCheck()) MarkChanged();
                    if (GUILayout.Button("X", GUILayout.Width(24)))
                    {
                        _profile.layoutBindings.RemoveAt(i);
                        MarkChanged();
                        break;
                    }
                }
            }
        }

        private void DrawCanvasBlock()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _showEmbeddedCanvas = EditorGUILayout.Foldout(_showEmbeddedCanvas, "区域画布", true);
                GUILayout.FlexibleSpace();
                _showEmbeddedCanvas = GUILayout.Toggle(_showEmbeddedCanvas, _showEmbeddedCanvas ? "隐藏内嵌画布" : "显示内嵌画布", "Button", GUILayout.Width(120), GUILayout.Height(22));
            }
            DrawCanvasToolbar();
            if (_showEmbeddedCanvas) DrawEmbeddedCanvas(560f);
        }

        private void DrawCanvasToolbar()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _recordByDrag = GUILayout.Toggle(_recordByDrag, "拖拽创建 Rect", "Button", GUILayout.Height(24));
                _editRectMode = GUILayout.Toggle(_editRectMode, "拖拽编辑 Rect", "Button", GUILayout.Height(24));
                _drawSnapshotBackground = GUILayout.Toggle(_drawSnapshotBackground, "显示 Game 快照", "Button", GUILayout.Height(24));
                if (GUILayout.Button("刷新 Game 快照", GUILayout.Height(24))) CaptureGameViewSnapshot();
                if (GUILayout.Button("打开独立画布", GUILayout.Height(24))) OpenCanvas();
                _areaPrefix = EditorGUILayout.TextField("新区域前缀", _areaPrefix);
            }

            if (_inputRuntime != null)
            {
                EditorGUI.BeginChangeCheck();
                using (new EditorGUILayout.HorizontalScope())
                {
                    _inputRuntime.showEditorAreaOverlay = GUILayout.Toggle(_inputRuntime.showEditorAreaOverlay, "GameView 显示区域", "Button", GUILayout.Height(24));
                    _inputRuntime.enableEditorAreaEditing = GUILayout.Toggle(_inputRuntime.enableEditorAreaEditing, "GameView 可编辑", "Button", GUILayout.Height(24));
                }
                if (EditorGUI.EndChangeCheck()) EditorUtility.SetDirty(_inputRuntime);
            }
        }

        public void DrawCanvasOnly(float height)
        {
            DrawCanvasToolbar();
            DrawEmbeddedCanvas(height);
        }

        public void DrawEmbeddedCanvas(float height)
        {
            Rect outer = GUILayoutUtility.GetRect(10, height, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(outer, new Color(0.05f, 0.05f, 0.05f));
            Rect canvas = GetAspectCanvasRect(outer);
            DrawCanvasBackground(canvas);
            GUI.Label(new Rect(canvas.x + 8, canvas.y + 6, canvas.width - 16, 20), "等比例 RawImage/GameView 预览：可用 Game 快照作背景；关闭编辑后只显示区域。", EditorStyles.whiteLabel);
            HandleCanvasInput(canvas);
            DrawAreas(canvas);
            if (_draggingCreate) DrawRect(MakeRect(_dragStart, _dragCurrent), new Color(1f, 1f, 0f, 0.2f), Color.yellow);
        }

        private void DrawAreaBlock()
        {
            var layout = CurrentLayout;
            if (layout == null) return;
            if (layout.areas == null) layout.areas = new List<Live2DTouchArea>();

            _showAreaList = EditorGUILayout.Foldout(_showAreaList, "区域与交互编辑", true);
            if (!_showAreaList) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Rect")) AddRectArea();
                if (GUILayout.Button("Add Polygon")) AddPolygonArea();
                using (new EditorGUI.DisabledScope(SelectedArea == null))
                {
                    if (GUILayout.Button("Duplicate")) DuplicateSelectedArea();
                    if (GUILayout.Button("Delete")) DeleteSelectedArea();
                }
            }

            for (int i = 0; i < layout.areas.Count; i++)
            {
                var area = layout.areas[i];
                if (area == null) continue;
                string label = $"{i}. {area.displayName} / {area.areaId}  P:{area.priority} {(area.enabled ? string.Empty : "[Disabled]")}";
                if (GUILayout.Toggle(_selectedAreaIndex == i, label, "Button"))
                {
                    if (_selectedAreaIndex != i)
                    {
                        _selectedAreaIndex = i;
                        _selectedBindingIndex = -1;
                        _selectedActionIndex = -1;
                    }
                }
            }

            DrawSelectedAreaEditor();
            DrawBindingEditor();
        }

        private void DrawSelectedAreaEditor()
        {
            var area = SelectedArea;
            if (area == null) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("选中区域", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            area.enabled = EditorGUILayout.Toggle("Enabled", area.enabled);
            area.areaId = EditorGUILayout.TextField("Area Id", area.areaId);
            area.displayName = EditorGUILayout.TextField("Display Name", area.displayName);
            area.priority = EditorGUILayout.IntField("Priority", area.priority);
            area.shapeType = (Live2DTouchShapeType)EditorGUILayout.EnumPopup("Shape", area.shapeType);
            area.normalizedRect = ClampRect(EditorGUILayout.RectField("Normalized Rect", area.normalizedRect));

            if (area.shapeType == Live2DTouchShapeType.Polygon)
            {
                if (area.normalizedPoints == null) area.normalizedPoints = new List<Vector2>();
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Button("从 Rect 重建 4 点 Polygon")) ResetPolygonFromRect(area);
                    if (GUILayout.Button("Add Point")) area.normalizedPoints.Add(new Vector2(area.normalizedRect.center.x, area.normalizedRect.center.y));
                }
                for (int i = 0; i < area.normalizedPoints.Count; i++)
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        area.normalizedPoints[i] = Clamp01(EditorGUILayout.Vector2Field($"Point {i}", area.normalizedPoints[i]));
                        if (GUILayout.Button("X", GUILayout.Width(24)))
                        {
                            area.normalizedPoints.RemoveAt(i);
                            break;
                        }
                    }
                }
            }
            if (EditorGUI.EndChangeCheck()) MarkChanged();
        }

        private void DrawBindingEditor()
        {
            var area = SelectedArea;
            if (area == null) return;
            if (area.bindings == null) area.bindings = new List<Live2DInteractionBinding>();

            _showBindingEditor = EditorGUILayout.Foldout(_showBindingEditor, "Binding / Action 编辑", true);
            if (!_showBindingEditor) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Tap PlayMotion")) AddDefaultBinding(Live2DTriggerType.Tap, Live2DInteractionActionType.PlayMotion);
                if (GUILayout.Button("Add Drag Group")) AddDefaultBinding(Live2DTriggerType.DragRepeat, Live2DInteractionActionType.PlayMotionGroup);
                if (GUILayout.Button("Add Binding")) AddDefaultBinding(Live2DTriggerType.Tap, Live2DInteractionActionType.PlayMotionGroup);
            }

            for (int i = 0; i < area.bindings.Count; i++)
            {
                var binding = area.bindings[i];
                if (binding == null) continue;
                using (new EditorGUILayout.HorizontalScope())
                {
                    if (GUILayout.Toggle(_selectedBindingIndex == i, $"{i}. {binding.trigger} Actions:{binding.actions?.Count ?? 0}", "Button"))
                    {
                        if (_selectedBindingIndex != i)
                        {
                            _selectedBindingIndex = i;
                            _selectedActionIndex = -1;
                        }
                    }
                    if (GUILayout.Button("X", GUILayout.Width(24)))
                    {
                        area.bindings.RemoveAt(i);
                        ResetBindingSelection();
                        MarkChanged();
                        break;
                    }
                }
            }

            var bindingSelected = SelectedBinding;
            if (bindingSelected == null) return;

            EditorGUI.BeginChangeCheck();
            bindingSelected.enabled = EditorGUILayout.Toggle("Binding Enabled", bindingSelected.enabled);
            bindingSelected.trigger = (Live2DTriggerType)EditorGUILayout.EnumPopup("Trigger", bindingSelected.trigger);
            bindingSelected.cooldown = EditorGUILayout.FloatField("Cooldown", bindingSelected.cooldown);
            if (EditorGUI.EndChangeCheck()) MarkChanged();

            if (bindingSelected.actions == null) bindingSelected.actions = new List<Live2DInteractionAction>();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("Add Action"))
                {
                    bindingSelected.actions.Add(new Live2DInteractionAction());
                    _selectedActionIndex = bindingSelected.actions.Count - 1;
                    MarkChanged();
                }
                if (GUILayout.Button("Add Set Param"))
                {
                    bindingSelected.actions.Add(new Live2DInteractionAction { actionType = Live2DInteractionActionType.SetParameterValue });
                    _selectedActionIndex = bindingSelected.actions.Count - 1;
                    MarkChanged();
                }
                if (GUILayout.Button("Add Toggle Param"))
                {
                    bindingSelected.actions.Add(new Live2DInteractionAction { actionType = Live2DInteractionActionType.ToggleParameterValue });
                    _selectedActionIndex = bindingSelected.actions.Count - 1;
                    MarkChanged();
                }
                using (new EditorGUI.DisabledScope(SelectedAction == null))
                {
                    if (GUILayout.Button("Delete Action"))
                    {
                        bindingSelected.actions.RemoveAt(_selectedActionIndex);
                        _selectedActionIndex = -1;
                        MarkChanged();
                    }
                }
            }

            for (int i = 0; i < bindingSelected.actions.Count; i++)
            {
                var action = bindingSelected.actions[i];
                string label = action == null ? $"{i}. <Null>" : $"{i}. {action.actionType}";
                if (GUILayout.Toggle(_selectedActionIndex == i, label, "Button")) _selectedActionIndex = i;
            }

            DrawSelectedActionEditor();
        }

        private void DrawSelectedActionEditor()
        {
            var action = SelectedAction;
            if (action == null) return;
            EditorGUILayout.LabelField("选中 Action", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            action.actionType = (Live2DInteractionActionType)EditorGUILayout.EnumPopup("Action Type", action.actionType);
            action.layerId = (Live2DLayerId)EditorGUILayout.EnumPopup("Layer", action.layerId);
            action.interruptPolicy = (Live2DInterruptPolicy)EditorGUILayout.EnumPopup("Interrupt", action.interruptPolicy);

            switch (action.actionType)
            {
                case Live2DInteractionActionType.PlayMotion:
                    action.motion.library = (Live2DMotionLibrary)EditorGUILayout.ObjectField("Library", action.motion.library != null ? action.motion.library : _library, typeof(Live2DMotionLibrary), false);
                    if (action.motion.library == null) action.motion.library = _library;
                    action.motion.motionId = DrawMotionIdPopup(action.motion.motionId);
                    action.playAfterMotionOnComplete = EditorGUILayout.Toggle("结束后播放指定 Motion", action.playAfterMotionOnComplete);
                    if (action.playAfterMotionOnComplete)
                    {
                        action.afterMotion.library = (Live2DMotionLibrary)EditorGUILayout.ObjectField("After Library", action.afterMotion.library != null ? action.afterMotion.library : _library, typeof(Live2DMotionLibrary), false);
                        if (action.afterMotion.library == null) action.afterMotion.library = _library;
                        action.afterMotion.motionId = DrawMotionIdPopup(action.afterMotion.motionId);
                        action.afterMotionLayerId = (Live2DLayerId)EditorGUILayout.EnumPopup("After Layer", action.afterMotionLayerId);
                        action.afterMotionInterruptPolicy = (Live2DInterruptPolicy)EditorGUILayout.EnumPopup("After Interrupt", action.afterMotionInterruptPolicy);
                    }
                    break;
                case Live2DInteractionActionType.PlayMotionGroup:
                    action.groupId = DrawGroupIdPopup(action.groupId);
                    break;
                case Live2DInteractionActionType.PlayParameterAnimation:
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        action.parameterAnimation = (Live2DParameterAnimationAsset)EditorGUILayout.ObjectField("Parameter Animation", action.parameterAnimation, typeof(Live2DParameterAnimationAsset), false);
                        if (GUILayout.Button("新建", GUILayout.Width(56))) action.parameterAnimation = CreateParameterAnimationAsset(action);
                    }
                    action.parameterApplyMode = (Live2DParameterApplyMode)EditorGUILayout.EnumPopup("Apply Mode", action.parameterApplyMode);
                    DrawParameterAnimationInline(action.parameterAnimation);
                    break;
                case Live2DInteractionActionType.StopLayer:
                    break;
                case Live2DInteractionActionType.SwitchLayout:
                    action.layoutId = DrawLayoutIdPopup(action.layoutId);
                    break;
                case Live2DInteractionActionType.SendEvent:
                    action.eventName = EditorGUILayout.TextField("Event Name", action.eventName);
                    action.payload = EditorGUILayout.TextField("Payload", action.payload);
                    break;
                case Live2DInteractionActionType.PlayAudio:
                    action.audioKey = EditorGUILayout.TextField("Audio Key", action.audioKey);
                    action.fallbackClip = (AudioClip)EditorGUILayout.ObjectField("Fallback Clip", action.fallbackClip, typeof(AudioClip), false);
                    action.volume = EditorGUILayout.FloatField("Volume", action.volume);
                    action.pitch = EditorGUILayout.FloatField("Pitch", action.pitch);
                    break;
                case Live2DInteractionActionType.SetParameterValue:
                    action.parameterId = DrawParameterIdPopup(action.parameterId);
                    action.parameterValue = EditorGUILayout.FloatField("Value", action.parameterValue);
                    break;
                case Live2DInteractionActionType.ToggleParameterValue:
                    action.parameterId = DrawParameterIdPopup(action.parameterId);
                    action.toggleOffValue = EditorGUILayout.FloatField("Off Value", action.toggleOffValue);
                    action.toggleOnValue = EditorGUILayout.FloatField("On Value", action.toggleOnValue);
                    action.toggleThreshold = EditorGUILayout.FloatField("Threshold", action.toggleThreshold);
                    break;
            }
            if (EditorGUI.EndChangeCheck()) MarkChanged();
        }

        private Live2DParameterAnimationAsset CreateParameterAnimationAsset(Live2DInteractionAction action)
        {
            string folder = GetProfileAssetFolder();
            string baseName = SelectedArea != null && !string.IsNullOrEmpty(SelectedArea.areaId) ? SelectedArea.areaId : "parameter_animation";
            string path = AssetDatabase.GenerateUniqueAssetPath($"{folder}/{baseName}_ParameterAnimation.asset");
            var asset = CreateInstance<Live2DParameterAnimationAsset>();
            asset.animationId = System.IO.Path.GetFileNameWithoutExtension(path);
            asset.displayName = asset.animationId;
            asset.defaultLayerId = action.layerId;
            asset.simpleParameters.Add(new Live2DSimpleParameterSet
            {
                parameterId = action.parameterId,
                value = action.parameterValue,
                applyImmediately = action.parameterApplyMode == Live2DParameterApplyMode.Immediate,
                blendMode = Live2DParameterBlendMode.Override
            });
            AssetDatabase.CreateAsset(asset, path);
            AssetDatabase.SaveAssets();
            Selection.activeObject = asset;
            MarkChanged();
            return asset;
        }

        private void DrawParameterAnimationInline(Live2DParameterAnimationAsset asset)
        {
            if (asset == null) return;
            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Parameter Animation 快速编辑", EditorStyles.boldLabel);
            EditorGUI.BeginChangeCheck();
            asset.animationId = EditorGUILayout.TextField("Animation Id", asset.animationId);
            asset.displayName = EditorGUILayout.TextField("Display Name", asset.displayName);
            asset.mode = (Live2DParameterAnimationMode)EditorGUILayout.EnumPopup("Mode", asset.mode);
            asset.duration = EditorGUILayout.FloatField("Duration", asset.duration);
            asset.loopMode = (Live2DLoopMode)EditorGUILayout.EnumPopup("Loop", asset.loopMode);
            asset.fadeIn = EditorGUILayout.FloatField("Fade In", asset.fadeIn);
            asset.fadeOut = EditorGUILayout.FloatField("Fade Out", asset.fadeOut);
            asset.defaultLayerId = (Live2DLayerId)EditorGUILayout.EnumPopup("Default Layer", asset.defaultLayerId);

            if (asset.mode == Live2DParameterAnimationMode.SimpleSet || asset.mode == Live2DParameterAnimationMode.Mixed)
            {
                if (asset.simpleParameters == null) asset.simpleParameters = new List<Live2DSimpleParameterSet>();
                DrawSimpleParameterSetList(asset.simpleParameters);
            }

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(asset);
                MarkChanged();
            }
        }

        private void DrawSimpleParameterSetList(List<Live2DSimpleParameterSet> parameters)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.LabelField("Simple Parameters", EditorStyles.boldLabel);
                if (GUILayout.Button("Add", GUILayout.Width(52))) parameters.Add(new Live2DSimpleParameterSet());
            }

            for (int i = 0; i < parameters.Count; i++)
            {
                var parameter = parameters[i];
                if (parameter == null)
                {
                    parameter = new Live2DSimpleParameterSet();
                    parameters[i] = parameter;
                }

                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        EditorGUILayout.LabelField($"Parameter {i}", EditorStyles.boldLabel);
                        if (GUILayout.Button("X", GUILayout.Width(24)))
                        {
                            parameters.RemoveAt(i);
                            break;
                        }
                    }
                    parameter.parameterId = DrawParameterIdPopup(parameter.parameterId, value => parameter.parameterId = value);
                    parameter.value = EditorGUILayout.FloatField("Value", parameter.value);
                    parameter.applyImmediately = EditorGUILayout.Toggle("Apply Immediately", parameter.applyImmediately);
                    parameter.blendMode = (Live2DParameterBlendMode)EditorGUILayout.EnumPopup("Blend", parameter.blendMode);
                }
            }
        }

        private string DrawParameterIdPopup(string current, Action<string> onSelected = null)
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                EditorGUILayout.TextField("Parameter Id", FormatParameterLabel(current));
                if (GUILayout.Button("选择", GUILayout.Width(56)))
                {
                    ParameterSelectWindow.Open(GetCubismParameterIds(), GetParameterDisplayNames(), current, value =>
                    {
                        if (onSelected != null) onSelected(value);
                        else
                        {
                            var action = SelectedAction;
                            if (action != null) action.parameterId = value;
                        }
                        MarkChanged();
                        Repaint();
                    });
                }
                if (GUILayout.Button("清空", GUILayout.Width(48)))
                {
                    current = string.Empty;
                    onSelected?.Invoke(current);
                }
            }
            return current;
        }

        private string FormatParameterLabel(string id)
        {
            if (string.IsNullOrEmpty(id)) return string.Empty;
            var names = GetParameterDisplayNames();
            return names.TryGetValue(id, out string displayName) && !string.IsNullOrEmpty(displayName) ? $"{id}  ({displayName})" : id;
        }

        private Dictionary<string, string> GetParameterDisplayNames()
        {
            string path = FindCdi3Path();
            if (_parameterDisplayNames != null && string.Equals(_parameterDisplayNameSourcePath, path, StringComparison.Ordinal)) return _parameterDisplayNames;
            _parameterDisplayNameSourcePath = path;
            _parameterDisplayNames = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (string.IsNullOrEmpty(path)) return _parameterDisplayNames;
            var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
            if (asset == null || string.IsNullOrEmpty(asset.text)) return _parameterDisplayNames;
            foreach (Match match in Regex.Matches(asset.text, "\\\"Id\\\"\\s*:\\s*\\\"(?<id>[^\\\"]+)\\\"[\\s\\S]*?\\\"Name\\\"\\s*:\\s*\\\"(?<name>[^\\\"]*)\\\""))
            {
                string id = match.Groups["id"].Value;
                string name = match.Groups["name"].Value;
                if (!string.IsNullOrEmpty(id) && !_parameterDisplayNames.ContainsKey(id)) _parameterDisplayNames.Add(id, name);
            }
            return _parameterDisplayNames;
        }

        private string FindCdi3Path()
        {
            string folder = null;
            if (_library != null && !string.IsNullOrEmpty(_library.motionFolderPath)) folder = System.IO.Path.GetDirectoryName(_library.motionFolderPath)?.Replace('\\', '/');
            if (string.IsNullOrEmpty(folder) && _profile != null)
            {
                string profilePath = AssetDatabase.GetAssetPath(_profile);
                if (!string.IsNullOrEmpty(profilePath)) folder = System.IO.Path.GetDirectoryName(profilePath)?.Replace('\\', '/');
            }
            if (string.IsNullOrEmpty(folder)) return string.Empty;
            string[] guids = AssetDatabase.FindAssets("t:TextAsset", new[] { folder });
            foreach (string guid in guids)
            {
                string path = AssetDatabase.GUIDToAssetPath(guid);
                if (path.EndsWith(".cdi3.json", StringComparison.OrdinalIgnoreCase)) return path;
            }
            return string.Empty;
        }

        private List<string> GetCubismParameterIds()
        {
            var ids = new List<string>();
            var model = _controller != null ? _controller.cubismModel : null;
            if (model == null || model.Parameters == null) return ids;
            foreach (var parameter in model.Parameters)
            {
                string id = GetCubismIdString(parameter);
                if (!string.IsNullOrEmpty(id) && !ids.Contains(id)) ids.Add(id);
            }
            return ids;
        }

        private static string GetCubismIdString(Component comp)
        {
            if (comp == null) return string.Empty;
            Type t = comp.GetType();
            var prop = t.GetProperty("Id", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null) return ConvertCubismIdObject(prop.GetValue(comp, null));
            var field = t.GetField("Id", BindingFlags.Public | BindingFlags.Instance);
            if (field != null) return ConvertCubismIdObject(field.GetValue(comp));
            return comp.name;
        }

        private static string ConvertCubismIdObject(object idObj)
        {
            if (idObj == null) return string.Empty;
            if (idObj is string s) return s;
            Type t = idObj.GetType();
            var prop = t.GetProperty("Value", BindingFlags.Public | BindingFlags.Instance) ?? t.GetProperty("Name", BindingFlags.Public | BindingFlags.Instance);
            if (prop != null)
            {
                object value = prop.GetValue(idObj, null);
                if (value != null) return value.ToString();
            }
            return idObj.ToString();
        }

        private string GetProfileAssetFolder()
        {
            if (_profile != null)
            {
                string path = AssetDatabase.GetAssetPath(_profile);
                if (!string.IsNullOrEmpty(path)) return System.IO.Path.GetDirectoryName(path).Replace('\\', '/');
            }
            return "Assets";
        }

        private void DrawMotionBlock()
        {
            _showMotionControls = EditorGUILayout.Foldout(_showMotionControls, "Motion 预览与关联", true);
            if (!_showMotionControls) return;
            if (_library == null)
            {
                EditorGUILayout.HelpBox("当前未关联 MotionLibrary。", MessageType.Info);
                return;
            }

            _motionSearch = EditorGUILayout.TextField("Search", _motionSearch);
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_controller == null || !EditorApplication.isPlaying))
                {
                    if (GUILayout.Button("停止 Base 层")) _controller.Mixer.Stop(Live2DLayerId.Base, _controller.defaultFadeOut);
                    if (GUILayout.Button("播放 idle_base")) _controller.PlayGroup("idle_base", Live2DLayerId.Base, Live2DInterruptPolicy.ImmediateCrossFade, "Editor");
                }
            }

            if (_library.clips == null) return;
            int columns = 3;
            int col = 0;
            EditorGUILayout.BeginHorizontal();
            foreach (var clip in _library.clips)
            {
                if (clip == null || string.IsNullOrEmpty(clip.motionId)) continue;
                if (!string.IsNullOrEmpty(_motionSearch) && clip.motionId.IndexOf(_motionSearch, StringComparison.OrdinalIgnoreCase) < 0) continue;
                using (new EditorGUI.DisabledScope(_controller == null || !EditorApplication.isPlaying))
                {
                    if (GUILayout.Button(clip.motionId, GUILayout.Height(24)))
                        _controller.PlayMotion(clip.motionId, clip.defaultLayer, clip.interruptPolicy, "EditorPreview");
                }
                col++;
                if (col >= columns)
                {
                    col = 0;
                    EditorGUILayout.EndHorizontal();
                    EditorGUILayout.BeginHorizontal();
                }
            }
            EditorGUILayout.EndHorizontal();
        }

        private void DrawTransformBlock()
        {
            _showTransformTools = EditorGUILayout.Foldout(_showTransformTools, "RawImage / Model Transform", true);
            if (!_showTransformTools) return;
            if (_profile == null) return;

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_rawImage == null))
                {
                    if (GUILayout.Button("Capture RawImage -> Profile")) CaptureRawImageTransform();
                    if (GUILayout.Button("Apply Profile -> RawImage")) ApplyRawImageTransform();
                }
            }

            EditorGUI.BeginChangeCheck();
            _profile.rawImageTransform.enabled = EditorGUILayout.Toggle("RawImage Preset Enabled", _profile.rawImageTransform.enabled);
            _profile.rawImageTransform.anchoredPosition = EditorGUILayout.Vector2Field("Anchored Position", _profile.rawImageTransform.anchoredPosition);
            _profile.rawImageTransform.localScale = EditorGUILayout.Vector3Field("Local Scale", _profile.rawImageTransform.localScale);
            if (EditorGUI.EndChangeCheck()) MarkChanged();

            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_controller == null || _controller.cubismModel == null))
                {
                    if (GUILayout.Button("Capture Model -> Profile")) CaptureModelTransform();
                    if (GUILayout.Button("Apply Profile -> Model")) ApplyModelTransform();
                }
            }

            EditorGUI.BeginChangeCheck();
            _profile.modelTransform.enabled = EditorGUILayout.Toggle("Model Preset Enabled", _profile.modelTransform.enabled);
            _profile.modelTransform.localPosition = EditorGUILayout.Vector3Field("Local Position", _profile.modelTransform.localPosition);
            _profile.modelTransform.localEulerAngles = EditorGUILayout.Vector3Field("Local Euler", _profile.modelTransform.localEulerAngles);
            _profile.modelTransform.localScale = EditorGUILayout.Vector3Field("Local Scale", _profile.modelTransform.localScale);
            if (EditorGUI.EndChangeCheck()) MarkChanged();
        }

        private void DrawSaveBlock()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUI.DisabledScope(_profile == null))
                {
                    if (GUILayout.Button("保存 Profile Asset", GUILayout.Height(30)))
                    {
                        MarkChanged();
                        AssetDatabase.SaveAssets();
                        AssetDatabase.Refresh();
                    }
                }
                if (GUILayout.Button("Repaint", GUILayout.Height(30))) Repaint();
            }
        }

        private void HandleCanvasInput(Rect canvas)
        {
            Event e = Event.current;
            if (CurrentLayout == null) return;

            if (_recordByDrag && e.type == EventType.MouseDown && e.button == 0 && canvas.Contains(e.mousePosition))
            {
                _draggingCreate = true;
                _dragStart = e.mousePosition;
                _dragCurrent = e.mousePosition;
                e.Use();
            }
            else if (_draggingCreate && e.type == EventType.MouseDrag)
            {
                _dragCurrent = e.mousePosition;
                Repaint();
                e.Use();
            }
            else if (_draggingCreate && e.type == EventType.MouseUp && e.button == 0)
            {
                _dragCurrent = e.mousePosition;
                AddRecordedRect(canvas, MakeRect(_dragStart, _dragCurrent));
                _draggingCreate = false;
                e.Use();
            }
            else if (!_recordByDrag && _editRectMode && e.type == EventType.MouseDown && e.button == 0 && canvas.Contains(e.mousePosition))
            {
                _selectedAreaIndex = HitArea(canvas, e.mousePosition);
                _selectedBindingIndex = -1;
                _selectedActionIndex = -1;
                if (SelectedArea != null && SelectedArea.shapeType == Live2DTouchShapeType.Rect)
                {
                    Rect r = NormalizedToGuiRect(canvas, SelectedArea.normalizedRect);
                    _activeHandle = ResolveHandle(r, e.mousePosition);
                    _editStartRect = SelectedArea.normalizedRect;
                    _dragStart = e.mousePosition;
                    _draggingEdit = _activeHandle != EditHandle.None;
                }
                Repaint();
                e.Use();
            }
            else if (_draggingEdit && e.type == EventType.MouseDrag)
            {
                ApplyRectEdit(canvas, e.mousePosition - _dragStart);
                Repaint();
                e.Use();
            }
            else if (_draggingEdit && e.type == EventType.MouseUp && e.button == 0)
            {
                _draggingEdit = false;
                _activeHandle = EditHandle.None;
                MarkChanged();
                e.Use();
            }
        }

        private Rect GetAspectCanvasRect(Rect outer)
        {
            float aspect = 16f / 9f;
            if (_rawImage != null)
            {
                Rect rect = _rawImage.rectTransform.rect;
                if (rect.height > 0.001f) aspect = Mathf.Abs(rect.width / rect.height);
            }
            else if (_gameViewSnapshot != null && _gameViewSnapshot.height > 0)
            {
                aspect = (float)_gameViewSnapshot.width / _gameViewSnapshot.height;
            }

            float width = outer.width - 16f;
            float height = width / aspect;
            if (height > outer.height - 16f)
            {
                height = outer.height - 16f;
                width = height * aspect;
            }
            return new Rect(outer.x + (outer.width - width) * 0.5f, outer.y + (outer.height - height) * 0.5f, width, height);
        }

        private void DrawCanvasBackground(Rect canvas)
        {
            EditorGUI.DrawRect(canvas, new Color(0.08f, 0.08f, 0.08f));
            if (_drawSnapshotBackground && _gameViewSnapshot != null)
            {
                GUI.DrawTexture(canvas, _gameViewSnapshot, ScaleMode.ScaleToFit, false);
                EditorGUI.DrawRect(canvas, new Color(0f, 0f, 0f, 0.18f));
            }
            DrawRect(canvas, new Color(0f, 0f, 0f, 0f), new Color(0.25f, 0.7f, 1f, 1f));
        }

        private void CaptureGameViewSnapshot()
        {
            if (CaptureRawImageTextureSnapshot())
            {
                RepaintCanvasWindows();
                return;
            }

            var texture = InternalEditorUtility.ReadScreenPixel(Vector2.zero, Screen.width, Screen.height);
            if (texture == null || texture.Length == 0)
            {
                Debug.LogWarning("[Live2D] GameView 快照获取失败，请确认 RawImage 有可读取 Texture，或 GameView 已显示并处于 PlayMode。");
                return;
            }

            if (_gameViewSnapshot != null) DestroyImmediate(_gameViewSnapshot);
            _gameViewSnapshot = new Texture2D(Screen.width, Screen.height, TextureFormat.RGB24, false);
            _gameViewSnapshot.SetPixels(texture);
            _gameViewSnapshot.Apply();
            RepaintCanvasWindows();
        }

        private bool CaptureRawImageTextureSnapshot()
        {
            if (_rawImage == null || _rawImage.texture == null) return false;
            if (_rawImage.texture is RenderTexture renderTexture)
            {
                RenderTexture active = RenderTexture.active;
                try
                {
                    RenderTexture.active = renderTexture;
                    if (_gameViewSnapshot != null) DestroyImmediate(_gameViewSnapshot);
                    _gameViewSnapshot = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGB24, false);
                    _gameViewSnapshot.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0);
                    _gameViewSnapshot.Apply();
                    return true;
                }
                finally
                {
                    RenderTexture.active = active;
                }
            }

            if (_rawImage.texture is Texture2D texture2D)
            {
                if (_gameViewSnapshot != null) DestroyImmediate(_gameViewSnapshot);
                _gameViewSnapshot = new Texture2D(texture2D.width, texture2D.height, TextureFormat.RGB24, false);
                Graphics.CopyTexture(texture2D, _gameViewSnapshot);
                return true;
            }

            return false;
        }

        private void RepaintCanvasWindows()
        {
            Repaint();
            _canvasWindow?.Repaint();
        }

        private void DrawAreas(Rect canvas)
        {
            var layout = CurrentLayout;
            if (layout == null || layout.areas == null) return;
            for (int i = 0; i < layout.areas.Count; i++)
            {
                var area = layout.areas[i];
                if (area == null) continue;
                Color border = i == _selectedAreaIndex ? Color.cyan : (area.enabled ? Color.green : Color.gray);
                if (area.shapeType == Live2DTouchShapeType.Rect)
                {
                    Rect r = NormalizedToGuiRect(canvas, area.normalizedRect);
                    DrawRect(r, area.enabled ? new Color(0f, 1f, 0f, 0.12f) : new Color(0.4f, 0.4f, 0.4f, 0.08f), border);
                    GUI.Label(new Rect(r.x + 4, r.y + 2, Mathf.Max(30, r.width - 8), 20), $"{i}:{area.displayName} P:{area.priority}");
                    if (i == _selectedAreaIndex && _editRectMode && !_recordByDrag) DrawHandles(r);
                }
                else if (area.normalizedPoints != null && area.normalizedPoints.Count >= 3)
                {
                    Handles.BeginGUI();
                    Handles.color = border;
                    for (int p = 0; p < area.normalizedPoints.Count; p++)
                        Handles.DrawAAPolyLine(2f, NormalizedToGuiPoint(canvas, area.normalizedPoints[p]), NormalizedToGuiPoint(canvas, area.normalizedPoints[(p + 1) % area.normalizedPoints.Count]));
                    Handles.EndGUI();
                }
            }
        }

        private void DrawHandles(Rect r)
        {
            DrawHandle(GetCornerTopLeft(r));
            DrawHandle(GetCornerTopRight(r));
            DrawHandle(GetCornerBottomLeft(r));
            DrawHandle(GetCornerBottomRight(r));
            DrawHandle(GetEdgeLeft(r));
            DrawHandle(GetEdgeRight(r));
            DrawHandle(GetEdgeTop(r));
            DrawHandle(GetEdgeBottom(r));
        }

        private void DrawHandle(Rect r)
        {
            EditorGUI.DrawRect(r, Color.white);
            Handles.BeginGUI();
            Handles.color = Color.black;
            Handles.DrawAAPolyLine(1f, new Vector3(r.xMin, r.yMin), new Vector3(r.xMax, r.yMin), new Vector3(r.xMax, r.yMax), new Vector3(r.xMin, r.yMax), new Vector3(r.xMin, r.yMin));
            Handles.EndGUI();
        }

        private void ApplyRectEdit(Rect canvas, Vector2 guiDelta)
        {
            var area = SelectedArea;
            if (area == null) return;
            float dx = guiDelta.x / Mathf.Max(1f, canvas.width);
            float dy = -guiDelta.y / Mathf.Max(1f, canvas.height);
            Rect r = _editStartRect;

            switch (_activeHandle)
            {
                case EditHandle.Move:
                    r.x += dx;
                    r.y += dy;
                    break;
                case EditHandle.Left:
                    r.xMin += dx;
                    break;
                case EditHandle.Right:
                    r.xMax += dx;
                    break;
                case EditHandle.Top:
                    r.yMax += dy;
                    break;
                case EditHandle.Bottom:
                    r.yMin += dy;
                    break;
                case EditHandle.TopLeft:
                    r.xMin += dx;
                    r.yMax += dy;
                    break;
                case EditHandle.TopRight:
                    r.xMax += dx;
                    r.yMax += dy;
                    break;
                case EditHandle.BottomLeft:
                    r.xMin += dx;
                    r.yMin += dy;
                    break;
                case EditHandle.BottomRight:
                    r.xMax += dx;
                    r.yMin += dy;
                    break;
            }

            area.normalizedRect = ClampRect(NormalizeRect(r));
        }

        private EditHandle ResolveHandle(Rect r, Vector2 p)
        {
            if (GetCornerTopLeft(r).Contains(p)) return EditHandle.TopLeft;
            if (GetCornerTopRight(r).Contains(p)) return EditHandle.TopRight;
            if (GetCornerBottomLeft(r).Contains(p)) return EditHandle.BottomLeft;
            if (GetCornerBottomRight(r).Contains(p)) return EditHandle.BottomRight;
            if (GetEdgeLeft(r).Contains(p)) return EditHandle.Left;
            if (GetEdgeRight(r).Contains(p)) return EditHandle.Right;
            if (GetEdgeTop(r).Contains(p)) return EditHandle.Top;
            if (GetEdgeBottom(r).Contains(p)) return EditHandle.Bottom;
            return r.Contains(p) ? EditHandle.Move : EditHandle.None;
        }

        private void AddRectArea()
        {
            if (!EnsureLayout()) return;
            var layout = CurrentLayout;
            layout.areas.Add(new Live2DTouchArea
            {
                areaId = $"{_areaPrefix}_{layout.areas.Count + 1}",
                displayName = $"{_areaPrefix} {layout.areas.Count + 1}",
                shapeType = Live2DTouchShapeType.Rect,
                priority = layout.areas.Count,
                normalizedRect = new Rect(0.4f, 0.4f, 0.2f, 0.2f)
            });
            _selectedAreaIndex = layout.areas.Count - 1;
            MarkChanged();
        }

        private void AddPolygonArea()
        {
            if (!EnsureLayout()) return;
            var layout = CurrentLayout;
            var area = new Live2DTouchArea
            {
                areaId = $"Polygon_{layout.areas.Count + 1}",
                displayName = $"Polygon {layout.areas.Count + 1}",
                shapeType = Live2DTouchShapeType.Polygon,
                priority = layout.areas.Count,
                normalizedRect = new Rect(0.35f, 0.35f, 0.3f, 0.3f)
            };
            ResetPolygonFromRect(area);
            layout.areas.Add(area);
            _selectedAreaIndex = layout.areas.Count - 1;
            MarkChanged();
        }

        private void AddRecordedRect(Rect canvas, Rect guiRect)
        {
            Rect normalized = GuiRectToNormalized(canvas, guiRect);
            if (normalized.width <= 0.001f || normalized.height <= 0.001f) return;
            AddRectArea();
            if (SelectedArea != null) SelectedArea.normalizedRect = normalized;
            MarkChanged();
        }

        private void DuplicateSelectedArea()
        {
            var layout = CurrentLayout;
            var area = SelectedArea;
            if (layout == null || area == null) return;
            var copy = new Live2DTouchArea
            {
                areaId = MakeUniqueAreaId(area.areaId),
                displayName = area.displayName + " Copy",
                enabled = area.enabled,
                priority = area.priority + 1,
                shapeType = area.shapeType,
                normalizedRect = area.normalizedRect,
                normalizedPoints = new List<Vector2>(area.normalizedPoints ?? new List<Vector2>()),
                bindings = CloneBindings(area.bindings)
            };
            layout.areas.Add(copy);
            _selectedAreaIndex = layout.areas.Count - 1;
            MarkChanged();
        }

        private void DeleteSelectedArea()
        {
            var layout = CurrentLayout;
            if (layout == null || _selectedAreaIndex < 0 || _selectedAreaIndex >= layout.areas.Count) return;
            layout.areas.RemoveAt(_selectedAreaIndex);
            ResetAreaSelection();
            MarkChanged();
        }

        private void AddDefaultBinding(Live2DTriggerType trigger, Live2DInteractionActionType actionType)
        {
            var area = SelectedArea;
            if (area == null) return;
            if (area.bindings == null) area.bindings = new List<Live2DInteractionBinding>();
            var binding = new Live2DInteractionBinding { trigger = trigger, cooldown = trigger == Live2DTriggerType.DragRepeat ? 0.15f : 0.3f };
            binding.actions.Add(new Live2DInteractionAction
            {
                actionType = actionType,
                motion = new Live2DMotionReference { library = _library },
                groupId = actionType == Live2DInteractionActionType.PlayMotionGroup ? GuessGroupId(trigger) : string.Empty,
                layerId = trigger == Live2DTriggerType.Tap ? Live2DLayerId.Overlay : Live2DLayerId.OverlayBody,
                interruptPolicy = Live2DInterruptPolicy.ImmediateCrossFade
            });
            area.bindings.Add(binding);
            _selectedBindingIndex = area.bindings.Count - 1;
            _selectedActionIndex = 0;
            MarkChanged();
        }

        private string DrawMotionIdPopup(string current, params GUILayoutOption[] options)
        {
            if (_library == null || _library.clips == null || _library.clips.Count == 0)
                return EditorGUILayout.TextField(current, options);

            var ids = new List<string> { "<None>" };
            int selected = 0;
            for (int i = 0; i < _library.clips.Count; i++)
            {
                string id = _library.clips[i]?.motionId;
                if (string.IsNullOrEmpty(id)) continue;
                if (string.Equals(id, current, StringComparison.OrdinalIgnoreCase)) selected = ids.Count;
                ids.Add(id);
            }
            int next = EditorGUILayout.Popup(selected, ids.ToArray(), options);
            return next <= 0 ? string.Empty : ids[next];
        }

        private string DrawGroupIdPopup(string current)
        {
            if (_library == null || _library.groups == null || _library.groups.Count == 0)
                return EditorGUILayout.TextField("Group Id", current);

            var ids = new List<string> { "<None>" };
            int selected = 0;
            foreach (var group in _library.groups)
            {
                if (group == null || string.IsNullOrEmpty(group.groupId)) continue;
                if (string.Equals(group.groupId, current, StringComparison.OrdinalIgnoreCase)) selected = ids.Count;
                ids.Add(group.groupId);
            }
            int next = EditorGUILayout.Popup("Group Id", selected, ids.ToArray());
            return next <= 0 ? string.Empty : ids[next];
        }

        private string DrawLayoutIdPopup(string current, params GUILayoutOption[] options)
        {
            if (_profile == null || _profile.layouts == null || _profile.layouts.Count == 0)
                return EditorGUILayout.TextField(current, options);

            var ids = new List<string> { "<None>" };
            int selected = 0;
            foreach (var layout in _profile.layouts)
            {
                if (layout == null || string.IsNullOrEmpty(layout.layoutId)) continue;
                if (string.Equals(layout.layoutId, current, StringComparison.Ordinal)) selected = ids.Count;
                ids.Add(layout.layoutId);
            }
            int next = EditorGUILayout.Popup(selected, ids.ToArray(), options);
            return next <= 0 ? string.Empty : ids[next];
        }

        private void AutoLinkFromScene(bool log)
        {
            if (_controller == null) _controller = FindFirstObjectByType<Live2DMotionController>();
            if (_inputRuntime == null) _inputRuntime = FindFirstObjectByType<Live2DRawImageInputRuntime>();
            SyncReferences();
            if (log) Debug.Log($"[Live2D] Touch Area Editor auto linked. controller={(_controller ? _controller.name : "null")}, runtime={(_inputRuntime ? _inputRuntime.name : "null")}");
        }

        private void LinkFromSelection()
        {
            var go = Selection.activeGameObject;
            if (go == null) return;
            _controller = go.GetComponentInParent<Live2DMotionController>() ?? go.GetComponentInChildren<Live2DMotionController>(true) ?? _controller;
            _inputRuntime = go.GetComponentInParent<Live2DRawImageInputRuntime>() ?? go.GetComponentInChildren<Live2DRawImageInputRuntime>(true) ?? _inputRuntime;
            SyncReferences();
        }

        private void SyncReferences()
        {
            if (_inputRuntime != null)
            {
                if (_controller == null) _controller = _inputRuntime.controller;
                if (_profile == null) _profile = _inputRuntime.profile;
                if (_rawImage == null) _rawImage = _inputRuntime.targetRawImage;
            }
            if (_controller != null)
            {
                if (_profile == null) _profile = _controller.interactionProfile;
                if (_library == null) _library = _controller.motionLibrary;
            }
            if (_profile != null && _library == null) _library = _profile.motionLibrary;
            if (_profile != null && _profile.motionLibrary == null && _library != null) _profile.motionLibrary = _library;
            if (_inputRuntime != null)
            {
                if (_inputRuntime.controller == null && _controller != null) _inputRuntime.controller = _controller;
                if (_inputRuntime.profile == null && _profile != null) _inputRuntime.profile = _profile;
                if (_inputRuntime.targetRawImage == null && _rawImage != null) _inputRuntime.targetRawImage = _rawImage;
            }
        }

        private void CaptureRawImageTransform()
        {
            if (_profile == null || _rawImage == null) return;
            var rt = _rawImage.rectTransform;
            _profile.rawImageTransform.enabled = true;
            _profile.rawImageTransform.anchoredPosition = rt.anchoredPosition;
            _profile.rawImageTransform.localScale = rt.localScale;
            MarkChanged();
        }

        private void ApplyRawImageTransform()
        {
            if (_profile == null || _rawImage == null) return;
            var rt = _rawImage.rectTransform;
            rt.anchoredPosition = _profile.rawImageTransform.anchoredPosition;
            rt.localScale = _profile.rawImageTransform.localScale;
            EditorUtility.SetDirty(rt);
        }

        private void CaptureModelTransform()
        {
            if (_profile == null || _controller == null || _controller.cubismModel == null) return;
            var t = _controller.cubismModel.transform;
            _profile.modelTransform.enabled = true;
            _profile.modelTransform.localPosition = t.localPosition;
            _profile.modelTransform.localEulerAngles = t.localEulerAngles;
            _profile.modelTransform.localScale = t.localScale;
            MarkChanged();
        }

        private void ApplyModelTransform()
        {
            if (_profile == null || _controller == null || _controller.cubismModel == null) return;
            var t = _controller.cubismModel.transform;
            t.localPosition = _profile.modelTransform.localPosition;
            t.localEulerAngles = _profile.modelTransform.localEulerAngles;
            t.localScale = _profile.modelTransform.localScale;
            EditorUtility.SetDirty(t);
        }

        private void BindMotionToLayout(string motionId, string layoutId)
        {
            if (string.IsNullOrEmpty(motionId) || string.IsNullOrEmpty(layoutId)) return;
            foreach (var binding in _profile.layoutBindings)
            {
                if (binding != null && string.Equals(binding.baseMotionId, motionId, StringComparison.OrdinalIgnoreCase))
                {
                    binding.layoutId = layoutId;
                    MarkChanged();
                    return;
                }
            }
            _profile.layoutBindings.Add(new Live2DLayoutBinding { baseMotionId = motionId, layoutId = layoutId });
            MarkChanged();
        }

        private void CreateLayout(string baseName)
        {
            if (_profile == null) return;
            string id = MakeUniqueLayoutId(string.IsNullOrWhiteSpace(baseName) ? "Layout" : baseName.Trim());
            _profile.layouts.Add(new Live2DInteractionLayout { layoutId = id, displayName = id });
            _layoutIndex = _profile.layouts.Count - 1;
            ResetAreaSelection();
            MarkChanged();
        }

        private void CopyCurrentLayout()
        {
            var source = CurrentLayout;
            if (source == null) return;
            var copy = new Live2DInteractionLayout
            {
                layoutId = MakeUniqueLayoutId(source.layoutId + "_Copy"),
                displayName = source.displayName + " Copy",
                areas = new List<Live2DTouchArea>()
            };
            foreach (var area in source.areas)
            {
                if (area == null) continue;
                copy.areas.Add(new Live2DTouchArea
                {
                    areaId = area.areaId,
                    displayName = area.displayName,
                    enabled = area.enabled,
                    priority = area.priority,
                    shapeType = area.shapeType,
                    normalizedRect = area.normalizedRect,
                    normalizedPoints = new List<Vector2>(area.normalizedPoints ?? new List<Vector2>()),
                    bindings = CloneBindings(area.bindings)
                });
            }
            _profile.layouts.Add(copy);
            _layoutIndex = _profile.layouts.Count - 1;
            ResetAreaSelection();
            MarkChanged();
        }

        private void DeleteCurrentLayout()
        {
            if (_profile == null || CurrentLayout == null) return;
            string id = CurrentLayout.layoutId;
            _profile.layouts.RemoveAt(_layoutIndex);
            _profile.layoutBindings.RemoveAll(b => b != null && b.layoutId == id);
            _layoutIndex = Mathf.Clamp(_layoutIndex, 0, _profile.layouts.Count - 1);
            ResetAreaSelection();
            MarkChanged();
        }

        private bool EnsureLayout()
        {
            if (_profile == null) return false;
            if (_profile.layouts == null) _profile.layouts = new List<Live2DInteractionLayout>();
            if (CurrentLayout != null) return true;
            CreateLayout("Default");
            return CurrentLayout != null;
        }

        private string[] BuildLayoutNames()
        {
            if (_profile == null || _profile.layouts == null) return Array.Empty<string>();
            var names = new List<string>();
            foreach (var layout in _profile.layouts) names.Add(layout != null ? layout.layoutId : "<Null>");
            return names.ToArray();
        }

        private void MarkChanged()
        {
            if (_profile != null) EditorUtility.SetDirty(_profile);
            Repaint();
        }

        private void ClampSelection()
        {
            if (_profile == null || _profile.layouts == null || _profile.layouts.Count == 0) _layoutIndex = 0;
            else _layoutIndex = Mathf.Clamp(_layoutIndex, 0, _profile.layouts.Count - 1);
            ResetAreaSelection();
        }

        private void ResetAreaSelection()
        {
            _selectedAreaIndex = -1;
            ResetBindingSelection();
        }

        private void ResetBindingSelection()
        {
            _selectedBindingIndex = -1;
            _selectedActionIndex = -1;
        }

        private int HitArea(Rect canvas, Vector2 mouse)
        {
            var layout = CurrentLayout;
            if (layout == null || layout.areas == null) return -1;
            Vector2 normalized = GuiToNormalized(canvas, mouse);
            int best = -1;
            int bestPriority = int.MinValue;
            for (int i = 0; i < layout.areas.Count; i++)
            {
                var area = layout.areas[i];
                if (area == null || !area.Contains(normalized)) continue;
                if (area.priority >= bestPriority)
                {
                    best = i;
                    bestPriority = area.priority;
                }
            }
            return best;
        }

        private string MakeUniqueLayoutId(string baseId)
        {
            string id = baseId;
            int index = 1;
            while (_profile.GetLayout(id) != null) id = $"{baseId}_{index++}";
            return id;
        }

        private string MakeUniqueAreaId(string baseId)
        {
            var layout = CurrentLayout;
            string id = string.IsNullOrWhiteSpace(baseId) ? "Area" : baseId;
            string next = id;
            int index = 1;
            while (layout != null && layout.areas.Exists(a => a != null && a.areaId == next)) next = $"{id}_{index++}";
            return next;
        }

        private static List<Live2DInteractionBinding> CloneBindings(List<Live2DInteractionBinding> source)
        {
            var result = new List<Live2DInteractionBinding>();
            if (source == null) return result;
            foreach (var binding in source)
            {
                if (binding == null) continue;
                var copy = new Live2DInteractionBinding
                {
                    trigger = binding.trigger,
                    enabled = binding.enabled,
                    cooldown = binding.cooldown,
                    conditions = new List<Live2DInteractionCondition>(binding.conditions ?? new List<Live2DInteractionCondition>()),
                    actions = new List<Live2DInteractionAction>()
                };
                if (binding.actions != null)
                {
                    foreach (var action in binding.actions)
                    {
                        if (action == null) continue;
                        copy.actions.Add(new Live2DInteractionAction
                        {
                            actionType = action.actionType,
                            motion = action.motion,
                            afterMotion = action.afterMotion,
                            playAfterMotionOnComplete = action.playAfterMotionOnComplete,
                            afterMotionLayerId = action.afterMotionLayerId,
                            afterMotionInterruptPolicy = action.afterMotionInterruptPolicy,
                            groupId = action.groupId,
                            parameterAnimation = action.parameterAnimation,
                            layerId = action.layerId,
                            interruptPolicy = action.interruptPolicy,
                            parameterApplyMode = action.parameterApplyMode,
                            layoutId = action.layoutId,
                            eventName = action.eventName,
                            payload = action.payload,
                            audioKey = action.audioKey,
                            fallbackClip = action.fallbackClip,
                            volume = action.volume,
                            pitch = action.pitch,
                            parameterId = action.parameterId,
                            parameterValue = action.parameterValue,
                            toggleOffValue = action.toggleOffValue,
                            toggleOnValue = action.toggleOnValue,
                            toggleThreshold = action.toggleThreshold
                        });
                    }
                }
                result.Add(copy);
            }
            return result;
        }

        private string GuessGroupId(Live2DTriggerType trigger)
        {
            if (trigger == Live2DTriggerType.DragStart || trigger == Live2DTriggerType.DragRepeat || trigger == Live2DTriggerType.DragEnd) return "drag";
            return "touch";
        }

        private static void ResetPolygonFromRect(Live2DTouchArea area)
        {
            area.normalizedPoints.Clear();
            area.normalizedPoints.Add(new Vector2(area.normalizedRect.xMin, area.normalizedRect.yMin));
            area.normalizedPoints.Add(new Vector2(area.normalizedRect.xMax, area.normalizedRect.yMin));
            area.normalizedPoints.Add(new Vector2(area.normalizedRect.xMax, area.normalizedRect.yMax));
            area.normalizedPoints.Add(new Vector2(area.normalizedRect.xMin, area.normalizedRect.yMax));
        }

        private static Rect NormalizeRect(Rect r)
        {
            if (r.width < 0f)
            {
                r.x += r.width;
                r.width = -r.width;
            }
            if (r.height < 0f)
            {
                r.y += r.height;
                r.height = -r.height;
            }
            return r;
        }

        private static Rect ClampRect(Rect r)
        {
            r = NormalizeRect(r);
            r.width = Mathf.Clamp(r.width, MinRectSize, 1f);
            r.height = Mathf.Clamp(r.height, MinRectSize, 1f);
            r.x = Mathf.Clamp(r.x, 0f, 1f - r.width);
            r.y = Mathf.Clamp(r.y, 0f, 1f - r.height);
            return r;
        }

        private static Vector2 Clamp01(Vector2 value) => new Vector2(Mathf.Clamp01(value.x), Mathf.Clamp01(value.y));

        private static Rect NormalizedToGuiRect(Rect canvas, Rect normalized)
        {
            return new Rect(canvas.x + normalized.xMin * canvas.width, canvas.y + (1f - normalized.yMax) * canvas.height, normalized.width * canvas.width, normalized.height * canvas.height);
        }

        private static Vector2 NormalizedToGuiPoint(Rect canvas, Vector2 normalized)
        {
            return new Vector2(canvas.x + normalized.x * canvas.width, canvas.y + (1f - normalized.y) * canvas.height);
        }

        private static Vector2 GuiToNormalized(Rect canvas, Vector2 point)
        {
            return new Vector2(Mathf.InverseLerp(canvas.xMin, canvas.xMax, point.x), 1f - Mathf.InverseLerp(canvas.yMin, canvas.yMax, point.y));
        }

        private static Rect GuiRectToNormalized(Rect canvas, Rect rect)
        {
            Vector2 a = GuiToNormalized(canvas, new Vector2(rect.xMin, rect.yMax));
            Vector2 b = GuiToNormalized(canvas, new Vector2(rect.xMax, rect.yMin));
            return ClampRect(Rect.MinMaxRect(Mathf.Clamp01(a.x), Mathf.Clamp01(a.y), Mathf.Clamp01(b.x), Mathf.Clamp01(b.y)));
        }

        private static Rect MakeRect(Vector2 a, Vector2 b)
        {
            return Rect.MinMaxRect(Mathf.Min(a.x, b.x), Mathf.Min(a.y, b.y), Mathf.Max(a.x, b.x), Mathf.Max(a.y, b.y));
        }

        private static void DrawRect(Rect rect, Color fill, Color border)
        {
            EditorGUI.DrawRect(rect, fill);
            Handles.BeginGUI();
            Handles.color = border;
            Handles.DrawAAPolyLine(2f, new Vector3(rect.xMin, rect.yMin), new Vector3(rect.xMax, rect.yMin), new Vector3(rect.xMax, rect.yMax), new Vector3(rect.xMin, rect.yMax), new Vector3(rect.xMin, rect.yMin));
            Handles.EndGUI();
        }

        private static Rect GetCornerTopLeft(Rect r) => new Rect(r.xMin - HandleSize * 0.5f, r.yMin - HandleSize * 0.5f, HandleSize, HandleSize);
        private static Rect GetCornerTopRight(Rect r) => new Rect(r.xMax - HandleSize * 0.5f, r.yMin - HandleSize * 0.5f, HandleSize, HandleSize);
        private static Rect GetCornerBottomLeft(Rect r) => new Rect(r.xMin - HandleSize * 0.5f, r.yMax - HandleSize * 0.5f, HandleSize, HandleSize);
        private static Rect GetCornerBottomRight(Rect r) => new Rect(r.xMax - HandleSize * 0.5f, r.yMax - HandleSize * 0.5f, HandleSize, HandleSize);
        private static Rect GetEdgeLeft(Rect r) => new Rect(r.xMin - HandleSize * 0.5f, r.yMin + HandleSize * 0.5f, HandleSize, Mathf.Max(0f, r.height - HandleSize));
        private static Rect GetEdgeRight(Rect r) => new Rect(r.xMax - HandleSize * 0.5f, r.yMin + HandleSize * 0.5f, HandleSize, Mathf.Max(0f, r.height - HandleSize));
        private static Rect GetEdgeTop(Rect r) => new Rect(r.xMin + HandleSize * 0.5f, r.yMin - HandleSize * 0.5f, Mathf.Max(0f, r.width - HandleSize), HandleSize);
        private static Rect GetEdgeBottom(Rect r) => new Rect(r.xMin + HandleSize * 0.5f, r.yMax - HandleSize * 0.5f, Mathf.Max(0f, r.width - HandleSize), HandleSize);
    }

    public class ParameterSelectWindow : EditorWindow
    {
        private List<string> _ids = new List<string>();
        private Dictionary<string, string> _displayNames = new Dictionary<string, string>();
        private Action<string> _onSelected;
        private Vector2 _scroll;
        private string _search = string.Empty;

        public static void Open(List<string> ids, Dictionary<string, string> displayNames, string current, Action<string> onSelected)
        {
            var window = CreateInstance<ParameterSelectWindow>();
            window.titleContent = new GUIContent("选择 Parameter");
            window._ids = ids ?? new List<string>();
            window._displayNames = displayNames ?? new Dictionary<string, string>();
            window._onSelected = onSelected;
            window._search = current ?? string.Empty;
            window.minSize = new Vector2(520, 420);
            window.ShowUtility();
        }

        private void OnGUI()
        {
            using (new EditorGUILayout.HorizontalScope())
            {
                _search = EditorGUILayout.TextField("搜索", _search);
                if (GUILayout.Button("清空", GUILayout.Width(56))) _search = string.Empty;
            }

            EditorGUILayout.Space(6);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (string id in _ids)
            {
                if (string.IsNullOrEmpty(id)) continue;
                _displayNames.TryGetValue(id, out string displayName);
                string label = string.IsNullOrEmpty(displayName) ? id : $"{id}    {displayName}";
                if (!string.IsNullOrEmpty(_search) && label.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0) continue;
                if (GUILayout.Button(label, EditorStyles.miniButtonLeft, GUILayout.Height(22)))
                {
                    _onSelected?.Invoke(id);
                    Close();
                }
            }
            EditorGUILayout.EndScrollView();
        }
    }

    public class Live2DTouchAreaCanvasWindow : EditorWindow
    {
        public Live2DTouchAreaEditorWindow Owner { get; set; }

        private void OnGUI()
        {
            if (Owner == null)
            {
                EditorGUILayout.HelpBox("请先打开 Touch Area Editor 主窗口。", MessageType.Info);
                if (GUILayout.Button("打开主窗口")) Live2DTouchAreaEditorWindow.Open();
                return;
            }

            Owner.DrawCanvasOnly(position.height - 8f);
        }
    }
}
#endif
