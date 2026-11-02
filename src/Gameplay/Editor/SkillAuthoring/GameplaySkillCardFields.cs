using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Editor metadata relative to one real action/value payload. FloatValue edits only
    /// an existing Constant AST; every other AST keeps its type and opens the advanced Inspector.</summary>
    public sealed class GameplaySkillCardField
    {
        public string Path { get; }
        public string Label { get; }
        public bool FloatValue { get; }
        public IReadOnlyList<string> Choices {get;}
        public GameplaySkillCardField(string path, string label, bool floatValue = false,IReadOnlyList<string> choices=null)
        { Path = path; Label = label; FloatValue = floatValue; Choices=choices; }
    }

    /// <summary>Integration/game Editor assemblies provide key fields without adding SDK or
    /// game references to the generic editor. Paths must refer to actual serialized fields.</summary>
    public interface IGameplaySkillCardFieldProvider
    {
        IEnumerable<GameplaySkillCardField> CardFields(object values);
    }

    /// <summary>Optional SDK field controls for the existing card and its advanced source link.
    /// The Spine provider uses this to replace native resource pickers without adding SDK references here.</summary>
    public interface IGameplaySkillFieldControlProvider
    {
        /// <summary>Claims only actual serialized fields owned by this integration.</summary>
        bool SupportsControl(SerializedProperty property);
        /// <summary>Creates a source-backed control. Deferred callbacks must honor canWrite; refresh reads source without committing.</summary>
        VisualElement CreateControl(SerializedProperty property,string label,Func<bool> canWrite,out Action refresh);
        /// <summary>Draws the advanced Inspector link for an already card-owned field, without a second raw editor.</summary>
        bool DrawSourceLink(SerializedProperty root,SerializedProperty property);
    }

    /// <summary>Derived card bindings. Stable owner IDs are resolved again after Undo/reorder;
    /// no values, SO copies, or execution data are retained by this adapter.</summary>
    public static class GameplaySkillCardFields
    {
        public sealed class Binding
        {
            public string Path { get; internal set; }
            public string Label { get; internal set; }
            public string Summary { get; internal set; }
            internal Func<bool> SourceGuard {get;set;}
            /// <summary>The original value AST opened by the card source menu; never the editable Constant.Value leaf.</summary>
            public string SourcePath { get; internal set; }
            public IReadOnlyList<string> Choices {get;internal set;}
            public bool Editable => Path != null && Summary == null;
        }
        private static readonly IGameplaySkillCardFieldProvider[] Providers = TypeCache.GetTypesDerivedFrom<IGameplaySkillCardFieldProvider>()
            .Where(t => !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null).OrderBy(t => t.FullName)
            .Select(t => (IGameplaySkillCardFieldProvider)Activator.CreateInstance(t)).ToArray();

        public static List<Binding> Describe(GameplaySkillDocumentAsset document, string nodeId)
        {
            using(var serialized=new SerializedObject(document))return Describe(document,nodeId,serialized);
        }
        internal static List<Binding> Describe(GameplaySkillDocumentAsset document,string nodeId,SerializedObject so)
        {
            var result = new List<Binding>();
            var graph = document.AuthoringGraph;
            int index = graph.Nodes.FindIndex(n => n.Id == nodeId);
            if (index < 0) return result;
            var node = graph.Nodes[index];
            string root = "authoringGraph.Nodes.Array.data[" + index + "].Payload";
            {
                Func<bool> sourceGuard=null;
                if (node.Payload is GameplayAbilityNodePayload ability)
                {
                    Add("DisplayName", "技能名称");
                    if (GameplayBehaviorGraphView.InlinePrimaryRule(document))
                    {
                        int ruleIndex=graph.Nodes.FindIndex(n=>n.Id==graph.PrimaryRuleNodeId);
                        Add("authoringGraph.Nodes.Array.data["+ruleIndex+"].Payload.Target","施放目标");
                    }
                }
                else if (node.Payload is GameplayRuleNodePayload rule)
                {
                    if (rule.Trigger is GameplaySkillEventTriggerAuthoring) Add(root + ".Trigger.EventTag", "触发事件");
                    Add(root + ".Once", "仅一次");
                    Add(root+".Target","目标来源");
                }
                else if (node.Payload is GameplayActionNodePayload action) Values(action.Action, root + ".Action");

                foreach (var binding in node.Payload.Definitions)
                {
                    if(node.Payload is GameplayAbilityNodePayload&&binding.PropertyPath=="CooldownEffect")continue;
                    var actionValues=(node.Payload as GameplayActionNodePayload)?.Action;
                    // Historical nested bindings may accompany a lowered explicit child. Only
                    // the actual resource consumer owns its editor, never a Search wrapper.
                    if(actionValues!=null&&!GameplayGraphFields.ResourceSlots(actionValues).Any(s=>s.Path==binding.PropertyPath))continue;
                    if (binding.External)
                    { result.Add(new Binding { Label = "共享来源", Summary = GameplaySkillNodeDisplay.SharedSummary(binding.External) }); continue; }
                    int localIndex = graph.LocalDefinitions.FindIndex(d => d.Id == binding.LocalId);
                    if (localIndex < 0) continue;
                    var definition = graph.LocalDefinitions[localIndex];
                    sourceGuard=GameplaySkillFieldSchema.CaptureBinding(document,nodeId,binding.PropertyPath,binding.LocalId);
                    string prefix = "authoringGraph.LocalDefinitions.Array.data[" + localIndex + "].Payload";
                    if (binding.PropertyPath == "CooldownEffect" && definition.Payload is GameplayEffectDefinitionData cd)
                    { if (cd.DurationPolicy == GameplayEffectDurationPolicy.Duration) Add(prefix + (cd.DurationValue == null ? ".Duration" : ".DurationValue"), "冷却秒数", cd.DurationValue != null); }
                    else Values(definition.Payload, prefix);
                }

                void Add(string path, string label, bool value = false,IReadOnlyList<string> choices=null)
                {
                    var property = so.FindProperty(path);
                    if (property == null) return;
                    string summary = null;string sourcePath=value?path:null;
                    if (value)
                    {
                        if (property.managedReferenceValue is GameplayConstantFloatValueAuthoring) {path += ".Value";label+=" · 常量";}
                        else {var ast=property.managedReferenceValue;string kind=ast is GameplaySetByCallerFloatValueAuthoring?"Caller":ast is GameplayAttributeFloatValueAuthoring?"属性":ast is GameplayFormulaFloatValueAuthoring?"Formula":"表达式";summary=kind+" · "+GameplaySkillNodeDisplay.Value(ast as GameplayFloatValueAuthoring);}
                    }
                    result.Add(new Binding { Path = path, Label = label, Summary = summary,SourcePath=sourcePath,Choices=choices,SourceGuard=sourceGuard });
                }
                void Values(object values, string prefix)
                {
                    foreach (var field in CoreFields(values).Concat(Providers.SelectMany(p => p.CardFields(values))))
                        Add(prefix + "." + field.Path, field.Label, field.FloatValue,field.Choices);
                }
            }
            return result;
        }

        internal static List<Binding> DescribePolicy(GameplaySkillDocumentAsset document,string nodeId,string role)
        {
            var graph=document.AuthoringGraph;int index=graph.Nodes.FindIndex(n=>n.Id==nodeId);var result=new List<Binding>();
            if(index<0||!(graph.Nodes[index].Payload is GameplayAbilityNodePayload ability)||!(ability.Settings is GameplayAbilityDefinitionData settings))return result;
            string root="authoringGraph.Nodes.Array.data["+index+"].Payload.Settings";
            void Scalar(string path,string label)=>result.Add(new Binding{Path=path,Label=label});
            void Value(string path,string label,GameplayFloatValueAuthoring value,Func<bool> guard=null){result.Add(new Binding{Path=value is GameplayConstantFloatValueAuthoring?path+".Value":path,Label=label+(value is GameplayConstantFloatValueAuthoring?" · 常量":""),SourcePath=path,Summary=value is GameplayConstantFloatValueAuthoring?null:GameplaySkillNodeDisplay.Value(value),SourceGuard=guard});}
            if(role=="Cost"&&settings.Cost is GameplayAttributeAbilityCostAuthoring cost){
                for(int i=0;i<cost.Resources.Length;i++){string entry=root+".Cost.Resources.Array.data["+i+"]";Scalar(entry+".Attribute","资源属性");Value(entry+".Delta","资源变化",cost.Resources[i].Delta);}
            }else if(role=="Recharge"&&settings.Charge!=null){Scalar(root+".Charge.MaxCharges","最大充能");Scalar(root+".Charge.RechargeDuration","回充秒数");}
            else if(role=="Recharge"){
                var binding=ability.Definitions.SingleOrDefault(b=>b.PropertyPath=="CooldownEffect");
                if(binding?.External)result.Add(new Binding{Label="共享冷却 · 只读",Summary=GameplaySkillNodeDisplay.SharedSummary(binding.External)});
                int local=graph.LocalDefinitions.FindIndex(d=>d.Id==binding?.LocalId);
                if(local>=0&&graph.LocalDefinitions[local].Payload is GameplayEffectDefinitionData effect){
                    string prefix="authoringGraph.LocalDefinitions.Array.data["+local+"].Payload";var guard=GameplaySkillFieldSchema.CaptureBinding(document,nodeId,"CooldownEffect",binding.LocalId);
                    if(effect.DurationValue!=null)Value(prefix+".DurationValue","冷却秒数",effect.DurationValue,guard);else {Scalar(prefix+".Duration","冷却秒数");result.Last().SourceGuard=guard;}
                    result.Add(new Binding{Label="冷却状态",Summary=string.Join(" / ",effect.GrantedTags.Select(t=>t.Name))});
                }
            }
            return result;
        }

        private static IEnumerable<GameplaySkillCardField> CoreFields(object value)
        {
            if (value is GameplaySearchTargetActionAuthoring search)
            {
                yield return new GameplaySkillCardField("Shape", "搜索形状");
                yield return new GameplaySkillCardField("Faction", "阵营");
                yield return new GameplaySkillCardField("Origin", "中心来源");
                yield return new GameplaySkillCardField("IncludeSelf", "包含施法者");
                yield return new GameplaySkillCardField("MaxTargets", "目标上限");
                foreach(string direction in new[]{"Direction","DirectionAngle","LengthFromTarget","CellSize"})
                    if(GameplayAuthoringFieldSchema.Visible(search,direction))yield return new GameplaySkillCardField(direction,direction=="Direction"?"方向来源":direction=="DirectionAngle"?"方向偏转":direction=="LengthFromTarget"?"长度跟随目标":"网格尺寸",direction=="DirectionAngle");
                foreach (string dimension in new[] { "Radius", "Angle", "Length", "Width" })
                    if (GameplayAuthoringFieldSchema.Visible(search, dimension))
                        yield return new GameplaySkillCardField(dimension, dimension == "Radius" ? "半径" : dimension == "Angle" ? "角度" : dimension == "Length" ? "长度" : "宽度", true);
            }
            if (value is GameplayEffectDefinitionData effect)
            {
                if (effect.DurationPolicy == GameplayEffectDurationPolicy.Duration)
                    yield return new GameplaySkillCardField(effect.DurationValue == null ? "Duration" : "DurationValue", "持续秒数", effect.DurationValue != null);
                if (effect.DurationPolicy != GameplayEffectDurationPolicy.Instant)
                    yield return new GameplaySkillCardField(effect.PeriodValue == null ? "Period" : "PeriodValue", "周期间隔", effect.PeriodValue != null);
                if(effect.Stacking!=null){yield return new GameplaySkillCardField("Stacking.StackKey","叠层标识");yield return new GameplaySkillCardField("Stacking.MaxStacks","叠层上限");}
                // A compound effect keeps its real execution ordering in the advanced editor.
                if (effect.Executions.Length == 1 && effect.Modifiers.Length == 0)
                {
                    string prefix = "Executions.Array.data[0].";
                    if (effect.Executions[0] is GameplayDamageExecutionAuthoring)
                    { yield return new GameplaySkillCardField(prefix + "DamageType", "伤害类型"); yield return new GameplaySkillCardField(prefix + "Amount", "伤害数值", true); }
                    else if (effect.Executions[0] is GameplayHealExecutionAuthoring || effect.Executions[0] is GameplayShieldExecutionAuthoring)
                        yield return new GameplaySkillCardField(prefix + "Amount", "结算数值", true);
                }
            }
            if (value is GameplayProjectileDefinitionData projectile)
            {
                yield return new GameplaySkillCardField("Motion", "运动轨迹"); yield return new GameplaySkillCardField("Plane", "空间平面");
                yield return new GameplaySkillCardField("Speed", "速度", true); yield return new GameplaySkillCardField("Duration", "存续秒数", true);
                if(projectile.Motion==Standard.GameplayProjectileMotionKind.Straight)yield return new GameplaySkillCardField("StraightDestination","终点来源");
                if(projectile.Motion==Standard.GameplayProjectileMotionKind.Straight||projectile.Motion==Standard.GameplayProjectileMotionKind.Arc)yield return new GameplaySkillCardField("DirectionAngle","方向偏转");
                if(projectile.Motion==Standard.GameplayProjectileMotionKind.Arc||projectile.Motion==Standard.GameplayProjectileMotionKind.Parabolic)yield return new GameplaySkillCardField("CurveHeight","曲线高度",true);
            }
            if (value is GameplayAreaDefinitionData area)
            {
                yield return new GameplaySkillCardField("Shape", "区域形状"); yield return new GameplaySkillCardField("Plane", "空间平面");
                yield return new GameplaySkillCardField("Origin", "中心来源"); yield return new GameplaySkillCardField("Faction", "阵营");
                yield return new GameplaySkillCardField("Duration", "持续秒数", true); yield return new GameplaySkillCardField("TickInterval", "周期间隔", true);
                yield return new GameplaySkillCardField("TickOnEnter", "进入立即周期"); yield return new GameplaySkillCardField("FollowOwner", "跟随施法者");
                if (area.Shape == GameplayAreaAuthoringShape.Circle || area.Shape == GameplayAreaAuthoringShape.Sector) yield return new GameplaySkillCardField("Radius", "半径", true);
                if (area.Shape == GameplayAreaAuthoringShape.Sector) yield return new GameplaySkillCardField("Angle", "角度", true);
                if (area.Shape == GameplayAreaAuthoringShape.Box)
                { yield return new GameplaySkillCardField("Length", "长度", true); yield return new GameplaySkillCardField("Width", "宽度", true); }
            }
            if (value is GameplaySummonDefinitionData)
            { yield return new GameplaySkillCardField("Duration", "持续秒数", true); yield return new GameplaySkillCardField("AutoCast", "自动施放"); }
            if (value is GameplayMoveTargetsActionAuthoring)
            { yield return new GameplaySkillCardField("Mode", "位移方式"); yield return new GameplaySkillCardField("Distance", "距离", true); yield return new GameplaySkillCardField("Speed", "速度", true); }
            if (value is GameplayGrantTimedShieldActionAuthoring)
            { yield return new GameplaySkillCardField("Amount", "护盾数值", true); yield return new GameplaySkillCardField("Duration", "持续秒数", true); }
            if (value is GameplayRotateToPointActionAuthoring) yield return new GameplaySkillCardField("RotateOwner", "转向自身");
            if(value is GameplayCooldownActionAuthoring cooldown){yield return new GameplaySkillCardField("SkillId","技能 ID（0当前）");yield return new GameplaySkillCardField("Operation","冷却操作");if(cooldown.Operation!=Standard.GameplayCooldownOperation.Restart)yield return new GameplaySkillCardField("Value","变化数值",true);}
            if(value is GameplayCaptureCombatRollActionAuthoring){yield return new GameplaySkillCardField("CriticalChance","暴击概率",true);yield return new GameplaySkillCardField("MissChance","未命中概率",true);}
            if (value is GameplayCaptureValueActionAuthoring)
            { yield return new GameplaySkillCardField("Key", "快照键"); yield return new GameplaySkillCardField("Value", "捕获数值", true); }
            if (value is GameplaySkillSetContextValueActionAuthoring)
            { yield return new GameplaySkillCardField("Key", "上下文键"); yield return new GameplaySkillCardField("Value", "数值"); }
            if (value is GameplayChangeStateActionAuthoring) yield return new GameplaySkillCardField("State", "行为状态");
            if (value is GameplayEndAbilityActionAuthoring) yield return new GameplaySkillCardField("Cancel", "以取消结束");
        }

        /// <summary>Validates only the edited payload at the external editor boundary, then uses
        /// the existing native-buffer Apply/Undo/notification chain. Failure leaves source clean.</summary>
        public static bool Apply(SerializedObject serialized, GameplaySkillDocumentAsset document, string path, out string error)
        {
            if(GameplaySkillAuthoringCommands.IsReadOnly(document)){error="只读范例；请先创建副本。";serialized.Update();return false;}
            var validation = new GameplayAuthoringValidationContext();
            if (path.StartsWith("authoringGraph.Nodes.", StringComparison.Ordinal))
            {
                int end = path.IndexOf(".Payload", StringComparison.Ordinal);
                var payload = GameplayGraphFields.Clone(serialized.FindProperty(path.Substring(0, end) + ".Payload").managedReferenceValue);
                GameplaySkillFieldSchema.SetPending(payload,path.Substring(end+9),serialized.FindProperty(path));
                if (payload is GameplayActionNodePayload action) action.Action.Validate(validation, "卡片");
                if (payload is GameplayAbilityNodePayload ability&&ability.Settings is GameplayAbilityDefinitionData settings)
                {settings.Cost?.Validate(validation,"卡片.Cost");settings.Charge?.Validate(validation,"卡片.Charge");}
                if (payload is GameplayRuleNodePayload rule) {rule.Trigger?.Validate(validation,"卡片.Trigger");rule.Target?.Validate(validation,"卡片.Target");}
            }
            else if (path.StartsWith("authoringGraph.LocalDefinitions.", StringComparison.Ordinal))
            {
                int end = path.IndexOf(".Payload", StringComparison.Ordinal);
                var payload=(GameplayLocalDefinitionPayload)GameplayGraphFields.Clone(serialized.FindProperty(path.Substring(0, end) + ".Payload").managedReferenceValue);
                GameplaySkillFieldSchema.SetPending(payload,path.Substring(end+9),serialized.FindProperty(path));
                payload.Validate(validation,"卡片");
            }
            int payloadAt=path.IndexOf(".Payload",StringComparison.Ordinal);
            string relative=payloadAt<0?path:"卡片"+path.Substring(payloadAt+8).Replace(".Array.data[","[");
            if(relative.StartsWith("卡片.Action.",StringComparison.Ordinal))relative="卡片."+relative.Substring(10);
            if(relative.StartsWith("卡片.Settings.",StringComparison.Ordinal))relative="卡片."+relative.Substring(12);
            var issues=validation.Issues.Where(issue=>issue.Severity==GameplayAuthoringIssueSeverity.Error&&
                (issue.Path=="卡片"||relative==issue.Path||relative.StartsWith(issue.Path+".",StringComparison.Ordinal)||issue.Path.StartsWith(relative+".",StringComparison.Ordinal))).ToArray();
            error = issues.Length>0 ? string.Join("\n", issues) : null;
            if (error != null) { serialized.Update(); return false; }
            bool changed = GameplaySkillFieldSchema.ApplyFields(serialized, document, false, new[] { path });
            if (!changed) error = GameplaySkillFieldSchema.Error(document);
            return changed;
        }
    }

    /// <summary>Native retained controls over one canvas-owned serialized view. Controls keep
    /// focus during scalar refresh; only a real field/type change rebuilds their layout.</summary>
    internal sealed class GameplaySkillCardEditor : VisualElement
    {
        private readonly GameplaySkillDocumentAsset _document;
        private readonly string _nodeId;
        private readonly SerializedObject _serialized;
        private List<GameplaySkillCardFields.Binding> _fields;
        private readonly List<Action> _sync=new List<Action>();
        private string _signature;
        private HelpBox _error;
        private bool _current=true;
        private readonly bool _compact;
        private static readonly IGameplaySkillFieldControlProvider[] ControlProviders=TypeCache.GetTypesDerivedFrom<IGameplaySkillFieldControlProvider>().Where(t=>!t.IsAbstract&&t.GetConstructor(Type.EmptyTypes)!=null).OrderBy(t=>t.FullName).Select(t=>(IGameplaySkillFieldControlProvider)Activator.CreateInstance(t)).ToArray();
        private static readonly Dictionary<string,Type> ObjectTypes=new Dictionary<string,Type>();
        internal bool HasFields=>_fields.Count>0;
        internal Action<string> Advanced;
        internal Action Selecting;
        private readonly string _policyRole;
        internal GameplaySkillCardEditor(GameplaySkillDocumentAsset document,string nodeId,SerializedObject serialized,bool compact=false,string policyRole=null)
        {
            _document=document;_nodeId=nodeId;_serialized=serialized;_compact=compact;_policyRole=policyRole;name="CardQuickFields";
            if(compact){style.flexDirection=FlexDirection.Row;style.flexWrap=Wrap.Wrap;style.alignItems=Align.FlexStart;}
            style.marginLeft=10;style.marginRight=10;style.marginBottom=8;style.fontSize=12;
            RegisterCallback<PointerDownEvent>(_=>Selecting?.Invoke(),TrickleDown.TrickleDown);
            RegisterCallback<PointerDownEvent>(e=>e.StopPropagation());
            RegisterCallback<KeyDownEvent>(e=>{
                if((e.ctrlKey||e.commandKey)&&(e.keyCode==KeyCode.Z||e.keyCode==KeyCode.Y)){
                    if(e.keyCode==KeyCode.Y||e.shiftKey)Undo.PerformRedo();else Undo.PerformUndo();
                    e.PreventDefault();e.StopImmediatePropagation();
                }
            },TrickleDown.TrickleDown);
            RegisterCallback<KeyDownEvent>(e=>{if(!(e.keyCode==KeyCode.S&&(e.ctrlKey||e.commandKey)))e.StopPropagation();});
            RegisterCallback<ContextualMenuPopulateEvent>(e=>e.StopPropagation());
            Refresh();
        }
        internal void Refresh()
        {
            _serialized.UpdateIfRequiredOrScript();
            _fields=_policyRole==null?GameplaySkillCardFields.Describe(_document,_nodeId,_serialized):GameplaySkillCardFields.DescribePolicy(_document,_nodeId,_policyRole);
            string signature=string.Join("|",_fields.Select(f=>f.Path+":"+f.Label+":"+f.Editable+":"+string.Join(",",f.Choices??Array.Empty<string>())));
            if(signature==_signature){foreach(var sync in _sync)sync();return;}
            _signature=signature;Invalidate();Clear();_sync.Clear();
            for(int i=0;i<_fields.Count;i++)
            {
                int slot=i;var field=_fields[i];
                if(!field.Editable){
                    var row=new VisualElement{style={flexDirection=FlexDirection.Row}};if(_compact)row.style.width=Length.Percent(100);
                    var text=new Label{style={flexGrow=1,whiteSpace=WhiteSpace.Normal,fontSize=12}};
                    text.style.whiteSpace=WhiteSpace.Normal;
                    text.RegisterCallback<ClickEvent>(e=>{Advanced?.Invoke(_fields[slot].Path);e.StopPropagation();});row.Add(text);Add(row);
                    _sync.Add(()=>{text.text=_fields[slot].Label+" · "+_fields[slot].Summary;text.tooltip=_fields[slot].Label+" · "+_fields[slot].Summary+"\n单击定位此高级字段";});continue;
                }
                var p=_serialized.FindProperty(field.Path);string path=field.Path;
                var provider=ControlProviders.FirstOrDefault(editor=>editor.SupportsControl(p));
                if(provider!=null){var control=provider.CreateControl(p,field.Label,()=>_current&&panel!=null&&!GameplaySkillAuthoringCommands.IsReadOnly(_document)&&(field.SourceGuard==null||field.SourceGuard()),out var refresh);control.name=path;control.style.fontSize=12;if(_compact){control.style.width=Length.Percent(100);control.style.flexShrink=0;control.style.marginRight=6;}Add(control);_sync.Add(refresh);continue;}
                switch(p.propertyType)
                {
                    case SerializedPropertyType.Float:
                        Bind(new FloatField(field.Label){isDelayed=true},path,q=>q.floatValue,(q,v)=>q.floatValue=v);break;
                    case SerializedPropertyType.Integer:
                        Bind(new IntegerField(field.Label){isDelayed=true},path,q=>q.intValue,(q,v)=>q.intValue=v);break;
                    case SerializedPropertyType.String:
                        if(field.Choices!=null){var options=field.Choices.ToList();if(!options.Contains(p.stringValue))options.Insert(0,p.stringValue);Bind(new DropdownField(field.Label,options,Math.Max(0,options.IndexOf(p.stringValue))),path,q=>q.stringValue,(q,v)=>q.stringValue=v);}
                        else Bind(new TextField(field.Label){isDelayed=path.EndsWith(".AnimationName",StringComparison.Ordinal)||path.EndsWith(".StateName",StringComparison.Ordinal)},path,q=>q.stringValue,(q,v)=>q.stringValue=v);break;
                    case SerializedPropertyType.Boolean:
                        Bind(new Toggle(field.Label),path,q=>q.boolValue,(q,v)=>q.boolValue=v);break;
                    case SerializedPropertyType.Enum:
                        var choices=GameplaySkillBusinessLabels.Options(p);
                        Bind(new DropdownField(field.Label,choices,p.enumValueIndex),path,q=>choices[q.enumValueIndex],(q,v)=>q.enumValueIndex=choices.IndexOf(v));break;
                    case SerializedPropertyType.ObjectReference:
                        if(!ObjectTypes.TryGetValue(p.type,out var type)){
                            string name=p.type.Replace("PPtr<$","").TrimEnd('>');
                            type=TypeCache.GetTypesDerivedFrom<UnityEngine.Object>().FirstOrDefault(t=>t.Name==name)??typeof(UnityEngine.Object);ObjectTypes[p.type]=type;
                        }
                        Bind(new UnityEditor.UIElements.ObjectField(field.Label){objectType=type,allowSceneObjects=false},path,q=>q.objectReferenceValue,(q,v)=>q.objectReferenceValue=v);break;
                    case SerializedPropertyType.ManagedReference:
                        if(p.managedReferenceValue is GameplaySkillTargetAuthoring){
                            var types=GameplayAuthoringTypeCatalog.Query(typeof(GameplaySkillTargetAuthoring),_document.AuthoringGraph.Scope).ToArray();
                            var names=types.Select(t=>GameplaySkillNodeDescriptors.Get(t).DisplayName).ToList();
                            var select=new DropdownField(field.Label,names,0);
                            Bind(select,path,q=>names[Math.Max(0,Array.IndexOf(types,q.managedReferenceValue.GetType()))],(q,v)=>q.managedReferenceValue=Activator.CreateInstance(types[names.IndexOf(v)]));
                        }break;
                    case SerializedPropertyType.Generic:
                        if(p.boxedValue is GameplayTagReference)Bind(new TextField(field.Label){isDelayed=path.Contains(".Trigger.EventTag")},path+".Name",q=>q.stringValue,(q,v)=>q.stringValue=v);
                        else if(p.boxedValue is GameplayAttributeReference)Bind(new TextField(field.Label),path+".Value",q=>q.stringValue,(q,v)=>q.stringValue=v);
                        break;
                }
            }
            if(_fields.Any(f=>f.SourcePath!=null||!f.Editable))Add(new Button(()=>{var menu=new GenericMenu();foreach(var source in _fields.Where(f=>f.SourcePath!=null||!f.Editable)){string path=source.SourcePath??source.Path;menu.AddItem(new GUIContent(source.Label+" / 数值来源与高级"),false,()=>Advanced?.Invoke(path));}menu.ShowAsContext();}){text="数值来源 / 高级参数",tooltip="显式切换常量、Formula、Caller或属性来源；保留验证及 Undo"});
            _error=new HelpBox("",HelpBoxMessageType.Error);_error.style.display=DisplayStyle.None;Add(_error);
            _current=true;SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode&&!GameplaySkillAuthoringCommands.IsReadOnly(_document));
            foreach(var sync in _sync)sync();
        }
        // A structural command replaces the indexed view. Disabling its old controls first
        // prevents delayed numeric FocusOut from committing through a retired property path.
        internal void Invalidate(){_current=false;SetEnabled(false);}
        private void Bind<T>(BaseField<T> control,string path,Func<SerializedProperty,T> read,Action<SerializedProperty,T> write)
        {
            control.name=path;var source=_serialized.FindProperty(path);control.tooltip=source.propertyType==SerializedPropertyType.Enum?string.Join(" / ",source.enumNames)+"\n"+path:path;control.style.fontSize=12;
            control.labelElement.style.minWidth=_compact?70:92;control.labelElement.style.maxWidth=_compact?70:92;
            if(_compact){control.style.width=194;control.style.marginRight=6;control.style.flexShrink=0;}
            Add(control);
            _sync.Add(()=>{var p=_serialized.FindProperty(path);if(p==null)return;var value=read(p);if(!EqualityComparer<T>.Default.Equals(control.value,value))control.SetValueWithoutNotify(value);});
            var sourceGuard=_fields.First(f=>f.Path==path||path.StartsWith(f.Path+".",StringComparison.Ordinal)).SourceGuard;
            control.RegisterValueChangedCallback(e=>{
                if(!_current||sourceGuard!=null&&!sourceGuard())return;
                _serialized.UpdateIfRequiredOrScript();var p=_serialized.FindProperty(path);write(p,e.newValue);
                bool applied=GameplaySkillCardFields.Apply(_serialized,_document,path,out var error);
                if(!applied)control.SetValueWithoutNotify(read(_serialized.FindProperty(path)));
                _error.text=error==null?"":control.label+" · "+error;_error.style.display=error==null?DisplayStyle.None:DisplayStyle.Flex;
                e.StopPropagation();
            });
        }
    }
}
