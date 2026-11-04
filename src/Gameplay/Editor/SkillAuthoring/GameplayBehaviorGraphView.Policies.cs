using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.UIElements;
using Node=UnityEditor.Experimental.GraphView.Node;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    public sealed partial class GameplayBehaviorGraphView
    {
        // These are editor port types, never execution types or serialized graph records.
        private sealed class CostConfiguration {}
        private sealed class RechargeConfiguration {}
        private readonly Dictionary<string,Node> _policyCards=new Dictionary<string,Node>();
        // The window owns this session-only list so native Undo can restore a deleted
        // disconnected card as well as a connected card's document fields.
        [Serializable] internal sealed class DetachedPolicy {
            public string Key;
            [SerializeReference] public GameplayAbilityCostAuthoring Cost;
            [SerializeReference] public GameplayAbilityChargeAuthoring Charge;
            public GameplayDefinitionBinding Cooldown;
        }
        internal GameplaySkillWorkbench ConfigurationOwner;
        private List<DetachedPolicy> _policyDrafts=>ConfigurationOwner.DetachedPolicies;
        public Action<string> PolicySelected;
        private string PolicyKey(string role)=>_document.GetInstanceID()+":"+role;
        private GameplayAbilityNodePayload AbilityPayload=>_document.AuthoringGraph.Nodes.Single(n=>n.Id==_document.AuthoringGraph.AbilityNodeId).Payload as GameplayAbilityNodePayload;

        private void AddPolicies(Node skill)
        {
            foreach(string role in new[]{"Cost","Recharge"}){
                var type=role=="Cost"?typeof(CostConfiguration):typeof(RechargeConfiguration);
                var input=skill.InstantiatePort(Orientation.Horizontal,Direction.Input,Port.Capacity.Single,type);
                input.portName=role=="Cost"?"◇ 消耗":"◇ 冷却 / 充能";input.tooltip="从创建菜单添加独立配置节点，自动关联当前技能。";input.userData=new Socket{Owner=_document.AuthoringGraph.AbilityNodeId,Config=role};input.portColor=new Color(.64f,.8f,.57f);skill.inputContainer.Add(input);
                var settings=AbilityPayload.Settings as GameplayAbilityDefinitionData;
                if(settings==null){skill.extensionContainer.Add(new Label("共享释放配置只读；请先在高级配置选择本地来源。"));continue;}
                bool connected=role=="Cost"?settings.Cost!=null:settings.Charge!=null||AbilityPayload.Definitions.Any(b=>b.PropertyPath=="CooldownEffect");
                if(connected||_policyDrafts.Any(p=>p.Key==PolicyKey(role)))AddPolicyCard(skill,input,role,connected);
            }
            skill.extensionContainer.Add(new Button(()=>ShowMenu(null,null,null,new Socket{Owner=_document.AuthoringGraph.PrimaryRuleNodeId,Port="Actions"})){text=_document.Mode==FRFrame.Gameplay.Standard.GameplayStandardActivationMode.Passive?"下一步：从主触发规则创建并连接行为":"下一步：从施放执行创建并连接行为",tooltip="连接实际主规则；被动技能先设置真实事件与主体，再选择行为。创建和连接是一次事务。"});
        }
        /// <summary>Creates the visible configuration cards through the same typed Ability fields
        /// as the card menu. Re-selecting existing content never resets it or moves its card.</summary>
        internal void AddConfigurationEntries(IGameplaySkillCreationMenu menu,Vector2 position)
        {
            foreach(string kind in new[]{"消耗","冷却","充能"}){
                string selection=kind;var caption=new GUIContent("技能/"+kind,"创建独立配置节点并自动关联当前技能；已有同类配置则保留。冷却与充能互斥，更换前会确认。");
                if(!(AbilityPayload.Settings is GameplayAbilityDefinitionData)||GameplaySkillAuthoringCommands.IsReadOnly(_document)){caption.tooltip="当前技能使用共享释放配置或处于只读范例视图；请先选择可编辑的本地技能配置。";menu.AddDisabledItem(caption);}
                else menu.AddItem(caption,false,()=>{
                    string role=selection=="消耗"?"Cost":"Recharge";bool existed=_policyCards.ContainsKey(role);var source=_document;
                    // Adding a second configuration must not displace an existing one.
                    foreach(var pair in _policyCards){string saved=GameplaySkillEditorState.Key(_document)+".Policy."+pair.Key;SessionState.SetVector3(saved,pair.Value.GetPosition().position);SessionState.SetBool(saved+".Moved",true);}
                    if(role=="Cost"&&((GameplayAbilityDefinitionData)AbilityPayload.Settings).Cost!=null)PolicySelected?.Invoke(role);
                    else if(role=="Cost")ChangeCost(typeof(GameplayAttributeAbilityCostAuthoring),false);else ChangeRecharge(selection=="充能");
                    var settings=(GameplayAbilityDefinitionData)AbilityPayload.Settings;
                    bool created=role=="Cost"?settings.Cost!=null:settings.Charge!=null||AbilityPayload.Definitions.Any(b=>b.PropertyPath=="CooldownEffect");
                    if(!created)return;
                    if(!existed){string key=GameplaySkillEditorState.Key(_document)+".Policy."+role;SessionState.SetVector3(key,position);SessionState.SetBool(key+".Moved",true);}
                    schedule.Execute(()=>{if(_document!=source)return;if(_policyCards.TryGetValue(role,out var card)){ClearSelection();AddToSelection(card);PolicySelected?.Invoke(role);}});
                });
            }
        }
        private void ShowPolicyChoices(string role)
        {
            GameplaySkillCreationMenu.Show(this,role=="Cost"?"更换消耗配置":"更换冷却 / 充能配置",menu=>{
                if(_policyDrafts.Any(p=>p.Key==PolicyKey(role)))menu.AddItem(new GUIContent("重新关联已解绑配置"),false,()=>ReconnectPolicy(role));
                if(role=="Cost")foreach(var type in GameplayAuthoringTypeCatalog.Query(typeof(GameplayAbilityCostAuthoring),_document.AuthoringGraph.Scope)){
                    var item=type;string name=GameplaySkillNodeDescriptors.Get(type).DisplayName;
                    bool existing=((GameplayAbilityDefinitionData)AbilityPayload.Settings).Cost?.GetType()==type;
                    menu.AddItem(new GUIContent("消耗/选择或创建/"+name),existing,()=>ChangeCost(item,false));
                    if(existing)menu.AddItem(new GUIContent("消耗/新建并替换现有/"+name),false,()=>ChangeCost(item,true));
                }
                else {
                    menu.AddItem(new GUIContent("冷却/普通冷却（已有则保留）"),false,()=>ChangeRecharge(false));
                    menu.AddItem(new GUIContent("冷却/顺序充能（已有则保留）"),false,()=>ChangeRecharge(true));
                    if(AbilityPayload.Definitions.Any(b=>b.PropertyPath=="CooldownEffect"))menu.AddItem(new GUIContent("冷却/新建普通冷却并替换现有…"),false,()=>ChangeRecharge(false,true));
                    if(((GameplayAbilityDefinitionData)AbilityPayload.Settings).Charge!=null)menu.AddItem(new GUIContent("冷却/新建默认充能并替换现有…"),false,()=>ChangeRecharge(true,true));
                    foreach(var local in _document.AuthoringGraph.LocalDefinitions.Where(d=>d.Payload is GameplayEffectDefinitionData e&&e.DurationPolicy==GameplayEffectDurationPolicy.Duration&&e.GrantedTags.Length>0&&e.Modifiers.Length==0&&e.Executions.Length==0&&e.Period==0)){
                        string id=local.Id;menu.AddItem(new GUIContent("冷却/复用本技能/"+local.Name),false,()=>{
                            if(AbilityPayload.Definitions.Any(b=>b.PropertyPath=="CooldownEffect"&&b.LocalId==id))return;
                            if(!ConfirmPolicyReplacement("Recharge","关联所选局部冷却"))return;
                            Try(()=>{Undo.RegisterCompleteObjectUndo(ConfigurationOwner,"关联冷却配置");GameplaySkillAuthoringCommands.Transaction(_document,"重新关联保留的冷却配置",g=>{
                            var ability=(GameplayAbilityNodePayload)g.Nodes.Single(n=>n.Id==g.AbilityNodeId).Payload;((GameplayAbilityDefinitionData)ability.Settings).Charge=null;
                            ability.Definitions.RemoveAll(b=>b.PropertyPath=="CooldownEffect");ability.Definitions.Add(new GameplayDefinitionBinding{PropertyPath="CooldownEffect",LocalId=id});
                        });_policyDrafts.RemoveAll(p=>p.Key==PolicyKey("Recharge"));});});
                    }
                }
            });
        }
        private void ChangeCost(Type type,bool replace)
        {
            if(!replace&&((GameplayAbilityDefinitionData)AbilityPayload.Settings).Cost?.GetType()==type){PolicySelected?.Invoke("Cost");return;}
            if(!ConfirmPolicyReplacement("Cost","新建消耗并替换原配置"))return;
            Try(()=>{Undo.RegisterCompleteObjectUndo(ConfigurationOwner,"配置消耗");GameplaySkillAuthoringCommands.Transaction(_document,"配置消耗",g=>{
                var settings=(GameplayAbilityDefinitionData)((GameplayAbilityNodePayload)g.Nodes.Single(n=>n.Id==g.AbilityNodeId).Payload).Settings;
                settings.Cost=(GameplayAbilityCostAuthoring)Activator.CreateInstance(type);
                if(settings.Cost is GameplayAttributeAbilityCostAuthoring cost)cost.Resources=new[]{new GameplayAttributeAbilityCostAuthoring.Entry{Attribute=new GameplayAttributeReference("Mana")}};
            });_policyDrafts.RemoveAll(p=>p.Key==PolicyKey("Cost"));});
        }
        private void ChangeRecharge(bool charge,bool replace=false)
        {
            var current=(GameplayAbilityDefinitionData)AbilityPayload.Settings;
            if(!replace&&(charge?current.Charge!=null:AbilityPayload.Definitions.Any(b=>b.PropertyPath=="CooldownEffect"))){PolicySelected?.Invoke("Recharge");return;}
            if(!ConfirmPolicyReplacement("Recharge",charge?"新建默认充能（2次 / 3秒）":"新建普通冷却（3秒）"))return;
            Try(()=>{Undo.RegisterCompleteObjectUndo(ConfigurationOwner,"切换冷却或充能");GameplaySkillAuthoringCommands.Transaction(_document,"切换冷却或充能",g=>{
                var ability=(GameplayAbilityNodePayload)g.Nodes.Single(n=>n.Id==g.AbilityNodeId).Payload;var settings=(GameplayAbilityDefinitionData)ability.Settings;
                ability.Definitions.RemoveAll(b=>b.PropertyPath=="CooldownEffect");settings.Charge=charge?new GameplayAbilityChargeAuthoring{MaxCharges=2,RechargeDuration=3}:null;
                if(!charge){var local=new GameplayLocalDefinition{Name="技能冷却",SourceDefinitionId=_document.DefinitionId,Payload=new GameplayEffectDefinitionData{DurationPolicy=GameplayEffectDurationPolicy.Duration,Duration=3,GrantedTags=new[]{new GameplayTagReference("Cooldown.Skill."+_document.SkillId)}}};g.LocalDefinitions.Add(local);ability.Definitions.Add(new GameplayDefinitionBinding{PropertyPath="CooldownEffect",LocalId=local.Id});}
            },generatedTags:charge?null:new[]{"Cooldown.Skill."+_document.SkillId});_policyDrafts.RemoveAll(p=>p.Key==PolicyKey("Recharge"));});
        }
        private bool ConfirmPolicyReplacement(string role,string operation,bool detach=false)
        {
            var settings=(GameplayAbilityDefinitionData)AbilityPayload.Settings;
            var draft=_policyDrafts.Find(p=>p.Key==PolicyKey(role));
            object value=role=="Cost"?(object)(settings.Cost??draft?.Cost):settings.Charge??draft?.Charge;
            var cooldown=AbilityPayload.Definitions.SingleOrDefault(b=>b.PropertyPath=="CooldownEffect")??draft?.Cooldown;
            if(value==null&&(role=="Cost"||cooldown==null))return true;
            string summary;
            if(value is GameplayAttributeAbilityCostAuthoring cost)summary=string.Join("\n",cost.Resources.Select(e=>e.Attribute.Value+"："+GameplaySkillFieldSchema.FloatSummary(e.Delta)+"；最低余额 "+e.MinimumBalance));
            else if(value is GameplayAbilityChargeAuthoring charge)summary="充能 "+charge.MaxCharges+" 次 / 回充 "+charge.RechargeDuration+" 秒";
            else if(value!=null)summary=GameplaySkillNodeDescriptors.Get(value.GetType()).DisplayName+"\n"+JsonUtility.ToJson(value,true);
            else if(cooldown.External)summary="普通冷却："+cooldown.External.name+"（共享来源，只读）";
            else {var local=_document.AuthoringGraph.LocalDefinitions.Single(d=>d.Id==cooldown.LocalId);var effect=(GameplayEffectDefinitionData)local.Payload;summary="普通冷却："+local.Name+"\n时长："+(effect.DurationValue==null?effect.Duration.ToString():GameplaySkillFieldSchema.FloatSummary(effect.DurationValue))+" 秒；Tag："+string.Join("、",effect.GrantedTags.Select(t=>t.Name));}
            string warning=detach?"将清除技能中的这项配置。仅在当前工作台保留临时重连缓存；保存不包含缓存，关闭、换源或域重载后不能靠缓存恢复。":"将丢弃原消耗 / 充能配置（包括临时重连缓存），使用新的配置。保存、关闭后不能靠缓存恢复。";
            return EditorUtility.DisplayDialog(operation,"原配置：\n"+summary+"\n\n"+warning+"\n只有本次编辑的 Undo 可撤销。局部冷却定义仍留在技能中，可从菜单重新关联；外部共享 SO 不修改。",detach?"确认清除关联":"确认替换","取消");
        }
        private void AddPolicyCard(Node skill,Port input,string role,bool connected)
        {
            var settings=(GameplayAbilityDefinitionData)AbilityPayload.Settings;
            bool charge=(connected?settings.Charge:_policyDrafts.Single(p=>p.Key==PolicyKey(role)).Charge)!=null;
            var card=new Node{title=role=="Cost"?"消耗":charge?"充能":"冷却",viewDataKey="Config:"+role,layer=-100};card.style.width=310;card.style.minWidth=310;
            card.style.fontSize=12;card.mainContainer.style.backgroundColor=new Color(.18f,.19f,.2f,1);
            card.titleContainer.style.backgroundColor=new Color(.26f,.36f,.25f);
            card.titleContainer.style.minHeight=30;var heading=card.titleContainer.Q<Label>();heading.style.fontSize=14;heading.style.unityFontStyleAndWeight=FontStyle.Bold;
            card.RegisterCallback<PointerDownEvent>(_=>PolicySelected?.Invoke(role),TrickleDown.TrickleDown);card.expanded=true;card.Q("collapse-button")?.RemoveFromHierarchy();
            var output=card.InstantiatePort(Orientation.Horizontal,Direction.Output,Port.Capacity.Multi,input.portType);
            output.portName="◇ 关联技能";output.userData=new Socket{Owner=_document.AuthoringGraph.AbilityNodeId,Config=role};output.portColor=input.portColor;
            output.RemoveManipulator(output.edgeConnector);output.AddManipulator(new EdgeConnector<Edge>(new CreationConnector(this)));card.outputContainer.Add(output);
            StylePortBackground(card);
            card.extensionContainer.Add(new Label(connected?"已关联技能施放":"已解绑 · 临时重连缓存不随技能保存；关闭 / 换源 / 域重载后丢失"){style={whiteSpace=WhiteSpace.Normal,fontSize=11}});
            if(connected){
                if(role=="Cost")card.extensionContainer.Add(new Label("消耗方式："+GameplaySkillNodeDescriptors.Get(settings.Cost.GetType()).DisplayName){style={whiteSpace=WhiteSpace.Normal,fontSize=11}});
                if(role=="Cost"&&settings.Cost is GameplayAttributeAbilityCostAuthoring)card.extensionContainer.Add(new Label("资源变化：负数扣除，正数增加。"){style={whiteSpace=WhiteSpace.Normal,fontSize=11}});
                card.extensionContainer.Add(new GameplaySkillCardEditor(_document,_document.AuthoringGraph.AbilityNodeId,_cardSerialized,policyRole:role){Selecting=()=>PolicySelected?.Invoke(role),Advanced=path=>{PolicySelected?.Invoke(role);if(path!=null)GameplaySkillFieldSchema.RevealField(_document,path);ConfigurationOwner.OpenDetails();}});
                card.extensionContainer.Add(new Button(()=>{PolicySelected?.Invoke(role);ConfigurationOwner.OpenDetails();}){text="复杂配置 / 数值来源…"});
                card.extensionContainer.Add(new Button(()=>ShowPolicyChoices(role)){text="更换配置 / 关联来源…"});
                card.extensionContainer.Add(new Button(()=>DetachPolicy(role)){text="清除关联…（临时重连 / Undo）"});
            }else card.extensionContainer.Add(new Button(()=>ReconnectPolicy(role)){text="重新关联此配置"});
            if(role=="Recharge"&&connected){var binding=AbilityPayload.Definitions.SingleOrDefault(b=>b.PropertyPath=="CooldownEffect");var local=_document.AuthoringGraph.LocalDefinitions.Find(d=>d.Id==binding?.LocalId);if(local!=null&&!_outputs.Values.Any(p=>(p.userData as Socket)?.Owner==local.Id)){foreach(var branch in GameplayGraphFields.ActionArrays(local.Payload))ResourcePort(card,local,binding,branch.Path);foreach(var branch in GameplayGraphEffectCallbacks.Ports(local.Payload))ResourcePort(card,local,binding,branch);}}
            string key=GameplaySkillEditorState.Key(_document)+".Policy."+role;
            var position=SessionState.GetVector3(key,new Vector3(skill.GetPosition().x-390,skill.GetPosition().y+(role=="Cost"?0:300),0));
            card.SetPosition(new Rect(position,new Vector2(310,0)));AddElement(card);_policyCards[role]=card;card.RefreshExpandedState();card.RefreshPorts();
            if(connected){var wire=new GameplaySkillExecutionEdge{output=output,input=input,layer=1};output.Connect(wire);input.Connect(wire);wire.edgeControl.edgeWidth=2;wire.userData=new Socket{Config=role};wire.tooltip="关联当前技能的消耗、冷却或充能配置。";AddElement(wire);}
        }
        private void PositionPolicies()
        {
            if(!_nodes.TryGetValue(_document.AuthoringGraph.AbilityNodeId,out var skill))return;
            foreach(var pair in _policyCards){
                string key=GameplaySkillEditorState.Key(_document)+".Policy."+pair.Key;
                if(SessionState.GetBool(key+".Moved",false))continue;
                var rect=pair.Value.GetPosition();var origin=_layoutPreview!=null&&_layoutPreview.TryGetValue(skill.viewDataKey,out var projected)?projected:skill.GetPosition().position;if(_layoutPreview!=null)origin.x=Mathf.Min(origin.x,_layoutPreview.Values.Min(p=>p.x));rect.position=origin+new Vector2(-390,pair.Key=="Recharge"&&_policyCards.ContainsKey("Cost")?Mathf.Max(280,_policyCards["Cost"].GetPosition().height+40):0);pair.Value.SetPosition(rect);
            }
        }
        private void DetachPolicy(string role)
        {
            var settings=(GameplayAbilityDefinitionData)AbilityPayload.Settings;
            if(role=="Cost"?settings.Cost==null:settings.Charge==null&&!AbilityPayload.Definitions.Any(b=>b.PropertyPath=="CooldownEffect"))return;
            if(!ConfirmPolicyReplacement(role,"清除配置关联",true))return;
            var draft=new DetachedPolicy{Key=PolicyKey(role),Cost=settings.Cost,Charge=settings.Charge,Cooldown=AbilityPayload.Definitions.SingleOrDefault(b=>b.PropertyPath=="CooldownEffect")};
            Try(()=>{Undo.RegisterCompleteObjectUndo(ConfigurationOwner,"解除配置关联");GameplaySkillAuthoringCommands.Transaction(_document,"解除配置关联（可撤销）",g=>{
                var ability=(GameplayAbilityNodePayload)g.Nodes.Single(n=>n.Id==g.AbilityNodeId).Payload;var value=(GameplayAbilityDefinitionData)ability.Settings;
                if(role=="Cost")value.Cost=null;else{value.Charge=null;ability.Definitions.RemoveAll(b=>b.PropertyPath=="CooldownEffect");}
            });_policyDrafts.RemoveAll(p=>p.Key==PolicyKey(role));_policyDrafts.Add(draft);Load(_document);});
        }
        private void ReconnectPolicy(string role)
        {
            var draft=_policyDrafts.Find(p=>p.Key==PolicyKey(role));if(draft==null)return;
            var settings=(GameplayAbilityDefinitionData)AbilityPayload.Settings;
            if(role=="Cost"?settings.Cost!=null:settings.Charge!=null||AbilityPayload.Definitions.Any(b=>b.PropertyPath=="CooldownEffect"))return;
            Try(()=>GameplaySkillAuthoringCommands.Transaction(_document,"重新关联配置",g=>{
                var ability=(GameplayAbilityNodePayload)g.Nodes.Single(n=>n.Id==g.AbilityNodeId).Payload;var value=(GameplayAbilityDefinitionData)ability.Settings;
                if(role=="Cost")value.Cost=(GameplayAbilityCostAuthoring)GameplayGraphFields.Clone(draft.Cost);
                else {value.Charge=(GameplayAbilityChargeAuthoring)GameplayGraphFields.Clone(draft.Charge);ability.Definitions.RemoveAll(b=>b.PropertyPath=="CooldownEffect");if(draft.Cooldown!=null)ability.Definitions.Add((GameplayDefinitionBinding)GameplayGraphFields.Clone(draft.Cooldown));}
            }));
        }
        /// <summary>Policy cards and the dedicated advanced view edit only the original Ability fields.</summary>
        public void DrawPolicy(string role,bool advanced)
        {
            if(!_document)return;int i=_document.AuthoringGraph.Nodes.FindIndex(n=>n.Id==_document.AuthoringGraph.AbilityNodeId);
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode||GameplaySkillAuthoringCommands.IsReadOnly(_document)))using(var so=new SerializedObject(_document)){
                var ability=so.FindProperty("authoringGraph.Nodes").GetArrayElementAtIndex(i).FindPropertyRelative("Payload");var settings=ability.FindPropertyRelative("Settings");
                if(!(settings?.managedReferenceValue is GameplayAbilityDefinitionData)){GUILayout.Label("共享释放配置只读");return;}
                var paths=new List<string>();EditorGUI.BeginChangeCheck();
                if(role=="Cost"){
                    var cost=settings.FindPropertyRelative("Cost");
                    if(cost.managedReferenceValue is GameplayAttributeAbilityCostAuthoring attributeCost){
                        var resources=cost.FindPropertyRelative("Resources");
                        GUILayout.Label("资源消耗 · "+resources.arraySize+" 项",EditorStyles.boldLabel);
                        for(int j=0;j<resources.arraySize;j++){
                            var entry=resources.GetArrayElementAtIndex(j);var delta=entry.FindPropertyRelative("Delta");
                            EditorGUILayout.BeginHorizontal();
                            EditorGUILayout.LabelField("资源属性",attributeCost.Resources[j].Attribute.Value+"（配置节点编辑）");
                            bool remove;
                            using(new EditorGUI.DisabledScope(resources.arraySize<=1))
                                remove=GUILayout.Button(new GUIContent("移除","至少保留一项资源。取消全部消耗可删除消耗节点。"),GUILayout.Width(46));
                            EditorGUILayout.EndHorizontal();
                            if(remove)EditCostResources(j);
                            GameplayAuthoringInspector.Field(entry.FindPropertyRelative("MinimumBalance"));
                            if(delta.managedReferenceValue is GameplayConstantFloatValueAuthoring){
                                EditorGUILayout.LabelField("资源变化",GameplaySkillFieldSchema.FloatSummary(attributeCost.Resources[j].Delta)+"（配置节点编辑）");
                                if(GUILayout.Button("切换此资源的数值来源…")){
                                    var handle=GameplaySourceHandle.Capture(_document,delta.propertyPath);
                                    GameplayAuthoringTypePicker.Show(typeof(GameplayFloatValueAuthoring),_document,type=>{
                                        if(!handle.TryResolve(out string target))return;
                                        using(var pending=new SerializedObject(handle.Owner)){pending.FindProperty(target).managedReferenceValue=type==null?null:Activator.CreateInstance(type);GameplaySkillFieldSchema.ApplyFields(pending,(GameplaySkillDocumentAsset)handle.Owner,false,new[]{target});}
                                    },_document.AuthoringGraph.Scope);
                                }
                            }else GameplayAuthoringInspector.Field(delta);
                            paths.Add(entry.propertyPath);
                        }
                        if(GUILayout.Button(new GUIContent("+ 添加资源项","复制第一项的资源属性，变化值为0。可在消耗节点修改属性和数值。")))
                            EditCostResources(-1);
                    }else GameplayAuthoringInspector.Field(cost);
                    paths.Add(cost.propertyPath);
                }else if(settings.FindPropertyRelative("Charge").managedReferenceValue!=null){var value=((GameplayAbilityDefinitionData)AbilityPayload.Settings).Charge;EditorGUILayout.LabelField("充能 / 回充",value.MaxCharges+" 次 / "+value.RechargeDuration+" 秒（配置节点编辑）");}
                else {
                    var binding=AbilityPayload.Definitions.SingleOrDefault(b=>b.PropertyPath=="CooldownEffect");
                    if(binding?.External){EditorGUILayout.ObjectField("共享冷却（只读）",binding.External,typeof(GameplayEffectDefinitionAsset),false);if(GUILayout.Button("定位共享冷却"))EditorGUIUtility.PingObject(binding.External);}
                    else {int index=_document.AuthoringGraph.LocalDefinitions.FindIndex(d=>d.Id==binding?.LocalId);if(index>=0){
                        var effect=so.FindProperty("authoringGraph.LocalDefinitions").GetArrayElementAtIndex(index).FindPropertyRelative("Payload");
                        var duration=effect.FindPropertyRelative("DurationValue");
                        if(duration.managedReferenceValue==null||duration.managedReferenceValue is GameplayConstantFloatValueAuthoring){
                            var data=(GameplayEffectDefinitionData)_document.AuthoringGraph.LocalDefinitions[index].Payload;
                            EditorGUILayout.LabelField("冷却秒数",(data.DurationValue==null?data.Duration.ToString("0.###"):GameplaySkillFieldSchema.FloatSummary(data.DurationValue))+"（配置节点编辑）");
                            if(GUILayout.Button("切换冷却的数值来源…")){
                                var guard=GameplaySkillFieldSchema.CaptureBinding(_document,_document.AuthoringGraph.AbilityNodeId,"CooldownEffect",binding.LocalId);
                                var handle=GameplaySourceHandle.Capture(_document,duration.propertyPath);
                                GameplayAuthoringTypePicker.Show(typeof(GameplayFloatValueAuthoring),_document,type=>{
                                    if(!guard()||!handle.TryResolve(out string target))return;
                                    using(var pending=new SerializedObject(handle.Owner)){pending.FindProperty(target).managedReferenceValue=type==null?null:Activator.CreateInstance(type);GameplaySkillFieldSchema.ApplyFields(pending,(GameplaySkillDocumentAsset)handle.Owner,false,new[]{target});}
                                },_document.AuthoringGraph.Scope);
                            }
                        }else GameplayAuthoringInspector.Field(duration);
                        paths.Add(duration.propertyPath);
                        var tags=effect.FindPropertyRelative("GrantedTags");GameplayAuthoringInspector.Field(tags);paths.Add(tags.propertyPath);
                    }}
                }
                if(EditorGUI.EndChangeCheck())GameplaySkillFieldSchema.ApplyFields(so,_document,false,paths);
            }
        }

        private void EditCostResources(int removeIndex)
        {
            bool add=removeIndex<0;
            Try(()=>GameplaySkillAuthoringCommands.Transaction(_document,add?"添加消耗资源项":"移除消耗资源项",graph=>{
                var ability=(GameplayAbilityNodePayload)graph.Nodes.Single(n=>n.Id==graph.AbilityNodeId).Payload;
                var cost=(GameplayAttributeAbilityCostAuthoring)((GameplayAbilityDefinitionData)ability.Settings).Cost;
                cost.Resources=add
                    ?cost.Resources.Append(new GameplayAttributeAbilityCostAuthoring.Entry{
                        Attribute=cost.Resources.Length==0?new GameplayAttributeReference("Mana"):cost.Resources[0].Attribute,
                        Delta=new GameplayConstantFloatValueAuthoring{Value=0}
                    }).ToArray()
                    :cost.Resources.Where((_,index)=>index!=removeIndex).ToArray();
            },structural:false,invalidation:GameplaySkillAuthoringChange.Local(new[]{_document.AuthoringGraph.AbilityNodeId})));
            GUIUtility.ExitGUI();
        }
    }
}
