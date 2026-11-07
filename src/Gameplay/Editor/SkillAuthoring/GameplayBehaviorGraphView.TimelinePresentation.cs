using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using Node=UnityEditor.Experimental.GraphView.Node;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    public sealed partial class GameplayBehaviorGraphView
    {
        // Only the view is compacted. The same stable action, event, binding and position remain
        // in the document; shared, branched or explicitly revealed behavior always retains a card.
        private readonly Dictionary<string,string> _compactOwners=new Dictionary<string,string>();
        private readonly Dictionary<string,string> _inlineOwners=new Dictionary<string,string>();
        private string _selectedInlineAction;
        private Dictionary<string,Vector2> _layoutPreview;
        private Vector3 _layoutPan,_layoutZoom;
        /// <summary>Whether dragging edits only the session reading projection, including before its first layout frame.</summary>
        public bool BusinessReading {get;private set;}
        /// <summary>Notifies the workbench view selector without changing authoring data.</summary>
        public Action<bool> ViewModeChanged;
        private bool _explicitLayoutPreview;
        private string _focusAfterLayout,_readableFocusId;
        [Serializable] private sealed class ReadingLayout {public List<string> Ids;public List<Vector2> Positions;}
        private string CameraKey=>GameplaySkillEditorState.Key(_document)+(BusinessReading?".Reading":"");
        /// <summary>Switches only the editor projection. Business-view dragging stays in
        /// SessionState; fields and typed connections still edit the one source document.</summary>
        public void SetBusinessReading(bool reading)
        {
            if(!_document||BusinessReading==reading)return;
            SaveViewState();_layoutPreview=null;_explicitLayoutPreview=false;BusinessReading=reading;
            SessionState.SetString(GameplaySkillEditorState.Key(_document)+".ViewMode",reading?"Reading":"Author");
            if(reading)RestoreReadingLayout();else SynchronizeLayout();
            RestoreCamera();ViewModeChanged?.Invoke(reading);
            Feedback?.Invoke(reading?"业务阅读 · 拖动只保存会话排布；参数与连接仍编辑正式源。":"作者布局 · 保留源坐标；移动节点使用正常 Undo。");
        }
        private void RestoreReadingLayout()
        {
            if(!BusinessReading)return; // A queued reading-layout pass cannot overwrite a later switch to author layout.
            // Before the first panel layout GraphView reports NaN dimensions, not just zero.
            if(_nodes.Values.Any(n=>!float.IsFinite(n.GetPosition().width)||!float.IsFinite(n.GetPosition().height)||n.GetPosition().height<=1)){
                var source=_document;int generation=RefreshCount;schedule.Execute(()=>{if(_document==source&&RefreshCount==generation&&panel!=null)RestoreReadingLayout();}).StartingIn(50);return;
            }
            string json=SessionState.GetString(GameplaySkillEditorState.Key(_document)+".ReadingLayout","");
            var saved=json.Length==0?null:JsonUtility.FromJson<ReadingLayout>(json);
            if(saved!=null&&saved.Ids.Count==saved.Positions.Count&&saved.Positions.All(p=>float.IsFinite(p.x)&&float.IsFinite(p.y))){
                // Topology edits do not request a reflow. Retain existing view positions and
                // place newly visible/created cards at their actual authoring coordinates.
                var remembered=saved.Ids.Select((id,i)=>new{id,position=saved.Positions[i]}).ToDictionary(p=>p.id,p=>p.position);
                _layoutPreview=_nodes.Keys.ToDictionary(id=>id,id=>remembered.TryGetValue(id,out var position)?position:Record(id).Position);
                foreach(var p in _layoutPreview){var card=_nodes[p.Key];card.SetPosition(new Rect(p.Value,card.GetPosition().size));}
            }else ArrangeLayout();
            PositionPolicies();
            if(_focusAfterLayout!=null){string focus=_focusAfterLayout;_focusAfterLayout=null;FocusNode(focus);}
        }
        private void RestoreViewMode(bool switched)
        {
            if(switched){
                string key=GameplaySkillEditorState.Key(_document);
                // Only an explicit ViewMode is a preference; old author Pan/Zoom stays available.
                BusinessReading=SessionState.GetString(key+".ViewMode","Reading")=="Reading";
            }
            _explicitLayoutPreview=false;ViewModeChanged?.Invoke(BusinessReading);
            if(BusinessReading)RestoreReadingLayout();
            if(switched){var source=_document;int generation=RefreshCount;schedule.Execute(()=>{if(_document==source&&RefreshCount==generation&&_readableFocusId==null&&_focusAfterLayout==null)RestoreCamera();}).StartingIn(120);}
        }
        private float TimelineWidth(GameplayAuthoringNode data)
        {
            if(data.Size.x>0)return Mathf.Max(440,data.Size.x);
            var graph=_document.AuthoringGraph;
            bool animated=graph.Timelines.Where(t=>data.Payload.Definitions.Any(b=>b.LocalId==t.Id)).SelectMany(t=>t.Events)
                .Any(e=>graph.Nodes.Find(n=>n.Id==e.EntryNodeId)?.Payload is GameplayActionNodePayload p&&GameplaySkillNodeDescriptors.Get(p.Action.GetType()).IsAnimation);
            return animated?800:Mathf.Clamp(layout.width>0?layout.width-70:820,480,860);
        }

        /// <summary>Shows a derived arrangement without writing Node.Position. Only the explicit
        /// Apply action persists one Undo transaction; cancel restores the authored layout.</summary>
        public void PreviewLayout()
        {
            if(!_document)return;
            if(BusinessReading){Feedback?.Invoke("业务阅读不需应用；要整理持久坐标请先切换作者布局。");return;}
            if(_layoutPreview==null){_layoutPan=viewTransform.position;_layoutZoom=viewTransform.scale;}
            _explicitLayoutPreview=true;ArrangeLayout();FocusMainFlow();
            Feedback?.Invoke("作者布局预览未写入；应用后一次 Undo 可恢复，取消还原原视图。");
        }
        private void ArrangeLayout()
        {
            // Breadth-first ranks are finite even with back edges. They organize the view,
            // never imply new execution order; actual typed edges remain the authority.
            var links=edges.Where(e=>(e.userData as Socket)?.Config==null&&e.input?.node!=null&&e.output?.node!=null).Select(e=>(from:e.output.node.viewDataKey,to:e.input.node.viewDataKey)).Distinct().ToArray();
            var ranks=new Dictionary<string,int>();var queue=new Queue<string>();
            string root=_document.AuthoringGraph.AbilityNodeId;
            if(_nodes.ContainsKey(root)){ranks[root]=0;queue.Enqueue(root);}
            while(queue.Count>0){string id=queue.Dequeue();foreach(var link in links.Where(e=>e.from==id))if(!ranks.ContainsKey(link.to)){ranks[link.to]=ranks[id]+1;queue.Enqueue(link.to);}}
            foreach(string id in _nodes.Keys)if(!ranks.ContainsKey(id))ranks[id]=1;
            // A Search's immediate Cue can share its visual business row; its real Then edge
            // still identifies the dependency instead of suggesting a second scheduled event.
            foreach(var link in links){var from=Record(link.from);var to=Record(link.to);if((from?.Payload as GameplayActionNodePayload)?.Action is GameplaySearchTargetActionAuthoring&&
                (to?.Payload as GameplayActionNodePayload)?.Action is GameplaySkillActionAuthoring action&&GameplaySkillNodeDescriptors.Get(action.GetType()).VisualKind==GameplayNodeVisualKind.Presentation&&!GameplaySkillNodeDescriptors.Get(action.GetType()).IsAnimation)ranks[link.to]=ranks[link.from];}
            int terminal=ranks.Values.DefaultIfEmpty(0).Max()+1;
            foreach(string id in _nodes.Keys)if((Record(id)?.Payload as GameplayActionNodePayload)?.Action is GameplayEndAbilityActionAuthoring)ranks[id]=terminal;
            _layoutPreview=new Dictionary<string,Vector2>();float y=40;
            foreach(var row in ranks.GroupBy(p=>p.Value).OrderBy(g=>g.Key)){
                var cards=row.OrderBy(p=>Record(p.Key)?.Payload is GameplayActionNodePayload action&&GameplaySkillNodeDescriptors.Get(action.Action.GetType()).VisualKind==GameplayNodeVisualKind.Presentation?1:0).ThenBy(p=>Record(p.Key)?.Payload is GameplayActionNodePayload action&&GameplaySkillNodeDescriptors.Get(action.Action.GetType()).IsAnimation?1:0).ThenBy(p=>_nodes[p.Key].GetPosition().x).Select(p=>_nodes[p.Key]).ToArray();
                for(int first=0;first<cards.Length;first+=2){
                    var line=cards.Skip(first).Take(2).ToArray();float width=line.Sum(n=>n.GetPosition().width)+80*(line.Length-1);float x=40+Mathf.Max(0,(720-width)*.5f),height=0;
                    foreach(var card in line){var rect=card.GetPosition();var pos=new Vector2(x,y);_layoutPreview[card.viewDataKey]=pos;card.SetPosition(new Rect(pos,rect.size));x+=rect.width+80;height=Mathf.Max(height,rect.height);}y+=height+65;
                }
            }
            PositionPolicies();
        }
        /// <summary>Explicit session-only reflow after editing; authored Node.Position remains unchanged.</summary>
        public void ReflowReading(){if(!BusinessReading)return;ArrangeLayout();SaveViewState();FocusMainFlow();}
        /// <summary>Readable camera navigation over current view positions. It never moves data.</summary>
        public void FocusMainFlow()
        {
            if(!_document||_nodes.Count==0)return;
            var first=_nodes.TryGetValue(_document.AuthoringGraph.AbilityNodeId,out var root)?root:_nodes.Values.First();
            var rect=first.GetPosition();if(_layoutPreview!=null&&_layoutPreview.TryGetValue(first.viewDataKey,out var projected))rect.position=projected;float zoom=Mathf.Min(1,Mathf.Max(.85f,(layout.width-60)/Mathf.Max(320,rect.width)));
            float left=_layoutPreview!=null?_layoutPreview.Values.Min(p=>p.x):edges.Where(e=>e.output?.node==first&&e.input?.node!=null).Select(e=>e.input.node.GetPosition().x).Append(rect.x).Min();
            PositionPolicies();
            left=Mathf.Min(left,_policyCards.Values.Select(n=>n.style.left.value.value).DefaultIfEmpty(left).Min());
            float top=Mathf.Min(rect.y,_policyCards.Values.Select(n=>n.style.top.value.value).DefaultIfEmpty(rect.y).Min());
            UpdateViewTransform(new Vector3(35-left*zoom,30-top*zoom,0),Vector3.one*zoom);
        }
        public void FocusSelectionReadable(){var card=selection.OfType<Node>().FirstOrDefault();if(card==null){FocusMainFlow();return;}if(card.viewDataKey.StartsWith("Config:",StringComparison.Ordinal))FocusReadable(card);else FocusNode(card.viewDataKey);}
        private void FocusReadable(Node card){_readableFocusId=card.viewDataKey;var rect=card.GetPosition();if(_layoutPreview!=null&&_layoutPreview.TryGetValue(card.viewDataKey,out var position))rect.position=position;float zoom=Mathf.Clamp((layout.width-50)/Mathf.Max(320,rect.width),.4f,1);UpdateViewTransform(new Vector3(Mathf.Max(25,(layout.width-rect.width*zoom)*.5f)-rect.x*zoom,35-rect.y*zoom,0),Vector3.one*zoom);}
        public void ApplyLayoutPreview(){if(!_explicitLayoutPreview||_layoutPreview==null)return;var positions=_layoutPreview;_layoutPreview=null;_explicitLayoutPreview=false;Try(()=>GameplaySkillAuthoringCommands.MoveNodes(_document,positions));}
        public void CancelLayoutPreview(){if(!_explicitLayoutPreview)return;_layoutPreview=null;_explicitLayoutPreview=false;SynchronizeLayout();UpdateViewTransform(_layoutPan,_layoutZoom);}
        /// <summary>The exact scheduled occurrence selected by the Timeline, for its Inspector.</summary>
        public string SelectedEventId {get;private set;}
        public void ClearEventSelection(){SelectedEventId=null;}
        public void SetPreviewCursor(string owner,float seconds){if(!_document)return;var node=_document.AuthoringGraph.Nodes.Find(n=>n.Id==owner);if(node==null)return;foreach(var binding in node.Payload.Definitions)if(_timelineStates.TryGetValue(_document.GetInstanceID()+":"+binding.LocalId,out var state))state.Cursor=seconds;}
        internal float? PreviewCursor(string owner){var node=_document.AuthoringGraph.Nodes.Find(n=>n.Id==owner);if(node!=null)foreach(var binding in node.Payload.Definitions)if(_timelineStates.TryGetValue(_document.GetInstanceID()+":"+binding.LocalId,out var state))return state.Cursor;return null;}
        /// <summary>Whether an eligible leaf animation is currently edited inside its owning Timeline.</summary>
        public bool IsTimelineInline(string id)=>id!=null&&_inlineOwners.ContainsKey(id);
        private void BuildTimelinePresentation(GameplayAuthoringGraph graph)
        {
            _compactOwners.Clear();_inlineOwners.Clear();
            var uses=graph.Timelines.SelectMany(t=>t.Events.Select(e=>new{timeline=t,entry=e})).Where(x=>!string.IsNullOrEmpty(x.entry.EntryNodeId)).GroupBy(x=>x.entry.EntryNodeId).ToDictionary(g=>g.Key,g=>g.ToArray());
            foreach(var node in graph.Nodes){
                var action=(node.Payload as GameplayActionNodePayload)?.Action;if(action==null)continue;
                var descriptor=GameplaySkillNodeDescriptors.Get(action.GetType());
                if(!descriptor.IsAnimation)continue; // End remains a real terminal card in the business flow.
                if(!uses.TryGetValue(node.Id,out var calls)||calls.Length!=1||graph.Connections.Any(e=>e.FromNodeId==node.Id||e.ToNodeId==node.Id))continue;
                if(calls[0].entry.Conditions.Length>0||!(calls[0].entry.Target is GameplaySkillContextTargetAuthoring))continue;
                string timeline=calls[0].timeline.Id;var owners=graph.Nodes.Where(n=>n.Payload.Definitions.Any(b=>b.LocalId==timeline)).ToArray();if(owners.Length!=1||owners[0].Id==node.Id)continue;
                var resources=new HashSet<string>();Action<string> visit=null;visit=id=>{if(string.IsNullOrEmpty(id)||!resources.Add(id))return;var local=graph.LocalDefinitions.Find(d=>d.Id==id);if(local!=null)foreach(var binding in local.Definitions)visit(binding.LocalId);};
                foreach(var binding in node.Payload.Definitions)visit(binding.LocalId);
                if(graph.Connections.Any(e=>resources.Contains(e.FromNodeId)))continue;
                if(_inlineOwners.Values.Contains(owners[0].Id))continue;
                _compactOwners[node.Id]=owners[0].Id;
                if(!SessionState.GetBool(GameplaySkillEditorState.Key(_document)+".Standalone."+node.Id,false))_inlineOwners[node.Id]=owners[0].Id;
            }
        }
        private IEnumerable<string> SelectedBehaviorIds(IEnumerable<Node> cards)
        {
            foreach(var card in cards.Where(c=>!c.viewDataKey.StartsWith("Config:",StringComparison.Ordinal)))yield return _selectedInlineAction!=null&&_inlineOwners.TryGetValue(_selectedInlineAction,out var owner)&&card.viewDataKey==owner?_selectedInlineAction:card.viewDataKey;
        }
        private void SelectScheduledEntry(string eventId,string ownerId=null)
        {
            var timeline=_document.AuthoringGraph.Timelines.FirstOrDefault(t=>t.Events.Any(e=>e.Id==eventId));var entry=timeline?.Events.FirstOrDefault(e=>e.Id==eventId);if(entry==null)return;
            SelectedEventId=eventId;
            ownerId=ownerId??_document.AuthoringGraph.Nodes.FirstOrDefault(n=>n.Payload.Definitions.Any(b=>b.LocalId==timeline.Id))?.Id;
            if(ownerId!=null)PreviewSeeked?.Invoke(ownerId,eventId,entry.TimeSeconds);
            if(_timelineStates.TryGetValue(_document.GetInstanceID()+":"+timeline.Id,out var state)){state.SelectedEventId=eventId;state.Save();}
            SelectNodeWithoutCamera(entry.EntryNodeId);HighlightBehavior(entry.EntryNodeId);Selected?.Invoke(entry.EntryNodeId);MarkDirtyRepaint();
            // IMGUI event rows can leave keyboard focus in the previous Inspector container.
            // Return it to the selected behavior so native Copy/Paste/Delete reach GraphView.
            schedule.Execute(()=>{if(panel!=null)Focus();});
        }
        private void HighlightBehavior(string id)
        {
            foreach(var timeline in _document.AuthoringGraph.Timelines)
                if(_timelineStates.TryGetValue(_document.GetInstanceID()+":"+timeline.Id,out var state)){state.SelectedNodeId=id;state.SelectedEventId=timeline.Events.Any(e=>e.Id==SelectedEventId&&e.EntryNodeId==id)?SelectedEventId:null;}
            foreach(var wire in edges){var socket=wire.userData as Socket;bool related=wire.input?.node?.viewDataKey==id||wire.output?.node?.viewDataKey==id||socket?.Scheduled==true&&_document.AuthoringGraph.Timelines.SelectMany(t=>t.Events).Any(e=>e.Id==socket.Owner&&e.EntryNodeId==id);wire.edgeControl.edgeWidth=related?4:2;}
        }
        /// <summary>Explicit advanced view action. Reveals the existing behavior without creating
        /// a node, execution edge, SO, Undo record or dirty source.</summary>
        public void RevealTimelineAction(string id)
        {
            if(!_compactOwners.ContainsKey(id)){Selected?.Invoke(id);FocusNode(id);return;}
            SessionState.SetBool(GameplaySkillEditorState.Key(_document)+".Standalone."+id,true);Load(_document);Selected?.Invoke(id);FocusNode(id);
        }
        private void CompactTimelineAction(string id)
        {
            if(!_compactOwners.ContainsKey(id))return;
            SessionState.SetBool(GameplaySkillEditorState.Key(_document)+".Standalone."+id,false);Load(_document);SelectNodeWithoutCamera(id);Selected?.Invoke(id);
        }
        private void ResourcePort(Node card,GameplayLocalDefinition definition,GameplayDefinitionBinding binding,string key)
        {
            var port=AddPort(card,definition.Id,key);string role=binding.PropertyPath=="CooldownEffect"?"冷却":definition.Payload is GameplayProjectileDefinitionData?"弹体":definition.Payload is GameplayAreaDefinitionData?"区域":definition.Payload is GameplayEffectDefinitionData?"效果":null;
            if(role=="冷却"&&(key==GameplayGraphEffectCallbacks.Completion||key.StartsWith("Completion/",StringComparison.Ordinal)))port.portName=key==GameplayGraphEffectCallbacks.Completion?"冷却到期 · 添加效果":"冷却到期 · 续接效果";
            else if(role!=null&&role!="效果")port.portName=role+" · "+port.portName;
            if(GameplayGraphEffectCallbacks.Slots(definition.Payload).Any(s=>s.Key==key)){
                var slots=GameplayGraphEffectCallbacks.Slots(definition.Payload).Where(s=>s.IsCompletion==key.StartsWith("Completion/",StringComparison.Ordinal)).ToList();
                port.portName=(role=="冷却"?"冷却到期":key.StartsWith("Completion/",StringComparison.Ordinal)?"到期":"溢出")+" → 效果 "+(slots.FindIndex(s=>s.Key==key)+1);
                if(!_document.AuthoringGraph.Connections.Any(e=>e.FromNodeId==definition.Id&&e.OutputPortKey==key))port.portName+=" · 待接";
            }else if(key==GameplayGraphEffectCallbacks.Completion||key==GameplayGraphEffectCallbacks.Overflow)
                port.portName=role=="冷却"?"＋ 冷却到期":key==GameplayGraphEffectCallbacks.Completion?"＋ 到期效果":"＋ 溢出效果";
            port.tooltip=port.portName+"\n"+port.tooltip;
        }
    }
}
