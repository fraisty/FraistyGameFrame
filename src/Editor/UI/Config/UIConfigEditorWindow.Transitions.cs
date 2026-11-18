using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace FRFrame.Editor
{
    public sealed partial class UIConfigEditorWindow
    {
        private sealed class TransitionDraftBuffer : ScriptableObject
        {
            [NonSerialized] public UIFormTransition open;
            [NonSerialized] public UIFormTransition close;
            [SerializeField] private UndoClip _open = new UndoClip();
            [SerializeField] private UndoClip _close = new UndoClip();

            internal void Capture() { _open.Capture(open); _close.Capture(close); }
            internal void Restore() { open = _open.Restore(); close = _close.Restore(); }
        }

        [Serializable]
        private sealed class UndoClip
        {
            [SerializeField] private bool _enabled, _hasTrackList;
            [SerializeField] private float _duration;
            // Preserve missing rows in the Undo buffer only. The runtime configuration stays inline.
            [SerializeReference] private UITransitionTrack[] _tracks;

            internal void Capture(UIFormTransition clip)
            {
                _enabled = clip.enabled;
                _duration = clip.duration;
                _hasTrackList = clip.tracks != null;
                _tracks = clip.tracks;
            }

            internal UIFormTransition Restore() => new UIFormTransition
                { enabled = _enabled, duration = _duration, tracks = _hasTrackList ? _tracks : null };
        }

        private UIFormConfig _transitionForm;
        private UIFormTransition _mappedOpen, _mappedClose;
        private TransitionDraftBuffer _transitionDraft;
        private string _committedTransition;
        private bool _transitionFold, _previewClose, _playingTransition;
        private double _previewStarted;
        private float _previewSeconds;
        private PreviewRenderUtility _transitionPreview;
        private RectTransform _previewRoot;
        private UITransitionSample _previewSample;
        private string _previewError;
        private string _transitionAssetPath;
        private string _previewAssetPath;
        private Hash128 _previewAssetHash;
        private uint _transitionRevision;

        private UIFormTransition CurrentTransition => _previewClose ? _transitionDraft.close : _transitionDraft.open;

        private void BindTransitionEditor()
        {
            var form = _selectedFormItem?.data;
            if (form == _transitionForm && form?.assetPath == _transitionAssetPath &&
                form?.openTransition == _mappedOpen && form?.closeTransition == _mappedClose) return;
            DiscardTransitionEditor();
            _transitionForm = form;
            if (form == null) return;
            _transitionAssetPath = form.assetPath;
            _mappedOpen = form.openTransition;
            _mappedClose = form.closeTransition;
            _transitionDraft = CreateInstance<TransitionDraftBuffer>();
            _transitionDraft.hideFlags = HideFlags.HideAndDontSave;
            _transitionDraft.open = UIConfigGroupDraftMapper.CloneTransition(_mappedOpen) ?? new UIFormTransition();
            _transitionDraft.close = UIConfigGroupDraftMapper.CloneTransition(_mappedClose) ?? new UIFormTransition();
            _transitionDraft.Capture();
            _committedTransition = EditorJsonUtility.ToJson(_transitionDraft);
        }

        private void OnDisable()
        {
            Undo.undoRedoPerformed -= UndoTransitionDraft;
            EditorApplication.projectChanged -= CheckTransitionPreviewSource;
            DiscardTransitionEditor();
        }

        private void DiscardTransitionEditor()
        {
            StopTransitionPreview();
            if (_transitionDraft)
            {
                Undo.ClearUndo(_transitionDraft);
                DestroyImmediate(_transitionDraft);
            }
            _transitionDraft = null;
            _committedTransition = null;
            _transitionForm = null;
            _mappedOpen = _mappedClose = null;
            _transitionAssetPath = null;
        }

        private void EditTransition(Action update)
        {
            _transitionDraft.Capture();
            Undo.RegisterCompleteObjectUndo(_transitionDraft,"修改 Form 多通道动画");
            update();
            _transitionDraft.Capture();
            CommitTransitionDraft();
        }

        private void UndoTransitionDraft()
        {
            if (!_transitionDraft || _selectedFormItem?.data != _transitionForm) return;
            _transitionDraft.Restore();
            CommitTransitionDraft();
        }

        private void CommitTransitionDraft()
        {
            _transitionRevision++;
            string revision = EditorJsonUtility.ToJson(_transitionDraft);
            if (revision == _committedTransition) return;
            _committedTransition = revision;
            StopTransitionPreview();
            _transitionForm.openTransition = _mappedOpen = UIConfigGroupDraftMapper.CloneTransition(_transitionDraft.open);
            _transitionForm.closeTransition = _mappedClose = UIConfigGroupDraftMapper.CloneTransition(_transitionDraft.close);
            _selectedFormItem.isModified = true;
            MarkCurrentDraftDirty();
            Repaint();
        }

        private void DrawTransitionEditor()
        {
            if (!_transitionDraft) return;
            if (Event.current.type == EventType.Layout) CheckTransitionPreviewSource();
            bool expanded = EditorGUILayout.Foldout(_transitionFold,"多通道动画与 Prefab 预览",true);
            if (expanded != _transitionFold && !expanded) StopTransitionPreview();
            _transitionFold = expanded;
            if (!expanded) return;
            int mode = GUILayout.Toolbar(_previewClose ? 1 : 0,new[] { "打开", "关闭" });
            if (_previewClose != (mode == 1))
            {
                _previewClose = mode == 1;
                _transitionRevision++;
                StopTransitionPreview();
            }
            var clip = CurrentTransition;
            int method = EditorGUILayout.Popup("方式",clip.enabled ? 1 : 0,new[] { "预设", "多通道" });
            if (clip.enabled != (method == 1)) EditTransition(() => clip.enabled = method == 1);
            if (!clip.enabled) return;
            float duration = EditorGUILayout.DelayedFloatField("总时长（秒）",clip.duration);
            if (duration != clip.duration) EditTransition(() => clip.duration = duration);
            GameObject prefab = string.IsNullOrEmpty(_transitionAssetPath) ? null : AssetDatabase.LoadAssetAtPath<GameObject>(
                UIConfigEditorWindowHelper.TryLoadPrefabAtRuntimeStylePath(_transitionAssetPath) ?? _transitionAssetPath);
            var tracks = clip.tracks ?? Array.Empty<UITransitionTrack>();
            if (clip.tracks == null)
                EditorGUILayout.HelpBox("多通道列表缺失。请增加一条通道以修复。",MessageType.Warning);
            for (int i = 0; i < tracks.Length; i++)
            {
                var track = tracks[i];
                using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                {
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label($"通道 {i + 1}");
                        int slot = i;
                        using (new EditorGUI.DisabledScope(slot == 0))
                            if (GUILayout.Button("↑",GUILayout.Width(27)))
                                EditTransition(() => (clip.tracks[slot - 1],clip.tracks[slot]) = (clip.tracks[slot],clip.tracks[slot - 1]));
                        using (new EditorGUI.DisabledScope(slot == clip.tracks.Length - 1))
                            if (GUILayout.Button("↓",GUILayout.Width(27)))
                                EditTransition(() => (clip.tracks[slot + 1],clip.tracks[slot]) = (clip.tracks[slot],clip.tracks[slot + 1]));
                        if (GUILayout.Button("移除",GUILayout.Width(46)))
                        {
                            EditTransition(() => clip.tracks = clip.tracks.Where((_,index) => index != slot).ToArray());
                            GUIUtility.ExitGUI();
                        }
                    }
                    if (track == null)
                    {
                        EditorGUILayout.HelpBox($"通道 {i + 1} 缺失，请移除或重建该槽。",MessageType.Warning);
                        if (GUILayout.Button("重建通道"))
                        {
                            int slot = i;
                            EditTransition(() => clip.tracks[slot] = new UITransitionTrack { duration = clip.duration });
                            GUIUtility.ExitGUI();
                        }
                        continue;
                    }
                    var property = (UITransitionProperty)EditorGUILayout.EnumPopup("属性",track.property);
                    string previousPath = track.targetPath ?? "";
                    string path = EditorGUILayout.DelayedTextField("相对路径（空 = 根）",previousPath);
                    if (prefab && GUILayout.Button("选择目标…")) CreateTransitionTargetMenu(prefab,track).ShowAsContext();
                    float start = EditorGUILayout.DelayedFloatField("起点（秒）",track.start);
                    float length = EditorGUILayout.DelayedFloatField("时长（秒）",track.duration);
                    string units = property switch { UITransitionProperty.Alpha => "透明度", UITransitionProperty.Scale => "倍率", UITransitionProperty.Rotation => "角度", _ => "偏移" };
                    Vector3 from = property == UITransitionProperty.Alpha
                        ? new Vector3(EditorGUILayout.DelayedFloatField($"{units} · 从",track.from.x),0,0)
                        : EditorGUILayout.Vector3Field($"{units} · 从",track.from);
                    Vector3 to = property == UITransitionProperty.Alpha
                        ? new Vector3(EditorGUILayout.DelayedFloatField($"{units} · 到",track.to.x),0,0)
                        : EditorGUILayout.Vector3Field($"{units} · 到",track.to);
                    EditorGUI.BeginChangeCheck();
                    var curve = EditorGUILayout.CurveField("曲线（0–1 时间）",track.curve);
                    if (EditorGUI.EndChangeCheck() || property != track.property || path != previousPath ||
                        start != track.start || length != track.duration || from != track.from || to != track.to)
                        EditTransition(() => {track.property = property; track.targetPath = path; track.start = start;
                            track.duration = length; track.from = from; track.to = to; track.curve = curve;});
                }
            }
            if (GUILayout.Button("增加通道"))
            {
                EditTransition(() => clip.tracks = tracks.Append(new UITransitionTrack { duration = clip.duration }).ToArray());
                GUIUtility.ExitGUI();
            }
            if (!prefab)
            {
                EditorGUILayout.HelpBox("先配置实际 UI Prefab，才能绑定子路径并预览。",MessageType.Info);
                return;
            }
            if (!(prefab.transform is RectTransform root))
            {
                EditorGUILayout.HelpBox("Form Prefab 根需要 RectTransform。",MessageType.Warning);
                return;
            }
            if (!UITransitionSample.TryValidate(clip,root,out string error,allowFormCanvasGroup:true))
            {
                EditorGUILayout.HelpBox(error,MessageType.Warning);
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Button("加载 / 重建预览")) BuildTransitionPreview(prefab,clip);
                using (new EditorGUI.DisabledScope(_previewSample == null))
                {
                    if (GUILayout.Button("播放"))
                    {
                        _previewStarted = EditorApplication.timeSinceStartup;
                        _playingTransition = true;
                        Repaint();
                    }
                    if (GUILayout.Button("停止"))
                    {
                        StopTransitionPreview();
                        GUIUtility.ExitGUI();
                    }
                }
            }
            if (_previewError != null) EditorGUILayout.HelpBox(_previewError,MessageType.Warning);
            if (_previewSample == null) return;
            if (_playingTransition)
            {
                _previewSeconds = Mathf.Min((float)(EditorApplication.timeSinceStartup - _previewStarted),clip.duration);
                _playingTransition = _previewSeconds < clip.duration;
                if (_playingTransition) Repaint();
            }
            float seek = EditorGUILayout.Slider("时间游标",_previewSeconds,0,clip.duration);
            if (seek != _previewSeconds) { _playingTransition = false; _previewSeconds = seek; }
            var rect = GUILayoutUtility.GetRect(1,210,GUILayout.ExpandWidth(true));
            if (Event.current.type != EventType.Repaint) return;
            _previewSample.Seek(_previewSeconds);
            _transitionPreview.BeginPreview(rect,GUIStyle.none);
            _transitionPreview.Render(true);
            var texture = _transitionPreview.EndPreview();
            GUI.DrawTexture(rect,texture,ScaleMode.ScaleToFit,false);
        }

        private GenericMenu CreateTransitionTargetMenu(GameObject prefab,UITransitionTrack track)
        {
            var menu = new GenericMenu();
            var draft = _transitionDraft;
            var clip = CurrentTransition;
            uint revision = _transitionRevision;
            string assetPath = AssetDatabase.GetAssetPath(prefab);
            var assetHash = AssetDatabase.GetAssetDependencyHash(assetPath);
            void Apply(string path)
            {
                if (!draft || draft != _transitionDraft || revision != _transitionRevision ||
                    _selectedFormItem?.data != _transitionForm || _transitionForm.assetPath != _transitionAssetPath ||
                    clip != CurrentTransition || !clip.tracks.Contains(track) ||
                    AssetDatabase.LoadAssetAtPath<GameObject>(assetPath) != prefab ||
                    AssetDatabase.GetAssetDependencyHash(assetPath) != assetHash)
                    return;
                EditTransition(() => track.targetPath = path);
            }
            menu.AddItem(new GUIContent("根"),string.IsNullOrEmpty(track.targetPath),() => Apply(""));
            foreach (var target in prefab.GetComponentsInChildren<RectTransform>(true))
            {
                if (target == prefab.transform) continue;
                string path = AnimationUtility.CalculateTransformPath(target,prefab.transform);
                menu.AddItem(new GUIContent(path),track.targetPath == path,() => Apply(path));
            }
            return menu;
        }

        private void BuildTransitionPreview(GameObject prefab,UIFormTransition clip)
        {
            StopTransitionPreview();
            if (!(prefab.transform is RectTransform))
            { _previewError = "预览根必须是 RectTransform。"; return; }
            _transitionPreview = new PreviewRenderUtility();
            var camera = _transitionPreview.camera;
            camera.orthographic = true;
            camera.orthographicSize = 300;
            camera.farClipPlane = 2000;
            camera.transform.position = new Vector3(0,0,-600);
            camera.clearFlags = CameraClearFlags.SolidColor;
            camera.backgroundColor = new Color(.12f,.14f,.18f);
            var surface = new GameObject("UI Form Preview",typeof(RectTransform),typeof(Canvas));
            _transitionPreview.AddSingleGO(surface);
            var canvas = surface.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = camera;
            ((RectTransform)surface.transform).sizeDelta = new Vector2(960,540);
            var instance = _transitionPreview.InstantiatePrefabInScene(prefab);
            _previewRoot = (RectTransform)instance.transform;
            _previewRoot.SetParent(surface.transform,false);
            if (!instance.GetComponent<CanvasGroup>()) instance.AddComponent<CanvasGroup>();
            LayoutRebuilder.ForceRebuildLayoutImmediate(_previewRoot);
            try
            {
                _previewSample = new UITransitionSample(_previewRoot,clip);
                _previewAssetPath = AssetDatabase.GetAssetPath(prefab);
                _previewAssetHash = AssetDatabase.GetAssetDependencyHash(_previewAssetPath);
            }
            catch (ArgumentException error) { StopTransitionPreview(); _previewError = error.Message; }
        }

        private void CheckTransitionPreviewSource()
        {
            if (_previewSample == null || AssetDatabase.GetAssetDependencyHash(_previewAssetPath) == _previewAssetHash)
                return;
            StopTransitionPreview();
            Repaint();
        }

        private void StopTransitionPreview()
        {
            _playingTransition = false;
            _previewSeconds = 0;
            _transitionPreview?.Cleanup();
            _transitionPreview = null;
            _previewRoot = null;
            _previewSample = null;
            _previewAssetPath = null;
            _previewError = null;
        }
    }
}
