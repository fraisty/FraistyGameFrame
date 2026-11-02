using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Debugging;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.RuntimeDebugger
{
    /// <summary>Read-only scene provider viewer. Editor update only captures state; it never advances rules.</summary>
    public sealed class GameplayRuntimeDebuggerWindow : EditorWindow
    {
        private readonly GameplayRuntimeDebuggerModel _model = new GameplayRuntimeDebuggerModel();
        private readonly List<IGameplayRuntimeDebugSource> _sources = new List<IGameplayRuntimeDebugSource>();
        private int _sourceIndex;
        private bool _autoRefresh = true;
        private double _lastRefresh;
        private string _search = string.Empty;
        private string _sourceHighlight = string.Empty;
        private Vector2 _unitScroll;
        private Vector2 _detailScroll;

        [MenuItem("FRFrame/Gameplay/Runtime Debugger")]
        public static void Open() => GetWindow<GameplayRuntimeDebuggerWindow>("Runtime Debugger");

        private void OnEnable()
        {
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
        }

        private void OnDisable()
        {
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            _sources.Clear();
            _model.Clear();
        }

        private void OnPlayModeChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.ExitingPlayMode || change == PlayModeStateChange.EnteredEditMode)
            {
                _sources.Clear();
                _model.Clear();
                Repaint();
            }
        }

        private void OnEditorUpdate()
        {
            if (!EditorApplication.isPlaying) return;
            if (EditorApplication.timeSinceStartup - _lastRefresh < .125d) return;
            if (_autoRefresh) Refresh();
            else
            {
                _lastRefresh = EditorApplication.timeSinceStartup;
                DiscoverSources();
                if (_sources.Count == 0) Repaint();
            }
        }

        private void DiscoverSources()
        {
            IGameplayRuntimeDebugSource selected = _sourceIndex < _sources.Count ? _sources[_sourceIndex] : null;
            _sources.Clear();
            foreach (MonoBehaviour behaviour in UnityEngine.Object.FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                if (behaviour != null && behaviour.isActiveAndEnabled &&
                    behaviour is IGameplayRuntimeDebugSource source)
                    _sources.Add(source);
            _sources.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));
            int retained = _sources.FindIndex(source => ReferenceEquals(source, selected));
            if (selected != null && retained < 0) _model.Clear();
            if (retained >= 0) _sourceIndex = retained;
            if (_sourceIndex >= _sources.Count) _sourceIndex = 0;
        }

        private void Refresh()
        {
            _lastRefresh = EditorApplication.timeSinceStartup;
            DiscoverSources();
            _model.Refresh(_sources.Count == 0 ? null : _sources[_sourceIndex]);
            Repaint();
        }

        private void OnGUI()
        {
            if (!EditorApplication.isPlaying)
            {
                EditorGUILayout.HelpBox("Enter Play Mode to inspect a Gameplay source.", MessageType.Info);
                return;
            }
            if (_sources.Count == 0) DiscoverSources();
            using (new EditorGUILayout.HorizontalScope(EditorStyles.toolbar))
            {
                string[] names = new string[_sources.Count];
                for (int i = 0; i < names.Length; i++) names[i] = _sources[i].DisplayName;
                if (names.Length > 0)
                {
                    int next = EditorGUILayout.Popup(_sourceIndex, names, GUILayout.Width(220));
                    if (next != _sourceIndex) { _sourceIndex = next; Refresh(); }
                }
                else GUILayout.Label("No provider", EditorStyles.toolbarButton, GUILayout.Width(220));
                if (GUILayout.Button("Refresh", EditorStyles.toolbarButton, GUILayout.Width(60))) Refresh();
                _autoRefresh = GUILayout.Toggle(_autoRefresh, "Auto", EditorStyles.toolbarButton, GUILayout.Width(50));
                _model.Frozen = GUILayout.Toggle(_model.Frozen, "Freeze", EditorStyles.toolbarButton, GUILayout.Width(65));
                _search = GUILayout.TextField(_search, GUI.skin.FindStyle("ToolbarSeachTextField") ??
                    EditorStyles.toolbarTextField, GUILayout.MinWidth(100));
            }
            if (_model.Snapshot == null)
            {
                EditorGUILayout.HelpBox("Waiting for a live scene provider.", MessageType.Info);
                return;
            }
            EditorGUILayout.LabelField("Tick " + _model.Snapshot.TickVersion +
                "   Paused " + _model.Snapshot.IsPaused +
                "   Units " + _model.Snapshot.Units.Count, EditorStyles.boldLabel);
            using (new EditorGUILayout.HorizontalScope())
            {
                _sourceHighlight = EditorGUILayout.TextField("Highlight SourceId", _sourceHighlight);
                if (GUILayout.Button("Copy Snapshot", GUILayout.Width(110)))
                    EditorGUIUtility.systemCopyBuffer = BuildSnapshotText();
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                using (new EditorGUILayout.VerticalScope(GUILayout.Width(230)))
                {
                    _unitScroll = EditorGUILayout.BeginScrollView(_unitScroll);
                    foreach (GameplayDebugUnitSnapshot unit in _model.Snapshot.Units)
                    {
                        if (!GameplayRuntimeDebuggerModel.MatchesUnit(unit, _search)) continue;
                        bool selected = unit.SourceId == _model.SelectedUnitSourceId&&unit.Group==_model.SelectedUnitGroup;
                        if (GUILayout.Toggle(selected, unit.DisplayName, "Button")) _model.SelectUnit(unit.SourceId,unit.Group);
                    }
                    EditorGUILayout.EndScrollView();
                }
                _detailScroll = EditorGUILayout.BeginScrollView(_detailScroll);
                GameplayDebugUnitSnapshot current = _model.SelectedUnit();
                if (current != null)
                    foreach (GameplayDebugSection section in current.Sections)
                    {
                        EditorGUILayout.LabelField(section.Name, EditorStyles.boldLabel);
                        foreach (string line in section.Lines)
                        {
                            if (!GameplayRuntimeDebuggerModel.Matches(line, _search)) continue;
                            Color old = GUI.contentColor;
                            if (!string.IsNullOrEmpty(_sourceHighlight) &&
                                TryGetSourceId(line, out string visibleSource) &&
                                visibleSource == _sourceHighlight) GUI.contentColor = Color.yellow;
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                EditorGUILayout.SelectableLabel(line, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                                if(TryGetActionLocation(line,out string definitionId,out long nodeId)&&
                                    GUILayout.Button("定位动作",GUILayout.Width(75)))LocateAction(definitionId,nodeId,ReadDocumentId(line));
                                if (section.Name == "Effects" && TryGetSourceId(line, out string effectSource) &&
                                    GUILayout.Button("Focus source", GUILayout.Width(95)))
                                    _sourceHighlight = effectSource;
                            }
                            GUI.contentColor = old;
                        }
                        EditorGUILayout.Space(6);
                    }
                EditorGUILayout.LabelField("Events", EditorStyles.boldLabel);
                foreach (GameplayEventHistoryEntry entry in _model.Snapshot.RecentEvents)
                {
                    string line = entry.Sequence + " " + entry.Data.EventTag +
                        " Instigator=" + entry.Data.InstigatorId + " Target=" + entry.Data.TargetId +
                        " Magnitude=" + entry.Data.Magnitude;
                    if (GameplayRuntimeDebuggerModel.Matches(line, _search))
                        EditorGUILayout.SelectableLabel(line, GUILayout.Height(EditorGUIUtility.singleLineHeight));
                }
                EditorGUILayout.EndScrollView();
            }
        }

        private string BuildSnapshotText()
        {
            var lines = new List<string>();
            foreach (GameplayDebugUnitSnapshot unit in _model.Snapshot.Units)
            {
                lines.Add(unit.DisplayName);
                foreach (GameplayDebugSection section in unit.Sections)
                {
                    lines.Add("[" + section.Name + "]");
                    lines.AddRange(section.Lines);
                }
            }
            foreach (GameplayEventHistoryEntry entry in _model.Snapshot.RecentEvents)
                lines.Add("Event " + entry.Sequence + " " + entry.Data.EventTag);
            return string.Join("\n", lines);
        }

        private static bool TryGetSourceId(string line, out string sourceId)
        {
            sourceId = null;
            int start = line.IndexOf("SourceId=", StringComparison.Ordinal);
            if (start < 0) return false;
            start += "SourceId=".Length;
            int end = start;
            if (end < line.Length && line[end] == '-') end++;
            while (end < line.Length && char.IsDigit(line[end])) end++;
            if (end == start || (end == start + 1 && line[start] == '-')) return false;
            sourceId = line.Substring(start, end - start);
            return true;
        }

        private static bool TryGetActionLocation(string line,out string definitionId,out long nodeId)
        {
            definitionId=null;nodeId=-1;
            var tokens=line.Split(' ');
            var asset=tokens.FirstOrDefault(t=>t.StartsWith("SourceAsset=",StringComparison.Ordinal));
            var node=tokens.FirstOrDefault(t=>t.StartsWith("NodeId=",StringComparison.Ordinal));
            if(asset==null||node==null||!long.TryParse(node.Substring(7),out nodeId))return false;
            definitionId=Uri.UnescapeDataString(asset.Substring(12));return nodeId>=0;
        }

        /// <summary>Resolves a saved native node only on explicit navigation. Duplicate identities,
        /// deleted nodes and unavailable workspace mappings never select an arbitrary result.</summary>
        private static string ReadDocumentId(string line)
        {
            var token=line.Split(' ').FirstOrDefault(t=>t.StartsWith("SourceDocument=",StringComparison.Ordinal));
            return token==null?null:Uri.UnescapeDataString(token.Substring(15));
        }
        public static bool LocateAction(string definitionId,long nodeId,string documentId=null)
        {

            var matches=SkillAuthoring.GameplaySkillWorkbench.Documents().Where(d=>d.AuthoringSchemaVersion==2&&(string.IsNullOrEmpty(documentId)||d.DefinitionId==documentId))
                .SelectMany(d=>d.AuthoringGraph.Nodes.Where(n=>n.Payload is FRFrame.Gameplay.Authoring.Graph.GameplayActionNodePayload&&
                    (string.IsNullOrEmpty(n.SourceDefinitionId)?d.DefinitionId:n.SourceDefinitionId)==definitionId&&
                    (n.SourceActionId>=0?n.SourceActionId:FRFrame.Gameplay.Authoring.Graph.GameplayGraphCompiler.StableActionId(n.Id))==nodeId).Select(n=>(document:d,node:n))).ToArray();
            if(matches.Length==1){SkillAuthoring.GameplaySkillWorkbench.Open(matches[0].document,matches[0].node.Id);return true;}
            if(matches.Length>1){Debug.LogWarning("Trace 来源有多个调用位置，请在对应主技能中选择调用；未随意选择节点。");return false;}
            var assets=GameplayReferenceScanner.FindDefinitionAssets().Where(a=>!a.MigrationHistoryOnly&&a.DefinitionId==definitionId).ToArray();
            if(assets.Length!=1){Debug.LogWarning("Trace 定位失败：来源已删除或定义标识重复。");return false;}
            var source=assets[0];string path=null;
            using(var serialized=new SerializedObject(source)){var property=serialized.GetIterator();while(property.Next(true))if(property.propertyType==SerializedPropertyType.ManagedReference&&property.managedReferenceId==nodeId){path=property.propertyPath;break;}}
            if(path==null){Debug.LogWarning("Trace 定位失败：原动作已删除或替换。");return false;}
            Selection.activeObject=source;EditorGUIUtility.PingObject(source);GameplayAuthoringInspector.RequestFieldFocus(source,path);
            return true;

        }
    }
}
