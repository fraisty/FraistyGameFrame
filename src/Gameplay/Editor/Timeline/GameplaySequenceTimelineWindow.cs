using System;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.Timeline
{
    /// <summary>Standalone host for the same Sequence panel embedded in the Skill Workbench.</summary>
    public sealed class GameplaySequenceTimelineWindow : EditorWindow
    {
        private GameplaySequenceTimelinePanel _panel;
        private string _search = string.Empty;
        private Vector2 _browserScroll;
        private string[] _assetPaths = Array.Empty<string>();
        public GameplaySequenceDefinitionAsset CurrentSequence => _panel?.CurrentSequence;
        public int SelectedMarker => _panel?.SelectedMarker ?? -1;
        public float Playhead => _panel?.Playhead ?? 0f;

        [MenuItem("FRFrame/Gameplay/Skill Timeline")]
        public static void Open()
        {
            var window = GetWindow<GameplaySequenceTimelineWindow>();
            window.titleContent = new GUIContent("技能时间轴");
            window.minSize = new Vector2(1060, 540);
            window.Show();
        }

        private void OnEnable()
        {
            _panel = new GameplaySequenceTimelinePanel(Repaint);
            Undo.undoRedoPerformed += _panel.OnUndo;
            EditorApplication.projectChanged += RefreshBrowser;
            RefreshBrowser();
            if (Selection.activeObject is GameplaySequenceDefinitionAsset selected) Select(selected);
        }

        private void OnDisable()
        {
            _panel?.CancelDrag();
            if (_panel != null) Undo.undoRedoPerformed -= _panel.OnUndo;
            EditorApplication.projectChanged -= RefreshBrowser;
        }

        private void OnLostFocus() => _panel?.CancelDrag();

        private void RefreshBrowser()
        {
            _assetPaths = AssetDatabase.FindAssets("t:GameplaySequenceDefinitionAsset", new[] { "Assets" })
                .Select(AssetDatabase.GUIDToAssetPath).OrderBy(path => path, StringComparer.Ordinal).ToArray();
            Repaint();
        }

        public void Select(GameplaySequenceDefinitionAsset sequence) => _panel.Select(sequence);

        private void OnGUI()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            var sequence = CurrentSequence;
            GUILayout.Label(sequence == null ? "选择 Sequence Asset" : sequence.name + "  ·  " + sequence.DefinitionId +
                (EditorUtility.IsDirty(sequence) ? "  *" : ""), EditorStyles.toolbarButton);
            GUILayout.FlexibleSpace();
            if (GUILayout.Button("校验", EditorStyles.toolbarButton, GUILayout.Width(45))) _panel.Validate();
            if (GUILayout.Button("保存", EditorStyles.toolbarButton, GUILayout.Width(45)) && sequence != null)
            { AssetDatabase.SaveAssetIfDirty(sequence); _panel.Validate(); }
            GUILayout.EndHorizontal();
            GUILayout.BeginHorizontal();
            DrawBrowser();
            _panel.DrawTimeline(position.width - 550f);
            _panel.DrawInspector();
            GUILayout.EndHorizontal();
        }

        private void DrawBrowser()
        {
            GUILayout.BeginVertical(EditorStyles.helpBox, GUILayout.Width(230), GUILayout.ExpandHeight(true));
            GUILayout.Label("序列资源", EditorStyles.boldLabel);
            _search = EditorGUILayout.TextField("搜索", _search);
            _browserScroll = GUILayout.BeginScrollView(_browserScroll);
            foreach (string path in _assetPaths)
            {
                var asset = AssetDatabase.LoadAssetAtPath<GameplaySequenceDefinitionAsset>(path);
                if (asset == null || (asset.name.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0 &&
                    asset.DefinitionId.IndexOf(_search, StringComparison.OrdinalIgnoreCase) < 0)) continue;
                bool chosen = asset == CurrentSequence;
                if (GUILayout.Toggle(chosen, asset.name, EditorStyles.toolbarButton) && !chosen) Select(asset);
            }
            GUILayout.EndScrollView();
            GUILayout.EndVertical();
        }
    }
}
