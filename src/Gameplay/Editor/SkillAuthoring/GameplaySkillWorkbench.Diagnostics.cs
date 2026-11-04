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
            _feedback=_diagnostics.Any(i=>i.Severity==GameplayAuthoringIssueSeverity.Error)?"编译未通过；点击校验条目定位同源字段。":"正式运行图编译通过；未接入流程的节点提示另列。";
            _detailTab=3;_details?.MarkDirtyRepaint();Repaint();
        }
        private void RecordPreviewError(string node,string message)
        {if(!_diagnostics.Any(i=>i.Path==node&&i.Message==message))_diagnostics.Add(new GameplayAuthoringValidationIssue(GameplayAuthoringIssueSeverity.Error,node,message));}
        private void DrawDiagnostics()
        {
            EditorGUILayout.HelpBox(_diagnosticsStale?"配置已修改，以下诊断已过期；重新校验可更新。":"诊断对应当前正式数据；未接入流程的节点不参与执行。",_diagnosticsStale?MessageType.Warning:MessageType.Info);
            if(GUILayout.Button("重新校验正式技能"))ValidateDocument();
            using(new EditorGUI.DisabledScope(!_returnPan.HasValue))if(GUILayout.Button("返回定位前的视角")){Canvas.UpdateViewTransform(_returnPan.Value,_returnScale);_selected=_returnNode;Canvas.SelectNodeWithoutCamera(_selected);_returnPan=null;}
            foreach(var group in _diagnostics.GroupBy(i=>i.Severity)){
                GUILayout.Label(group.Key==GameplayAuthoringIssueSeverity.Error?"错误 / 不可运行":"提示 / 未安排",EditorStyles.boldLabel);
                foreach(var issue in group){if(GUILayout.Button(new GUIContent(issue.Message,issue.Path),new GUIStyle(EditorStyles.miniButton){wordWrap=true,alignment=TextAnchor.MiddleLeft}))NavigateDiagnostic(issue);GUILayout.Label(issue.Path,EditorStyles.wordWrappedMiniLabel);}
            }
        }
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
