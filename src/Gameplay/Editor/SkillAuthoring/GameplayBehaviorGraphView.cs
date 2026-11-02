using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using Node = UnityEditor.Experimental.GraphView.Node;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Direct view of the document's explicit execution edges. It performs no asset
    /// discovery, runtime simulation, or dependency projection during pointer/repaint events.</summary>
    public sealed partial class GameplayBehaviorGraphView : GraphView
    {
        public sealed class Socket
        {
            public string Owner, Port, Config; public bool Scheduled;
        }
        private GameplaySkillDocumentAsset _document;
        private SerializedObject _cardSerialized;
        // The canvas owns this buffer through Load/Detach; the adjacent Inspector borrows it.
        internal SerializedObject SerializedSource => _cardSerialized;
        private readonly Dictionary<string, Node> _nodes = new Dictionary<string, Node>();
        private readonly Dictionary<string, Port> _outputs = new Dictionary<string, Port>();
        private readonly Dictionary<string, Port> _inputs = new Dictionary<string, Port>();
        private readonly Dictionary<string,GameplaySkillTimeline.ViewState> _timelineStates=new Dictionary<string,GameplaySkillTimeline.ViewState>();
        private bool _loading;
        public bool ShowDrafts { get; set; }
        public Action<string> Selected;
        public Action<string> Feedback;
        public Action<string, float> AddTimed;
        public Action<IGameplaySkillCreationMenu, Vector2, string, float?> Populate;
        public Action DeleteMain;
        internal GameplaySkillAuthoringCommands.CreationLink CreationLink {get;private set;}
        public Action<string> PreviewRequested;
        public Func<string,bool> HasPreview;
        public Action<string> DrawInlinePreview;
        public Action<string,bool> InlinePreviewChanged;
        public Action<string,string,float> PreviewSeeked;
        public Func<string,bool> PreviewPlaying;
        public Action<string> TogglePreview;
        private string _inlinePreview;
        public bool HasInlinePreview(string owner)=>owner!=null&&_inlineViews.ContainsKey(owner);
        public float PreviewWidth(string owner)=>_inlineViews.TryGetValue(owner,out var view)&&view.layout.width>0?view.layout.width:200;
        private readonly Dictionary<string,IMGUIContainer> _inlineViews=new Dictionary<string,IMGUIContainer>();
        public Vector2 CreationPosition { get; private set; } = new Vector2(340, 80);
        public int RefreshCount { get; private set; }
        /// <summary>Actual editor refresh work, exposed to the bounded in-memory UX probe.</summary>
        public int ContextAnalysisCount {get;private set;}
        public int NodePresentationUpdateCount {get;private set;}
        public double ContextAnalysisMilliseconds {get;private set;}
        public double SummaryRefreshMilliseconds {get;private set;}
        public GameplayBehaviorGraphView()
        {
            style.flexGrow = 1; SetupZoom(.2f, 1.6f);
            RegisterCallback<DetachFromPanelEvent>(_=>{InvalidateFields();_cardSerialized?.Dispose();_cardSerialized=null;});
            RegisterCallback<AttachToPanelEvent>(_=>{if(_document&&_cardSerialized==null)Load(_document);});
            Insert(0, new GridBackground());
            this.AddManipulator(new ContentDragger()); this.AddManipulator(new SelectionDragger()); this.AddManipulator(new RectangleSelector());
            graphViewChanged = Change;
            RegisterCallback<GeometryChangedEvent>(_=>{if(!_document||_loading)return;foreach(var data in _document.AuthoringGraph.Nodes.Where(HasTimeline))if(_nodes.TryGetValue(data.Id,out var card)){float width=TimelineWidth(data);if(Mathf.Abs(card.resolvedStyle.width-width)>.5f){card.style.width=width;card.style.minWidth=width;card.style.maxWidth=width;if(selection.Contains(card)||_readableFocusId==data.Id)schedule.Execute(()=>{if(card.panel!=null&&(selection.Contains(card)||_readableFocusId==data.Id))FocusReadable(card);});}}});
            serializeGraphElements = elements => { GameplaySkillAuthoringCommands.Copy(_document, SelectedBehaviorIds(elements.OfType<Node>())); return "FRFrame.SkillAuthoring.Clipboard.Schema2"; };
            canPasteSerializedData = data => data == "FRFrame.SkillAuthoring.Clipboard.Schema2";
            unserializeAndPaste = (_, data) => {
                if(GameplaySkillAuthoringCommands.ClipboardHasRules){var menu=new GenericMenu();menu.AddItem(new GUIContent("粘贴为正式附加规则"),false,()=>Paste(true));menu.AddItem(new GUIContent("粘贴为待安排草稿"),false,()=>Paste(false));menu.ShowAsContext();}
                else Paste(false);
            };
            deleteSelection = (_, __) => {if(!EditingText())RemoveSelected();};
            RegisterCallback<PointerDownEvent>(e => {
                if (e.button == 1) CreationPosition = contentViewContainer.WorldToLocal(e.position);
                var node = e.target as Node??(e.target as VisualElement)?.GetFirstAncestorOfType<Node>();
                if((e.target as VisualElement)?.GetFirstAncestorOfType<GameplaySkillTimeline>()!=null)return;
                if (node != null) {if(node.viewDataKey.StartsWith("Config:",StringComparison.Ordinal)){PolicySelected?.Invoke(node.viewDataKey.Substring(7));return;} _selectedInlineAction=null;SelectedEventId=null;HighlightBehavior(node.viewDataKey);Selected?.Invoke(node.viewDataKey); }
            });
        }
        private void Paste(bool active)=>Try(()=>{
            ShowDrafts=true;var source=_document;
            var ids=GameplaySkillAuthoringCommands.Paste(source,new Vector2(50,50),active);
            // The source notification queues the topology rebuild first. Select only its new
            // identities afterwards, so Delete acts on the paste rather than the original.
            schedule.Execute(()=>{
                if(_document!=source||ids.Length==0)return;
                ClearSelection();_selectedInlineAction=null;SelectedEventId=null;
                foreach(string id in ids)if(_nodes.TryGetValue(id,out var node))AddToSelection(node);
                Selected?.Invoke(ids[0]);Focus();FrameSelection();
            });
        });
        public void Load(GameplaySkillDocumentAsset document)
        {
            this.Q<VisualElement>("canvas-action-menu")?.RemoveFromHierarchy();
            bool switched=_document!=document;SaveViewState();
            if(switched){_policyDrafts.Clear();_selectedInlineAction=null;SelectedEventId=null;_focusAfterLayout=null;_readableFocusId=null;}
            InvalidateFields();_layoutPreview=null;_document = document; _cardSerialized?.Dispose();_cardSerialized=document?new SerializedObject(document):null;_loading = true; RefreshCount++;
            try
            {
                DeleteElements(graphElements.ToList()); _nodes.Clear();_policyCards.Clear(); _inputs.Clear(); _outputs.Clear();_inlineViews.Clear();
                _compactOwners.Clear();_inlineOwners.Clear();if (!document || document.AuthoringGraph == null) return;
                var graph = document.AuthoringGraph;IndexPresentation();_portsByCard.Clear();_contextContracts.Clear(); var active = Reachable(graph);BuildTimelinePresentation(graph);
                // A displaced End is still a real authoring choice. Keep it visible across
                // save/reopen while a spatial start needs an explicit ending strategy.
                bool spatial=graph.Nodes.Any(n=>active.Contains(n.Id)&&GameplaySkillAuthoringCommands.StartsSpatialLifetime(n));
                foreach (var data in graph.Nodes.Where(n => (ShowDrafts || active.Contains(n.Id)||spatial&&(n.Payload as GameplayActionNodePayload)?.Action is GameplayEndAbilityActionAuthoring)&&!IsTimelineInline(n.Id)&&!(n.Id==graph.PrimaryRuleNodeId&&InlinePrimaryRule(document)))) AddCard(data, active.Contains(data.Id));
                foreach (var edge in graph.Connections)
                    if (_outputs.TryGetValue(Key(edge.FromNodeId, edge.OutputPortKey), out var output) && _inputs.TryGetValue(edge.InputPortKey==GameplayGraphEffectCallbacks.Input?Key(edge.ToNodeId,GameplayGraphEffectCallbacks.Input):edge.ToNodeId, out var input)) AddWire(output, input, edge.Id, false);
                foreach (var timeline in graph.Timelines)
                    foreach (var entry in timeline.Events)
                        if (_outputs.TryGetValue(Key(entry.Id, "Schedule"), out var output) && _inputs.TryGetValue(entry.EntryNodeId ?? "", out var input)) AddWire(output, input, entry.Id, true);
            }
            finally { _loading = false;if(document){RefreshPortPolicy();RefreshContextContracts(true);SetInlinePreview(_inlineViews.Keys.FirstOrDefault());RestoreViewMode(switched);} }
        }
        public static HashSet<string> Reachable(GameplayAuthoringGraph graph)
        { return GameplayGraphContextPolicy.Reachable(graph); }
        internal static bool InlinePrimaryRule(GameplaySkillDocumentAsset document)
        {
            var rule=document.AuthoringGraph.Nodes.Find(n=>n.Id==document.AuthoringGraph.PrimaryRuleNodeId)?.Payload as GameplayRuleNodePayload;
            return document.Mode==FRFrame.Gameplay.Standard.GameplayStandardActivationMode.Active&&rule?.Trigger is GameplaySkillEventTriggerAuthoring trigger&&trigger.EventTag.Name==document.CastEvent.Name&&!rule.Once&&(rule.Conditions?.Length??0)==0;
        }
        private void AddCard(GameplayAuthoringNode data, bool active)
        {
            var card = new Node { title = GameplaySkillNodeDisplay.Name(data,_document.AuthoringGraph), viewDataKey = data.Id, userData = data.Id,layer=-100 };
            card.style.width = data.Payload is GameplayAbilityNodePayload ? 330 : HasTimeline(data) ? TimelineWidth(data) : 360;
            card.style.minWidth = card.style.width; card.style.maxWidth = card.style.width;
            card.mainContainer.style.overflow = Overflow.Hidden;
            card.mainContainer.style.backgroundColor=new Color(.18f,.19f,.2f,1);
            var kind=GameplaySkillNodeDisplay.Kind(data);var color=GameplayNodeVisuals.Color(kind);
            card.titleContainer.style.backgroundColor=Color.Lerp(new Color(.12f,.14f,.16f),color,.42f);
            card.style.borderLeftColor=color;card.style.borderLeftWidth=4;card.titleContainer.style.minHeight=34;
            var heading=card.titleContainer.Q<Label>();if(heading!=null){heading.style.fontSize=14;heading.style.unityFontStyleAndWeight=FontStyle.Bold;heading.style.color=Color.white;}
            card.tooltip=GameplayNodeVisuals.Labels[(int)kind]+" · 直接编辑关键参数；右侧补充高级配置";
            card.style.fontSize=12;
            card.inputContainer.style.paddingLeft = 13; card.inputContainer.style.paddingRight = 12;
            card.outputContainer.style.paddingLeft = 12; card.outputContainer.style.paddingRight = 13;
            var info = new Label((active ? "正式执行" : "未安排草稿 · 尚不执行")+(data.Payload.Definitions.Any(b=>b.External)?" · 共享来源":" · 本地")); info.style.fontSize = 12; info.style.paddingLeft = 10; info.style.whiteSpace=WhiteSpace.Normal; card.extensionContainer.Add(info);
            if (data.Payload is GameplayActionNodePayload)
            {
                var input = card.InstantiatePort(Orientation.Horizontal, Direction.Input, Port.Capacity.Multi, typeof(GameplaySkillActionAuthoring));
                input.portName = "执行入口"; input.tooltip="从此入口执行该行为及其后续链；左侧接收，右侧输出。";input.userData = new Socket { Owner = data.Id, Port = "Execute" }; card.inputContainer.Add(input); _inputs[data.Id] = input;
                if(GameplayGraphEffectCallbacks.EffectBinding(data)!=null){var effectInput=card.InstantiatePort(Orientation.Horizontal,Direction.Input,Port.Capacity.Multi,typeof(GameplayEffectDefinitionAsset));effectInput.portName="作为后续效果";effectInput.tooltip="只消费此 Effect 定义；不会执行该卡的 Then 动作链。";effectInput.userData=new Socket{Owner=data.Id,Port=GameplayGraphEffectCallbacks.Input};card.inputContainer.Add(effectInput);_inputs[Key(data.Id,GameplayGraphEffectCallbacks.Input)]=effectInput;}
            }
            foreach (var output in GameplayGraphFields.OutputPorts(data).Where(key=>key!="Then"||!((data.Payload as GameplayActionNodePayload)?.Action is GameplayEndAbilityActionAuthoring))) AddPort(card, data.Id, output);
            if(data.Payload is GameplayAbilityNodePayload&&InlinePrimaryRule(_document)){
                var output=AddPort(card,_document.AuthoringGraph.PrimaryRuleNodeId,"Actions");output.portName="施放执行";
            }
            var fields=new GameplaySkillCardEditor(_document,data.Id,_cardSerialized){Selecting=()=>{SelectedEventId=null;SelectNodeWithoutCamera(data.Id);Selected?.Invoke(data.Id);},Advanced=path=>{Selected?.Invoke(data.Id);if(path!=null)GameplaySkillFieldSchema.RevealField(_document,path);}};
            var summary=new Label(GameplaySkillNodeDisplay.CardSummary(data,_document.AuthoringGraph,fields.HasFields)){name="BusinessSummary"};
            summary.style.whiteSpace=WhiteSpace.Normal;summary.style.paddingLeft=10;summary.style.paddingRight=10;summary.style.paddingTop=7;summary.style.paddingBottom=7;
            summary.style.display=string.IsNullOrEmpty(summary.text)?DisplayStyle.None:DisplayStyle.Flex;card.extensionContainer.Add(summary);
            card.extensionContainer.Add(fields);
            AddSpatialLifecycleGuidance(card,data);
            foreach (var binding in data.Payload.Definitions)
            {
                if(data.Payload is GameplayAbilityNodePayload&&binding.PropertyPath=="CooldownEffect")continue;
                var definition = _document.AuthoringGraph.LocalDefinitions.Find(d => d.Id == binding.LocalId);
                
                if(definition!=null&&_outputs.Values.Any(p=>((Socket)p.userData).Owner==definition.Id)){var shared=new Label("复用局部配置 · 后续端口位于首个消费者卡片");shared.style.whiteSpace=WhiteSpace.Normal;shared.style.paddingLeft=10;card.extensionContainer.Add(shared);definition=null;}
                if (definition != null) foreach (var port in GameplayGraphFields.ActionArrays(definition.Payload)) ResourcePort(card,definition,binding,port.Path);
                if(definition!=null)foreach(var port in GameplayGraphEffectCallbacks.Ports(definition.Payload))ResourcePort(card,definition,binding,port);
                
            var timeline = _document.AuthoringGraph.Timelines.Find(t => t.Id == binding.LocalId);
                if (timeline != null)
                {
                    string stateKey=_document.GetInstanceID()+":"+timeline.Id;
                    if(!_timelineStates.TryGetValue(stateKey,out var state))_timelineStates[stateKey]=state=GameplaySkillTimeline.ViewState.Load(GameplaySkillEditorState.Key(_document)+".Timeline."+timeline.Id);
                    var view=new GameplaySkillTimeline(_document,data.Id,timeline.Id,state,seconds=>ShowMenu(null,timeline.Id,seconds),entry=>SelectScheduledEntry(entry,data.Id),RevealTimelineAction);
                    view.CursorChanged=seconds=>PreviewSeeked?.Invoke(data.Id,view.SelectedEventId,seconds);
                    view.IsPlaying=()=>PreviewPlaying?.Invoke(data.Id)==true;view.TogglePlaying=()=>TogglePreview?.Invoke(data.Id);
                    var animationNodes=timeline.Events.Select(e=>_document.AuthoringGraph.Nodes.Find(n=>n.Id==e.EntryNodeId)).Where(n=>(n?.Payload as GameplayActionNodePayload)?.Action is GameplaySkillActionAuthoring a&&GameplaySkillNodeDescriptors.Get(a.GetType()).IsAnimation).GroupBy(n=>n.Id).Select(g=>g.First()).ToArray();
                    view.IsMainAnimation=IsTimelineInline;view.InvalidatePresentation();
                    view.HasAnimation=animationNodes.Length>0;
                    AddMainAnimationControls(card,data.Id,timeline);
                    var animationHeader=new VisualElement{name="AnimationHeader",style={flexDirection=FlexDirection.Row,alignItems=Align.FlexStart}};
                    var animationConfig=new VisualElement{name="AnimationConfiguration",style={flexGrow=1,flexShrink=1,minWidth=180}};
                    animationHeader.Add(animationConfig);
                    foreach(var source in animationNodes){
                        string actionId=source.Id;bool inline=IsTimelineInline(actionId);
                        object values=GameplaySkillNodeDisplay.AnimationValues(source,_document.AuthoringGraph);
                        var label=new Label(inline?(values==null?"动画资源未配置":GameplaySkillFieldSchema.Name(values.GetType()))+" · "+(source.Payload.Definitions.Any(b=>b.External)?"共享，只读":"本地"):
                            GameplaySkillNodeDisplay.EventLabel(source,_document.AuthoringGraph)+" · 参数在独立动画卡"){name="AnimationSource-"+actionId,userData=actionId};
                        label.AddToClassList("animation-source");label.style.whiteSpace=WhiteSpace.Normal;label.style.marginLeft=10;label.style.fontSize=12;animationConfig.Add(label);
                        if(inline){
                            var animationFields=new GameplaySkillCardEditor(_document,actionId,_cardSerialized,true){Selecting=()=>{SelectedEventId=null;SelectNodeWithoutCamera(actionId);Selected?.Invoke(actionId);},Advanced=path=>{SelectedEventId=null;SelectNodeWithoutCamera(actionId);Selected?.Invoke(actionId);if(path!=null)GameplaySkillFieldSchema.RevealField(_document,path);}};
                            animationConfig.Add(animationFields);
                        }else animationConfig.Add(new Button(()=>{SelectedEventId=null;FocusNode(actionId);Selected?.Invoke(actionId);}){text="定位独立动画行为",tooltip="编辑同一真实动画行为 "+actionId});
                    }
                    if(animationNodes.Length>0){
                        var host=new IMGUIContainer(()=>DrawInlinePreview?.Invoke(data.Id)){name="AnimationViewport",style={width=210,flexShrink=0,minHeight=150,marginLeft=8,marginRight=8}};
                        animationHeader.Add(host);_inlineViews[data.Id]=host;card.extensionContainer.Add(animationHeader);
                        animationHeader.RegisterCallback<GeometryChangedEvent>(e=>{
                            bool narrow=e.newRect.width<470;animationHeader.style.flexDirection=narrow?FlexDirection.Column:FlexDirection.Row;
                            host.style.width=narrow?Mathf.Min(260,e.newRect.width-16):Mathf.Clamp(e.newRect.width*.3f,170,240);
                            host.style.alignSelf=narrow?Align.Center:Align.FlexStart;
                        });
                    }
                    card.extensionContainer.Add(new IMGUIContainer(view.DrawControls){name="TimelineTransport",style={marginLeft=10,marginRight=12}});
                    view.style.alignSelf=Align.Stretch;view.style.marginLeft=8;view.style.marginRight=8;
                    card.extensionContainer.Add(view);
                    foreach(var entry in timeline.Events){
                        if(IsTimelineInline(entry.EntryNodeId))continue;
                        var port=AddPort(card,entry.Id,"Schedule",true);port.portColor=new Color(.94f,.68f,.3f);
                        view.AttachPort(entry.Id,port);
                    }

                }
            }
            if(data.Payload is GameplayRuleNodePayload){
                bool enabled=_document.AuthoringGraph.PrimaryRuleNodeId==data.Id||_document.AuthoringGraph.AdditionalRuleNodeIds.Contains(data.Id);
                if(!enabled)card.extensionContainer.Add(new Button(()=>Try(()=>GameplaySkillAuthoringCommands.SetRuleActive(_document,data.Id,true))){text="启用为正式附加规则",tooltip="先连接真实行为，再显式启用；不完整规则会说明原因。"});
            }
            if (data.Payload is GameplayAbilityNodePayload)
            {
                var label = new Label(_document.Mode==FRFrame.Gameplay.Standard.GameplayStandardActivationMode.Active?"主动技能":"被动技能"){name="AbilitySummary"};
                label.style.whiteSpace = WhiteSpace.Normal; label.style.marginLeft = 10; card.extensionContainer.Add(label);
                card.extensionContainer.Add(new Button(()=>{
                    ShowDrafts=true;string rule=null;
                    Try(()=>rule=GameplaySkillAuthoringCommands.CreateRule(_document,card.GetPosition().position+new Vector2(card.GetPosition().width+80,0)));
                    if(rule!=null)schedule.Execute(()=>{FocusNode(rule);Selected?.Invoke(rule);});
                }){text="+ 附加触发规则（草稿）",tooltip="创建 → 配置触发/连接行为 → 在规则卡启用为正式规则；不会生成 Cost/CD 假执行线。"});

            }
            
            StylePorts(card);
            if(data.Payload is GameplayActionNodePayload&&!HasTimeline(data)){
                // Real outgoing branches sit beside the business values, not in a tall empty
                // half-width header. Port objects/owners remain the exact original instances.
                var content=new VisualElement{name="BusinessContent",style={flexGrow=1,minWidth=0}};
                content.Add(summary);content.Add(fields);
                var body=new VisualElement{name="BusinessBody",style={flexDirection=FlexDirection.Row,alignItems=Align.FlexStart}};
                var outputs=new VisualElement{name="BusinessOutputs",style={width=112,flexShrink=0,paddingRight=8}};
                foreach(var port in card.outputContainer.Children().OfType<Port>().ToArray())outputs.Add(port);
                body.Add(content);body.Add(outputs);
                card.extensionContainer.Insert(1,body);
            }
            card.expanded=true;card.Q("collapse-button")?.RemoveFromHierarchy();
            card.RegisterCallback<GeometryChangedEvent>(_=>{if(!card.expanded){card.expanded=true;card.RefreshExpandedState();}});
            card.SetPosition(new Rect(data.Position, Vector2.zero)); AddElement(card); _nodes[data.Id] = card;if(data.Payload is GameplayAbilityNodePayload)AddPolicies(card);
            if(data.Payload is GameplayAbilityNodePayload)card.RegisterCallback<GeometryChangedEvent>(_=>{if(_document)PositionPolicies();});
            card.RefreshExpandedState(); card.RefreshPorts();if(HasTimeline(data))AddWidthHandle(card,data);
        }
        private void AddWidthHandle(Node card,GameplayAuthoringNode data)
        {
            var handle=new VisualElement{tooltip="拖动调整编排卡宽度；只改变视图布局",style={position=Position.Absolute,right=2,bottom=2,width=20,height=12,backgroundColor=new Color(.25f,.32f,.4f)}};
            card.mainContainer.Add(handle);float start=0,width=0;
            handle.RegisterCallback<PointerDownEvent>(e=>{if(e.button!=0||_layoutPreview!=null)return;start=contentViewContainer.WorldToLocal(e.position).x;width=card.resolvedStyle.width;handle.CapturePointer(e.pointerId);e.StopPropagation();});
            handle.RegisterCallback<PointerMoveEvent>(e=>{if(!handle.HasPointerCapture(e.pointerId))return;float next=Mathf.Clamp(width+contentViewContainer.WorldToLocal(e.position).x-start,440,1200);card.style.width=next;card.style.minWidth=next;card.style.maxWidth=next;e.StopPropagation();});
            handle.RegisterCallback<PointerUpEvent>(e=>{if(!handle.HasPointerCapture(e.pointerId))return;handle.ReleasePointer(e.pointerId);float next=card.resolvedStyle.width;Try(()=>GameplaySkillAuthoringCommands.Transaction(_document,"调整编排卡宽度",g=>g.Nodes.Single(n=>n.Id==data.Id).Size=new Vector2(next,0),false,GameplaySkillAuthoringChange.Local(new[]{data.Id},true)));e.StopPropagation();});
        }
        private bool HasTimeline(GameplayAuthoringNode node) => node.Payload.Definitions.Any(b => _document.AuthoringGraph.Timelines.Any(t => t.Id == b.LocalId));
        private static string DefinitionSummary(GameplayLocalDefinition definition)
        {var effect=definition.Payload as GameplayEffectDefinitionData;return effect==null?definition.Name:definition.Name+" · "+effect.DurationPolicy+(effect.DurationPolicy==GameplayEffectDurationPolicy.Duration?" · "+effect.Duration+"s":"")+(effect.Period>0?" · 周期 "+effect.Period+"s":"");}
        internal void InvalidateFields(){foreach(var fields in this.Query<GameplaySkillCardEditor>().ToList())fields.Invalidate();}
        public void RefreshLabels()=>RefreshPresentation(null);
        private Port AddPort(Node card, string owner, string key, bool scheduled = false)
        {
            var local=_document.AuthoringGraph.LocalDefinitions.Find(d=>d.Id==owner);bool effect=local!=null&&GameplayGraphEffectCallbacks.IsPort(local.Payload,key);
            var port = card.InstantiatePort(Orientation.Horizontal, Direction.Output, Port.Capacity.Multi, effect?typeof(GameplayEffectDefinitionAsset):typeof(GameplaySkillActionAuthoring));
            port.RemoveManipulator(port.edgeConnector);port.AddManipulator(new EdgeConnector<Edge>(new CreationConnector(this)));
            port.portName = GameplaySkillFieldSchema.PortName(key); port.userData = new Socket { Owner = owner, Port = key, Scheduled = scheduled };
            port.AddManipulator(new ContextualMenuManipulator(evt=>{
                foreach(var edge in port.connections.ToArray()){
                    string id=(edge.userData as Socket)?.Owner;
                    if(id==null)continue;
                    evt.menu.AppendAction("断开 → "+edge.input.node.title,_=>Try(()=>GameplaySkillAuthoringCommands.Disconnect(_document,id,scheduled)),
                        GameplaySkillAuthoringCommands.IsReadOnly(_document)?DropdownMenuAction.Status.Disabled:DropdownMenuAction.Status.Normal);
                }
                evt.StopPropagation();
            }));
            port.portColor=GameplayNodeVisuals.Color(GameplaySkillNodeDisplay.Kind(_document.AuthoringGraph.Nodes.Find(n=>n.Id==card.viewDataKey)));port.tooltip=GameplaySkillFieldSchema.PortName(key)+" · "+(scheduled?"按此时间事件调用；时点和次序在编排中编辑。":effect?"只消费接收卡的效果定义，不执行接收卡的后续行为。":"连接同一执行链的后续行为；多条连接按保存的顺序调用。");port.style.marginRight = 0; card.outputContainer.Add(port); _outputs[Key(owner, key)] = port; return port;
        }
        private static string Key(string id, string port) => id + ":" + port;
        private void AddWire(Port output, Port input, string id, bool scheduled)
        { var wire = new GameplaySkillExecutionEdge{Scheduled=scheduled,output=output,input=input};output.Connect(wire);input.Connect(wire);wire.layer=1;wire.visible=!scheduled||output.visible;wire.edgeControl.edgeWidth=2;wire.edgeControl.inputOrientation=Orientation.Horizontal;wire.edgeControl.outputOrientation=Orientation.Horizontal;wire.edgeControl.inputColor=input.portColor;wire.edgeControl.outputColor=output.portColor;wire.tooltip=scheduled?"◇ 时间调度 · 真实事件 "+id+" → "+(input.node?.title??"行为")+"；当前时间、次序与条件见同编号事件。":(output.node?.title??"后续行为")+" · → 同步后续 · "+output.portName+" → "+(input.node?.title??"行为")+" · "+input.portName; wire.userData = new Socket { Owner = id, Scheduled = scheduled }; AddElement(wire); }
        public override List<Port> GetCompatiblePorts(Port startPort, NodeAdapter nodeAdapter){var check=_connectionChecker??(_connectionChecker=GameplayGraphContextPolicy.ConnectionChecker(_document.AuthoringGraph));return ports.Where(p=>p.direction!=startPort.direction&&p.node!=startPort.node&&p.portType==startPort.portType&&CanUse(p)&&CanUse(startPort)&&ContextCompatible(startPort,p,check)).ToList();}
        private bool ContextCompatible(Port first,Port second,Func<string,string,string,string,bool,string> check){if((first.userData as Socket)?.Config!=null)return (first.userData as Socket).Config==(second.userData as Socket)?.Config;var from=(Socket)(first.direction==Direction.Output?first:second).userData;var to=(Socket)(first.direction==Direction.Input?first:second).userData;
            string reason=check(from.Owner,from.Port,to.Owner,to.Port,from.Scheduled);if(reason!=null){(first.direction==Direction.Input?first:second).tooltip=reason;return false;}return true;}
        private bool CanUse(Port port){var socket=port.userData as Socket;return socket==null||socket.Config!=null||port.direction==Direction.Input||socket.Scheduled||GameplayGraphPortPolicy.Unavailable(_document.AuthoringGraph,socket.Owner,socket.Port)==null;}
        private void RefreshPortPolicy(IEnumerable<string> affected=null){foreach(var port in affected==null?ports:affected.Where(_portsByCard.ContainsKey).SelectMany(id=>_portsByCard[id])){bool usable=CanUse(port);port.SetEnabled(usable);if(!usable){var socket=(Socket)port.userData;port.tooltip=GameplayGraphPortPolicy.Unavailable(_document.AuthoringGraph,socket.Owner,socket.Port);}}}
        private GraphViewChange Change(GraphViewChange change)
        {
            if (_loading) return change;
            if (change.edgesToCreate?.Count>0||change.elementsToRemove?.OfType<Edge>().Any()==true)
            {
                var pending=change.edgesToCreate?.Select(e=>(source:(Socket)e.output.userData,target:(Socket)e.input.userData)).ToArray()??Array.Empty<(Socket source,Socket target)>();
                var removed=change.elementsToRemove?.OfType<Edge>().Select(e=>e.userData as Socket).Where(s=>s!=null).ToArray()??Array.Empty<Socket>();
                change.edgesToCreate?.Clear();change.elementsToRemove?.Clear();
                if(pending.Any(p=>p.source.Config!=null)||removed.Any(e=>e.Config!=null)){
                    foreach(var edge in removed.Where(e=>e.Config!=null))if(!pending.Any(p=>p.source.Config==edge.Config))DetachPolicy(edge.Config);
                    foreach(var item in pending.Where(p=>p.source.Config!=null))if(item.source.Config==item.target.Config&&_policyDrafts.ContainsKey(PolicyKey(item.source.Config)))ReconnectPolicy(item.source.Config);
                    return change;
                }
                Try(()=>GameplaySkillAuthoringCommands.Transaction(_document,"重连正式行为",graph=>{
                    foreach(var edge in removed){
                        if(edge.Scheduled){if(!pending.Any(p=>p.source.Scheduled&&p.source.Owner==edge.Owner))foreach(var timeline in graph.Timelines)timeline.Events.RemoveAll(e=>e.Id==edge.Owner);}
                        else {var old=graph.Connections.SingleOrDefault(e=>e.Id==edge.Owner);if(old!=null&&pending.Any(p=>p.source.Owner==old.FromNodeId&&p.source.Port==old.OutputPortKey))graph.Connections.Remove(old);else GameplaySkillAuthoringCommands.DisconnectInGraph(graph,edge.Owner);}
                    }
                    foreach(var item in pending){
                        if(item.source.Scheduled)graph.Timelines.SelectMany(t=>t.Events).Single(e=>e.Id==item.source.Owner).EntryNodeId=item.target.Owner;
                        else{
                            GameplaySkillAuthoringCommands.ConnectInGraph(graph,item.source.Owner,item.source.Port,item.target.Owner,item.target.Port);
                        }
                    }
                }));
            }
            if (change.movedElements != null)
            {
                foreach(var card in change.movedElements.OfType<Node>().Where(n=>n.viewDataKey.StartsWith("Config:",StringComparison.Ordinal))){string key=GameplaySkillEditorState.Key(_document)+".Policy."+card.viewDataKey.Substring(7);SessionState.SetVector3(key,card.GetPosition().position);SessionState.SetBool(key+".Moved",true);} var positions = change.movedElements.OfType<Node>().Where(n=>!n.viewDataKey.StartsWith("Config:",StringComparison.Ordinal)).ToDictionary(n => n.viewDataKey, n => n.GetPosition().position);
                if(BusinessReading||_explicitLayoutPreview){if(_layoutPreview==null)_layoutPreview=_nodes.ToDictionary(p=>p.Key,p=>p.Value.GetPosition().position);foreach(var pair in positions)_layoutPreview[pair.Key]=pair.Value;SaveViewState();}
                else if (positions.Count > 0) Try(() => GameplaySkillAuthoringCommands.MoveNodes(_document, positions)); }
            return change;
        }
        // Native curve picking shares the retained card map instead of querying the visual tree per edge.
        internal bool IsCardPoint(Vector2 contentPoint){foreach(var card in _nodes.Values.Concat(_policyCards.Values))if(card.GetPosition().Contains(contentPoint))return true;return false;}
        internal bool EditingText()
        {
            if(EditorGUIUtility.editingTextField||!string.IsNullOrEmpty(Input.compositionString))return true;
            for(var focus=panel?.focusController?.focusedElement as VisualElement;focus!=null;focus=focus.parent)
                if(focus is TextField||focus is FloatField||focus is IntegerField)return true;
            return false;
        }
        private void RemoveSelected()
        {
            if (!_document) return;
            foreach(string role in selection.OfType<Node>().Where(n=>n.viewDataKey.StartsWith("Config:",StringComparison.Ordinal)).Select(n=>n.viewDataKey.Substring(7)).Concat(selection.OfType<Edge>().Select(e=>(e.userData as Socket)?.Config).Where(r=>r!=null)).Distinct().ToArray())DetachPolicy(role);
            var selected = SelectedBehaviorIds(selection.OfType<Node>()).ToArray();
            if(selected.Length==0&&!selection.OfType<Edge>().Any(e=>(e.userData as Socket)?.Config==null))return;
            if (selected.Contains(_document.AuthoringGraph.AbilityNodeId)) { DeleteMain?.Invoke(); return; }
            Try(() => GameplaySkillAuthoringCommands.Transaction(_document, "删除所选行为 / 连接", graph => {
                var edges = selection.OfType<Edge>().Select(e => e.userData as Socket).Where(s => s != null&&s.Config==null).ToArray();
                foreach (var edge in edges) { if (edge.Scheduled) foreach (var timeline in graph.Timelines) timeline.Events.RemoveAll(e => e.Id == edge.Owner); else GameplaySkillAuthoringCommands.DisconnectInGraph(graph,edge.Owner); }
                GameplaySkillAuthoringCommands.DeleteFromGraph(graph, selected);
            }));
        }
        public override void BuildContextualMenu(ContextualMenuPopulateEvent evt)
        {
            CreationPosition = contentViewContainer.WorldToLocal(evt.mousePosition);
            evt.menu.AppendAction("创建业务行为…", _ => ShowMenu(evt.mousePosition));
            AddCallbackCreationMenu(evt);
            if(_selectedInlineAction!=null&&_inlineOwners.TryGetValue(_selectedInlineAction,out var inlineOwner)){
                string action=_selectedInlineAction;evt.menu.AppendAction("时间事件 / 展开独立卡片",_=>RevealTimelineAction(action));
                evt.menu.AppendAction("时间事件 / 选择整个编排",_=>{SelectNodeWithoutCamera(inlineOwner);Selected?.Invoke(inlineOwner);});
            }
            foreach(var selectedCard in selection.OfType<Node>().Where(n=>_compactOwners.ContainsKey(n.viewDataKey)&&!IsTimelineInline(n.viewDataKey))){string id=selectedCard.viewDataKey;evt.menu.AppendAction("收回时间轴内编辑",_=>CompactTimelineAction(id));}
            foreach(var selected in selection.OfType<Node>().Select(n=>_document.AuthoringGraph.Nodes.FirstOrDefault(d=>d.Id==n.viewDataKey)).Where(n=>n?.Payload is GameplayRuleNodePayload)){
                string id=selected.Id;bool active=_document.AuthoringGraph.PrimaryRuleNodeId==id||_document.AuthoringGraph.AdditionalRuleNodeIds.Contains(id);
                evt.menu.AppendAction(active?"规则 / 停用为草稿":"规则 / 启用为正式附加规则",_=>Try(()=>GameplaySkillAuthoringCommands.SetRuleActive(_document,id,!active)));
            }
            if (selection.Count > 0) evt.menu.AppendAction("删除所选", _ => RemoveSelected());
        }
        public void ShowMenu(Vector2? anchor = null, string timeline = null, float? seconds = null, Socket source=null)
        { CreationLink=source==null?null:new GameplaySkillAuthoringCommands.CreationLink{Owner=source.Owner,Port=source.Port,Scheduled=source.Scheduled,Input=_document.AuthoringGraph.LocalDefinitions.Any(d=>d.Id==source.Owner&&GameplayGraphEffectCallbacks.IsPort(d.Payload,source.Port))?GameplayGraphEffectCallbacks.Input:"Execute"};if(anchor.HasValue)CreationPosition=contentViewContainer.WorldToLocal(anchor.Value);if(!anchor.HasValue){if(timeline==null)CreationPosition=contentViewContainer.WorldToLocal(worldBound.center);else{var owner=_document.AuthoringGraph.Nodes.Find(n=>n.Payload.Definitions.Any(b=>b.LocalId==timeline));if(owner!=null&&_nodes.TryGetValue(owner.Id,out var card))CreationPosition=card.GetPosition().position+new Vector2(card.GetPosition().width+90,30);}}GameplaySkillCreationMenu.Show(this, source!=null?"从此端口创建并连接":timeline == null ? "创建未安排行为（不执行）" : "此帧创建并安排", menu => Populate?.Invoke(menu, CreationPosition, timeline, seconds), anchor); }
        private sealed class CreationConnector:IEdgeConnectorListener
        {
            private readonly GameplayBehaviorGraphView _view;
            internal CreationConnector(GameplayBehaviorGraphView view){_view=view;}
            public void OnDropOutsidePort(Edge edge,Vector2 position){
                var source=edge.output?.userData as Socket;
                if(source?.Config!=null)_view.ShowPolicyChoices(source.Config);else if(source!=null)_view.ShowMenu(position,null,null,source);
            }
            public void OnDrop(GraphView graphView,Edge edge){
                _view.Change(new GraphViewChange{edgesToCreate=new List<Edge>{edge}});
            }
        }
        public void SynchronizeLayout(){
            if(!_document)return;foreach(var data in _document.AuthoringGraph.Nodes)if(_nodes.TryGetValue(data.Id,out var card)){
                var position=_layoutPreview!=null&&_layoutPreview.TryGetValue(data.Id,out var preview)?preview:data.Position;
                if(card.GetPosition().position!=position)card.SetPosition(new Rect(position,card.GetPosition().size));
                if(HasTimeline(data)){float width=TimelineWidth(data);card.style.width=width;card.style.minWidth=width;card.style.maxWidth=width;}
            }RefreshLabels();
        }
        public void SelectNodeWithoutCamera(string id){ClearSelection();_selectedInlineAction=IsTimelineInline(id)?id:null;string cardId=_selectedInlineAction!=null?_inlineOwners[id]:id;if(_nodes.TryGetValue(cardId??"",out var node))AddToSelection(node);if(_document)HighlightBehavior(id);}
        public void SetInlinePreview(string id){_inlinePreview=id;InlinePreviewChanged?.Invoke(id,id!=null);foreach(var pair in _inlineViews)pair.Value.MarkDirtyRepaint();}
        public void SaveViewState(){if(!_document)return;foreach(var state in _timelineStates.Values)state.Save();string key=CameraKey;SessionState.SetVector3(key+".Pan",_explicitLayoutPreview?_layoutPan:viewTransform.position);SessionState.SetFloat(key+".Zoom",_explicitLayoutPreview?_layoutZoom.x:viewTransform.scale.x);if(BusinessReading&&_layoutPreview!=null)SessionState.SetString(GameplaySkillEditorState.Key(_document)+".ReadingLayout",JsonUtility.ToJson(new ReadingLayout{Ids=_layoutPreview.Keys.ToList(),Positions=_layoutPreview.Values.ToList()}));}
        private void RestoreCamera(){string key=CameraKey;float zoom=SessionState.GetFloat(key+".Zoom",-1);if(zoom<0){UpdateViewTransform(Vector3.zero,Vector3.one);var source=_document;schedule.Execute(()=>{if(_document==source)FocusMainFlow();}).StartingIn(100);}else UpdateViewTransform(SessionState.GetVector3(key+".Pan",Vector3.zero),Vector3.one*zoom);}
        public void FocusNode(string id)
        { string cardId=IsTimelineInline(id)?_inlineOwners[id]:id;if (_nodes.ContainsKey(cardId??"")) { SelectNodeWithoutCamera(id);if(BusinessReading&&_layoutPreview==null){_focusAfterLayout=id;return;} FocusReadable(_nodes[cardId]); } }
        private void Try(Action action) { try { action();Feedback?.Invoke(null); } catch (Exception e) { Feedback?.Invoke(e.Message); } }
    }

}
