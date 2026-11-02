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
            var sourceDocument=_document;string main=timeline.Events.Select(e=>e.EntryNodeId).FirstOrDefault(IsTimelineInline);
            card.extensionContainer.Add(new Button(()=>GameplaySkillCreationMenu.Show(this,"设置主动画 · 使用真实后端",menu=>{
                foreach(var type in GameplayAuthoringTypeCatalog.Query(typeof(GameplaySkillActionAuthoring),_document.AuthoringGraph.Scope).Where(t=>GameplaySkillNodeDescriptors.Get(t).IsAnimation)){
                    var action=(GameplaySkillActionAuthoring)Activator.CreateInstance(type);
                    var slot=GameplayGraphFields.ResourceSlots(action).FirstOrDefault();if(slot==null)continue;
                    foreach(var valueType in GameplaySkillFieldSchema.Values(slot.ValueType)){
                        var actionType=type;var value=valueType;string path=slot.Path;
                        var label=new GUIContent(GameplaySkillNodeDescriptors.Get(type).DisplayName+"/"+GameplaySkillFieldSchema.Name(valueType));
                        string reason=GameplaySkillTypeAvailability.Reason(_document,type);
                        if(reason!=null){label.tooltip=reason;menu.AddDisabledItem(label);continue;}
                        menu.AddItem(label,false,()=>Try(()=>{
                            if(main==null)GameplaySkillAuthoringCommands.CreateAction(_document,(GameplaySkillActionAuthoring)Activator.CreateInstance(actionType),"主动画",card.GetPosition().position+new Vector2(0,-160),(GameplayLocalDefinitionPayload)Activator.CreateInstance(value),path,timeline.Id,0);
                            else GameplaySkillAuthoringCommands.Transaction(_document,"更换主动画后端",g=>{
                                var node=g.Nodes.Single(n=>n.Id==main);var old=node.Payload.Definitions.Select(b=>b.LocalId).ToArray();
                                var local=new GameplayLocalDefinition{Name="主动画",SourceDefinitionId=_document.DefinitionId,Payload=(GameplayLocalDefinitionPayload)Activator.CreateInstance(value)};
                                node.Payload=new GameplayActionNodePayload{Action=(GameplaySkillActionAuthoring)Activator.CreateInstance(actionType)};
                                node.Payload.Definitions.Add(new GameplayDefinitionBinding{PropertyPath=path,LocalId=local.Id});g.LocalDefinitions.Add(local);GameplaySkillAuthoringCommands.CollectReleased(g,old);
                            });
                            schedule.Execute(()=>{if(_document==sourceDocument&&BusinessReading){ReflowReading();FocusNode(owner);}}).StartingIn(250);
                        }));
                    }
                }
            })){text=main==null?"＋ 设置主动画（可选；无动画也能执行）":"更换主动画后端…",tooltip="主动画使用真实 Animation Action 和唯一 Schedule，显示在卡头，不重复普通事件行。"});
            if(main==null)return;
            string entry=timeline.Events.Single(e=>e.EntryNodeId==main).Id;
            card.extensionContainer.Add(new IMGUIContainer(()=>{
                var current=_document.AuthoringGraph.Timelines.Find(t=>t.Id==timeline.Id);var occurrence=current?.Events.Find(e=>e.Id==entry);if(occurrence==null)return;
                float frame=EditorGUILayout.DelayedFloatField("主动画开始（帧）",occurrence.TimeSeconds*current.FramesPerSecond);
                if(frame!=occurrence.TimeSeconds*current.FramesPerSecond)Try(()=>GameplaySkillAuthoringCommands.MoveEvent(_document,entry,frame/current.FramesPerSecond));
                if(GameplaySkillFieldSchema.TryDuration(_document.AuthoringGraph,main,out _,out _,out float duration)){
                    EditorGUILayout.LabelField("动画 / 编排长度",duration.ToString("0.###")+"s / "+current.DurationSeconds.ToString("0.###")+"s");
                    string followKey=FollowKey(_document,timeline.Id);bool follow=SessionState.GetString(followKey,"")==main;
                    bool next=EditorGUILayout.Toggle(new GUIContent("本次会话跟随主动画","修改Clip、速度或偏移时更新真实编排总长；其它事件与End时点保持。关闭后可独立编辑总长。"),follow);
                    if(next!=follow){SessionState.SetString(followKey,next?main:"");if(next)Try(()=>GameplaySkillAuthoringCommands.Transaction(_document,"编排跟随主动画",g=>{}));}
                    if(GUILayout.Button(new GUIContent("编排时长覆盖动画","其它事件与 End 时点不变")))Try(()=>GameplaySkillAuthoringCommands.Transaction(_document,"编排长度覆盖主动画",g=>{
                        var t=g.Timelines.Single(x=>x.Id==timeline.Id);t.DurationSeconds=Mathf.Max(t.Events.Max(e=>e.TimeSeconds),occurrence.TimeSeconds+duration);
                    }));
                }
            }){style={minWidth=0}});
            card.extensionContainer.Add(new Button(()=>Try(()=>GameplaySkillAuthoringCommands.Transaction(_document,"移除主动画调度，保留行为草稿",g=>g.Timelines.Single(t=>t.Id==timeline.Id).Events.RemoveAll(e=>e.Id==entry)))){text="移除主动画调度 · 保留行为草稿"});
        }
    }
}
