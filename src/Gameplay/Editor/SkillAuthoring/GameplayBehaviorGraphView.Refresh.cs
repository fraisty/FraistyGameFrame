using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.Experimental.GraphView;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    public sealed partial class GameplayBehaviorGraphView
    {
        private readonly Dictionary<string,int> _nodeIndexes=new Dictionary<string,int>();
        private readonly Dictionary<string,HashSet<string>> _consumerCards=new Dictionary<string,HashSet<string>>();
        private readonly Dictionary<string,List<Port>> _portsByCard=new Dictionary<string,List<Port>>();
        private readonly HashSet<UnityEngine.Object> _externalSources=new HashSet<UnityEngine.Object>();
        private readonly HashSet<string> _externalDependencyGuids=new HashSet<string>();
        private Dictionary<string,GameplayGraphContextPolicy.Requirement> _contextContracts=new Dictionary<string,GameplayGraphContextPolicy.Requirement>();
        private Func<string,string,string,string,bool,string> _connectionChecker;
        public bool UsesExternal(UnityEngine.Object source){
            if(!source||source==_document)return false;if(_externalSources.Contains(source))return true;if(_externalDependencyGuids.Count==0)return false;
            string path=UnityEditor.AssetDatabase.GetAssetPath(source);return !string.IsNullOrEmpty(path)&&_externalDependencyGuids.Contains(UnityEditor.AssetDatabase.AssetPathToGUID(path));
        }
        // Derived view dependencies only. Rebuilt with topology, never used as execution data.
        private void IndexPresentation()
        {
            _nodeIndexes.Clear();_consumerCards.Clear();_externalSources.Clear();_externalDependencyGuids.Clear();_connectionChecker=null;
            if(!_document)return;var graph=_document.AuthoringGraph;
            var definitions=graph.LocalDefinitions.ToDictionary(d=>d.Id);var timelines=graph.Timelines.ToDictionary(t=>t.Id);
            for(int i=0;i<graph.Nodes.Count;i++){
                var node=graph.Nodes[i];_nodeIndexes[node.Id]=i;
                string card=node.Id==graph.PrimaryRuleNodeId&&InlinePrimaryRule(_document)?graph.AbilityNodeId:node.Id;
                var seen=new HashSet<string>();Visit(node.Id,node.Payload.Definitions);
                void Visit(string owner,IEnumerable<GameplayDefinitionBinding> bindings){
                    if(string.IsNullOrEmpty(owner)||!seen.Add(owner))return;
                    if(!_consumerCards.TryGetValue(owner,out var cards))_consumerCards[owner]=cards=new HashSet<string>();cards.Add(card);
                    foreach(var b in bindings){if(b.External)_externalSources.Add(b.External);if(string.IsNullOrEmpty(b.LocalId))continue;
                        if(definitions.TryGetValue(b.LocalId,out var local))Visit(local.Id,local.Definitions);
                        if(timelines.TryGetValue(b.LocalId,out var timeline)){Visit(timeline.Id,Array.Empty<GameplayDefinitionBinding>());foreach(var e in timeline.Events)Visit(e.EntryNodeId,graph.Nodes.Find(n=>n.Id==e.EntryNodeId)?.Payload.Definitions??new List<GameplayDefinitionBinding>());}
                    }
                }
            }
            foreach(var source in _externalSources){string path=UnityEditor.AssetDatabase.GetAssetPath(source);if(!string.IsNullOrEmpty(path))_externalDependencyGuids.UnionWith(UnityEditor.AssetDatabase.GetDependencies(path,true).Select(UnityEditor.AssetDatabase.AssetPathToGUID).Where(guid=>!string.IsNullOrEmpty(guid)));}
        }
        private GameplayAuthoringNode Record(string id)
        {
            if(!_document||!_nodeIndexes.TryGetValue(id,out int at))return null;
            var nodes=_document.AuthoringGraph.Nodes;
            return at<nodes.Count&&nodes[at].Id==id?nodes[at]:null;
        }
        /// <summary>Only changed owners and their shared/Timeline consumers are recomputed.
        /// Context analysis is cached until a semantic edit; changed contracts alone touch ports.</summary>
        public void RefreshPresentation(GameplaySkillAuthoringChange change)
        {
            if(!_document)return;
            bool full=change==null||change.Unknown;
            if(full)IndexPresentation();
            var affected=new HashSet<string>();
            if(full)affected.UnionWith(_nodes.Keys);
            else foreach(string owner in change.Owners){if(_consumerCards.TryGetValue(owner,out var cards))affected.UnionWith(cards);if(_nodes.ContainsKey(owner))affected.Add(owner);}
            long sample=System.Diagnostics.Stopwatch.GetTimestamp();
            using(new Unity.Profiling.ProfilerMarker("FRFrame.Workbench.Summaries").Auto())try{
                foreach(string id in affected){var data=Record(id);if(data==null||!_nodes.TryGetValue(id,out var node))continue;NodePresentationUpdateCount++;
                    if(change?.Layout==true){var position=_layoutPreview!=null&&_layoutPreview.TryGetValue(id,out var projected)?projected:data.Position;if(node.GetPosition().position!=position)node.SetPosition(new Rect(position,node.GetPosition().size));if(HasTimeline(data)){float width=TimelineWidth(data);node.style.width=width;node.style.minWidth=width;node.style.maxWidth=width;}}
                    string title=GameplaySkillNodeDisplay.Name(data,_document.AuthoringGraph);if(node.title!=title)node.title=title;
                    var editors=node.extensionContainer.Query<GameplaySkillCardEditor>().ToList();foreach(var editor in editors)editor.Refresh();
                    Set("BusinessSummary",GameplaySkillNodeDisplay.CardSummary(data,_document.AuthoringGraph,editors.Any(e=>e.HasFields)));
                    if(data.Payload is GameplayAbilityNodePayload)Set("AbilitySummary",_document.Mode==FRFrame.Gameplay.Standard.GameplayStandardActivationMode.Active?"主动技能":"被动技能");
                    foreach(var binding in data.Payload.Definitions){var definition=_document.AuthoringGraph.LocalDefinitions.Find(d=>d.Id==binding.LocalId);if(definition!=null)Set("Definition-"+definition.Id,DefinitionSummary(definition));}
                    foreach(var label in node.extensionContainer.Query<Label>(className:"animation-source").ToList()){var animation=Record((string)label.userData);if(animation==null)continue;object values=GameplaySkillNodeDisplay.AnimationValues(animation,_document.AuthoringGraph);string text=IsTimelineInline(animation.Id)?(values==null?"动画资源未配置":GameplaySkillFieldSchema.Name(values.GetType()))+" · "+(animation.Payload.Definitions.Any(b=>b.External)?"共享，只读":"本地"):GameplaySkillNodeDisplay.EventLabel(animation,_document.AuthoringGraph)+" · 参数在独立动画卡";if(label.text!=text)label.text=text;}
                    foreach(var timeline in _document.AuthoringGraph.Timelines.Where(t=>data.Payload.Definitions.Any(b=>b.LocalId==t.Id))){var entries=timeline.Events.Where(e=>!IsTimelineInline(e.EntryNodeId)).OrderBy(e=>e.TimeSeconds).ThenBy(e=>e.Order).ToArray();for(int i=0;i<entries.Length;i++)if(_outputs.TryGetValue(Key(entries[i].Id,"Schedule"),out var port)){string caption="#"+(i+1)+" "+(entries[i].TimeSeconds*timeline.FramesPerSecond).ToString("0.#")+"f";if(port.portName!=caption){port.tooltip="◇ 真实时间调度 · "+caption+"\n事件 "+entries[i].Id+"\n单击编号定位；Order、目标与条件见该事件。";}}}
                    foreach(var timelineView in node.Query<GameplaySkillTimeline>().ToList())timelineView.InvalidatePresentation();
                    node.MarkDirtyRepaint();
                    void Set(string name,string value){var label=node.extensionContainer.Q<Label>(name);if(label!=null){if(label.text!=value)label.text=value;label.style.display=string.IsNullOrEmpty(value)?DisplayStyle.None:DisplayStyle.Flex;}}
                }
                if(full||affected.Contains(_document.AuthoringGraph.AbilityNodeId))foreach(var policy in _policyCards.Values)foreach(var editor in policy.Query<GameplaySkillCardEditor>().ToList())editor.Refresh();
                if(full||change.Context){_connectionChecker=null;RefreshPortPolicy(full?null:affected);RefreshContextContracts();}
            }finally{SummaryRefreshMilliseconds+=(System.Diagnostics.Stopwatch.GetTimestamp()-sample)*1000d/System.Diagnostics.Stopwatch.Frequency;}
        }
    }
}
