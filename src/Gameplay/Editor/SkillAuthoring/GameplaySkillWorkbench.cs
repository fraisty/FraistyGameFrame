using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>The sole writable skill workbench. Browser entries are typed document instances,
    /// including embedded documents; UI state is separate from executable graph ordering.</summary>
    [UnityEngine.Scripting.APIUpdating.MovedFrom(true,"FRFrame.Gameplay.Editor.V2","FRFrame.Gameplay.Editor","GameplayV2Workbench")]
    public sealed partial class GameplaySkillWorkbench : EditorWindow
    {
        [SerializeField] private GameplaySkillDocumentAsset _document;
        [SerializeField] private GameplayDefinitionAsset _sharedSource;
        [SerializeField] private string _selected;
        [SerializeField] private bool _referenceView;
        [SerializeField] private List<GameplayBehaviorGraphView.DetachedPolicy> _detachedPolicies=new List<GameplayBehaviorGraphView.DetachedPolicy>();
        internal List<GameplayBehaviorGraphView.DetachedPolicy> DetachedPolicies=>_detachedPolicies;
        private string _policySelection;
        private string _definition, _search = "", _feedback;
        private Vector2 _browserScroll, _inspectorScroll;
        private VisualElement _browser, _inspector;
        private IMGUIContainer _details;
        public GameplayBehaviorGraphView Canvas { get; private set; }
        public GameplaySkillDocumentAsset Document => _document;
        public IMGUIContainer InspectorHost=>_details;
        public int ScalarRefreshCount {get;private set;}
        /// <summary>Editor diagnostics for actual topology work; never used by compilation.</summary>
        public int ShapeAnalysisCount {get;private set;}
        public double ShapeAnalysisMilliseconds {get;private set;}
        public int CompositionRefreshCount=>Canvas?.RefreshCount??0;
        public event Action<bool,double> RefreshMeasured;
        private static List<GameplaySkillDocumentAsset> _documents;
        private static bool _stale = true;

        private bool _previewOpen;
        private bool _refreshPending;
        private IGameplaySkillHostSession _installation;
        private static readonly IGameplaySkillHost[] Hosts=TypeCache.GetTypesDerivedFrom<IGameplaySkillHost>().Where(t=>!t.IsAbstract&&t.GetConstructor(Type.EmptyTypes)!=null).Select(t=>(IGameplaySkillHost)Activator.CreateInstance(t)).ToArray();
        private string _shape;
        private int _knownDocumentDirtyCount;
        private void RememberSourceVersion(){_knownDocumentDirtyCount=_document?EditorUtility.GetDirtyCount(_document):0;}

        private static readonly IGameplaySkillPreviewFactory[] PreviewFactories = TypeCache.GetTypesDerivedFrom<IGameplaySkillPreviewFactory>()
            .Where(t=>!t.IsAbstract&&t.GetConstructor(Type.EmptyTypes)!=null).Select(t=>(IGameplaySkillPreviewFactory)Activator.CreateInstance(t)).ToArray();
        [InitializeOnLoadMethod] private static void Initialize() => EditorApplication.projectChanged += () => _stale = true;
        public static IReadOnlyList<GameplaySkillDocumentAsset> Documents()
        {
            if (_stale || _documents == null)
            {
                _documents = AssetDatabase.FindAssets("t:GameplaySkillDocumentAsset").Select(AssetDatabase.GUIDToAssetPath).Distinct()
                    .SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<GameplaySkillDocumentAsset>().Distinct()
                    .OrderBy(d => d.AuthoringGraph?.Scope).ThenBy(d => d.SkillId).ThenBy(d => d.name).ToList(); _stale = false;
            }
            return _documents;
        }
        [MenuItem("FRFrame/Gameplay/Skill Workbench")]
        public static void ShowWorkbench() { var window = GetWindow<GameplaySkillWorkbench>(); window.Show();window.Focus(); }
        [MenuItem("Assets/Create/FRFrame Gameplay/正式技能")]
        private static void CreateAsset()=>GetWindow<GameplaySkillWorkbench>().NewDocument();
        public static void Open(GameplaySkillDocumentAsset document, string nodeId = null)
        {
            var window = GetWindow<GameplaySkillWorkbench>();window.SelectDocument(document,nodeId);window.Show();window.Focus();if(nodeId!=null)window.Canvas.FocusNode(nodeId);
        }
        /// <summary>Resolves an Inspector/debugger source to the actual formal graph owner.
        /// Unowned shared definitions remain shared; navigation never creates a skill or workspace.</summary>
        public static void OpenSource(GameplayDefinitionAsset source,string propertyPath="")
        {
            if(!source)return;
            if(source is GameplaySkillDocumentAsset document){
                string node=null;
                var match=System.Text.RegularExpressions.Regex.Match(propertyPath??"",@"authoringGraph.Nodes.Array.data\[(\d+)\]");
                if(match.Success&&int.TryParse(match.Groups[1].Value,out int index)&&index<document.AuthoringGraph.Nodes.Count)
                    node=document.AuthoringGraph.Nodes[index].Id;
                Open(document,node);return;
            }
            long actionId=-1;
            if(!string.IsNullOrEmpty(propertyPath))using(var serialized=new SerializedObject(source)){
                var property=serialized.FindProperty(propertyPath);
                if(property?.propertyType==SerializedPropertyType.ManagedReference)actionId=property.managedReferenceId;
            }
            var owners=Documents().Where(d=>d.AuthoringGraph!=null).SelectMany(d=>d.AuthoringGraph.Nodes
                .Where(n=>n.SourceDefinitionId==source.DefinitionId||n.Payload.Definitions.Any(b=>b.External==source)||
                    n.Payload.Definitions.Any(b=>d.AuthoringGraph.LocalDefinitions.Any(l=>l.Id==b.LocalId&&l.Definitions.Any(v=>v.External==source))))
                .Select(n=>new{Document=d,Node=n})).ToArray();
            var window=GetWindow<GameplaySkillWorkbench>();
            var owner=owners.OrderByDescending(x=>x.Document==window.Document).ThenByDescending(x=>actionId>=0&&x.Node.SourceActionId==actionId).FirstOrDefault();
            if(owner!=null){Open(owner.Document,owner.Node.Id);return;}
            window.SelectDocument(null);window._sharedSource=source;
            window._feedback="共享定义 · "+source.name+"；此配置没有正式技能图所有者。";
            window._detailTab=0;window._inspectorOpen=true;window.UpdateSurface();window.ApplyPaneWidths();window.Show();window.Repaint();
        }
        public void SelectDocument(GameplaySkillDocumentAsset document,string nodeId=null,bool reveal=true)
        {_examplePanel?.RemoveFromHierarchy();_referenceView=false;GameplaySkillAuthoringCommands.ReadOnlyDocument=null;if(document)_sharedSource=null;if(_document!=document){_previewClockOwner=null;DisposeInstallation();DisposePreview();_previews.Clear();if(Event.current==null||GUI.GetNameOfFocusedControl()!="SkillDocumentBrowser"){GUIUtility.keyboardControl=0;EditorGUIUtility.editingTextField=false;_details?.Blur();}_diagnostics.Clear();_diagnosticsStale=true;_returnPan=null;_feedback=null;}_previewChoices.Clear();_document=document;_policySelection=null;_inlinePreviewNode=document?SessionState.GetString(GameplaySkillEditorState.Key(document)+".InlinePreview",""):null;if(string.IsNullOrEmpty(_inlinePreviewNode))_inlinePreviewNode=null;_selected=nodeId??document?.AuthoringGraph?.AbilityNodeId;_definition=null;if(reveal)_documentBrowser?.Reveal(document);Canvas?.Load(document);Canvas?.SelectNodeWithoutCamera(_selected);_shape=Shape();RememberSourceVersion();if(document)RememberRecent(document);UpdateSurface();Repaint();}
        [UnityEditor.Callbacks.OnOpenAsset(0)] private static bool OnOpen(int id, int line)
        {
            var asset = EditorUtility.InstanceIDToObject(id);
            if (asset is GameplaySkillDocumentAsset document) { Open(document); return true; }
            return false;
        }
        private void OnEnable()
        {
            if(_referenceView)GameplaySkillAuthoringCommands.ReadOnlyDocument=_document;
            titleContent = new GUIContent("技能工作台"); minSize = new Vector2(900, 600);
            _browserOpen=EditorPrefs.GetBool(ProjectKey+".BrowserOpen",true);_browserForced=EditorPrefs.GetBool(ProjectKey+".BrowserForced",false);_inspectorOpen=EditorPrefs.GetBool(ProjectKey+".InspectorOpen",true);EditorApplication.projectChanged+=RecentSourcesChanged;
            Undo.undoRedoPerformed += UndoChanged; GameplaySkillAuthoringCommands.DetailedChanged += Changed;
            EditorApplication.playModeStateChanged+=PlayChanged;
            _browserWidth=EditorPrefs.GetFloat("FRFrame.Workbench.BrowserWidth",260);_inspectorWidth=EditorPrefs.GetFloat("FRFrame.Workbench.InspectorWidth",370);GameplayAuthoringSourceChanges.Changed+=ExternalChanged;EditorApplication.update+=PreviewTick;
            _inlinePreviewNode=_document?SessionState.GetString(GameplaySkillEditorState.Key(_document)+".InlinePreview",""):null;if(string.IsNullOrEmpty(_inlinePreviewNode))_inlinePreviewNode=null;BuildUI();
        }
        private void DisposeInstallation(){_installation?.Dispose();_installation=null;}
        private void PlayChanged(PlayModeStateChange state){if(state==PlayModeStateChange.ExitingPlayMode||state==PlayModeStateChange.ExitingEditMode){DisposeInstallation();DisposePreview();}if(state==PlayModeStateChange.ExitingEditMode||state==PlayModeStateChange.EnteredEditMode)foreach(var fields in Canvas.Query<GameplaySkillCardEditor>().ToList())fields.SetEnabled(state==PlayModeStateChange.EnteredEditMode);}
        private void OnDisable() {
            if(GameplaySkillAuthoringCommands.IsReadOnly(_document))GameplaySkillAuthoringCommands.ReadOnlyDocument=null; SavePanePreferences();EditorApplication.projectChanged-=RecentSourcesChanged;Canvas?.SaveViewState();EditorApplication.update-=PreviewTick;GameplayAuthoringSourceChanges.Changed-=ExternalChanged;Undo.undoRedoPerformed -= UndoChanged; GameplaySkillAuthoringCommands.DetailedChanged -= Changed;EditorApplication.playModeStateChanged-=PlayChanged; DisposePreview();DisposeInstallation(); }
        private void BuildUI()
        {

            rootVisualElement.Clear();
            rootVisualElement.UnregisterCallback<KeyDownEvent>(OnWorkbenchKeyDown,TrickleDown.TrickleDown);rootVisualElement.RegisterCallback<KeyDownEvent>(OnWorkbenchKeyDown,TrickleDown.TrickleDown);
            var toolbar = new Toolbar();toolbar.style.flexWrap=Wrap.Wrap;toolbar.style.height=StyleKeyword.Auto;toolbar.style.flexShrink=0;rootVisualElement.Add(toolbar);
            var readingView=new ToolbarToggle{text="业务阅读",name="BusinessReadingToggle",tooltip="勾选：会话内业务排布；取消：作者原始布局。两种视图的参数和连接都编辑同一正式源；只有阅读视图中的拖动不写坐标。"};
            var viewCaption=new Label("作者布局"){style={alignSelf=Align.Center,fontSize=11,marginLeft=4,marginRight=6}};readingView.RegisterValueChangedCallback(e=>Canvas.SetBusinessReading(e.newValue));
            toolbar.Add(new ToolbarButton(() => { _browserOpen=_browser.resolvedStyle.display==DisplayStyle.None;_browserForced=true;ApplyPaneWidths();SavePanePreferences(); }) { text = "技能库" });
            toolbar.Add(new ToolbarButton(ShowHome){text="首页"});
            toolbar.Add(new ToolbarButton(NewDocument) { text = "新建技能" });
            toolbar.Add(new ToolbarButton(ShowOpenDocumentMenu){text="打开技能…"});
            toolbar.Add(new ToolbarButton(ShowRecentDocumentMenu){text="最近使用…"});
            var tools=_documentTools=new VisualElement{name="DocumentTools",style={flexDirection=FlexDirection.Row,flexWrap=Wrap.Wrap}};toolbar.Add(tools);
            tools.Add(new ToolbarButton(() => Canvas.ShowMenu()) { text = "+ 节点" });
            tools.Add(new ToolbarButton(() => Attempt(() => { if (_document) ValidateDocument(); })) { text = "校验" });
            tools.Add(new ToolbarButton(() => GameplayReferenceEditorWindow.OpenForScope(GameplayReferenceCatalogProvider.Scope(_document))) { text = "Reference" });
            tools.Add(new ToolbarButton(SaveDocument) { text = "保存" });
            tools.Add(readingView);tools.Add(viewCaption);
            tools.Add(new ToolbarButton(() => Canvas.FrameAll()) { text = "全图概览" });
            tools.Add(new ToolbarButton(() => Canvas.FocusMainFlow()) { text = "聚焦主流程" });
            tools.Add(new ToolbarButton(() => Canvas.FocusSelectionReadable()) { text = "聚焦选中" });
            var layoutMenu=new ToolbarMenu{text="布局整理"};
            layoutMenu.menu.AppendAction("重新会话排布（不写坐标）",_=>Canvas.ReflowReading(),_=>Canvas.BusinessReading?DropdownMenuAction.Status.Normal:DropdownMenuAction.Status.Disabled);
            layoutMenu.menu.AppendAction("预览整理（不写入）",_=>Canvas.PreviewLayout(),_=>Canvas.BusinessReading?DropdownMenuAction.Status.Disabled:DropdownMenuAction.Status.Normal);
            layoutMenu.menu.AppendAction("应用预览（一次 Undo）",_=>Canvas.ApplyLayoutPreview(),_=>Canvas.BusinessReading?DropdownMenuAction.Status.Disabled:DropdownMenuAction.Status.Normal);
            layoutMenu.menu.AppendAction("取消预览",_=>Canvas.CancelLayoutPreview());tools.Add(layoutMenu);
            tools.Add(new ToolbarButton(() => { _inspectorOpen=!_inspectorOpen;ApplyPaneWidths();SavePanePreferences(); }) { text = "详情 / 预览 / 试装" });
            tools.Add(new ToolbarButton(()=>EditorWindow.GetWindow<FRFrame.Gameplay.Editor.RuntimeDebugger.GameplayRuntimeDebuggerWindow>().Show()){text="Runtime 调试"});
            rootVisualElement.Add(new IMGUIContainer(DrawSourceBar){style={height=27,flexShrink=0}});
            var workspace=new VisualElement{style={flexDirection=FlexDirection.Row,flexGrow=1,minHeight=0}};
            rootVisualElement.Add(workspace);
            _documentBrowser=new GameplaySkillDocumentBrowser{Select=d=>SelectDocument(d,null,false)};
            _browser = new VisualElement();_browser.style.flexShrink=0;_browser.style.paddingLeft=5;_browser.style.paddingRight=5;_browser.style.backgroundColor=GameplayEditorStyles.Panel;
            _browser.Add(new IMGUIContainer(DrawBrowser){style={flexGrow=1}});workspace.Add(_browser);
            workspace.Add(_browserHandle=PaneHandle(delta=>{_browserWidth=Mathf.Clamp(_browserWidth+delta,200,500);ApplyPaneWidths();},()=>EditorPrefs.SetFloat("FRFrame.Workbench.BrowserWidth",_browserWidth)));
            var content=new VisualElement{style={flexGrow=1,minWidth=0,minHeight=0}};
            workspace.Add(content);
            _welcome=new IMGUIContainer(DrawWelcome){name="SkillWorkbenchWelcome",style={flexGrow=1,paddingLeft=24,paddingRight=24}};
            content.Add(_welcome);
            var row=_editingArea=new VisualElement{style={flexDirection=FlexDirection.Row,flexGrow=1,minHeight=0}};
            content.Add(row);
            toolbar.Add(new ToolbarButton(ShowExamples){text="技能范例"});
            Canvas = new GameplayBehaviorGraphView { ConfigurationOwner=this, ViewModeChanged=reading=>{readingView.SetValueWithoutNotify(reading);viewCaption.text=reading?"会话排布 · 不写坐标":"作者原位";},PolicySelected=role=>{_selected=_document.AuthoringGraph.AbilityNodeId;_definition=null;_policySelection=role;_detailTab=0;_details.MarkDirtyRepaint();},Selected = id => { _policySelection=null;_selected = id; _definition = null;_detailTab=0;if(Canvas.HasInlinePreview(id))_inlinePreviewNode=id; _details.MarkDirtyRepaint(); }, Feedback = message => { _feedback = message; Repaint(); }, Populate = Populate, DeleteMain = DeleteMain,PreviewSeeked=RequestPreviewSeek,PreviewPlaying=id=>_previewClockOwner==id&&_previewPlaying,TogglePreview=TogglePreviewPlayback,HasPreview=HasPreview,DrawInlinePreview=DrawInlinePreview,InlinePreviewChanged=SetInlinePreview,PreviewRequested=id=>{_selected=id;_definition=null;_policySelection=null;_inlinePreviewNode=Canvas.HasInlinePreview(id)?id:null;OpenDetails(1);} };
            row.Add(Canvas);
            row.Add(_inspectorHandle=PaneHandle(delta=>{_inspectorWidth=Mathf.Clamp(_inspectorWidth-delta,300,650);ApplyPaneWidths();},()=>EditorPrefs.SetFloat("FRFrame.Workbench.InspectorWidth",_inspectorWidth)));
            _inspector = new VisualElement();_inspector.style.flexShrink=0;_inspector.style.paddingLeft=7;_inspector.style.paddingRight=7;_inspector.style.backgroundColor=GameplayEditorStyles.Panel;
            _details = new IMGUIContainer(DrawDetails); _details.style.flexGrow = 1; _inspector.Add(_details); row.Add(_inspector);
            rootVisualElement.RegisterCallback<GeometryChangedEvent>(_=>ApplyPaneWidths());
            ApplyPaneWidths();Canvas.Load(_document);Canvas.SelectNodeWithoutCamera(_selected);_shape=Shape();RememberSourceVersion();UpdateSurface();

        }
        private void UndoChanged() {

            _diagnosticsStale=true;DisposePreview();string shape=Shape();
            if(shape!=_shape){Canvas?.Load(_document);_shape=shape;}else Canvas?.SynchronizeLayout();
            Canvas?.SelectNodeWithoutCamera(_selected);RememberSourceVersion();_details?.MarkDirtyRepaint();Repaint();

        }
        private void Changed(GameplaySkillDocumentAsset document,GameplaySkillAuthoringChange change)
        {
            long started=System.Diagnostics.Stopwatch.GetTimestamp();
            bool browserChanged=_documentBrowser?.Changed(document)==true;
            if(document!=_document){if(browserChanged)_browser?.MarkDirtyRepaint();return;}
            _diagnosticsStale=true;if(PreviewAffected(change))DisposePreview();
            if(change.Structural)QueueRebuild();
            else if(change.Unknown)RefreshUnknown();
            else {Canvas?.RefreshPresentation(change);ScalarRefreshCount++;RefreshMeasured?.Invoke(false,(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000d/System.Diagnostics.Stopwatch.Frequency);}
            RememberSourceVersion();_details?.MarkDirtyRepaint();if(browserChanged)_browser?.MarkDirtyRepaint();Repaint();
        }
        private void QueueRebuild()
        {
            if(_refreshPending)return;Canvas?.InvalidateFields();_refreshPending=true;
            rootVisualElement.schedule.Execute(()=>{long start=System.Diagnostics.Stopwatch.GetTimestamp();_refreshPending=false;
                Canvas?.Load(_document);Canvas?.SelectNodeWithoutCamera(_selected);_shape=Shape();RememberSourceVersion();
                RefreshMeasured?.Invoke(true,(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000d/System.Diagnostics.Stopwatch.Frequency);});
        }
        private void RefreshUnknown()
        {
            string shape=Shape();if(shape!=_shape){_shape=shape;QueueRebuild();}
            else {long start=System.Diagnostics.Stopwatch.GetTimestamp();Canvas?.SynchronizeLayout();ScalarRefreshCount++;RefreshMeasured?.Invoke(false,(System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000d/System.Diagnostics.Stopwatch.Frequency);}
        }
        private string Shape(){
            ShapeAnalysisCount++;long sample=System.Diagnostics.Stopwatch.GetTimestamp();
            using(new Unity.Profiling.ProfilerMarker("FRFrame.Workbench.Shape").Auto())try{
            var g=_document?.AuthoringGraph;if(g==null)return "";
            var settings=(g.Nodes.Find(n=>n.Id==g.AbilityNodeId)?.Payload as GameplayAbilityNodePayload)?.Settings as GameplayAbilityDefinitionData;
            return string.Join(",",_detachedPolicies.Select(p=>p.Key))+"|"+settings?.Cost?.GetType().FullName+":"+(settings?.Charge!=null)+"|"+GameplayBehaviorGraphView.InlinePrimaryRule(_document)+":"+g.Scope+":"+g.AbilityNodeId+":"+g.PrimaryRuleNodeId+":"+string.Join(",",g.AdditionalRuleNodeIds)+"|"+
                string.Join("|",g.Nodes.Select(n=>n.Id+":"+n.Payload.GetType().Name+":"+(n.Payload as GameplayActionNodePayload)?.Action?.GetType().FullName+":"+string.Join(",",GameplayGraphFields.OutputPorts(n))+":"+string.Join(",",n.Payload.Definitions.Select(b=>b.PropertyPath+"="+b.LocalId+"@"+(b.External?b.External.GetInstanceID():0)))))+"|"+
                string.Join("|",g.LocalDefinitions.Select(d=>d.Id+":"+d.Payload.GetType().FullName+":"+string.Join(",",GameplayGraphFields.ActionArrays(d.Payload).Select(GameplayGraphEffectCallbacks.ActionKey).Concat(GameplayGraphEffectCallbacks.Slots(d.Payload).Select(s=>s.Key)))+EffectShape(d.Payload)))+"|"+
                string.Join("|",g.Connections.Select(e=>e.Id+":"+e.FromNodeId+":"+e.OutputPortKey+":"+e.ToNodeId+":"+e.InputPortKey+":"+e.Order))+"|"+
                string.Join("|",g.Timelines.SelectMany(t=>t.Events).Select(e=>e.Id+":"+e.EntryNodeId+":"+(e.Conditions.Length>0)+":"+e.Target?.GetType().FullName));
            static string EffectShape(GameplayLocalDefinitionPayload payload) => payload is GameplayEffectDefinitionData effect
                ? ":" + effect.DurationPolicy + ":" + (effect.Period > 0) + ":" + effect.Stacking?.Overflow + ":" +
                    string.Join(",", effect.Stacking?.OverflowExecutions.Select(e => e.AuthoringId + e.GetType().Name) ?? Enumerable.Empty<string>()) + ":" +
                    string.Join(",", effect.CompletionExecutions.Select(e => e.AuthoringId + e.GetType().Name)) : "";
            }
            finally{ShapeAnalysisMilliseconds+=(System.Diagnostics.Stopwatch.GetTimestamp()-sample)*1000d/System.Diagnostics.Stopwatch.Frequency;}
        }
        private void DrawBrowser()
        {
            _browserTab=_document?GUILayout.Toolbar(_browserTab,new[]{"文件夹 / 技能","未接入 / 资源"}):0;
            if(_browserTab==0){_documentBrowser.Draw(Documents(),_document,_browser.resolvedStyle.width);return;}
            _browserScroll = EditorGUILayout.BeginScrollView(_browserScroll);
            if (_document?.AuthoringGraph != null)
            {
                var graph = _document.AuthoringGraph; var active = GameplayBehaviorGraphView.Reachable(graph);
                GUILayout.Space(12); GUILayout.Label("未安排行为", EditorStyles.boldLabel);
                foreach (var node in graph.Nodes.Where(n => !active.Contains(n.Id)))
                    if (GUILayout.Button(GameplaySkillFieldSchema.NodeName(node), EditorStyles.miniButton)) { _selected = node.Id; _definition = null; Canvas.FocusNode(node.Id); }
                var used = new HashSet<string>(graph.Nodes.SelectMany(n => n.Payload.Definitions).Concat(graph.LocalDefinitions.SelectMany(d => d.Definitions)).Select(b => b.LocalId));
                GUILayout.Label("资源 / 未使用配置", EditorStyles.boldLabel);
                foreach (var d in graph.LocalDefinitions.Where(d => !used.Contains(d.Id)).ToArray()){
                    GUILayout.BeginHorizontal();if (GUILayout.Button(d.Name+" · 未使用", EditorStyles.miniButton)) { _definition = d.Id; _selected = null; }
                    if(GUILayout.Button(new GUIContent("删除","删除这项未使用局部配置及独占依赖；一次 Undo 可恢复。"),GUILayout.Width(44))){Attempt(()=>GameplaySkillAuthoringCommands.DeleteLocalDefinition(_document,d.Id));GUIUtility.ExitGUI();}GUILayout.EndHorizontal();
                }
                foreach (var legacy in graph.LegacyResources.ToArray())
                { using (new EditorGUI.DisabledScope(true)) EditorGUILayout.ObjectField(new GUIContent("只读历史",legacy.Reason), legacy.Resource, typeof(UnityEngine.Object), false);
                    if(GUILayout.Button(new GUIContent("移除历史登记（保留原资源）","只撤销此归档引用，不删除原 SO。原内容保留至另一次显式物理清理审计。"),EditorStyles.miniButton)){
                        Attempt(()=>GameplaySkillAuthoringCommands.Transaction(_document,"移除历史登记",g=>g.LegacyResources.RemoveAt(graph.LegacyResources.IndexOf(legacy))));GUIUtility.ExitGUI();
                    }
                }
                foreach (var shared in graph.Nodes.SelectMany(n => n.Payload.Definitions).Concat(graph.LocalDefinitions.SelectMany(d => d.Definitions)).Where(b => b.External).Select(b => b.External).Distinct())
                    if (GUILayout.Button("共享 · " + shared.name, EditorStyles.miniButton)) { Selection.activeObject = shared; EditorGUIUtility.PingObject(shared); }
            }
            EditorGUILayout.EndScrollView();
        }
        private void DrawDetails()
        {

            float previousLabel=EditorGUIUtility.labelWidth;EditorGUIUtility.labelWidth=Mathf.Clamp(_inspector.resolvedStyle.width*.4f,110,170);
            _detailTab=GUILayout.Toolbar(_detailTab,new[]{"业务参数","预览 / 范围","Scene / 调试","校验"});
            _inspectorScroll = EditorGUILayout.BeginScrollView(_inspectorScroll);
            if (!string.IsNullOrEmpty(_feedback)) EditorGUILayout.HelpBox(_feedback, MessageType.Info);
            if(!_document&&_sharedSource){using(var source=new SerializedObject(_sharedSource))GameplayAuthoringInspector.Draw(source);}
            else if (!_document) EditorGUILayout.HelpBox("选择正式技能，或新建一个正式技能。", MessageType.Info);
            else {
                if(_detailTab==0){
                    if(_policySelection!=null)Canvas.DrawPolicy(_policySelection,true);
                    else if(Canvas.SelectedEventId==null){if(!Canvas.IsTimelineInline(_selected)||_definition!=null)using(GameplayAuthoringInspector.UseFieldContext("Inspector"))GameplaySkillFieldSchema.Draw(Canvas.SerializedSource,_document,_selected,_definition,true);}
                    else {GUILayout.Label("选中调度事件 · 行为参数在卡片编辑",EditorStyles.wordWrappedLabel);if(GUILayout.Button("行为高级参数")){Canvas.SelectNodeWithoutCamera(_selected);Canvas.ClearEventSelection();}}
                    if(_policySelection==null&&_definition==null&&Canvas.SelectedEventId==null)Canvas.DrawMainAnimationSettings(_selected);
                    DrawTimelineFields(Canvas.SerializedSource,_document,_selected,Canvas.SelectedEventId,Canvas);
                }
                else if(_detailTab==1){DrawRangePreview();DrawPreview();}
                else if(_detailTab==2){DrawInstallation();if(GUILayout.Button("打开 Runtime Debugger"))EditorWindow.GetWindow<FRFrame.Gameplay.Editor.RuntimeDebugger.GameplayRuntimeDebuggerWindow>().Show();}else DrawDiagnostics();
                GUILayout.Space(10);
                if (_selected != null && GUILayout.Button("在图中定位")) Canvas.FocusNode(_selected);
                if(Canvas.IsTimelineInline(_selected)&&GUILayout.Button("在画布展开独立卡片"))Canvas.RevealTimelineAction(_selected);
                if (GUILayout.Button("主技能 Project Inspector")) Selection.activeObject = _document;
            }
            EditorGUILayout.EndScrollView();EditorGUIUtility.labelWidth=previousLabel;

        }
        private void DrawInstallation()
        {
            if(_document.AuthoringGraph==null)return;var host=Hosts.FirstOrDefault(h=>h.Supports(_document));if(host==null){EditorGUILayout.HelpBox("当前 Scope 未注册工作台试装 Host；请使用该游戏现有的 SkillData 安装链。",MessageType.Info);return;}
            GUILayout.Space(10);GUILayout.Label("真实 Scene 试装",EditorStyles.boldLabel);host.DrawSetup(_document);string reason=host.Unavailable(_document);
            if(reason!=null)EditorGUILayout.HelpBox(reason,MessageType.Info);
            using(new EditorGUI.DisabledScope(reason!=null))if(GUILayout.Button(_installation==null?"安装当前正式技能":"释放并重装"))Attempt(()=>{DisposeInstallation();_installation=host.Install(_document);});
            _installation?.Draw();if(_installation!=null&&GUILayout.Button("结束本窗口试装"))DisposeInstallation();
        }

        public static void DrawTimelineFields(GameplaySkillDocumentAsset document, string nodeId,string selectedEventId=null)
        {
            using(var serialized=new SerializedObject(document))DrawTimelineFields(serialized,document,nodeId,selectedEventId);
        }
        private static void DrawTimelineFields(SerializedObject serialized,GameplaySkillDocumentAsset document,string nodeId,string selectedEventId,GameplayBehaviorGraphView canvas=null)
        {
            var node = document.AuthoringGraph?.Nodes.Find(n => n.Id == nodeId); if (node == null) return;
            foreach (var timeline in document.AuthoringGraph.Timelines.Where(t=>node.Payload.Definitions.Any(b=>b.LocalId==t.Id)||t.Events.Any(e=>e.EntryNodeId==node.Id)))
            {
                bool owns=node.Payload.Definitions.Any(b=>b.LocalId==timeline.Id);
                {
                    serialized.UpdateIfRequiredOrScript();
                    var p = serialized.FindProperty("authoringGraph.Timelines").GetArrayElementAtIndex(document.AuthoringGraph.Timelines.IndexOf(timeline));
                    using var disabled=new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode||GameplaySkillAuthoringCommands.IsReadOnly(document));
                    var paths=new HashSet<string>();EditorGUI.BeginChangeCheck(); GUILayout.Label("时间编排", EditorStyles.boldLabel);
                    if(owns)foreach (var f in new[] { "Name", "DurationSeconds", "FramesPerSecond" }){var field=p.FindPropertyRelative(f);EditorGUI.BeginChangeCheck();EditorGUILayout.PropertyField(field,new GUIContent(f=="Name"?"编排名称":f=="DurationSeconds"?"总时长（秒）":"帧率 / 吸附"),true);if(EditorGUI.EndChangeCheck())paths.Add(field.propertyPath);}
                    for (int i = 0; i < timeline.Events.Count; i++)
                    {
                        if(selectedEventId==null&&canvas?.IsTimelineInline(timeline.Events[i].EntryNodeId)==true)continue;
                        if(selectedEventId!=null&&timeline.Events[i].Id!=selectedEventId)continue;
                        if(!owns&&timeline.Events[i].EntryNodeId!=node.Id)continue;
                        if(!owns&&selectedEventId!=null&&timeline.Events.Any(e=>e.Id==selectedEventId&&e.EntryNodeId==node.Id)&&timeline.Events[i].Id!=selectedEventId)continue;
                        var entry = p.FindPropertyRelative("Events").GetArrayElementAtIndex(i); GUILayout.Label((timeline.Events[i].TimeSeconds*timeline.FramesPerSecond).ToString("0.##")+"f · "+GameplaySkillFieldSchema.NodeName(document.AuthoringGraph.Nodes.Find(n=>n.Id==timeline.Events[i].EntryNodeId))+" · #"+timeline.Events[i].Order, EditorStyles.boldLabel);
                        var time=entry.FindPropertyRelative("TimeSeconds");
                        float frame=time.floatValue*timeline.FramesPerSecond;float next=EditorGUILayout.FloatField("帧（秒数为运行权威）",frame);if(next!=frame){time.floatValue=next/timeline.FramesPerSecond;paths.Add(time.propertyPath);}
                        EditorGUI.BeginChangeCheck();
                        GameplayAuthoringInspector.Field(time,new GUIContent("时间（秒）"));
                        if(EditorGUI.EndChangeCheck())paths.Add(time.propertyPath);
                        DrawScheduleOptions(entry,paths);
                    }
                    if (EditorGUI.EndChangeCheck()) GameplaySkillFieldSchema.ApplyFields(serialized,document,false,paths);
                }
            }
        }
        internal static void DrawScheduleOptions(SerializedProperty entry,HashSet<string> paths)
        {
            foreach(var name in new[]{"Order","Conditions","Target"}){
                var field=entry.FindPropertyRelative(name);
                EditorGUI.BeginChangeCheck();
                GameplayAuthoringInspector.Field(field);
                if(EditorGUI.EndChangeCheck())paths.Add(field.propertyPath);
            }
        }

        private void Populate(IGameplaySkillCreationMenu menu, Vector2 position, string timeline, float? seconds)
        {
            if (!_document || _document.AuthoringGraph == null) return;
            var link=Canvas.CreationLink;
            bool effectLink=link!=null&&!link.Scheduled&&link.Input=="Execute"&&GameplayGraphEffectCallbacks.IsActionPort(link.Port)&&
                _document.AuthoringGraph.LocalDefinitions.Any(d=>d.Id==link.Owner&&d.Payload is GameplayEffectDefinitionData);
            // Inspect candidates against the real source contract without editing the document.
            var candidates=link==null?null:(GameplayAuthoringGraph)GameplayGraphFields.Clone(_document.AuthoringGraph);
            var candidate=new GameplayAuthoringNode{Payload=new GameplayActionNodePayload()};
            var candidateValue=new GameplayLocalDefinition();
            Func<string,string,string,string,bool,string> check=null;
            if(candidates!=null){candidates.Nodes.Add(candidate);candidates.LocalDefinitions.Add(candidateValue);check=GameplayGraphContextPolicy.ConnectionChecker(candidates);}
            string ContextReason(GameplaySkillActionAuthoring action,GameplayLocalDefinitionPayload value=null,string path=null){
                if(check==null)return null;
                if(link.Input==GameplayGraphEffectCallbacks.Input)return (action is GameplayApplyToTargetsActionAuthoring||action is GameplaySkillApplyEffectActionAuthoring)&&value is GameplayEffectDefinitionData?null:"生命周期回调只接收效果定义，不执行普通行为；请选择伤害、治疗或Buff。";
                ((GameplayActionNodePayload)candidate.Payload).Action=action;candidate.Payload.Definitions.Clear();candidateValue.Payload=value;
                if(value!=null)candidate.Payload.Definitions.Add(new GameplayDefinitionBinding{PropertyPath=path,LocalId=candidateValue.Id});
                var reason=check(link.Owner,link.Port,candidate.Id,link.Input,link.Scheduled);
                return reason==null?null:reason+" 请改从搜索逐目标/弹体命中连接，或明确使用自身目标。";
            }
            if(timeline!=null){
                foreach(var node in _document.AuthoringGraph.Nodes.Where(n=>n.Payload is GameplayActionNodePayload)){
                    string id=node.Id;menu.AddItem(new GUIContent("安排已有行为/"+GameplaySkillFieldSchema.NodeName(node)+" · "+id.Substring(0,5)),false,()=>Attempt(()=>GameplaySkillAuthoringCommands.Schedule(_document,timeline,id,seconds??0)));
                }
            }
            if (timeline == null)
            {
                if(link==null)Canvas.AddConfigurationEntries(menu,position);
                if(link?.Input==GameplayGraphEffectCallbacks.Input)menu.AddDisabledItem(new GUIContent("技能/时间编排","效果生命周期只消费真实Effect定义，不能调用时间编排。"));
                else if(!effectLink)menu.AddItem(new GUIContent("技能/时间编排"), false, () => Create(() => GameplaySkillAuthoringCommands.CreateSequence(_document, position,link)));
                if(link==null)menu.AddItem(new GUIContent("[模板]/逐目标伤害与结束"), false, () => Create(() => GameplaySkillAuthoringCommands.CreateDamageTemplate(_document,position)));
                if(link==null)menu.AddItem(new GUIContent(_document.AuthoringGraph.PrimaryRuleNodeId==null?"技能/施放触发规则":"技能/附加触发规则"), false, () => Create(() => GameplaySkillAuthoringCommands.CreateRule(_document, position)));
            }
            string effectReason=ContextReason(new GameplayApplyToTargetsActionAuthoring(),new GameplayEffectDefinitionData(),"Effect");
            if(effectReason!=null){foreach(string title in new[]{"效果/瞬时/造成伤害","效果/瞬时/治疗","效果/持续与控制/Buff","效果/持续与控制/周期伤害（3层）","效果/持续与控制/周期治疗"})menu.AddDisabledItem(new GUIContent(title,effectReason));}
            else{
                menu.AddItem(new GUIContent("效果/瞬时/造成伤害"), false, () => Effect("造成伤害", new GameplayDamageExecutionAuthoring()));
                menu.AddItem(new GUIContent("效果/瞬时/治疗"), false, () => Effect("治疗", new GameplayHealExecutionAuthoring()));
                menu.AddItem(new GUIContent("效果/持续与控制/Buff"), false, () => Create(() => GameplaySkillAuthoringCommands.CreateAction(_document, new GameplayApplyToTargetsActionAuthoring(), "持续 Buff", position,
                    new GameplayEffectDefinitionData { DurationPolicy = GameplayEffectDurationPolicy.Duration, Duration = 10 }, "Effect", timeline, seconds,link)));
                menu.AddItem(new GUIContent("效果/持续与控制/周期伤害（3层）","真实持续Effect：4秒、每0.5秒伤害、最多3层；时长/周期/结算一起创建，后续由原字段编辑。"), false, () => Create(() => {string key="Effect.Stack.S"+_document.SkillId+".P"+Guid.NewGuid().ToString("N");return GameplaySkillAuthoringCommands.CreateAction(_document, new GameplayApplyToTargetsActionAuthoring(), "周期伤害", position,
                    new GameplayEffectDefinitionData { DurationPolicy=GameplayEffectDurationPolicy.Duration,Duration=4,Period=.5f,
                        Executions=new GameplayEffectExecutionAuthoring[]{new GameplayDamageExecutionAuthoring()},
                        Stacking=new GameplayEffectStackingAuthoring{StackKey=new GameplayTagReference(key),MaxStacks=3} },"Effect",timeline,seconds,link,new[]{key});}));
                menu.AddItem(new GUIContent("效果/持续与控制/周期治疗","真实持续Effect：4秒、每0.5秒治疗；不生成虚假的周期执行端口。"), false, () => Create(() => GameplaySkillAuthoringCommands.CreateAction(_document,new GameplayApplyToTargetsActionAuthoring(),"周期治疗",position,
                    new GameplayEffectDefinitionData{DurationPolicy=GameplayEffectDurationPolicy.Duration,Duration=4,Period=.5f,Executions=new GameplayEffectExecutionAuthoring[]{new GameplayHealExecutionAuthoring()}},"Effect",timeline,seconds,link)));
            }
            foreach (var type in GameplayAuthoringTypeCatalog.Query(typeof(GameplaySkillActionAuthoring), _document.AuthoringGraph.Scope))
            {
                if (type == typeof(GameplayOwnedSequenceActionAuthoring) || type == typeof(GameplaySkillPlaySequenceActionAuthoring) || type == typeof(GameplaySkillApplyEffectActionAuthoring)) continue;
                var unavailable=GameplaySkillTypeAvailability.Reason(_document,type);if(unavailable!=null){menu.AddDisabledItem(new GUIContent(GameplaySkillNodeDescriptors.Category(type)+"/"+GameplaySkillNodeDescriptors.Get(type).DisplayName+" · 不可用",unavailable));continue;}var descriptor = GameplaySkillNodeDescriptors.Get(type); var action = (GameplaySkillActionAuthoring)Activator.CreateInstance(type);
                var slot = GameplayGraphFields.ResourceSlots(action).FirstOrDefault();
                var values = slot == null ? new Type[0] : GameplaySkillFieldSchema.Values(slot.ValueType).ToArray();
                string category=GameplaySkillNodeDescriptors.Category(type);
                if(values.Length==0)AddOption(descriptor.DisplayName,descriptor.DisplayName,null);
                foreach(var valueType in values){
                    var presets=GameplaySkillFieldSchema.CreationPresets(valueType).ToArray();
                    if(presets.Length==0)
                        AddOption(descriptor.DisplayName+" · "+GameplaySkillFieldSchema.Name(valueType),descriptor.DisplayName,(GameplayLocalDefinitionPayload)Activator.CreateInstance(valueType));
                    else
                        foreach(var preset in presets)AddOption(preset.Name,preset.Name,preset.Value);
                }

                void AddOption(string label,string title,GameplayLocalDefinitionPayload value)
                {
                    string reason=ContextReason(action,value,slot?.Path);
                    if(effectLink&&reason!=null)return;
                    var caption=new GUIContent(category+"/"+label,reason??descriptor.Summary);
                    if(reason!=null)menu.AddDisabledItem(caption);
                    else menu.AddItem(caption,false,()=>Create(()=>GameplaySkillAuthoringCommands.CreateAction(
                        _document,(GameplaySkillActionAuthoring)Activator.CreateInstance(type),title,position,value,slot?.Path,timeline,seconds,link)));
                }
            }
            void Effect(string title, GameplayEffectExecutionAuthoring execution) => Create(() => GameplaySkillAuthoringCommands.CreateAction(_document, new GameplayApplyToTargetsActionAuthoring(), title, position,
                new GameplayEffectDefinitionData { DurationPolicy = GameplayEffectDurationPolicy.Instant, Executions = new[] { execution } }, "Effect", timeline, seconds,link));
        }
        private void Create(Func<string> command)
        { Attempt(() => { _policySelection=null;Canvas.ClearEventSelection(); _selected = command(); _definition = null;_details.MarkDirtyRepaint(); }); }
        private void DeleteMain()
        {

            if (!_document) return;
            var target = _document;
            int choice = EditorUtility.DisplayDialogComplex("删除主技能", "清空内容保留技能 ID、名称、图标和主文件。物理删除前会审计外部引用；仍被使用的文件不会删除。", "清空内容保留主技能", "取消", "删除整个主文件");
            if (choice == 0) Attempt(() => GameplaySkillAuthoringCommands.Clear(target));
            else if (choice == 2) Attempt(() => GameplaySkillAssetDeletion.Begin(target,(deleted,message)=>{
                _feedback=message;
                if(deleted&&_document==target)SelectDocument(null);
                Repaint();
            }));

        }
        private void NewDocument()
        {
if(EditorApplication.isPlayingOrWillChangePlaymode){_feedback="退出 Play 后创建正式技能。";return;}GameplaySkillWizard.Open(this);
        }
        private void Attempt(Action action) { try { action(); } catch (Exception e) { _feedback = e.Message; Repaint(); } }
    }
    internal static class GameplaySkillToolbarExtensions
    {
        internal static ToolbarToggle WithChanged(this ToolbarToggle toggle, Action<bool> callback)
        { toggle.RegisterValueChangedCallback(e => callback(e.newValue)); return toggle; }
    }
}
