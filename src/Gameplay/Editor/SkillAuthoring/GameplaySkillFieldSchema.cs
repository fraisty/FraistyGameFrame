using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Integration-owned value editors extend the same embedded Inspector. Providers
    /// describe actual resource types; they never create an intermediate ScriptableObject.</summary>
    public interface IGameplaySkillValueEditor
    {
        IEnumerable<Type> ValueTypes(Type resourceType);
        string Name(Type valueType);
        bool DrawField(SerializedProperty root, SerializedProperty field);
    }
    /// <summary>Typed integration metadata for a real constant presentation duration.
    /// Unknown/dynamic durations remain markers rather than fictitious resizable intervals.</summary>
    public interface IGameplaySkillValueDuration { bool TryReadDuration(object values,out string path,out float seconds); }
    public interface IGameplaySkillValueSections
    {
        IEnumerable<GameplayBusinessInspectorGroup> Sections(object values);
        /// <summary>Whether the field applies to the current embedded or shared resource.</summary>
        bool IsFieldVisible(object values,string field);
    }
    /// <summary>Names independently creatable variants of one integration-owned value type.</summary>
    public interface IGameplaySkillValueCreationPresets
    {
        IEnumerable<(string Name, GameplayLocalDefinitionPayload Value)> CreationPresets(Type valueType);
    }
    public interface IGameplaySkillPreviewFactory
    {
        bool Supports(object values);
        IGameplaySkillPreviewSession Create(GameplaySkillDocumentAsset document, object values, float startSeconds);
    }

    /// <summary>One field renderer for the Project Inspector and Workbench. Relationships are
    /// rendered from explicit bindings/ports; legacy SO slots and nested action arrays stay empty.</summary>
    public static class GameplaySkillFieldSchema
    {
        private static readonly IGameplaySkillValueEditor[] Providers = TypeCache.GetTypesDerivedFrom<IGameplaySkillValueEditor>()
            .Where(t => !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(t => t.FullName).Select(t => (IGameplaySkillValueEditor)Activator.CreateInstance(t)).ToArray();
        private static readonly IGameplaySkillValueSections[] ValueSections=Providers.OfType<IGameplaySkillValueSections>().ToArray();
        private static readonly Dictionary<Type, Type> CoreValues = new Dictionary<Type, Type> {
            {typeof(GameplayEffectDefinitionAsset),typeof(GameplayEffectDefinitionData)},
            {typeof(GameplayAbilityDefinitionAsset),typeof(GameplayAbilityDefinitionData)},
            {typeof(GameplayContinuationDefinitionAsset),typeof(GameplayContinuationDefinitionData)},
            {typeof(GameplayProjectileDefinitionAsset),typeof(GameplayProjectileDefinitionData)},
            {typeof(GameplayAreaDefinitionAsset),typeof(GameplayAreaDefinitionData)},
            {typeof(GameplaySummonDefinitionAsset),typeof(GameplaySummonDefinitionData)} };
        private sealed class EditState { internal bool Structural; internal readonly HashSet<string> Paths=new HashSet<string>(); }
        private static HashSet<string> _fieldChanges;
        private sealed class FieldCapture:IDisposable { private readonly HashSet<string> _previous;internal FieldCapture(EditState edit){_previous=_fieldChanges;_fieldChanges=edit.Paths;}public void Dispose(){_fieldChanges=_previous;} }
        private static void Field(SerializedProperty property,GUIContent caption=null){if(property==null||CardSummary(property))return;EditorGUI.BeginChangeCheck();if(property.propertyType==SerializedPropertyType.Enum){var options=GameplaySkillBusinessLabels.Options(property).Select((label,i)=>new GUIContent(label,property.enumNames[i])).ToArray();property.enumValueIndex=EditorGUILayout.Popup(caption??GameplayAuthoringInspector.Label(property),property.enumValueIndex,options);}else GameplayAuthoringInspector.Field(property,caption??GameplayAuthoringInspector.Label(property));if(EditorGUI.EndChangeCheck())_fieldChanges?.Add(property.propertyPath);}
        private static readonly HashSet<string> Open = new HashSet<string>();
        private static readonly HashSet<string> ClosedSections = new HashSet<string>();
        private static readonly Dictionary<int,string> Errors = new Dictionary<int,string>();
        private static HashSet<string> _cardPaths;
        public static string Error(GameplaySkillDocumentAsset document) => Errors.TryGetValue(document.GetInstanceID(),out var error)?error:null;
        // managedReferenceValue/boxedValue return the live managed object, not pending native
        // child edits. Overlay only supplied fields on a disposable value clone before validation.
        internal static void SetPending(object owner,string relative,SerializedProperty pending)
        {
            string[] parts=relative.Replace(".Array.data[","[").Split('.');
            Assign(owner,0);
            object Assign(object current,int at)
            {
                string part=parts[at];int bracket=part.IndexOf('[');
                var field=GameplayGraphFields.SerializedFields(current.GetType()).Single(f=>f.Name==(bracket<0?part:part.Substring(0,bracket)));
                object child=field.GetValue(current);
                if(bracket>=0){var array=child as System.Collections.IList;int index=int.Parse(part.Substring(bracket+1,part.Length-bracket-2));array[index]=at==parts.Length-1?PendingValue(pending,field.FieldType.IsArray?field.FieldType.GetElementType():field.FieldType.GetGenericArguments()[0]):Assign(array[index],at+1);}
                else field.SetValue(current,at==parts.Length-1?PendingValue(pending,field.FieldType):Assign(child,at+1));
                return current;
            }
        }
        private static object PendingValue(SerializedProperty property,Type declared=null)
        {
            if(property.propertyType==SerializedPropertyType.Enum)return Enum.ToObject(declared,property.intValue);
            if(property.isArray&&property.propertyType!=SerializedPropertyType.String){
                Type element=declared.IsArray?declared.GetElementType():declared.GetGenericArguments()[0];
                var list=declared.IsArray?(System.Collections.IList)Array.CreateInstance(element,property.arraySize):(System.Collections.IList)Activator.CreateInstance(declared);
                for(int i=0;i<property.arraySize;i++){var item=PendingValue(property.GetArrayElementAtIndex(i),element);if(declared.IsArray)list[i]=item;else list.Add(item);}return list;
            }
            object value=property.boxedValue;
            if(value==null||property.propertyType!=SerializedPropertyType.Generic&&property.propertyType!=SerializedPropertyType.ManagedReference)return value;
            var copy=GameplayGraphFields.Clone(value);
            foreach(var field in GameplayGraphFields.SerializedFields(copy.GetType())){var child=property.FindPropertyRelative(field.Name);if(child!=null)field.SetValue(copy,PendingValue(child,field.FieldType));}
            return copy;
        }
        /// <summary>Captures the actual initiating binding, including nested local-source routes.
        /// Direct local-definition editing has no node binding and uses its own source lifetime.</summary>
        public static Func<bool> CaptureBinding(GameplaySkillDocumentAsset document,string ownerId,string path,string expectedLocalId)
        {
            var parent=CurrentSourceGuard;
            return ()=>{
                if(!document||parent!=null&&!parent())return false;
                var graph=document.AuthoringGraph;
                var bindings=graph.Nodes.Find(n=>n.Id==ownerId)?.Payload.Definitions??graph.LocalDefinitions.Find(d=>d.Id==ownerId)?.Definitions;
                return bindings?.Any(b=>b.PropertyPath==path&&b.LocalId==expectedLocalId&&!b.External)==true;
            };
        }
        /// <summary>The current IMGUI source route; providers capture it before deferring a write.</summary>
        public static Func<bool> CurrentSourceGuard {get;private set;}
        private sealed class SourceScope:IDisposable
        {
            private readonly Func<bool> _previous=CurrentSourceGuard;
            internal SourceScope(Func<bool> guard){CurrentSourceGuard=guard;}
            public void Dispose(){CurrentSourceGuard=_previous;}
        }
        private sealed class CardResponsibility:IDisposable
        {
            private readonly HashSet<string> _previous=_cardPaths;
            internal CardResponsibility(GameplaySkillDocumentAsset document,string nodeId,bool enabled,SerializedObject serialized,bool animationHeader)
            { _cardPaths=enabled?new HashSet<string>(GameplaySkillCardFields.Describe(document,nodeId,serialized,animationHeader).Where(f=>f.Editable).Select(f=>f.Path)):null; }
            public void Dispose(){_cardPaths=_previous;}
        }
        private static bool CardSummary(SerializedProperty property)
        {
            if(_cardPaths?.Contains(property.propertyPath)!=true)return false;
            return true; // The card is the primary presentation; do not repeat scalar labels here.
        }
        public static void RevealField(GameplaySkillDocumentAsset document,string path)
        {
            ClosedSections.RemoveWhere(key=>key.StartsWith(document.GetInstanceID()+":",StringComparison.Ordinal));
            using(var serialized=new SerializedObject(document)){string current="";foreach(string part in path.Split('.')){current=current.Length==0?part:current+"."+part;var property=serialized.FindProperty(current);if(property!=null)property.isExpanded=true;}}
        }
        /// <summary>Validates the pending native serialized buffer before Apply records Undo or
        /// dirties the asset. Rejected field changes are discarded without touching the source.</summary>
        public static bool ApplyFields(SerializedObject serialized, GameplaySkillDocumentAsset document, bool validateStructure = true, IEnumerable<string> changedPaths = null)
        {
            if(GameplaySkillAuthoringCommands.IsReadOnly(document)){serialized.Update();return false;}
            if(!serialized.hasModifiedProperties)return false;
            try{
                if(changedPaths!=null&&changedPaths.Any(path=>path.EndsWith(".Action.Key",StringComparison.Ordinal)||path.EndsWith(".Action.Key.Name",StringComparison.Ordinal))){
                    var paths=new HashSet<string>(changedPaths);
                    GameplaySkillContextVariables.For(document).StageRenames(serialized,paths);
                    changedPaths=paths;
                }
                var invalidation=GameplaySkillAuthoringChange.Fields(document,changedPaths,validateStructure);
                if(GameplayBehaviorGraphView.FollowsMainAnimation(document)){
                    var followed=(GameplayAuthoringGraph)PendingValue(serialized.FindProperty("authoringGraph"));
                    GameplayBehaviorGraphView.FollowMainAnimationDurations(document,followed);
                    for(int i=0;i<followed.Timelines.Count;i++)serialized.FindProperty("authoringGraph.Timelines").GetArrayElementAtIndex(i).FindPropertyRelative("DurationSeconds").floatValue=followed.Timelines[i].DurationSeconds;
                    invalidation=GameplaySkillAuthoringChange.Full(invalidation.Structural);
                }
                if(validateStructure||invalidation.Structural||invalidation.Context){
                var staged=changedPaths==null?(GameplayAuthoringGraph)PendingValue(serialized.FindProperty("authoringGraph")):(GameplayAuthoringGraph)GameplayGraphFields.Clone(document.AuthoringGraph);
                if(changedPaths!=null)foreach(string path in changedPaths.Where(p=>p.StartsWith("authoringGraph.",StringComparison.Ordinal)))SetPending(staged,path.Substring(15),serialized.FindProperty(path));
                var validation=new GameplayAuthoringValidationContext();GameplayGraphCompiler.ValidateStructure(staged,validation,"字段",false);
                if(validation.HasErrors)throw new InvalidOperationException(string.Join("\n",validation.Issues));
                GameplayGraphCompiler.ValidateActionOrder(staged);
                }
                Errors.Remove(document.GetInstanceID());bool changed=serialized.ApplyModifiedProperties();if(changed)GameplaySkillAuthoringCommands.NotifyFields(document,invalidation);return changed;
            }catch(Exception error){Errors[document.GetInstanceID()]=error.Message;serialized.Update();return false;}
        }
        public static IEnumerable<Type> Values(Type resource) => CoreValues.Where(p => resource.IsAssignableFrom(p.Key)).Select(p => p.Value)
            .Concat(Providers.SelectMany(p => p.ValueTypes(resource))).Distinct();
        internal static IEnumerable<(string Name, GameplayLocalDefinitionPayload Value)> CreationPresets(Type valueType)
            => Providers.OfType<IGameplaySkillValueCreationPresets>().SelectMany(p => p.CreationPresets(valueType));
        internal static bool IsValueFieldVisible(object values,string field)
        {
            foreach(var provider in ValueSections)
                if(!provider.IsFieldVisible(values,field))return false;
            return true;
        }
        public static bool TryDuration(GameplayAuthoringGraph graph,string nodeId,out string localId,out string path,out float seconds){
            localId=null;path=null;seconds=0;var node=graph.Nodes.Find(n=>n.Id==nodeId);if(node==null)return false;
            foreach(var binding in node.Payload.Definitions){var local=graph.LocalDefinitions.Find(d=>d.Id==binding.LocalId);object values=local?.Payload;if(values==null&&binding.External){try{values=binding.External.CaptureGraphValues();}catch(NotSupportedException){}}if(values==null)continue;
                foreach(var provider in Providers.OfType<IGameplaySkillValueDuration>())if(provider.TryReadDuration(values,out path,out seconds)){localId=local?.Id;return seconds>0;}}
            return false;
        }
        public static string FloatSummary(GameplayFloatValueAuthoring value)=>GameplaySkillNodeDisplay.Value(value);
        public static string ValueSummary(object values)=>Providers.OfType<IGameplaySkillValueDescription>().Select(p=>p.Summary(values)).FirstOrDefault(s=>s!=null);
        public static string Name(Type type) => Providers.Select(p => p.Name(type)).FirstOrDefault(n => n != null) ??
            (type == typeof(GameplayEffectDefinitionData) ? "效果 / Buff" : type == typeof(GameplayProjectileDefinitionData) ? "逻辑弹体" :
             type == typeof(GameplayAreaDefinitionData) ? "逻辑区域" : type == typeof(GameplaySummonDefinitionData) ? "逻辑召唤" :
             type == typeof(GameplayAbilityDefinitionData) ? "释放 / 消耗 / 冷却" : "后续规则");

        public static string NodeName(GameplayAuthoringNode node)
        {
            if (!string.IsNullOrEmpty(node.Title)) return node.Title;
            if (node.Payload is GameplayAbilityNodePayload) return "技能施放";
            if (node.Payload is GameplayRuleNodePayload) return "触发规则";
            var action = (node.Payload as GameplayActionNodePayload)?.Action;
            return action == null ? "缺失行为" : GameplaySkillNodeDescriptors.Get(action.GetType()).DisplayName;
        }
        public static string PortName(string path)
        {
            if(path==GameplayGraphEffectCallbacks.Completion)return "自然完成 · 添加效果";
            if(path==GameplayGraphEffectCallbacks.Overflow)return "叠层溢出 · 添加效果";
            if(path.StartsWith("Completion/",StringComparison.Ordinal))return "自然完成 · 续接效果";
            if(path.StartsWith("Overflow/",StringComparison.Ordinal))return "叠层溢出 · 续接效果";
            if (path == "Then") return "后续执行";
            var key = path.Split('.').Last();
            return key == "Actions" ? "执行" : key == "EachTarget" ? "每个目标" : key == "Completed" ? "搜索完成" :
                key == "NoTargets" ? "无目标" : key == "TrueActions" ? "满足条件" : key == "FalseActions" ? "不满足" :
                key == "OnHit" ? "命中" : key == "OnArrive" ? "到达" : key == "OnRange" ? "射程结束" :
                key == "OnEnter" ? "进入" : key == "OnExit" ? "离开" : key == "OnTick" ? "周期" : key=="OnStay"?"持续停留（每步）":
                key == "OnEnd" ? "结束" : key == "OnCancelled" ? "取消" : ObjectNames.NicifyVariableName(path);
        }

        public static void Draw(GameplaySkillDocumentAsset document, string nodeId, string definitionId = null, bool cardFields = false)
        {
            if(!document)return;
            using(var serialized=new SerializedObject(document))Draw(serialized,document,nodeId,definitionId,cardFields);
        }
        /// <summary>Draws against the canvas-owned buffer; does not dispose it or cache property paths.</summary>
        internal static void Draw(SerializedObject serialized,GameplaySkillDocumentAsset document,string nodeId,string definitionId,bool cardFields,bool animationHeader=false)
        {
            if (!document) return;
            if(Errors.TryGetValue(document.GetInstanceID(),out string error))EditorGUILayout.HelpBox(error,MessageType.Error);
            if (document.AuthoringSchemaVersion != 2) { EditorGUILayout.HelpBox("此数据格式不能直接编辑；请选择 Schema 2 正式技能。", MessageType.Warning); return; }
            using (new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode||GameplaySkillAuthoringCommands.IsReadOnly(document)))
            using (new CardResponsibility(document,nodeId,cardFields&&definitionId==null,serialized,animationHeader))
            using (new SourceScope(null))
            using (GameplayAuthoringInspector.UseScope(document.AuthoringGraph.Scope))
            {
                if(cardFields){
                    var selected=document.AuthoringGraph.Nodes.Find(n=>n.Id==nodeId);
                    GUILayout.Label(selected==null?"高级配置":GameplaySkillNodeDisplay.Name(selected,document.AuthoringGraph),EditorStyles.boldLabel);
                    EditorGUILayout.LabelField("配置来源",AssetDatabase.GetAssetPath(document));
                    GUILayout.Label("Scope · "+document.AuthoringGraph.Scope+" · 常用值在画布直接编辑；以下为高级配置。",EditorStyles.wordWrappedMiniLabel);
                }
                serialized.UpdateIfRequiredOrScript();var edit=new EditState();using(var capture=new FieldCapture(edit)){EditorGUI.BeginChangeCheck();
                if (definitionId != null)
                {
                    int i = document.AuthoringGraph.LocalDefinitions.FindIndex(d => d.Id == definitionId);
                    if (i >= 0) DrawDefinition(document, serialized, i, new HashSet<string>(),edit);
                }
                else
                {
                    int i = document.AuthoringGraph.Nodes.FindIndex(n => n.Id == nodeId);
                    if(i<0&&nodeId!=null){EditorGUILayout.HelpBox("此行为已移除；撤销可恢复，或选择其它行为继续编辑。",MessageType.Info);return;}
                    if (i < 0 || document.AuthoringGraph.Nodes[i].Payload is GameplayAbilityNodePayload){
                        string identityKey="Identity:"+document.GetInstanceID();
                        if(!cardFields)DrawIdentity(serialized);
                        else{bool open=EditorGUILayout.Foldout(Open.Contains(identityKey),"技能身份 / 事件 / 默认输入",true);if(open){Open.Add(identityKey);DrawIdentity(serialized);}else Open.Remove(identityKey);}
                    }
                    if (i >= 0)
                    {
                        var node = document.AuthoringGraph.Nodes[i];
                        if(!cardFields)GUILayout.Label(NodeName(node), EditorStyles.boldLabel);
                        var root = serialized.FindProperty("authoringGraph.Nodes").GetArrayElementAtIndex(i);
                        Field(root.FindPropertyRelative("Title"), new GUIContent("卡片别名", "只改变画布标题；正式技能显示名在施放卡编辑。"));
                        var payload = root.FindPropertyRelative("Payload");
                        if (node.Payload is GameplayAbilityNodePayload ability)
                        {
                            if(GameplayBehaviorGraphView.InlinePrimaryRule(document)){
                                int ruleIndex=document.AuthoringGraph.Nodes.FindIndex(n=>n.Id==document.AuthoringGraph.PrimaryRuleNodeId);var rule=document.AuthoringGraph.Nodes[ruleIndex];
                                string key="PrimaryRule:"+document.GetInstanceID();
                                string caption=((GameplayRuleNodePayload)rule.Payload).Target is GameplaySkillPointTargetAuthoring
                                    ?"施放触发 / 固定地点坐标":"施放触发 / 目标（高级）";
                                bool open=EditorGUILayout.Foldout(Open.Contains(key),caption,true);
                                if(open){Open.Add(key);DrawValue(document,serialized,serialized.FindProperty("authoringGraph.Nodes").GetArrayElementAtIndex(ruleIndex).FindPropertyRelative("Payload"),rule.Id,rule.Payload.Definitions,"",new HashSet<string>(),edit);}else Open.Remove(key);
                            }
                            if (ability.Settings != null) DrawValue(document, serialized, payload.FindPropertyRelative("Settings"), node.Id, ability.Definitions, "", new HashSet<string>(),edit);
                            else DrawBinding(document, serialized, node.Id, ability.Definitions, "Settings", typeof(GameplayAbilityDefinitionAsset), new HashSet<string>(),edit);
                        }
                        else if (node.Payload is GameplayActionNodePayload action)
                            DrawValue(document, serialized, payload.FindPropertyRelative("Action"), node.Id, action.Definitions, "", new HashSet<string>(),edit);
                        else DrawValue(document, serialized, payload, node.Id, node.Payload.Definitions, "", new HashSet<string>(),edit);
                    }
                }
                if (EditorGUI.EndChangeCheck()) ApplyFields(serialized,document,edit.Structural,edit.Paths);
                }
            }
        }
        private static void DrawIdentity(SerializedObject serialized)
        {
            GUILayout.Label("正式技能", EditorStyles.boldLabel);
            foreach (string field in new[] { "SkillId", "DisplayName", "Icon", "Mode", "RunRulesOnPassiveActivation", "Subject", "CastEvent", "Defaults", "LearningRequirements" })
                Field(serialized.FindProperty(field));
            var scope = serialized.FindProperty("authoringGraph.Scope");
            Field(scope, new GUIContent("创作 Scope"));
        }
        private static void DrawDefinition(GameplaySkillDocumentAsset doc, SerializedObject serialized, int index, HashSet<string> visited,EditState edit)
        {
            var definition = doc.AuthoringGraph.LocalDefinitions[index];
            if (!visited.Add(definition.Id)) { EditorGUILayout.HelpBox("局部引用循环。", MessageType.Error); return; }
            GUILayout.Label(definition.Name + " · 内嵌配置", EditorStyles.boldLabel);
            var p = serialized.FindProperty("authoringGraph.LocalDefinitions").GetArrayElementAtIndex(index);
            Field(p.FindPropertyRelative("Name"), new GUIContent("名称"));
            DrawValue(doc, serialized, p.FindPropertyRelative("Payload"), definition.Id, definition.Definitions, "", visited,edit);
            visited.Remove(definition.Id);
        }
        private static void DrawValue(GameplaySkillDocumentAsset doc, SerializedObject serialized, SerializedProperty root,
            string ownerId, List<GameplayDefinitionBinding> bindings, string bindingPrefix, HashSet<string> visited,EditState edit)
        {
            if (root == null) return;
            object value = root.propertyType == SerializedPropertyType.ManagedReference ? root.managedReferenceValue : root.boxedValue;
            if (value == null) return;
            var resources = GameplayGraphFields.ResourceSlots(value).ToDictionary(s => s.Path, s => s.ValueType);
            var arrays = new HashSet<string>(GameplayGraphFields.ActionArrays(value).Select(s => s.Path));
            string inactiveKey="Inactive:"+doc.GetInstanceID()+":"+root.propertyPath;bool showInactive=Open.Contains(inactiveKey);
            DrawChildren(root, value, "");
            if(value is GameplayEffectDefinitionData||value is GameplayProjectileDefinitionData||value is GameplayAreaDefinitionData){
                bool next=EditorGUILayout.Foldout(showInactive,"高级 / 显示当前策略未启用的配置",true);
                if(next)Open.Add(inactiveKey);else Open.Remove(inactiveKey);
                if(next)GUILayout.Label("切换策略保留原配置；有校验冲突时可在此清除不再使用的字段。",EditorStyles.wordWrappedMiniLabel);
            }
            void DrawChildren(SerializedProperty parent, object owner, string relative)
            {
                var children=new List<SerializedProperty>();var cursor=parent.Copy();var end=parent.GetEndProperty();
                if(cursor.NextVisible(true))do{
                    if(SerializedProperty.EqualContents(cursor,end))break;
                    if(IsValueFieldVisible(owner,cursor.name))children.Add(cursor.Copy());
                }while(cursor.NextVisible(false));
                var sections=ValueSections.SelectMany(p=>p.Sections(owner)).ToList();if(sections.Count==0)sections=GameplayStandardBusinessInspector.Describe(owner).ToList();
                var covered=new HashSet<string>(sections.SelectMany(g=>g.Paths));
                var rest=children.Where(p=>!covered.Contains(p.name)&&p.name!="Definitions").Select(p=>p.name).ToArray();
                if(rest.Length>0)sections.Add(new GameplayBusinessInspectorGroup(null,sections.Count==0?"业务配置":"其它配置",null,rest));
                foreach(var section in sections){
                    var fields=section.Paths.Select(name=>children.Find(p=>p.name==name)).Where(p=>p!=null&&(showInactive||Visible(owner,p.name))).ToArray();
                    if(fields.Length==0)continue;
                    bool showHeading=sections.Count>1||section.Note!=null;
                    string sectionKey=doc.GetInstanceID()+":"+parent.propertyPath+":"+section.Title;
                    if(showHeading){
                        bool open=!ClosedSections.Contains(sectionKey);bool next=EditorGUILayout.Foldout(open,section.Title,true,EditorStyles.foldoutHeader);
                        if(next)ClosedSections.Remove(sectionKey);else ClosedSections.Add(sectionKey);
                        if(!next)continue;
                        if(!string.IsNullOrEmpty(section.Note))GUILayout.Label(section.Note,EditorStyles.wordWrappedMiniLabel);
                    }
                    foreach(var it in fields){
                    string path = relative.Length == 0 ? it.name : relative + "." + it.name;
                    string bindingPath = bindingPrefix.Length == 0 ? path : bindingPrefix + "." + path;
                    if (it.name == "Definitions") continue;
                    if(!showInactive&&!Visible(owner,it.name))continue;
                    if(_cardPaths!=null&&owner is GameplayAbilityDefinitionData&&(it.name=="Cost"||it.name=="Charge"||it.name=="CooldownEffect")){EditorGUILayout.LabelField(GameplayAuthoringInspector.Label(it),new GUIContent("在对应消耗、冷却或充能节点编辑；复杂值从节点进入"));continue;}
                    if(CardSummary(it)){
                        // The target dropdown owns the type choice. Its configuration still belongs here.
                        if(it.propertyType==SerializedPropertyType.ManagedReference&&it.managedReferenceValue is GameplaySkillTargetAuthoring target)
                            DrawChildren(it,target,path);
                        else
                            foreach(var provider in Providers.OfType<IGameplaySkillFieldControlProvider>())if(provider.DrawSourceLink(root,it))break;
                        continue;
                    }

                    if (arrays.Contains(path)) { EditorGUILayout.LabelField(PortName(path), "在画布端口连接行为", EditorStyles.miniLabel); continue; }
                    if (resources.TryGetValue(path, out var resource)) { DrawBinding(doc, serialized, ownerId, bindings, bindingPath, resource, visited,edit); continue; }
                    var fieldInfo = GameplayGraphFields.SerializedFields(owner.GetType()).FirstOrDefault(f => f.Name == it.name);
                    Type fieldType = fieldInfo?.FieldType;
                    if (fieldType != null && fieldType.IsArray && typeof(GameplayDefinitionAsset).IsAssignableFrom(fieldType.GetElementType()))
                    {
                        GUILayout.Label(GameplayAuthoringInspector.Label(it));
                        for (int j = 0; j < it.arraySize; j++) {
                            DrawBinding(doc, serialized, ownerId, bindings, bindingPath + ".Array.data[" + j + "]", fieldType.GetElementType(), visited,edit);
                            if(GUILayout.Button("移除此引用 #"+(j+1))){ApplyFields(serialized,doc);GameplaySkillAuthoringCommands.RemoveResourceEntry(doc,ownerId,bindingPath,j);GUIUtility.ExitGUI();}
                        }
                        if (GUILayout.Button("添加引用")) { it.InsertArrayElementAtIndex(it.arraySize); it.GetArrayElementAtIndex(it.arraySize - 1).objectReferenceValue = null;edit.Structural=true; }
                        continue;
                    }
                    EditorGUI.BeginChangeCheck();bool provided=Providers.Any(p => p.DrawField(root,it));if(EditorGUI.EndChangeCheck())edit.Paths.Add(it.propertyPath);if(provided)continue;
                    if (it.propertyType == SerializedPropertyType.ManagedReference || it.isArray && it.propertyType != SerializedPropertyType.String ||
                        it.propertyType == SerializedPropertyType.Generic && !(it.boxedValue is GameplayTagReference) && !(it.boxedValue is GameplayAttributeReference))
                        DrawStructured(it.Copy(), fieldType, path);
                    else {
                        bool policy=it.name=="Motion"||it.name=="StraightDestination"||it.name=="DurationPolicy"||it.name=="Overflow";
                        if(policy)EditorGUI.BeginChangeCheck();Field(it);if(policy&&EditorGUI.EndChangeCheck())edit.Structural=true;
                    }
                    }
                    if(showHeading)GUILayout.Space(7);
                }
            }
            void DrawStructured(SerializedProperty p, Type fieldType, string path)
            {
                if (p.isArray)
                {
                    p.isExpanded = EditorGUILayout.Foldout(p.isExpanded, GameplayAuthoringInspector.Label(p).text + " · " + p.arraySize, true);
                    if (!p.isExpanded) return;
                    EditorGUI.indentLevel++;
                    for (int j = 0; j < p.arraySize; j++)
                    {
                        var element = p.GetArrayElementAtIndex(j); Type elementType = fieldType?.GetElementType();
                        EditorGUILayout.BeginHorizontal(); GUILayout.Label("顺序 " + (j + 1), EditorStyles.miniLabel);
                        if (GUILayout.Button("↑", GUILayout.Width(25)) && j > 0) { p.MoveArrayElement(j, j - 1);edit.Structural=true; GUI.changed = true; }
                        if (GUILayout.Button("↓", GUILayout.Width(25)) && j + 1 < p.arraySize) { p.MoveArrayElement(j, j + 1);edit.Structural=true; GUI.changed = true; }
                        if (GUILayout.Button("×", GUILayout.Width(25))) {
                            object item=element.propertyType==SerializedPropertyType.ManagedReference?element.managedReferenceValue:null;
                            string callbackKey=item is GameplayApplyEffectOnCompletionAuthoring completion?"Completion/"+completion.AuthoringId:item is GameplayApplyEffectOnOverflowAuthoring overflow?"Overflow/"+overflow.AuthoringId:null;
                            if(callbackKey!=null){EditorGUILayout.EndHorizontal();ApplyFields(serialized,doc);GameplaySkillAuthoringCommands.RemoveEffectCallback(doc,ownerId,callbackKey);GUIUtility.ExitGUI();}
                            p.DeleteArrayElementAtIndex(j);edit.Structural=true; GUI.changed = true; EditorGUILayout.EndHorizontal(); break;
                        }
                        EditorGUILayout.EndHorizontal();
                        DrawStructured(element, elementType, path + ".Array.data[" + j + "]");
                    }
                    if (GUILayout.Button("添加 " + GameplayAuthoringInspector.Label(p).text))
                    {
                        edit.Structural=true;
                        if (fieldType?.GetElementType() is Type type && !type.IsAbstract)
                        { int at = p.arraySize++; var e = p.GetArrayElementAtIndex(at); if (e.propertyType == SerializedPropertyType.ManagedReference) e.managedReferenceValue = Activator.CreateInstance(type); GUI.changed = true; }
                        else Pick(p, fieldType?.GetElementType(), true);
                    }
                    EditorGUI.indentLevel--; return;
                }
                if (p.propertyType == SerializedPropertyType.ManagedReference)
                {
                    var current = p.managedReferenceValue;
                    EditorGUILayout.BeginHorizontal(); GUILayout.Label(GameplayAuthoringInspector.Label(p));
                    if (GUILayout.Button(current == null ? "选择配置…" : GameplaySkillNodeDescriptors.Get(current.GetType()).DisplayName)) Pick(p, fieldType, false);
                    EditorGUILayout.EndHorizontal();
                    if (current != null) { EditorGUI.indentLevel++; DrawChildren(p, current, path); EditorGUI.indentLevel--; }
                }
                else if (p.propertyType == SerializedPropertyType.Generic)
                { object current = p.boxedValue; if (current != null) { GUILayout.Label(GameplayAuthoringInspector.Label(p), EditorStyles.boldLabel); EditorGUI.indentLevel++; DrawChildren(p, current, path); EditorGUI.indentLevel--; } }
                else Field(p);
            }
            void Pick(SerializedProperty property, Type type, bool append)
            {
                if (type == null) return;
                string propertyPath = property.propertyPath;
                if(serialized.hasModifiedProperties&&!ApplyFields(serialized,doc))return;
                var handle=GameplaySourceHandle.Capture(doc,propertyPath);
                string expected=append?null:property.managedReferenceFullTypename;
                GameplayAuthoringTypePicker.Show(type, doc, selected => {
                    using (var so = new SerializedObject(doc)) {
                        var target = so.FindProperty(handle.ResolvePath()); if (target == null||!append&&target.managedReferenceFullTypename!=expected)throw new InvalidOperationException("字段已被替换或重排，请重新选择配置。");
                        if(selected!=null&&!GameplayAuthoringTypeCatalog.Query(type,doc.AuthoringGraph.Scope).Contains(selected))throw new InvalidOperationException("此类型在当前 Scope 不可用。");
                        if(!append&&selected==null&&(target.managedReferenceValue is GameplaySkillTriggerAuthoring||target.managedReferenceValue is GameplaySkillTargetAuthoring))throw new InvalidOperationException("这是必需的触发或目标配置，请选择替代类型。");
                        if(!append&&target.managedReferenceValue!=null&&target.managedReferenceValue.GetType()!=selected){if(!EditorUtility.DisplayDialog("替换业务配置","将替换「"+GameplaySkillNodeDescriptors.Get(target.managedReferenceValue.GetType()).DisplayName+"」及其字段；一次 Undo 可恢复原配置。","替换","取消"))return;}
                        if (append) { int at = target.arraySize++; target.GetArrayElementAtIndex(at).managedReferenceValue = selected == null ? null : Activator.CreateInstance(selected); }
                        else target.managedReferenceValue = selected == null ? null : Activator.CreateInstance(selected);
                        ApplyFields(so,doc);
                    }
                }, doc.AuthoringGraph.Scope);
            }
        }
        private static bool Visible(object owner,string field)
        {
            if(owner is GameplayEffectDefinitionData effect){
                if(field=="Duration"||field=="DurationValue"||field=="CompletionExecutions")return effect.DurationPolicy==GameplayEffectDurationPolicy.Duration;
                if(field=="Period"||field=="PeriodValue"||field=="PeriodicStart"||field=="Stacking"||field=="Modifiers"||field=="GrantedTags"||field=="OngoingRequirements")return effect.DurationPolicy!=GameplayEffectDurationPolicy.Instant;
            }
            if(owner is GameplayProjectileDefinitionData projectile){
                if(field=="StraightDestination")return projectile.Motion==Standard.GameplayProjectileMotionKind.Straight;
                if(field=="DirectionAngle")return projectile.Motion==Standard.GameplayProjectileMotionKind.Straight||projectile.Motion==Standard.GameplayProjectileMotionKind.Arc;
                if(field=="CurveHeight")return projectile.Motion==Standard.GameplayProjectileMotionKind.Arc||projectile.Motion==Standard.GameplayProjectileMotionKind.Parabolic;
                if(field=="Control1Offset"||field=="Control2Offset")return projectile.Motion==Standard.GameplayProjectileMotionKind.Bezier;
            }
            if(owner is GameplayAreaDefinitionData area){
                if(field=="Radius")return area.Shape==GameplayAreaAuthoringShape.Circle||area.Shape==GameplayAreaAuthoringShape.Sector;
                if(field=="Angle")return area.Shape==GameplayAreaAuthoringShape.Sector;
                if(field=="Length"||field=="Width")return area.Shape==GameplayAreaAuthoringShape.Box;
                if(field=="Cells"||field=="CellSize")return area.Shape==GameplayAreaAuthoringShape.Cells;
            }
            return GameplayAuthoringFieldSchema.Visible(owner,field);
        }
        private static void DrawBinding(GameplaySkillDocumentAsset doc, SerializedObject serialized, string ownerId,
            List<GameplayDefinitionBinding> bindings, string path, Type resourceType, HashSet<string> visited,EditState edit)
        {
            var binding = bindings.FirstOrDefault(b => b.PropertyPath == path);
            var callback=GameplayGraphEffectCallbacks.Slots(doc.AuthoringGraph.LocalDefinitions.Find(d=>d.Id==ownerId)?.Payload).SingleOrDefault(s=>s.Path==path);
            var callbackEdge=callback==null?null:doc.AuthoringGraph.Connections.SingleOrDefault(e=>e.FromNodeId==ownerId&&e.OutputPortKey==callback.Key&&e.InputPortKey==GameplayGraphEffectCallbacks.Input);
            if(callback!=null){
                if(callbackEdge!=null){
                    var target=doc.AuthoringGraph.Nodes.Single(n=>n.Id==callbackEdge.ToNodeId);EditorGUILayout.LabelField("续接效果",GameplaySkillFieldSchema.NodeName(target));
                    if(GUILayout.Button("定位后续效果"))GameplaySkillWorkbench.Open(doc,target.Id);
                    if(GUILayout.Button("断开连接 · 保留回调配置")){GameplaySkillAuthoringCommands.Disconnect(doc,callbackEdge.Id);GUIUtility.ExitGUI();}
                }else{
                    EditorGUILayout.HelpBox("待重连：拥有标志、技能 ID 和回调策略均保留；此执行器尚不参与运行。溢出没有活动执行器时拒绝本次溢出。",MessageType.Warning);
                    if(GUILayout.Button("选择后续效果…")){
                        var menu=new GenericMenu();foreach(var target in doc.AuthoringGraph.Nodes.Where(n=>GameplayGraphEffectCallbacks.EffectBinding(n)!=null)){
                            string targetId=target.Id;menu.AddItem(new GUIContent(NodeName(target)),false,()=>GameplaySkillAuthoringCommands.Connect(doc,ownerId,callback.Key,targetId,false,GameplayGraphEffectCallbacks.Input));
                        }menu.ShowAsContext();
                    }
                }
                if(GUILayout.Button("删除此回调及配置（可撤销）")){GameplaySkillAuthoringCommands.RemoveEffectCallback(doc,ownerId,callback.Key);GUIUtility.ExitGUI();}return;
            }
            string label = path == "Settings" ? "释放配置" : path == "Effect" ? "效果" : path == "Animation" ? "动画" : path == "Cue" ? "特效 / 音效" : path == "CooldownEffect" ? "冷却效果" : path.Split('.').Last();
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            var local = binding == null ? null : doc.AuthoringGraph.LocalDefinitions.Find(d => d.Id == binding.LocalId);
            var timeline = binding == null ? null : doc.AuthoringGraph.Timelines.Find(t => t.Id == binding.LocalId);
            EditorGUILayout.LabelField(label, timeline != null ? "时间编排 · " + timeline.Name : local != null ? "内嵌 · " + local.Name : binding?.External ? "共享资源" : "未配置（可选字段保持空）");
            if (local != null)
            {
                string key = doc.GetInstanceID() + ":" + ownerId + ":" + path;
                bool open = EditorGUILayout.Foldout(!ClosedSections.Contains(key), Name(local.Payload.GetType()), true);
                if (open) { ClosedSections.Remove(key); using(new SourceScope(CaptureBinding(doc,ownerId,path,local.Id)))DrawDefinition(doc, serialized, doc.AuthoringGraph.LocalDefinitions.IndexOf(local), visited,edit); } else ClosedSections.Add(key);
            }
            else if (binding?.External)
            {
                EditorGUILayout.ObjectField("来源",binding.External,resourceType,false);
                string key="Shared:"+doc.GetInstanceID()+":"+ownerId+":"+path;
                bool open=EditorGUILayout.Foldout(Open.Contains(key),"共享资源业务配置",true);
                if(open){Open.Add(key);using(var shared=new SerializedObject(binding.External))GameplayAuthoringInspector.Draw(shared);}else Open.Remove(key);
                if(GUILayout.Button("定位共享资源")){Selection.activeObject=binding.External;EditorGUIUtility.PingObject(binding.External);}
            }
            if (timeline == null)
            {
                EditorGUILayout.BeginHorizontal();
                if (GUILayout.Button("选择 / 新建…"))
                {
                    ApplyFields(serialized,doc);
                    var menu = new GenericMenu();
                    foreach (var type in Values(resourceType))
                    {
                        var presets=CreationPresets(type).ToArray();
                        if(presets.Length==0)
                            menu.AddItem(new GUIContent("新建内嵌/"+Name(type)),false,()=>Bind(doc,ownerId,path,type,null,null));
                        else
                            foreach(var preset in presets)
                                menu.AddItem(new GUIContent("新建内嵌/"+preset.Name),false,()=>BindValue(doc,ownerId,path,preset.Value,preset.Name,null,null,false));
                    }
                    foreach (var d in doc.AuthoringGraph.LocalDefinitions.Where(d => Values(resourceType).Any(t => t.IsInstanceOfType(d.Payload))))
                    { string id = d.Id; menu.AddItem(new GUIContent("复用本技能/" + d.Name), false, () => Bind(doc, ownerId, path, null, id, null)); }
                    menu.AddItem(new GUIContent("解除引用 / 删除独占局部配置"), false, () => Bind(doc, ownerId, path, null, null, null));
                    menu.AddItem(new GUIContent("解除引用 / 保留局部配置"), false, () => Bind(doc, ownerId, path, null, null, null,true)); menu.ShowAsContext();
                }

                EditorGUILayout.EndHorizontal();
                string sharedKey="ChooseShared:"+doc.GetInstanceID()+":"+ownerId+":"+path;
                bool chooseShared=EditorGUILayout.Foldout(Open.Contains(sharedKey),"改用外部共享资源",true);
                if(chooseShared){Open.Add(sharedKey);var external=(GameplayDefinitionAsset)EditorGUILayout.ObjectField("共享来源",binding?.External,resourceType,false);if(external!=binding?.External){ApplyFields(serialized,doc);Bind(doc,ownerId,path,null,null,external);}}else Open.Remove(sharedKey);
            }
            EditorGUILayout.EndVertical();
        }
        public static void Bind(GameplaySkillDocumentAsset doc, string ownerId, string path, Type create, string localId, GameplayDefinitionAsset external,bool keepReleasedDraft=false)
            => BindValue(doc,ownerId,path,create==null?null:(GameplayLocalDefinitionPayload)Activator.CreateInstance(create),
                create==null?null:Name(create),localId,external,keepReleasedDraft);

        private static void BindValue(GameplaySkillDocumentAsset doc,string ownerId,string path,GameplayLocalDefinitionPayload value,
            string name,string localId,GameplayDefinitionAsset external,bool keepReleasedDraft)
        {
            GameplaySkillAuthoringCommands.Transaction(doc, "配置正式资源", graph => {
                var node = graph.Nodes.Find(n => n.Id == ownerId);
                var bindings = node?.Payload.Definitions ?? graph.LocalDefinitions.Single(d => d.Id == ownerId).Definitions;
                var released=bindings.Where(b=>b.PropertyPath==path).Select(b=>b.LocalId).Where(id=>!string.IsNullOrEmpty(id)).ToArray();
                if(path=="Settings"&&node?.Payload is GameplayAbilityNodePayload ability){
                    ability.Settings=value;
                    bindings.RemoveAll(b=>b.PropertyPath==path);
                    if(external)bindings.Add(new GameplayDefinitionBinding{PropertyPath=path,External=external});return;
                }
                if (value != null) { var local = new GameplayLocalDefinition { Name = name, Payload = value, SourceDefinitionId = doc.DefinitionId }; graph.LocalDefinitions.Add(local); localId = local.Id; }
                bindings.RemoveAll(b => b.PropertyPath == path);
                if (localId != null || external) bindings.Add(new GameplayDefinitionBinding { PropertyPath = path, LocalId = localId, External = external });
                if(!keepReleasedDraft)GameplaySkillAuthoringCommands.CollectReleased(graph,released);
            });
        }
    }
}
