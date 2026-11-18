using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    public sealed partial class GameplaySkillWorkbench
    {
        private readonly List<GameplayAuthoringValidationIssue> _diagnostics=new List<GameplayAuthoringValidationIssue>();
        private bool _diagnosticsStale=true;
        private GameplayAuthoringDependencyGraph _dependencyGraph;
        private GameplaySkillDocumentAsset _dependencyDocument;
        private int _dependencyRevision;
        private bool _showDependencies, _includeDependencies, _dependencyProblems;
        private Vector3? _returnPan;private Vector3 _returnScale;private string _returnNode;
        public IReadOnlyList<GameplayAuthoringValidationIssue> Diagnostics=>_diagnostics;
        public void ValidateDocument()
        {
            _diagnostics.Clear();_diagnosticsStale=false;if(!_document)return;
            var validation=new GameplayAuthoringValidationContext();
            try{_document.Validate(validation,"技能");_diagnostics.AddRange(validation.Issues);_document.Build(new GameplayAuthoringBuildContext());}
            catch(GameplayGraphCompilationException error){foreach(var issue in error.Issues)if(!_diagnostics.Any(i=>i.Path==issue.Path&&i.Message==issue.Message))_diagnostics.Add(issue);}
            catch(Exception error){Exception cause=error;while(cause!=null&&!(cause is GameplayGraphCompilationException))cause=cause.InnerException;
                if(cause is GameplayGraphCompilationException structured){foreach(var issue in structured.Issues)if(!_diagnostics.Any(i=>i.Path==issue.Path&&i.Message==issue.Message))_diagnostics.Add(issue);}
                else _diagnostics.Add(new GameplayAuthoringValidationIssue(GameplayAuthoringIssueSeverity.Error,"技能",error.Message));}
            var active=GameplayGraphContextPolicy.Reachable(_document.AuthoringGraph);
            foreach(var node in _document.AuthoringGraph.Nodes.Where(n=>!active.Contains(n.Id)))_diagnostics.Add(new GameplayAuthoringValidationIssue(GameplayAuthoringIssueSeverity.Warning,node.Id,"未接入执行流程，不参与执行："+GameplaySkillFieldSchema.NodeName(node)));
            foreach(var node in _document.AuthoringGraph.Nodes.Where(n=>active.Contains(n.Id)&&(n.Payload as GameplayRuleNodePayload)?.Target is GameplaySkillContextTargetAuthoring))
                _diagnostics.Add(new GameplayAuthoringValidationIssue(GameplayAuthoringIssueSeverity.Warning,node.Id,"调用者输入契约：此规则保留 TriggerEvent.Context；宿主须提供下游所需 Actor / Point。地点回调之后仍按实际来源检查，不能假定自身。"));
            if(!_diagnostics.Any(i=>i.Severity==GameplayAuthoringIssueSeverity.Error))
                _diagnostics.AddRange(GameplaySkillContextVariables.For(_document).Diagnostics());
            foreach(string issue in GameplaySkillNodeDescriptors.DuplicateClaims(_document))
                _diagnostics.Add(new GameplayAuthoringValidationIssue(GameplayAuthoringIssueSeverity.Warning,"创作分类",issue));
            _feedback=_diagnostics.Any(i=>i.Severity==GameplayAuthoringIssueSeverity.Error)?"编译未通过；点击校验条目定位同源字段。":"正式运行图编译通过；未接入流程的节点提示另列。";
            _detailTab=3;_details?.MarkDirtyRepaint();Repaint();
        }
        private void RecordPreviewError(string node,string message)
        {if(!_diagnostics.Any(i=>i.Path==node&&i.Message==message))_diagnostics.Add(new GameplayAuthoringValidationIssue(GameplayAuthoringIssueSeverity.Error,node,message));}
        private void DrawDiagnostics()
        {
            if (_showDependencies = EditorGUILayout.Foldout(_showDependencies, "资产依赖与修复", true))
                DrawDependencies();
            EditorGUILayout.HelpBox(_diagnosticsStale?"配置已修改，以下诊断已过期；重新校验可更新。":"诊断对应当前正式数据；未接入流程的节点不参与执行。",_diagnosticsStale?MessageType.Warning:MessageType.Info);
            if(GUILayout.Button("重新校验正式技能"))ValidateDocument();
            using(new EditorGUI.DisabledScope(!_returnPan.HasValue))if(GUILayout.Button("返回定位前的视角")){Canvas.UpdateViewTransform(_returnPan.Value,_returnScale);_selected=_returnNode;Canvas.SelectNodeWithoutCamera(_selected);_returnPan=null;}
            foreach(var group in _diagnostics.GroupBy(i=>i.Severity)){
                GUILayout.Label(group.Key==GameplayAuthoringIssueSeverity.Error?"错误 / 不可运行":"提示 / 未安排",EditorStyles.boldLabel);
                foreach(var issue in group){if(GUILayout.Button(new GUIContent(issue.Message,issue.Path),new GUIStyle(EditorStyles.miniButton){wordWrap=true,alignment=TextAnchor.MiddleLeft}))NavigateDiagnostic(issue);GUILayout.Label(issue.Path,EditorStyles.wordWrappedMiniLabel);}
            }
        }

        private GameplayAuthoringDependencyGraph Dependencies()
        {
            int revision = EditorUtility.GetDirtyCount(_document);
            if (_dependencyGraph == null || _dependencyDocument != _document || _dependencyRevision != revision)
            {
                _dependencyDocument = _document;
                _dependencyRevision = revision;
                _dependencyGraph = GameplayAuthoringDependencyGraph.BuildDefinition(_document);
            }
            return _dependencyGraph;
        }

        private void DrawDependencies()
        {
            var current = _document;
            _includeDependencies = GUILayout.Toolbar(_includeDependencies ? 1 : 0, new[] { "本体", "包含实际引用" }) == 1;
            _dependencyProblems = EditorGUILayout.ToggleLeft("仅缺失 / 错误", _dependencyProblems);
            if (GUILayout.Button("刷新依赖")) _dependencyGraph = null;
            var model = Dependencies();
            IEnumerable<GameplayAuthoringDependencyNode> nodes = _includeDependencies ? model.Nodes : new[] { model.Root };
            foreach (var source in nodes)
            {
                var edges = source.Edges.Where(e => !_dependencyProblems || e.Missing).ToArray();
                if (edges.Length == 0 && source != model.Root) continue;
                GUILayout.Label(source.Asset.name, EditorStyles.boldLabel);
                if (!_dependencyProblems && source.UnusedDefinitions > 0)
                    GUILayout.Label($"未参与运行的节点与定义 {source.UnusedDefinitions}", EditorStyles.wordWrappedMiniLabel);
                foreach (var edge in edges)
                {
                    string status = edge.State switch
                    {
                        GameplayDependencyState.Inline => "内嵌",
                        GameplayDependencyState.Optional => "可选空",
                        GameplayDependencyState.Unset => "空 · 依配置判断",
                        GameplayDependencyState.Missing => "缺失",
                        GameplayDependencyState.Invalid => "错误",
                        _ => "共享 / 引用"
                    };
                    string owner = edge.GraphOwner == null ? null : current.AuthoringGraph.Nodes.Find(n=>n.Id==edge.GraphOwner) is GameplayAuthoringNode node
                        ? GameplaySkillFieldSchema.NodeName(node) : current.AuthoringGraph.LocalDefinitions.Find(d=>d.Id==edge.GraphOwner)?.Name;
                    string field = edge.GraphOwner == null ? edge.Label.Split('.').Last() : edge.Label;
                    if (GUILayout.Button(new GUIContent($"{status} · {(owner==null?"":owner+" / ")}{field}", edge.PropertyPath), FindRangeButtonStyle()))
                    {
                        if (edge.Open != null) edge.Open();
                        else if (!string.IsNullOrEmpty(edge.GraphOwner) && edge.Source == _document)
                            NavigateDiagnostic(new GameplayAuthoringValidationIssue(GameplayAuthoringIssueSeverity.Warning, edge.GraphOwner + "." + edge.SlotPath, ""));
                        else
                        {
                            GameplayInspectorOperations.EditResource(edge.Source);
                            if (edge.Source is GameplayDefinitionAsset)
                                GameplayAuthoringInspector.RequestFieldFocus(edge.Source, edge.PropertyPath);
                        }
                    }
                    if (edge.Target && GUILayout.Button("定位 " + edge.Target.name, EditorStyles.miniButton))
                    {
                        if (edge.Ping != null) edge.Ping();
                        else GameplayInspectorOperations.LocateResource(edge.Target);
                    }
                    if (!edge.Missing) continue;
                    EditorGUILayout.LabelField(edge.Issue + (edge.ExpectedType==null?"":" · "+edge.ExpectedType.Name), EditorStyles.wordWrappedMiniLabel);
                    if (edge.ExpectedType != null && typeof(GameplayDefinitionAsset).IsAssignableFrom(edge.ExpectedType))
                    {
                        using var disabled = new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode ||
                            edge.Source is GameplayDefinitionAsset { MigrationHistoryOnly: true } ||
                            !AssetDatabase.IsOpenForEdit(edge.Source) ||
                            edge.Source is GameplaySkillDocumentAsset doc && GameplaySkillAuthoringCommands.IsReadOnly(doc));
                        if (GUILayout.Button("绑定已有…", EditorStyles.miniButton))
                        {
                            GameplaySkillCreationMenu.Show(Canvas, "选择共享资源 · " + edge.Label, menu =>
                            {
                                var choices = AssetDatabase.FindAssets("t:" + edge.ExpectedType.Name, new[] { "Assets" })
                                    .Select(AssetDatabase.GUIDToAssetPath).Distinct().SelectMany(AssetDatabase.LoadAllAssetsAtPath)
                                    .OfType<GameplayDefinitionAsset>().Where(a => !a.MigrationHistoryOnly && edge.ExpectedType.IsInstanceOfType(a))
                                    .OrderBy(a => a.name, StringComparer.Ordinal).ToArray();
                                string scope = edge.Source is GameplaySkillDocumentAsset skill ? skill.AuthoringGraph.Scope :
                                    (edge.Source as IGameplayScopedAuthoring)?.RequiredScope;
                                foreach (var asset in choices)
                                {
                                    string path = AssetDatabase.GetAssetPath(asset);
                                    string message = GameplayActionArrayPolicy.ScopeReason(asset, scope);
                                    var item = new GUIContent(asset.name, message ?? path);
                                    if (message != null) menu.AddDisabledItem(item);
                                    else menu.AddItem(item, false, () =>
                                    {
                                        if (_document != current || ConfirmSharedRepair(edge))
                                            CompleteDependencyRepair(current, () => GameplayAuthoringDependencyRepair.Assign(edge, asset));
                                    });
                                }
                                if (choices.Length == 0) menu.AddDisabledItem(new GUIContent("没有匹配的共享定义"));
                            });
                        }
                        if (edge.GraphOwner != null && GUILayout.Button(new GUIContent("新建内嵌…","使用该类型的默认字段。必填项可在定位后的内嵌定义中配置。"), EditorStyles.miniButton))
                        {
                            var menu = new GenericMenu();
                            foreach (var type in GameplaySkillFieldSchema.Values(edge.ExpectedType))
                                menu.AddItem(new GUIContent(GameplaySkillFieldSchema.Name(type)), false,
                                    () => CompleteDependencyRepair(current, () => GameplayAuthoringDependencyRepair.AssignInline(edge, type)));
                            if (menu.GetItemCount() == 0) menu.AddDisabledItem(new GUIContent("没有可用内嵌类型"));
                            menu.ShowAsContext();
                        }
                        if (!edge.ExpectedType.IsAbstract && typeof(ScriptableObject).IsAssignableFrom(edge.ExpectedType) &&
                            GUILayout.Button("新建共享定义…", EditorStyles.miniButton) && ConfirmSharedRepair(edge) &&
                            EditorUtility.DisplayDialog("新建并绑定", "按类型默认值创建草稿，请填写必填项后再试装。撤销绑定会保留可复用的共享定义文件。", "选择位置", "取消"))
                        {
                            string path = EditorUtility.SaveFilePanelInProject("新建共享定义", edge.ExpectedType.Name, "asset", "选择一个空闲位置。");
                            if (path.Length > 0) CompleteDependencyRepair(current, () => GameplayAuthoringDependencyRepair.CreateAndAssign(edge, path));
                        }
                    }
                }
            }
        }

        private bool ConfirmSharedRepair(GameplayAuthoringDependencyEdge edge) => edge.Source == _document ||
            EditorUtility.DisplayDialog("编辑共享来源", $"修改 {edge.Source.name} 会影响所有引用该定义的技能。", "编辑共享来源", "取消");

        private void CompleteDependencyRepair(GameplaySkillDocumentAsset current, Action edit)
        {
            Attempt(() =>
            {
                if (_document != current) throw new InvalidOperationException("当前技能已切换。请从当前依赖列表重新选择。");
                edit();
                _diagnosticsStale = true;
                _feedback = "已更新引用，可重新校验正式技能。";
            });
            _dependencyGraph = null;
            _details?.MarkDirtyRepaint();
        }

        private static GUIStyle _sourceButtonStyle;
        private static GUIStyle FindRangeButtonStyle() => _sourceButtonStyle ??= new GUIStyle(EditorStyles.miniButton)
            { wordWrap = true, alignment = TextAnchor.MiddleLeft };
        public void NavigateDiagnostic(GameplayAuthoringValidationIssue issue)
        {
            if(!_document||_document.AuthoringGraph==null||_document.AuthoringGraph.Nodes.Count==0)return;var graph=_document.AuthoringGraph;string path=issue.Path+" "+issue.Message;
            var edge=graph.Connections.FirstOrDefault(e=>path.Contains(e.Id));if(edge!=null)path+=" "+edge.FromNodeId;
            var local=graph.LocalDefinitions.FirstOrDefault(d=>path.Contains(d.Id));var node=graph.Nodes.FirstOrDefault(n=>path.Contains(n.Id));
            var timeline=graph.Timelines.FirstOrDefault(t=>path.Contains(t.Id)||t.Events.Any(e=>path.Contains(e.Id)));var entry=timeline?.Events.FirstOrDefault(e=>path.Contains(e.Id));
            if(entry!=null)node=graph.Nodes.Find(n=>n.Id==entry.EntryNodeId);
            if(node==null&&local!=null)node=graph.Nodes.FirstOrDefault(n=>n.Payload.Definitions.Any(b=>b.LocalId==local.Id));
            if(node==null)node=graph.Nodes.FirstOrDefault(n=>n.Id==graph.AbilityNodeId);if(node==null)return;
            _returnPan=Canvas.viewTransform.position;_returnScale=Canvas.viewTransform.scale;_returnNode=_selected;_selected=node.Id;_definition=local?.Id;_detailTab=0;
            Canvas.FocusNode(node.Id);
            string property=local!=null?"authoringGraph.LocalDefinitions.Array.data["+graph.LocalDefinitions.IndexOf(local)+"].Payload":
                entry!=null?"authoringGraph.Timelines.Array.data["+graph.Timelines.IndexOf(timeline)+"].Events.Array.data["+timeline.Events.IndexOf(entry)+"]":
                "authoringGraph.Nodes.Array.data["+graph.Nodes.IndexOf(node)+"].Payload"+(node.Payload is GameplayActionNodePayload?".Action":node.Payload is GameplayAbilityNodePayload?".Settings":"");
            string id=local?.Id??entry?.Id??node.Id;int at=issue.Path.IndexOf(id,StringComparison.Ordinal);if(at>=0){string suffix=issue.Path.Substring(at+id.Length);if(suffix.StartsWith(".",StringComparison.Ordinal))property+=GameplayGraphFields.NormalizePath(suffix);}
            using(var serialized=new SerializedObject(_document)){while(serialized.FindProperty(property)==null&&property.Contains('.'))property=property.Substring(0,property.LastIndexOf('.'));}
            GameplaySkillFieldSchema.RevealField(_document,property);GameplayAuthoringInspector.RequestFieldFocus(_document,property);_inspectorScroll=Vector2.zero;_details?.MarkDirtyRepaint();Repaint();
        }
    }
}
