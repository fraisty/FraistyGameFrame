using System;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Node = UnityEditor.Experimental.GraphView.Node;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    public sealed partial class GameplayBehaviorGraphView
    {
        private static string FollowKey(GameplaySkillDocumentAsset document,string timeline)=>GameplaySkillEditorState.Key(document)+".FollowMain."+timeline;
        internal static bool FollowsMainAnimation(GameplaySkillDocumentAsset document)=>document.AuthoringGraph.Timelines.Any(t=>SessionState.GetString(FollowKey(document,t.Id),"").Length>0);
        /// <summary>Explicit editor preference updates the existing duration; it creates no runtime clock or end event.</summary>
        internal static void FollowMainAnimationDurations(GameplaySkillDocumentAsset document,GameplayAuthoringGraph graph)
        {
            foreach(var timeline in graph.Timelines){
                string main=SessionState.GetString(FollowKey(document,timeline.Id),"");
                var entry=timeline.Events.FirstOrDefault(e=>e.EntryNodeId==main);
                if(entry!=null&&GameplaySkillFieldSchema.TryDuration(graph,main,out _,out _,out float duration))
                    timeline.DurationSeconds=Mathf.Max(timeline.Events.Max(e=>e.TimeSeconds),entry.TimeSeconds+duration);
            }
        }
        private void AddMainAnimationControls(Node card,string owner,GameplayAuthoringTimeline timeline)
        {
            string main=timeline.Events.Select(e=>e.EntryNodeId).FirstOrDefault(IsTimelineInline);
            var source = new Label(MainAnimationSource(timeline)) { name = "MainAnimationProvenance" };
            source.style.whiteSpace = WhiteSpace.Normal;
            source.style.marginLeft = 10;
            source.style.marginRight = 10;
            card.extensionContainer.Add(source);
            if(main==null)card.extensionContainer.Add(new Button(()=>ShowMainAnimationMenu(null,timeline)){text="＋ 设置主动画（可选）",tooltip="主动画使用真实 Animation Action 和唯一 Schedule；无动画也能执行。"});
        }
        private string MainAnimationSource(GameplayAuthoringTimeline timeline)
        {
            var graph = _document.AuthoringGraph;
            var scheduled = timeline.Events
                .Select(e => graph.Nodes.Find(n => n.Id == e.EntryNodeId))
                .Where(n => (n?.Payload as GameplayActionNodePayload)?.Action is GameplaySkillActionAuthoring action &&
                    GameplaySkillNodeDescriptors.Get(action.GetType()).IsAnimation).ToArray();
            if (scheduled.Length == 0) return "技能主动画：未配置（合法）。运行时宿主可使用默认施放动作。";
            var main = scheduled.FirstOrDefault(n => IsTimelineInline(n.Id));
            if (main == null) return "技能主动画：未配置为内联主动画。已安排独立动画事件，可在时间轴定位。";
            if (!(GameplaySkillNodeDisplay.BoundValues(main, graph) is GameplayLocalDefinitionPayload source))
                return "技能主动画：已安排，资源待配置。";
            var validation = new GameplayAuthoringValidationContext();
            source.Validate(validation, "技能主动画");
            if (validation.HasErrors) return "技能主动画：已安排，资源待配置。";
            var binding = main.Payload.Definitions.FirstOrDefault(b => b.External);
            string resource = GameplaySkillFieldSchema.ValueSummary(source)?.Split('\n')[0] ?? GameplaySkillFieldSchema.Name(source.GetType());
            return "技能主动画：已配置 · " + resource +
                (binding == null ? " · 本地来源" : " · 共享来源 " + binding.External.name);
        }
        private void ShowMainAnimationMenu(string main,GameplayAuthoringTimeline timeline)
        {
            GameplaySkillCreationMenu.Show(this,"设置主动画 · 使用真实后端",menu=>{
                foreach(var type in GameplayAuthoringTypeCatalog.Query(typeof(GameplaySkillActionAuthoring),_document.AuthoringGraph.Scope).Where(t=>GameplaySkillNodeDescriptors.Get(t).IsAnimation)){
                    var action=(GameplaySkillActionAuthoring)Activator.CreateInstance(type);
                    var slot=GameplayGraphFields.ResourceSlots(action).FirstOrDefault();if(slot==null)continue;
                    foreach(var valueType in GameplaySkillFieldSchema.Values(slot.ValueType)){
                        var actionType=type;var value=valueType;string path=slot.Path;
                        var label=new GUIContent(GameplaySkillNodeDescriptors.Get(type).DisplayName+"/"+GameplaySkillFieldSchema.Name(valueType));
                        string reason=GameplaySkillTypeAvailability.Reason(_document,type);
                        if(reason!=null){label.tooltip=reason;menu.AddDisabledItem(label);continue;}
                        menu.AddItem(label,false,()=>Try(()=>{
                            if(main==null)GameplaySkillAuthoringCommands.CreateAction(_document,(GameplaySkillActionAuthoring)Activator.CreateInstance(actionType),"主动画",CreationCenter,(GameplayLocalDefinitionPayload)Activator.CreateInstance(value),path,timeline.Id,0);
                            else GameplaySkillAuthoringCommands.Transaction(_document,"更换主动画后端",g=>{
                                var node=g.Nodes.Single(n=>n.Id==main);var old=node.Payload.Definitions.Select(b=>b.LocalId).ToArray();
                                var local=new GameplayLocalDefinition{Name="主动画",SourceDefinitionId=_document.DefinitionId,Payload=(GameplayLocalDefinitionPayload)Activator.CreateInstance(value)};
                                node.Payload=new GameplayActionNodePayload{Action=(GameplaySkillActionAuthoring)Activator.CreateInstance(actionType)};
                                node.Payload.Definitions.Add(new GameplayDefinitionBinding{PropertyPath=path,LocalId=local.Id});g.LocalDefinitions.Add(local);GameplaySkillAuthoringCommands.CollectReleased(g,old);
                            });
                        }));
                    }
                }
            });
        }
        /// <summary>Edits the same inline animation and schedule from the right business Inspector.</summary>
        internal void DrawMainAnimationSettings(string selected)
        {
            string owner=selected!=null&&_inlineOwners.TryGetValue(selected,out var timelineOwner)?timelineOwner:selected;
            var node=_document.AuthoringGraph.Nodes.Find(n=>n.Id==owner);if(node==null)return;
            foreach(var current in _document.AuthoringGraph.Timelines.Where(t=>node.Payload.Definitions.Any(b=>b.LocalId==t.Id))){
                string main=current.Events.Select(e=>e.EntryNodeId).FirstOrDefault(IsTimelineInline);
                if (_nodes.TryGetValue(owner, out var card))
                    GUILayout.Label(card.extensionContainer.Q<Label>("MainAnimationProvenance")?.text, EditorStyles.wordWrappedMiniLabel);
                if(main==null)continue;
                string timelineId=current.Id;var occurrence=current.Events.Single(e=>e.EntryNodeId==main);string entry=occurrence.Id;
                GUILayout.Space(8);GUILayout.Label("主动画设置",EditorStyles.boldLabel);
                if(GUILayout.Button(new GUIContent("预览输入…","调整预览等级、变体采样和临时数值输入。")))
                    PreviewRequested?.Invoke(owner);
                using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode||GameplaySkillAuthoringCommands.IsReadOnly(_document))){
                float frame=EditorGUILayout.DelayedFloatField("主动画开始（帧）",occurrence.TimeSeconds*current.FramesPerSecond);
                if(frame!=occurrence.TimeSeconds*current.FramesPerSecond)Try(()=>GameplaySkillAuthoringCommands.MoveEvent(_document,entry,frame/current.FramesPerSecond));
                if(GameplaySkillFieldSchema.TryDuration(_document.AuthoringGraph,main,out _,out _,out float duration)){
                    EditorGUILayout.LabelField("动画 / 编排长度",duration.ToString("0.###")+"s / "+current.DurationSeconds.ToString("0.###")+"s");
                    string followKey=FollowKey(_document,timelineId);bool follow=SessionState.GetString(followKey,"")==main;
                    bool next=EditorGUILayout.Toggle(new GUIContent("本次会话跟随主动画","修改Clip、速度或偏移时更新真实编排总长；其它事件与End时点保持。关闭后可独立编辑总长。"),follow);
                    if(next!=follow){SessionState.SetString(followKey,next?main:"");if(next)Try(()=>GameplaySkillAuthoringCommands.Transaction(_document,"编排跟随主动画",g=>{}));}
                    if(GUILayout.Button(new GUIContent("编排时长覆盖动画","其它事件与 End 时点不变")))Try(()=>GameplaySkillAuthoringCommands.Transaction(_document,"编排长度覆盖主动画",g=>{
                        var t=g.Timelines.Single(x=>x.Id==timelineId);t.DurationSeconds=Mathf.Max(t.Events.Max(e=>e.TimeSeconds),occurrence.TimeSeconds+duration);
                    }));
                }
                if(GUILayout.Button("更换主动画后端…"))ShowMainAnimationMenu(main,current);
                if(GUILayout.Button("移除主动画调度 · 保留动画行为")){
                    Try(()=>GameplaySkillAuthoringCommands.Transaction(_document,"移除主动画调度，保留动画行为",g=>g.Timelines.Single(t=>t.Id==timelineId).Events.RemoveAll(e=>e.Id==entry)));
                    GUIUtility.ExitGUI();
                }
                var graph=_document.AuthoringGraph;
                var liveTimeline=graph.Timelines.Single(t=>t.Id==timelineId);
                var liveEvent=liveTimeline.Events.Single(e=>e.Id==entry);
                _cardSerialized.UpdateIfRequiredOrScript();
                var entryProperty=_cardSerialized.FindProperty("authoringGraph.Timelines")
                    .GetArrayElementAtIndex(graph.Timelines.IndexOf(liveTimeline))
                    .FindPropertyRelative("Events").GetArrayElementAtIndex(liveTimeline.Events.IndexOf(liveEvent));
                GUILayout.Label("调度条件 / 目标覆盖会将动画显示为独立事件。",EditorStyles.wordWrappedMiniLabel);
                var paths=new System.Collections.Generic.HashSet<string>();
                GameplaySkillWorkbench.DrawScheduleOptions(entryProperty,paths);
                if(paths.Count>0)GameplaySkillFieldSchema.ApplyFields(_cardSerialized,_document,false,paths);
                }
                using(GameplayAuthoringInspector.UseFieldContext("Inspector"))GameplaySkillFieldSchema.Draw(_cardSerialized,_document,main,null,true,true);
            }
        }
    }
}
