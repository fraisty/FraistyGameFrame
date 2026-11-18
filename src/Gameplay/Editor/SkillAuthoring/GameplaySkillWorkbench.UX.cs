using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using FRFrame.Gameplay.Standard;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    public sealed partial class GameplaySkillWorkbench
    {
        private GameplaySkillDocumentBrowser _documentBrowser;
        private VisualElement _browserHandle,_inspectorHandle;
        private float _browserWidth=260,_inspectorWidth=370;
        private bool _browserOpen=true,_inspectorOpen=true;
        private int _browserTab,_detailTab,_rangeLevel=1;
        private bool _browserForced,_previewPlaying;
        private double _previewLast,_hostRepaintLast;
        private string _rangeKey;
        private void PreviewTick()
        {
            double hostNow=EditorApplication.timeSinceStartup;
            if(_installation!=null&&EditorApplication.isPlaying&&_detailTab==2&&hostNow-_hostRepaintLast>=.1){_hostRepaintLast=hostNow;_details?.MarkDirtyRepaint();Repaint();}
            if(!_previewPlaying||_previewClockOwner==null||!_previews.TryGetValue(_previewClockOwner,out var state)||state.Session==null)return;
            double now=EditorApplication.timeSinceStartup;state.Seconds+=(float)(now-_previewLast);_previewLast=now;
            float duration=PreviewClockDuration(_previewClockOwner);if(state.Seconds>duration){state.Seconds=duration;_previewPlaying=false;}
            state.Session.Seek(state.Seconds);Canvas.SetPreviewCursor(_previewClockOwner,state.Seconds);_details?.MarkDirtyRepaint();Canvas?.MarkDirtyRepaint();
        }
        private sealed class RangeSource
        {
            public GameplaySourceHandle Handle;
            public string Owner, Use, Occurrence, Label;
            public Color Color;
            public string Key => Handle.Key + "@" + Occurrence;
            public GameplayRangePreviewShape Shape;
            public string Error, Revision, Cursor;
            public bool HitWindow;
        }

        private static readonly Color[] RangeColors = {
            new Color(.2f,.74f,.94f,.65f), new Color(1f,.62f,.25f,.63f),
            new Color(.63f,.89f,.37f,.64f), new Color(.84f,.51f,.89f,.62f)
        };
        private readonly GameplayRangeSimulation _rangeSimulation = new GameplayRangeSimulation();
        private readonly GameplayRangeViewport _rangeViewport = new GameplayRangeViewport();
        private readonly HashSet<string> _hiddenRangeSources = new HashSet<string>();
        private readonly List<RangeSource> _rangeSources = new List<RangeSource>();
        private GameplaySkillDocumentAsset _rangeDocument;
        private GameplayAuthoringDependencyGraph _rangeDependencyGraph;
        private GameplayActorProfileAsset _rangeProfile;
        private bool _rangeIncludeReferences, _rangeWholeSkill, _rangeShowInputs, _rangeManualSlice;
        private string _rangePin, _rangeSolo;
        private int _rangePlane;
        private float _rangeSlice;

        private void DrawRangePreview()
        {
            var dependencies = Dependencies();
            if (_rangeDocument != _document)
            {
                _rangeDocument = _document;
                _rangeProfile = GameplaySkillEditorState.PreviewProfile(_document);
                _rangePin = _rangeSolo = null;
                _rangeWholeSkill = false;
                _rangePlane = 0;
                _rangeManualSlice = false;
                _hiddenRangeSources.Clear();
            }

            bool references = GUILayout.Toolbar(_rangeIncludeReferences ? 1 : 0, new[] { "本体", "包含实际引用" }) == 1;
            if (_rangeDependencyGraph != dependencies || references != _rangeIncludeReferences)
            {
                _rangeIncludeReferences = references;
                _rangeDependencyGraph = dependencies;
                RefreshRangeSources(dependencies);
            }

            var related = _rangeSources.Where(s => s.Owner == (_definition ?? _selected) || s.Use == _selected ||
                s.Use == _definition && _definition != null).ToArray();
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GUILayout.Toggle(_rangeWholeSkill, "钉住技能", EditorStyles.miniButtonLeft) != _rangeWholeSkill)
                {
                    _rangeWholeSkill = !_rangeWholeSkill;
                    _rangePin = _rangeSolo = null;
                }
                using (new EditorGUI.DisabledScope(_rangePin == null && related.Length == 0))
                if (GUILayout.Button(_rangePin == null ? "钉住当前来源" : "取消来源钉住", EditorStyles.miniButtonRight))
                {
                    _rangePin = _rangePin == null ? (related.FirstOrDefault(s=>s.Key==_rangeSolo)??related[0]).Key : null;
                    _rangeSolo = null;
                    _rangeWholeSkill = false;
                }
            }

            var listed = _rangeWholeSkill ? _rangeSources.ToArray() : _rangePin != null ?
                _rangeSources.Where(s => s.Key == _rangePin).ToArray() : related;
            if (_rangeSolo != null && !listed.Any(s => s.Key == _rangeSolo)) _rangeSolo = null;
            if (listed.Length == 0)
            {
                GUILayout.Label("选中搜索、命中窗或区域，也可钉住技能查看全图范围。", EditorStyles.wordWrappedMiniLabel);
                return;
            }

            _rangeLevel = Mathf.Clamp(EditorGUILayout.IntField("预览等级", _rangeLevel), 1, _document.EffectiveMaxLevel);
            if (_rangeShowInputs = EditorGUILayout.Foldout(_rangeShowInputs, "模拟输入 · 世界坐标", true))
            {
                _rangeProfile = (GameplayActorProfileAsset)EditorGUILayout.ObjectField("属性 Profile", _rangeProfile, typeof(GameplayActorProfileAsset), false);
                _rangeSimulation.Owner = EditorGUILayout.Vector3Field("自身位置", _rangeSimulation.Owner);
                _rangeSimulation.Facing = EditorGUILayout.Vector3Field("自身朝向向量", _rangeSimulation.Facing);
                _rangeSimulation.HasTarget = EditorGUILayout.ToggleLeft("输入目标角色", _rangeSimulation.HasTarget);
                if (_rangeSimulation.HasTarget) _rangeSimulation.Target = EditorGUILayout.Vector3Field("目标位置", _rangeSimulation.Target);
                _rangeSimulation.HasPoint = EditorGUILayout.ToggleLeft("输入捕获地点", _rangeSimulation.HasPoint);
                if (_rangeSimulation.HasPoint) _rangeSimulation.Point = EditorGUILayout.Vector3Field("地点位置", _rangeSimulation.Point);
                GUILayout.Label("额外 Caller / Context 数值，每行一个 标签=数值");
                _rangeSimulation.Caller = EditorGUILayout.TextArea(_rangeSimulation.Caller, GUILayout.MinHeight(26));
            }

            string cursor = _previewClockOwner != null && _previews.TryGetValue(_previewClockOwner, out var clock) ?
                _previewClockOwner + ":" + clock.Seconds.ToString("R") + ":" + clock.Session?.GetHashCode() : "";
            var input = _rangeSimulation;
            if (!Finite(input.Owner) || !Finite(input.Facing) || input.HasTarget && !Finite(input.Target) || input.HasPoint && !Finite(input.Point))
            {
                EditorGUILayout.HelpBox("世界位置和朝向需要有限数值。", MessageType.Warning);
                return;
            }
            string key = $"{_rangeLevel}|{_rangeProfile?.GetInstanceID()}|{(_rangeProfile ? EditorUtility.GetDirtyCount(_rangeProfile) : 0)}|{input.Owner:R}|{input.Facing:R}|{input.HasTarget}|{input.Target:R}|{input.HasPoint}|{input.Point:R}|{input.Caller}";
            bool dirty = _rangeKey != key;
            var visible = new List<(GameplaySearchGeometry geometry, Color color)>();
            float sourceButtonWidth = Mathf.Max(65, _inspector.resolvedStyle.width - 101);
            foreach (var source in listed)
            {
                bool shown = !_hiddenRangeSources.Contains(source.Key);
                using (new EditorGUILayout.HorizontalScope())
                {
                    bool next = GUILayout.Toggle(shown, GUIContent.none, GUILayout.Width(18));
                    if (next != shown)
                    {
                        if (next) _hiddenRangeSources.Remove(source.Key);
                        else _hiddenRangeSources.Add(source.Key);
                        shown = next;
                    }
                    var original = GUI.contentColor;
                    GUI.contentColor = new Color(source.Color.r, source.Color.g, source.Color.b, 1);
                    if (GUILayout.Button(new GUIContent(source.Label, source.Label), FindRangeButtonStyle(), GUILayout.Width(sourceButtonWidth)))
                        LocateRange(source);
                    GUI.contentColor = original;
                    if (GUILayout.Button(_rangeSolo == source.Key ? "全部" : "单看", EditorStyles.miniButton, GUILayout.Width(45)))
                        _rangeSolo = _rangeSolo == source.Key ? null : source.Key;
                }
                if (!shown || _rangeSolo != null && _rangeSolo != source.Key) continue;
                if (!source.Handle.Owner) continue;
                if (dirty || source.Revision != key || source.Cursor != cursor && source.HitWindow)
                    ReadRange(source, key, cursor);
                if (source.Error != null) EditorGUILayout.HelpBox(source.Error, MessageType.Warning);
                else if (source.Shape.Geometry is GameplaySearchGeometry geometry)
                {
                    visible.Add((geometry, source.Color));
                    string data = geometry.Shape switch {
                        GameplaySearchShape.Circle => $"R {geometry.Radius:0.##}",
                        GameplaySearchShape.Sector => $"R {geometry.Radius:0.##} · {geometry.Angle:0.##}°",
                        GameplaySearchShape.Box or GameplaySearchShape.Line or GameplaySearchShape.Ray => $"{geometry.Length:0.##} × {geometry.Width:0.##}",
                        GameplaySearchShape.Capsule => $"L {geometry.Length:0.##} · R {geometry.Radius:0.##}",
                        GameplaySearchShape.GridCells => $"{geometry.Cells.Count} 格",
                        _ => "目标身份"
                    };
                    GUILayout.Label($"{source.Shape.Label} · {geometry.Plane} · {data} · H {geometry.Height:0.##}" +
                        $"\n原点 ({geometry.Origin.X:0.##}, {geometry.Origin.Y:0.##}, {geometry.Origin.Z:0.##})",
                        EditorStyles.wordWrappedMiniLabel);
                }
                else if (source.Shape.Label != null)
                {
                    GUILayout.Label(source.Shape.Label, EditorStyles.wordWrappedMiniLabel);
                    GameplayRangePreview.Draw(GUILayoutUtility.GetRect(100,150,GUILayout.ExpandWidth(true)), source.Shape);
                }
            }
            _rangeKey = key;
            if (visible.Count == 0) return;
            _rangePlane = GUILayout.Toolbar(_rangePlane, new[] { "规则平面", "观察 XY", "观察 XZ" });
            bool xz = _rangePlane == 2 || _rangePlane == 0 && visible[0].geometry.Plane == GameplaySpatialPlane.XZ;
            using (new EditorGUILayout.HorizontalScope())
            {
                _rangeManualSlice = EditorGUILayout.ToggleLeft("自定切片", _rangeManualSlice);
                if (_rangeManualSlice) _rangeSlice = EditorGUILayout.FloatField(_rangeSlice);
                if (GUILayout.Button("重置视图", EditorStyles.miniButton, GUILayout.Width(70)))
                {
                    _rangeManualSlice = false;
                    _rangePlane = 0;
                    _rangeSolo = null;
                }
            }
            _rangeViewport.Draw(GUILayoutUtility.GetRect(100,185,GUILayout.ExpandWidth(true)),visible.ToArray(),xz,
                _rangeManualSlice && float.IsFinite(_rangeSlice) ? _rangeSlice : (float?)null, input);
            GUILayout.Label("同一规则几何的中心切片。示意标记采用逻辑位置，实际碰撞还受宿主、筛选与碰撞体影响。", EditorStyles.wordWrappedMiniLabel);

            static bool Finite(Vector3 v)=>float.IsFinite(v.x)&&float.IsFinite(v.y)&&float.IsFinite(v.z);
        }

        private void RefreshRangeSources(GameplayAuthoringDependencyGraph dependencies)
        {
            _rangeKey = null;
            _rangeSources.Clear();
            var graph = _document.AuthoringGraph;
            var reachable = GameplayGraphContextPolicy.Reachable(graph);
            var edges = dependencies.Root.Edges;
            foreach (var context in GameplayRangePreview.Sources(_document))
            {
                string path = context.PropertyPath;
                if (path == null) continue;
                bool local = path.StartsWith("authoringGraph.LocalDefinitions.Array.data[", StringComparison.Ordinal);
                if (!local && !path.StartsWith("authoringGraph.Nodes.Array.data[", StringComparison.Ordinal)) continue;
                int start = path.IndexOf('[')+1;
                int index = int.Parse(path.Substring(start,path.IndexOf(']')-start));
                string owner = local ? graph.LocalDefinitions[index].Id : graph.Nodes[index].Id;
                if (!reachable.Contains(owner)) continue;
                string label = local ? graph.LocalDefinitions[index].Name : GameplaySkillFieldSchema.NodeName(graph.Nodes[index]);
                var handle = GameplaySourceHandle.Capture(_document, path);
                var users = edges.Where(e => e.LocalDefinitionId == owner).ToArray();
                if (users.Length == 0) AddUses(handle, owner, owner, "", label);
                else foreach (var user in users) AddUses(handle, owner, user.GraphOwner, user.SlotPath, label + " · " + Usage(user.GraphOwner));
            }
            if (_rangeIncludeReferences)
            {
                foreach (var node in dependencies.Nodes.Where(n => n != dependencies.Root && n.Asset is GameplayDefinitionAsset))
                {
                    foreach (var context in GameplayRangePreview.Sources(node.Asset))
                    {
                        var handle = GameplaySourceHandle.Capture(node.Asset, context.PropertyPath);
                        foreach (var source in dependencies.Nodes)
                        foreach (var edge in source.Edges.Where(e => e.Target == node.Asset && e.State == GameplayDependencyState.Linked))
                        {
                            if (source == dependencies.Root)
                                AddUses(handle, null, edge.GraphOwner, edge.SlotPath, node.Asset.name + " · " + Usage(edge.GraphOwner) + " / " + edge.Label);
                            else Add(handle, null, null, GameplaySourceHandle.Capture(source.Asset).AssetKey + edge.PropertyPath,
                                node.Asset.name + " · " + source.Asset.name + " / " + edge.Label);
                        }
                    }
                }
            }
            if (_rangePin != null && !_rangeSources.Any(s => s.Key == _rangePin)) _rangePin = null;
            if (_rangeSolo != null && !_rangeSources.Any(s => s.Key == _rangeSolo)) _rangeSolo = null;

            string Usage(string owner)
            {
                var node = graph.Nodes.Find(n => n.Id == owner);
                return node != null ? GameplaySkillFieldSchema.NodeName(node) :
                    graph.LocalDefinitions.Find(d => d.Id == owner)?.Name ?? "配置引用";
            }
            void AddUses(GameplaySourceHandle handle, string owner, string use, string slot, string label)
            {
                var events = graph.Timelines.Where(t => reachable.Contains(t.Id)).SelectMany(t => t.Events
                    .Where(e => e.EntryNodeId == use).Select(e => (timeline:t, entry:e))).ToArray();
                var connections = graph.Connections.Where(e => e.ToNodeId == use && reachable.Contains(e.FromNodeId)).ToArray();
                if (events.Length == 0 && connections.Length <= 1)
                    Add(handle, owner, use, use + "." + slot, label);
                else
                {
                    foreach (var e in events)
                        Add(handle, owner, use, e.entry.Id + "." + slot, $"{e.entry.TimeSeconds:0.###} s · {label}");
                    foreach (var edge in connections)
                        Add(handle, owner, use, edge.Id + "." + slot, label + " · " + Usage(edge.FromNodeId) +
                            " / " + GameplaySkillFieldSchema.PortName(edge.OutputPortKey));
                }
            }
            void Add(GameplaySourceHandle handle, string owner, string use, string occurrence, string label)
            {
                _rangeSources.Add(new RangeSource { Handle=handle, Owner=owner, Use=use, Label=label,
                    Occurrence=occurrence, Color=RangeColors[_rangeSources.Count%RangeColors.Length] });
            }
        }

        private void LocateRange(RangeSource source)
        {
            if (source.Handle.Owner != _document)
            {
                GameplayInspectorOperations.EditResource(source.Handle.Owner);
                return;
            }
            string id=source.Occurrence.Split('.')[0];
            bool isTimeline=_document.AuthoringGraph.Timelines.Any(t=>t.Events.Any(e=>e.Id==id));
            NavigateDiagnostic(new GameplayAuthoringValidationIssue(GameplayAuthoringIssueSeverity.Warning, isTimeline?id:source.Owner, ""));
        }

        private void ReadRange(RangeSource source, string key, string cursor)
        {
            source.Revision = key;
            source.Cursor = cursor;
            source.Error = null;
            source.Shape = default;
            if (!source.Handle.TryResolve(out string path))
            {
                source.Error = "范围来源已移动或删除。";
                return;
            }
            var asset = source.Handle.Owner;
            GameplayHitWindowData window = null;
            using (var serialized = new SerializedObject(asset))
            {
                var property = path.Length == 0 ? null : serialized.FindProperty(path);
                if (property?.propertyType == SerializedPropertyType.ManagedReference) window = property.managedReferenceValue as GameplayHitWindowData;
                else if (asset is GameplayHitWindowDefinitionAsset shared) window = (GameplayHitWindowData)shared.CaptureGraphValues();
            }
            source.HitWindow = window != null;
            GameplayHitPose? sampled = null;
            if (window != null && _previewClockOwner != null &&
                _previews.TryGetValue(_previewClockOwner,out var animation) && animation.Session is IGameplayHitPosePreview reader &&
                (window.AnchorKind == GameplayHitAnchorKind.OwnerRoot ||
                    (window.AnchorKind == GameplayHitAnchorKind.TransformPath ? !string.IsNullOrWhiteSpace(window.AnchorPath) : !string.IsNullOrWhiteSpace(window.AnchorBone))) &&
                reader.TryReadHitPose(new GameplayHitAnchor(window.AnchorKind,window.AnchorPath,window.AnchorBone),out var pose))
                sampled = pose;
            var context = new GameplayRangePreviewContext(asset, path.Length == 0 ? null : path,
                _rangeLevel, _rangeProfile, sampled, _rangeSimulation, _document);
            if (!GameplayRangePreview.TryRead(context,out source.Shape,out source.Error) && source.Error == null)
                source.Error = "该来源没有可用的范围读取器。";
        }

        // V1's native captured-pointer splitter: resizing a pane changes view geometry only.
        private static VisualElement PaneHandle(Action<float> move,Action save)
        {
            var handle=new VisualElement{style={width=5,flexShrink=0,backgroundColor=GameplayEditorStyles.Surface}};float previous=0;
            handle.RegisterCallback<PointerDownEvent>(e=>{if(e.button!=0)return;previous=e.position.x;handle.CapturePointer(e.pointerId);e.StopPropagation();});
            handle.RegisterCallback<PointerMoveEvent>(e=>{if(!handle.HasPointerCapture(e.pointerId))return;move(e.position.x-previous);previous=e.position.x;e.StopPropagation();});
            handle.RegisterCallback<PointerUpEvent>(e=>{if(!handle.HasPointerCapture(e.pointerId))return;handle.ReleasePointer(e.pointerId);save();e.StopPropagation();});return handle;
        }
        internal void OpenDetails(int tab=0)
        {
            _detailTab=tab;
            _inspectorScroll=Vector2.zero;
            if(!_inspectorOpen){
                _inspectorOpen=true;
                ApplyPaneWidths();
                SavePanePreferences();
            }
            _details.MarkDirtyRepaint();
        }

        private void ApplyPaneWidths()
        {
            if(_browser==null||_inspector==null)return;
            bool browser=_browserOpen&&(position.width>=1100||_browserForced);
            _browser.style.display=browser?DisplayStyle.Flex:DisplayStyle.None;
            _browserHandle.style.display=browser?DisplayStyle.Flex:DisplayStyle.None;
            _inspector.style.display=_inspectorOpen?DisplayStyle.Flex:DisplayStyle.None;
            _inspectorHandle.style.display=_inspectorOpen?DisplayStyle.Flex:DisplayStyle.None;
            float right=_inspectorOpen?Mathf.Clamp(_inspectorWidth,300,Mathf.Max(300,position.width-400)):0;
            _inspector.style.width=right;
            _browser.style.width=Mathf.Clamp(_browserWidth,200,Mathf.Max(200,position.width-right-360));
        }
        private void DrawSourceBar()
        {
            GUILayout.BeginHorizontal(EditorStyles.toolbar);
            if(_document){
                GUILayout.Label(new GUIContent(_document.DisplayName+"  #"+_document.SkillId+(EditorUtility.IsDirty(_document)?"  • 未保存":""),AssetDatabase.GetAssetPath(_document)),EditorStyles.boldLabel);
                GUILayout.FlexibleSpace();GUILayout.Label(new GUIContent(System.IO.Path.GetFileName(AssetDatabase.GetAssetPath(_document)),AssetDatabase.GetAssetPath(_document)),EditorStyles.miniLabel);
                if(GUILayout.Button("定位源文件",EditorStyles.toolbarButton,GUILayout.Width(78))){Selection.activeObject=_document;EditorGUIUtility.PingObject(_document);}
            }else GUILayout.Label("选择技能或新建正式技能 · 单击节点直接编辑业务配置",EditorStyles.miniLabel);
            GUILayout.EndHorizontal();
        }
        private void ExternalChanged(IReadOnlyList<GameplayAuthoringSourceChanges.Change> changes)
        {
            if(!_document)return;
            bool documentChanged=changes.Any(c=>c.Source==_document)&&EditorUtility.GetDirtyCount(_document)!=_knownDocumentDirtyCount;
            bool sharedChanged=changes.Any(c=>Canvas?.UsesExternal(c.Source)==true);
            if(changes.Any(c=>c.Source==_rangeProfile||_dependencyGraph?.Find(c.Source)!=null)){_dependencyGraph=null;_details?.MarkDirtyRepaint();}
            // Native Apply also emits a deferred object notification. Its already-handled
            // dirty revision must not trigger a second full refresh. Unknown revisions and
            // all shared-source changes retain conservative topology/context fallback.
            if(!documentChanged&&!sharedChanged)return;
            _diagnosticsStale=true;_rangeKey=null;DisposePreview();_documentBrowser?.Changed(_document);RefreshUnknown();RememberSourceVersion();_details?.MarkDirtyRepaint();Repaint();
        }

    }
}
