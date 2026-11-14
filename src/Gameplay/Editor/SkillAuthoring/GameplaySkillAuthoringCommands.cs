using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>All structural authoring writes are stable-ID transactions on the main document.
    /// Validation operates on a plain clone before Undo/Dirty/preview can change.</summary>
    public static class GameplaySkillAuthoringCommands
    {
        public static event Action<GameplaySkillDocumentAsset, bool> Changed;
        /// <summary>Precise UI invalidation. Legacy/unknown writers still request safe fallback.</summary>
        public static event Action<GameplaySkillDocumentAsset,GameplaySkillAuthoringChange> DetailedChanged;
        private static void Publish(GameplaySkillDocumentAsset document,GameplaySkillAuthoringChange change){DetailedChanged?.Invoke(document,change);Changed?.Invoke(document,change.Structural);}
        private static GameplayAuthoringGraph _clipboard;
        internal static Action<string> CreationFailureInjection;
        public static bool ClipboardHasRules=>_clipboard?.Nodes.Any(n=>n.Payload is GameplayRuleNodePayload)==true;

        /// <summary>Creates only the main SkillDocument. No writable workspace or child SOs
        /// are generated; the initial sequence and End are ordinary embedded behaviors.
        /// Template self references are remapped by typed field semantics. The copy wizard may
        /// explicitly preserve selected source property paths for intentional cross-skill use.</summary>
        public static GameplaySkillDocumentAsset CreateDocument(string path, int id, string name, string scope,bool createTimeline=false,int framesPerSecond=30,bool createInstant=false,GameplaySkillDocumentAsset template=null,ICollection<string> preservedTemplateReferences=null)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("退出 Play 后创建正式技能。");
            if (id <= 0 || string.IsNullOrWhiteSpace(name) || !path.StartsWith("Assets/", StringComparison.Ordinal) ||
                !path.EndsWith(".asset", StringComparison.Ordinal) || path.Contains("..") || AssetDatabase.LoadMainAssetAtPath(path))
                throw new ArgumentException("需要未占用的 Assets 内 .asset 路径、正整数 ID 和名称。");
            if(framesPerSecond<1||string.IsNullOrWhiteSpace(scope))throw new ArgumentException("Scope 和显示帧率无效。");
            if(template&&(template.AuthoringSchemaVersion!=2||template.AuthoringGraph.Scope!=scope))throw new ArgumentException("复制必须使用 Schema 2 源技能的原 Scope。");
            var references=template?GameplaySkillCopyReferences.Audit(template):null;
            var preserve=preservedTemplateReferences??Array.Empty<string>();
            if(preserve.Any(p=>references==null||!references.Any(r=>r.IsSelf&&r.LocalStackOrdinal==0&&r.Path==p)))throw new ArgumentException("复制引用选择已失效；请重新打开复制向导。");
            if(references!=null&&references.Where(r=>r.CooldownGroup!=null).GroupBy(r=>r.CooldownGroup).Any(g=>g.Any(r=>preserve.Contains(r.Path))&&g.Any(r=>!preserve.Contains(r.Path))))throw new ArgumentException("本地冷却的授予、门禁和业务引用必须整组独立化或整组共享。");
            var catalogScope=GameplayReferenceCatalogProvider.Scope(path,scope);
            var catalog=GameplayReferenceCatalogProvider.Find(catalogScope,out string catalogError);
            if(!catalog)throw new InvalidOperationException(catalogError);
            string catalogPath=AssetDatabase.GetAssetPath(catalog);
            if(catalog&&EditorUtility.IsDirty(catalog))throw new InvalidOperationException("Tag Catalog 有未保存修改；没有创建主技能。");
            string catalogJson=catalog?EditorJsonUtility.ToJson(catalog):null;
            byte[] catalogBytes=catalog&&System.IO.File.Exists(catalogPath)?System.IO.File.ReadAllBytes(catalogPath):null;
            var document = template?UnityEngine.Object.Instantiate(template):ScriptableObject.CreateInstance<GameplaySkillDocumentAsset>();
            Undo.IncrementCurrentGroup();int undoGroup=Undo.GetCurrentGroup();bool catalogTouched=false,created=false;
            try
            {
            document.name = name; document.DisplayName = name; document.SkillId = id;
            document.CastEvent = new GameplayTagReference(GeneratedSkillTag(catalogScope,"Event.Created.","S"+id));
            using (var serialized = new SerializedObject(document)) { serialized.FindProperty("definitionId").stringValue = id + ".Skill"; serialized.ApplyModifiedPropertiesWithoutUndo(); }
            var graph = new GameplayAuthoringGraph { Scope = scope };
            var ability = new GameplayAuthoringNode { Title = "技能施放", Position = new Vector2(30,40), Payload = new GameplayAbilityNodePayload { Settings = new GameplayAbilityDefinitionData() } };
            var rule = new GameplayAuthoringNode { Title = "施放入口", Position = new Vector2(30,240), Payload = new GameplayRuleNodePayload { Trigger = new GameplaySkillEventTriggerAuthoring { EventTag = document.CastEvent }, Target = new GameplaySkillContextTargetAuthoring(), PreserveTriggerContext = true } };
            var sequence = new GameplayAuthoringTimeline { Name = "施放时间编排",FramesPerSecond=framesPerSecond };
            var sequencePayload = new GameplayActionNodePayload { Action = new GameplayOwnedSequenceActionAuthoring() };
            sequencePayload.Definitions.Add(new GameplayDefinitionBinding { PropertyPath = "Sequence", LocalId = sequence.Id });
            var sequenceNode = new GameplayAuthoringNode { Title = "时间编排", Position = new Vector2(360,100), Payload = sequencePayload };
            var end = new GameplayAuthoringNode { Title = "结束施放", Position = new Vector2(850,300), Payload = new GameplayActionNodePayload { Action = new GameplayEndAbilityActionAuthoring() } };
            sequence.Events.Add(new GameplayScheduledEntry { TimeSeconds = 1, EntryNodeId = end.Id, Target = new GameplaySkillContextTargetAuthoring() });
            graph.Nodes.AddRange(new[] { ability,rule,sequenceNode,end }); graph.Timelines.Add(sequence); graph.AbilityNodeId = ability.Id; graph.PrimaryRuleNodeId = rule.Id;
            graph.Connections.Add(new GameplayAuthoringConnection { FromNodeId = rule.Id, OutputPortKey = "Actions", ToNodeId = sequenceNode.Id });
            if(!createTimeline){graph.Nodes.Remove(sequenceNode);graph.Timelines.Clear();graph.Connections.Clear();graph.Connections.Add(new GameplayAuthoringConnection{FromNodeId=rule.Id,OutputPortKey="Actions",ToNodeId=end.Id});}
            if(createInstant){
                var search=new GameplayAuthoringNode{Title="搜索目标",Position=new Vector2(450,80),Payload=new GameplayActionNodePayload{Action=new GameplaySearchTargetActionAuthoring()}};
                var effect=new GameplayLocalDefinition{Name="即时伤害",SourceDefinitionId=document.DefinitionId,Payload=new GameplayEffectDefinitionData{Executions=new GameplayEffectExecutionAuthoring[]{new GameplayDamageExecutionAuthoring()}}};
                var hit=new GameplayAuthoringNode{Title="造成伤害",Position=new Vector2(850,80),Payload=new GameplayActionNodePayload{Action=new GameplayApplyToTargetsActionAuthoring()}};
                hit.Payload.Definitions.Add(new GameplayDefinitionBinding{PropertyPath="Effect",LocalId=effect.Id});graph.Nodes.AddRange(new[]{search,hit});graph.LocalDefinitions.Add(effect);graph.Connections.Clear();
                ConnectInGraph(graph,rule.Id,"Actions",search.Id);ConnectInGraph(graph,search.Id,"EachTarget",hit.Id);ConnectInGraph(graph,search.Id,"Then",end.Id);
            }
            if(template)graph=CopyGraph(template,document);
            document.SetAuthoringGraph(graph);
                var generatedTags=template?GameplaySkillCopyReferences.Apply(document,references,preserve,catalogScope):new List<string>();
                generatedTags.Add(document.CastEvent.Name);
                var nextTags=GameplayReferenceCatalogProvider.WithRegistrations(catalog,generatedTags);
                document.Build(new GameplayAuthoringBuildContext()); AssetDatabase.CreateAsset(document,path);created=true; AssetDatabase.SaveAssetIfDirty(document);
                CreationFailureInjection?.Invoke("after-create");
                if (!nextTags.SequenceEqual(catalog.Tags)) { Undo.RecordObject(catalog,"登记复制身份与施放事件");catalogTouched=true; catalog.Tags = nextTags; EditorUtility.SetDirty(catalog);CreationFailureInjection?.Invoke("catalog-save"); AssetDatabase.SaveAssetIfDirty(catalog); }
                CreationFailureInjection?.Invoke("after-catalog-save");
                Undo.RegisterCreatedObjectUndo(document,"新建正式技能");Undo.CollapseUndoOperations(undoGroup);
                return document;
            }
            catch {
                Undo.RevertAllDownToGroup(undoGroup);
                if(catalogTouched&&catalog){EditorJsonUtility.FromJsonOverwrite(catalogJson,catalog);EditorUtility.ClearDirty(catalog);if(catalogBytes!=null){System.IO.File.WriteAllBytes(catalogPath,catalogBytes);AssetDatabase.ImportAsset(catalogPath,ImportAssetOptions.ForceUpdate);}}
                if(created&&AssetDatabase.LoadMainAssetAtPath(path)==document){if(!AssetDatabase.DeleteAsset(path))throw new InvalidOperationException("创建失败且 Unity 未能清理新文件："+path);}
                if(document&&!AssetDatabase.Contains(document))UnityEngine.Object.DestroyImmediate(document);throw;
            }
        }

        internal static string GeneratedSkillTag(string scope,string prefix,string suffix) =>
            prefix+(scope==GameplayReferenceCatalogProvider.Framework?"":scope+".")+suffix;

        /// <summary>Reference viewing is a workbench mode, never a second skill runtime source.</summary>
        internal static GameplaySkillDocumentAsset ReadOnlyDocument;
        /// <summary>Whether the current Workbench presents this document as a read-only reference.</summary>
        public static bool IsReadOnly(GameplaySkillDocumentAsset document)=>document&&document==ReadOnlyDocument;
        private static GameplayAuthoringGraph CopyGraph(GameplaySkillDocumentAsset source,GameplaySkillDocumentAsset destination)
        {
            var original=source.AuthoringGraph;
            var ids=original.Nodes.Select(n=>n.Id).Concat(original.LocalDefinitions.Select(d=>d.Id)).Concat(original.Timelines.Select(t=>t.Id)).Concat(original.Connections.Select(e=>e.Id)).Concat(original.Timelines.SelectMany(t=>t.Events).Select(e=>e.Id)).ToHashSet();
            string json=JsonUtility.ToJson(original);
            foreach(System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(json,"\"AuthoringId\":\"([a-f0-9]{32})\""))ids.Add(match.Groups[1].Value);
            foreach(string id in ids)json=json.Replace(id,GameplayAuthoringGraph.NewId());
            var graph=JsonUtility.FromJson<GameplayAuthoringGraph>(json);
            foreach(var node in graph.Nodes){node.SourceDefinitionId=destination.DefinitionId;node.SourceActionId=-1;}
            foreach(var local in graph.LocalDefinitions)local.SourceDefinitionId=destination.DefinitionId;
            foreach(var timeline in graph.Timelines)timeline.SourceDefinitionId=destination.DefinitionId;
            return graph;
        }

        public static string CreateRule(GameplaySkillDocumentAsset document, Vector2 position)
        {
            string id = GameplayAuthoringGraph.NewId();
            Transaction(document, "创建触发规则", graph => {
                graph.Nodes.Add(new GameplayAuthoringNode { Id = id, Title = "触发规则", Position = position, Payload = new GameplayRuleNodePayload {
                    Trigger = new GameplaySkillEventTriggerAuthoring { EventTag = document.CastEvent }, Target = new GameplaySkillContextTargetAuthoring() } });
                // New rules stay drafts until explicitly enabled after connecting real actions.
            }); return id;
        }

        public static void Transaction(GameplaySkillDocumentAsset document, string label, Action<GameplayAuthoringGraph> change, bool structural=true, GameplaySkillAuthoringChange invalidation=null, IEnumerable<string> generatedTags=null)
        {
            Editable(document);
            var graph = (GameplayAuthoringGraph)GameplayGraphFields.Clone(document.AuthoringGraph);
            GameplayGraphEffectCallbacks.ExposeBindings(graph);
            change(graph);
            GameplayBehaviorGraphView.FollowMainAnimationDurations(document,graph);
            GameplayGraphEffectCallbacks.ExposeBindings(graph);
            var validation = new GameplayAuthoringValidationContext();
            GameplayGraphCompiler.ValidateStructure(graph, validation, label, false);
            if (validation.HasErrors) throw new InvalidOperationException(string.Join("\n", validation.Issues));
            GameplayGraphCompiler.ValidateActionOrder(graph);
            GameplayReferenceCatalogAsset catalog=null;string[] nextTags=null;
            if(generatedTags!=null){
                catalog=GameplayReferenceCatalogProvider.Find(GameplayReferenceCatalogProvider.Scope(document),out string error);
                if(!catalog)throw new InvalidOperationException(error);
                nextTags=GameplayReferenceCatalogProvider.WithRegistrations(catalog,generatedTags);
            }
            if(catalog&&EditorUtility.IsDirty(catalog))throw new InvalidOperationException("Tag Catalog 有未保存修改；先保存后创建自动Tag。");
            Undo.RegisterCompleteObjectUndo(document, label);
            if(catalog&&!nextTags.SequenceEqual(catalog.Tags)){Undo.RecordObject(catalog,label);catalog.Tags=nextTags;EditorUtility.SetDirty(catalog);AssetDatabase.SaveAssetIfDirty(catalog);}
            document.SetAuthoringGraph(graph); EditorUtility.SetDirty(document);
            Publish(document,invalidation??GameplaySkillAuthoringChange.Full(structural));
        }

        private static void Editable(GameplaySkillDocumentAsset document)
        {
            if(IsReadOnly(document))throw new InvalidOperationException("只读范例；请先以此创建新技能。");
            if (!document || document.AuthoringSchemaVersion != 2 || document.AuthoringGraph == null)
                throw new InvalidOperationException("此数据格式不能创作，请使用 Schema 2 正式技能。");
            if (EditorApplication.isPlayingOrWillChangePlaymode) throw new InvalidOperationException("请退出 Play 后编辑正式技能。");
        }

        /// <summary>Removes a resource-array entry and shifts its explicit bindings together.
        /// Other references to the same local definition and external assets remain intact.</summary>
        public static void RemoveResourceEntry(GameplaySkillDocumentAsset document,string ownerId,string path,int index)
        {
            Transaction(document,"移除资源引用",graph=>{
                var node=graph.Nodes.Find(n=>n.Id==ownerId);var local=graph.LocalDefinitions.Find(d=>d.Id==ownerId);
                object payload=node?.Payload is GameplayAbilityNodePayload ability?ability.Settings:node?.Payload is GameplayActionNodePayload action?action.Action:local?.Payload;
                var bindings=node?.Payload.Definitions??local.Definitions;
                var slot=GameplayGraphFields.ResourceSlots(payload).Single(s=>s.Path==path+".Array.data["+index+"]");
                var array=(Array)slot.Field.GetValue(slot.Owner);var next=Array.CreateInstance(array.GetType().GetElementType(),array.Length-1);
                int at=0;for(int i=0;i<array.Length;i++)if(i!=index)next.SetValue(array.GetValue(i),at++);
                slot.Field.SetValue(slot.Owner,next);
                string prefix=path+".Array.data[";var released=new List<string>();
                foreach(var binding in bindings.ToArray())if(binding.PropertyPath.StartsWith(prefix,StringComparison.Ordinal)){
                    int end=binding.PropertyPath.IndexOf(']',prefix.Length);int old=int.Parse(binding.PropertyPath.Substring(prefix.Length,end-prefix.Length));
                    if(old==index){released.Add(binding.LocalId);bindings.Remove(binding);}
                    else if(old>index)binding.PropertyPath=prefix+(old-1)+binding.PropertyPath.Substring(end);
                }
                CollectOwned(graph,released.Where(id=>!string.IsNullOrEmpty(id)));
            });
        }

        public static void NotifyFields(GameplaySkillDocumentAsset document)
        { Publish(document,GameplaySkillAuthoringChange.Full()); }
        public static void NotifyFields(GameplaySkillDocumentAsset document,GameplaySkillAuthoringChange change)=>Publish(document,change);

        public static string CreateAction(GameplaySkillDocumentAsset document, GameplaySkillActionAuthoring action, string title,
            Vector2 position, GameplayLocalDefinitionPayload definition = null, string slot = null, string timelineId = null, float? seconds = null, CreationLink link=null, IEnumerable<string> generatedTags=null)
        {
            string id = GameplayAuthoringGraph.NewId();
            Transaction(document, "创建 " + title, graph =>
            {
                var payload = new GameplayActionNodePayload { Action = action };
                var node = new GameplayAuthoringNode { Id = id, Title = title, Payload = payload, Position = position, SourceDefinitionId = document.DefinitionId };
                graph.Nodes.Add(node);
                if (definition != null)
                {
                    var local = new GameplayLocalDefinition { Name = title, SourceDefinitionId = document.DefinitionId, Payload = definition };
                    graph.LocalDefinitions.Add(local); payload.Definitions.Add(new GameplayDefinitionBinding { PropertyPath = slot, LocalId = local.Id });
                }
                ConnectCreated(graph,link,id);
                if (timelineId != null)
                {
                    var timeline = graph.Timelines.Single(t => t.Id == timelineId);
                    timeline.Events.Add(new GameplayScheduledEntry { TimeSeconds = seconds ?? 0, EntryNodeId = id,
                        Order = timeline.Events.Count == 0 ? 0 : timeline.Events.Max(e => e.Order) + 1, Target = new GameplaySkillContextTargetAuthoring() });
                }
            },generatedTags:generatedTags);
            return id;
        }

        public static string CreateSequence(GameplaySkillDocumentAsset document, Vector2 position,CreationLink link=null)
        {
            string id = GameplayAuthoringGraph.NewId();
            Transaction(document, "创建时间编排", graph =>
            {
                var timeline = new GameplayAuthoringTimeline { Name = "时间编排", SourceDefinitionId = document.DefinitionId };
                graph.Timelines.Add(timeline);
                var payload = new GameplayActionNodePayload { Action = new GameplayOwnedSequenceActionAuthoring() };
                payload.Definitions.Add(new GameplayDefinitionBinding { PropertyPath = "Sequence", LocalId = timeline.Id });
                graph.Nodes.Add(new GameplayAuthoringNode { Id = id, Title = "时间编排", Payload = payload, Position = position, SourceDefinitionId = document.DefinitionId });
                ConnectCreated(graph,link,id);
            });
            return id;
        }

        /// <summary>Explicitly creates a private callback receiver. Only this macro assigns
        /// ownership; connecting a pre-existing draft never transfers its ownership. The overflow
        /// variant also enables ExecuteOverflow with its first executor in the same transaction.</summary>
        public static string CreateOwnedEffectCallback(GameplaySkillDocumentAsset document,string owner,string port,GameplayEffectDefinitionData values,Vector2 position)
        {
            string id=GameplayAuthoringGraph.NewId();
            Transaction(document,"创建随所有者删除的后续效果",graph=>{
                // The explicit overflow menu enables the policy and its first real executor
                // together; an ExecuteOverflow policy with an empty rule list is not valid.
                if(port==GameplayGraphEffectCallbacks.Overflow)((GameplayEffectDefinitionData)graph.LocalDefinitions.Single(d=>d.Id==owner).Payload).Stacking.Overflow=GameplayEffectStackOverflowPolicy.ExecuteOverflow;
                var local=new GameplayLocalDefinition{Name="后续效果",Payload=values,SourceDefinitionId=document.DefinitionId};
                var node=new GameplayAuthoringNode{Id=id,Title="后续效果",Position=position,CallbackOwnerDefinitionId=owner,Payload=new GameplayActionNodePayload{Action=new GameplayApplyToTargetsActionAuthoring()}};
                node.Payload.Definitions.Add(new GameplayDefinitionBinding{PropertyPath="Effect",LocalId=local.Id});
                graph.LocalDefinitions.Add(local);graph.Nodes.Add(node);ConnectInGraph(graph,owner,port,id,GameplayGraphEffectCallbacks.Input);
            });return id;
        }

        /// <summary>Deletes the executor and its flags explicitly. Ordinary disconnect keeps it
        /// as pending authoring configuration with the same stable callback identity.</summary>
        public static void RemoveEffectCallback(GameplaySkillDocumentAsset document,string owner,string port)=>Transaction(document,"删除生命周期回调及配置",graph=>{
            var local=graph.LocalDefinitions.Single(d=>d.Id==owner);var effect=(GameplayEffectDefinitionData)local.Payload;
            var removed=graph.Connections.Where(e=>e.FromNodeId==owner&&e.OutputPortKey==port).Select(e=>e.ToNodeId).ToArray();
            graph.Connections.RemoveAll(e=>e.FromNodeId==owner&&e.OutputPortKey==port);GameplayGraphEffectCallbacks.Remove(effect,port);
            if(GameplayGraphEffectCallbacks.IsActionPort(port))CollectEffectActionNodes(graph,owner,removed);
        });

        public static void ReorderEffectAction(GameplaySkillDocumentAsset document, string connectionId, int direction) =>
            Transaction(document, "调整生命周期动作顺序", graph => {
                var edge = graph.Connections.Single(e => e.Id == connectionId);
                var steps = graph.Connections.Where(e => e.FromNodeId == edge.FromNodeId && e.OutputPortKey == edge.OutputPortKey)
                    .OrderBy(e => e.Order).ToArray();
                int at = Array.IndexOf(steps, edge), next = Mathf.Clamp(at + direction, 0, steps.Length - 1);
                (edge.Order, steps[next].Order) = (steps[next].Order, edge.Order);
            });

        /// <summary>An explicit template inserts ordinary, editable graph entities as a draft.
        /// The author connects its sequence to a rule; no hidden scheduling is inferred.</summary>
        public static string CreateDamageTemplate(GameplaySkillDocumentAsset document, Vector2 position)
        {
            string id=GameplayAuthoringGraph.NewId();
            Transaction(document,"创建逐目标伤害模板",graph=>{
                var timeline=new GameplayAuthoringTimeline{Name="逐目标伤害编排",DurationSeconds=1};
                var effect=new GameplayLocalDefinition{Name="逐目标伤害",Payload=new GameplayEffectDefinitionData{
                    DurationPolicy=GameplayEffectDurationPolicy.Instant,Executions=new GameplayEffectExecutionAuthoring[]{new GameplayDamageExecutionAuthoring()}}};
                var sequence=new GameplayAuthoringNode{Id=id,Title="伤害编排",Position=position,Payload=new GameplayActionNodePayload{Action=new GameplayOwnedSequenceActionAuthoring()}};
                sequence.Payload.Definitions.Add(new GameplayDefinitionBinding{PropertyPath="Sequence",LocalId=timeline.Id});
                var search=new GameplayAuthoringNode{Title="搜索目标",Position=position+new Vector2(490,0),Payload=new GameplayActionNodePayload{Action=new GameplaySearchTargetActionAuthoring()}};
                var damage=new GameplayAuthoringNode{Title="造成伤害",Position=position+new Vector2(820,0),Payload=new GameplayActionNodePayload{Action=new GameplayApplyToTargetsActionAuthoring()}};
                damage.Payload.Definitions.Add(new GameplayDefinitionBinding{PropertyPath="Effect",LocalId=effect.Id});
                var end=new GameplayAuthoringNode{Title="结束施放",Position=position+new Vector2(490,330),Payload=new GameplayActionNodePayload{Action=new GameplayEndAbilityActionAuthoring()}};
                timeline.Events.Add(new GameplayScheduledEntry{EntryNodeId=search.Id,TimeSeconds=8f/30,Target=new GameplaySkillContextTargetAuthoring()});
                timeline.Events.Add(new GameplayScheduledEntry{EntryNodeId=end.Id,TimeSeconds=1,Order=1,Target=new GameplaySkillContextTargetAuthoring()});
                graph.Nodes.AddRange(new[]{sequence,search,damage,end});graph.Timelines.Add(timeline);graph.LocalDefinitions.Add(effect);
                graph.Connections.Add(new GameplayAuthoringConnection{FromNodeId=search.Id,OutputPortKey="EachTarget",ToNodeId=damage.Id});
            });return id;
        }

        /// <summary>One creation transaction can consume a dragged real execution/schedule port.
        /// No authoring data or Undo is published until the complete connection validates.</summary>
        public sealed class CreationLink {public string Owner,Port,Input="Execute";public bool Scheduled;}
        private static void ConnectCreated(GameplayAuthoringGraph graph,CreationLink link,string id)
        {
            if(link==null)return;
            if(link.Scheduled){graph.Timelines.SelectMany(t=>t.Events).Single(e=>e.Id==link.Owner).EntryNodeId=id;return;}
            var old=graph.Connections.Where(e=>e.FromNodeId==link.Owner&&e.OutputPortKey==link.Port).ToArray();
            var end=old.Length==1?graph.Nodes.Find(n=>n.Id==old[0].ToNodeId):null;
            bool insert=end?.Payload is GameplayActionNodePayload terminal&&terminal.Action is GameplayEndAbilityActionAuthoring;
            if(insert)graph.Connections.Remove(old[0]);
            ConnectInGraph(graph,link.Owner,link.Port,id,link.Input);
            if (link.Input == "Execute")
            {
                string effectOwner = graph.LocalDefinitions.Any(d => d.Id == link.Owner && d.Payload is GameplayEffectDefinitionData) &&
                    GameplayGraphEffectCallbacks.IsActionPort(link.Port) ? link.Owner : graph.Nodes.Find(n => n.Id == link.Owner)?.CallbackOwnerDefinitionId;
                if (effectOwner != null) graph.Nodes.Single(n => n.Id == id).CallbackOwnerDefinitionId = effectOwner;
            }
            if(insert){
                var node=graph.Nodes.Single(n=>n.Id==id);var timeline=graph.Timelines.FirstOrDefault(t=>node.Payload.Definitions.Any(b=>b.LocalId==t.Id));
                if(timeline!=null)timeline.Events.Add(new GameplayScheduledEntry{EntryNodeId=end.Id,TimeSeconds=timeline.DurationSeconds,Target=new GameplaySkillContextTargetAuthoring()});
                else if(!StartsSpatialLifetime(node)&&GameplayGraphFields.OutputPorts(node).Contains("Then"))ConnectInGraph(graph,id,"Then",end.Id);
            }
        }

        internal static bool StartsSpatialLifetime(GameplayAuthoringNode node)=>node.Payload is GameplayActionNodePayload p&&
            (p.Action is GameplaySpawnProjectileActionAuthoring||p.Action is GameplaySpawnAreaActionAuthoring||p.Action is GameplaySpawnSummonActionAuthoring);

        /// <summary>Author-selected ending for an actual spatial callback, or an explicitly
        /// world-owned local projectile or area. Replaces direct End associations from this start/its
        /// local resource in one Undo; other callback chains keep their authored behavior.
        /// Callers must review those retained paths or reused local consumers before replacement.
        /// No wait port, persisted policy mirror or shared SO write is introduced.</summary>
        public static void SetSpatialEnd(GameplaySkillDocumentAsset document,string nodeId,string endId,string port,bool worldOwned=false,bool reviewedRelatedPaths=false)
        {
            Transaction(document,"选择异步行为结束策略",graph=>{
                var node=graph.Nodes.Single(n=>n.Id==nodeId);
                if(!StartsSpatialLifetime(node)||!((graph.Nodes.Single(n=>n.Id==endId).Payload as GameplayActionNodePayload)?.Action is GameplayEndAbilityActionAuthoring))throw new ArgumentException("请选择持续行为及真实结束节点。");
                var binding=node.Payload.Definitions.Single(b=>b.PropertyPath=="Projectile"||b.PropertyPath=="Area"||b.PropertyPath=="Summon");
                if(binding.External)throw new InvalidOperationException("共享来源只读；先明确转为本地配置再选择结束策略。");
                var local=graph.LocalDefinitions.Single(d=>d.Id==binding.LocalId);
                if(worldOwned&&!(local.Payload is GameplayProjectileDefinitionData || local.Payload is GameplayAreaDefinitionData))
                    throw new InvalidOperationException("当前仅弹体和区域支持脱离施放的世界托管。");
                if(!worldOwned&&!GameplayGraphFields.ActionArrays(local.Payload).Any(s=>s.Path==port))throw new ArgumentException("此资源没有所选生命周期出口。");
                if(!reviewedRelatedPaths&&SpatialEndReview(graph,nodeId)!=null)throw new InvalidOperationException("此行为还有手工回调或复用关系；请在结束策略菜单审核将保留和替换的连接。");
                if(local.Payload is GameplayProjectileDefinitionData projectile)projectile.CancelOnAbilityEnd=!worldOwned;
                if(local.Payload is GameplayAreaDefinitionData area)area.CancelOnAbilityEnd=!worldOwned;
                var ends=graph.Nodes.Where(n=>(n.Payload as GameplayActionNodePayload)?.Action is GameplayEndAbilityActionAuthoring).Select(n=>n.Id).ToHashSet();
                graph.Connections.RemoveAll(e=>ends.Contains(e.ToNodeId)&&(e.FromNodeId==nodeId&&e.OutputPortKey=="Then"||e.FromNodeId==local.Id));
                ConnectInGraph(graph,worldOwned?nodeId:local.Id,worldOwned?"Then":port,endId);
            });
        }

        // The graph itself remains the policy source after reopen/Undo. A direct End can be
        // replaced without removing damage/VFX receivers. Handwritten chains may end elsewhere;
        // show them and their shared local consumers instead of guessing their ownership.
        internal static string SpatialEndReview(GameplayAuthoringGraph graph,string nodeId)
        {
            var node=graph.Nodes.Single(n=>n.Id==nodeId);
            string local=node.Payload.Definitions.Single(b=>b.PropertyPath=="Projectile"||b.PropertyPath=="Area"||b.PropertyPath=="Summon").LocalId;
            var ends=graph.Nodes.Where(n=>(n.Payload as GameplayActionNodePayload)?.Action is GameplayEndAbilityActionAuthoring).Select(n=>n.Id).ToHashSet();
            var outgoing=graph.Connections.Where(e=>e.FromNodeId==local||e.FromNodeId==nodeId&&e.OutputPortKey=="Then").ToArray();
            var replaced=outgoing.Where(e=>ends.Contains(e.ToNodeId)).ToArray();
            var retained=outgoing.Where(e=>!ends.Contains(e.ToNodeId)).ToArray();
            var consumers=graph.Nodes.Where(n=>n.Payload.Definitions.Any(b=>b.LocalId==local)).ToArray();
            if(retained.Length==0&&replaced.Select(e=>e.ToNodeId).Distinct().Count()<=1&&consumers.Length<=1)return null;
            string Describe(GameplayAuthoringConnection e)=>e.OutputPortKey+" → "+GameplaySkillNodeDisplay.Name(graph.Nodes.Single(n=>n.Id==e.ToNodeId),graph);
            return "替换直接结束关联：\n"+(replaced.Length==0?"（尚无）":string.Join("\n",replaced.Select(Describe)))+
                "\n\n完整保留手工回调及后继；其中的手工结束仍然生效：\n"+(retained.Length==0?"（无）":string.Join("\n",retained.Select(Describe)))+
                (consumers.Length>1?"\n\n此局部资源共用于以下动作，本次也影响这些调用者：\n"+string.Join("\n",consumers.Select(n=>GameplaySkillNodeDisplay.Name(n,graph))):"");
        }

        public static void Connect(GameplaySkillDocumentAsset document, string ownerId, string port, string targetId, bool scheduled = false,string inputPort="Execute")
        {
            Transaction(document, "连接正式行为", graph =>
            {
                if (scheduled)
                {
                    var entry = graph.Timelines.SelectMany(t => t.Events).Single(e => e.Id == ownerId);
                    if (!string.IsNullOrEmpty(entry.EntryNodeId)) throw new InvalidOperationException("时间事件已有入口；请先断开原连接。");
                    entry.EntryNodeId = targetId; return;
                }
                ConnectInGraph(graph,ownerId,port,targetId,inputPort);
            });
        }

        public static void Disconnect(GameplaySkillDocumentAsset document, string id, bool scheduled = false)
        {
            Transaction(document, "断开执行", graph =>
            {
                if (scheduled)
                {
                    // A schedule without an entry is not a fake runtime event; remove that invocation.
                    foreach (var timeline in graph.Timelines) timeline.Events.RemoveAll(e => e.Id == id);
                }
                else DisconnectInGraph(graph,id);
            });
        }

        /// <summary>Timeline region resizing edits the actual typed constant declared by its
        /// integration metadata. It does not invent an End event or a separate duration source.</summary>
        public static void ResizeConstantDuration(GameplaySkillDocumentAsset document,string localId,string valuePath,float seconds)
        {
            if(!float.IsFinite(seconds)||seconds<=0)throw new ArgumentOutOfRangeException(nameof(seconds));
            Transaction(document,"调整表现持续时间",graph=>{
                var local=graph.LocalDefinitions.Single(d=>d.Id==localId);
                var value=GameplayGraphFields.ObjectPaths(local.Payload).Single(p=>p.Value==valuePath).Key as GameplayConstantFloatValueAuthoring;
                if(value==null)throw new InvalidOperationException("持续配置已改变，请重新选择此事件。");value.Value=seconds;
            },false,GameplaySkillAuthoringChange.Local(new[]{localId}));
        }
        public static void MoveEvent(GameplaySkillDocumentAsset document, string eventId, float seconds)
        {
            Transaction(document, "移动时间事件", graph =>
            {
                var owner = graph.Timelines.Single(t => t.Events.Any(e => e.Id == eventId));
                owner.Events.Single(e => e.Id == eventId).TimeSeconds = Mathf.Clamp(seconds, 0, owner.DurationSeconds);
            },false,GameplaySkillAuthoringChange.Local(new[]{document.AuthoringGraph.Timelines.Single(t=>t.Events.Any(e=>e.Id==eventId)).Id}));
        }
        public static void Schedule(GameplaySkillDocumentAsset document, string timelineId, string nodeId, float seconds)
        {
            Transaction(document,"安排已有行为",graph=>{
                var timeline=graph.Timelines.Single(t=>t.Id==timelineId);
                timeline.Events.Add(new GameplayScheduledEntry{EntryNodeId=nodeId,TimeSeconds=seconds,
                    Order=timeline.Events.Count==0?0:timeline.Events.Max(e=>e.Order)+1,Target=new GameplaySkillContextTargetAuthoring()});
            });
        }

        public static void MoveNodes(GameplaySkillDocumentAsset document, IReadOnlyDictionary<string, Vector2> positions)
        {
            Editable(document); Undo.RegisterCompleteObjectUndo(document, "移动行为节点");
            foreach (var node in document.AuthoringGraph.Nodes) if (positions.TryGetValue(node.Id, out var value)) node.Position = value;
            EditorUtility.SetDirty(document); Publish(document,GameplaySkillAuthoringChange.Local(positions.Keys,true));
        }

        public static void Delete(GameplaySkillDocumentAsset document, IEnumerable<string> nodeIds)
        {
            var selected = new HashSet<string>(nodeIds);
            Transaction(document, "删除所选行为", graph => DeleteFromGraph(graph, selected));
        }
        internal static void DeleteFromGraph(GameplayAuthoringGraph graph, IEnumerable<string> nodeIds)
        {
                var selected = new HashSet<string>(nodeIds);
                if (selected.Contains(graph.AbilityNodeId))
                    throw new InvalidOperationException("主卡请明确选择『清空技能内容』或『删除整个技能文件』。");
                var released = graph.Nodes.Where(n => selected.Contains(n.Id)).SelectMany(n => n.Payload.Definitions)
                    .Select(b => b.LocalId).Where(id => !string.IsNullOrEmpty(id)).ToArray();
                var callbackChildren = graph.Nodes.Where(n => selected.Contains(n.Id) && n.CallbackOwnerDefinitionId != null)
                    .SelectMany(n => graph.Connections.Where(e => e.FromNodeId == n.Id).Select(e => (Owner: n.CallbackOwnerDefinitionId, Target: e.ToNodeId))).ToArray();
                foreach (var id in selected)
                {
                    foreach(var edge in graph.Connections.Where(e=>e.ToNodeId==id&&e.InputPortKey==GameplayGraphEffectCallbacks.Input).ToArray())DisconnectInGraph(graph,edge.Id);
                    graph.Nodes.RemoveAll(n => n.Id == id);
                    graph.Connections.RemoveAll(e => e.FromNodeId == id || e.ToNodeId == id);
                    foreach (var timeline in graph.Timelines) timeline.Events.RemoveAll(e => e.EntryNodeId == id);
                    graph.AdditionalRuleNodeIds.Remove(id);
                    if (graph.PrimaryRuleNodeId == id) graph.PrimaryRuleNodeId = null;
                }
                foreach (var group in callbackChildren.GroupBy(e => e.Owner))
                    CollectEffectActionNodes(graph, group.Key, group.Select(e => e.Target));
                CollectOwned(graph, released);
        }

        // Only nodes created by this Effect are eligible. A second caller or Timeline keeps
        // the reused branch, including its resources and descendants.
        private static void CollectEffectActionNodes(GameplayAuthoringGraph graph, string owner, IEnumerable<string> roots)
        {
            var branch = new HashSet<string>();
            var outgoing = graph.Connections.ToLookup(e => e.FromNodeId);
            var privateNodes = graph.Nodes.Where(n => n.CallbackOwnerDefinitionId == owner).Select(n => n.Id).ToHashSet();
            void Visit(string id)
            {
                if (!privateNodes.Contains(id) || !branch.Add(id)) return;
                foreach (var edge in outgoing[id]) Visit(edge.ToNodeId);
            }
            foreach (var root in roots) Visit(root);
            var shared = graph.Connections.Where(e => !branch.Contains(e.FromNodeId)).Select(e => e.ToNodeId)
                .Concat(graph.Timelines.SelectMany(t => t.Events).Select(e => e.EntryNodeId)).ToArray();
            void Retain(string id)
            {
                if (!branch.Remove(id)) return;
                foreach (var edge in outgoing[id]) Retain(edge.ToNodeId);
            }
            foreach (var id in shared) Retain(id);
            if (branch.Count != 0) DeleteFromGraph(graph, branch);
        }

        public static void ReorderEvent(GameplaySkillDocumentAsset document, string eventId, int direction)
        {
            Transaction(document, "调整同帧事件顺序", graph => {
                var timeline = graph.Timelines.Single(t => t.Events.Any(e => e.Id == eventId));
                var entry = timeline.Events.Single(e => e.Id == eventId);
                var peers = timeline.Events.Where(e => e.TimeSeconds == entry.TimeSeconds).OrderBy(e => e.Order).ToList();
                int at = peers.IndexOf(entry), next = Mathf.Clamp(at + direction, 0, peers.Count - 1);
                int order = peers[next].Order; peers[next].Order = entry.Order; entry.Order = order;
            },false);
        }

        /// <summary>Collects only definitions formerly owned by deleted calls. Unused migration
        /// drafts remain inspectable; shared external SOs are never removed by ordinary Delete.</summary>
        private static void CollectOwned(GameplayAuthoringGraph graph, IEnumerable<string> released)
        {
            var candidates = new HashSet<string>(released);
            bool changed;
            do
            {
                changed = false;
                var used = new HashSet<string>(graph.Nodes.SelectMany(n => n.Payload.Definitions).Concat(graph.LocalDefinitions.SelectMany(d => d.Definitions))
                    .Select(b => b.LocalId).Where(id => !string.IsNullOrEmpty(id)));
                foreach (var timeline in graph.Timelines.Where(t => candidates.Contains(t.Id) && !used.Contains(t.Id)).ToArray())
                {
                    var owned = new HashSet<string>();
                    Action<string> gather = null;
                    gather = id => { if (!owned.Add(id)) return; foreach (var e in graph.Connections.Where(e => e.FromNodeId == id)) gather(e.ToNodeId); };
                    foreach (var entry in timeline.Events) gather(entry.EntryNodeId);
                    var incomingElsewhere = new HashSet<string>(graph.Connections.Where(e => !owned.Contains(e.FromNodeId)).Select(e => e.ToNodeId)
                        .Concat(graph.Timelines.Where(t => t != timeline).SelectMany(t => t.Events).Select(e => e.EntryNodeId)));
                    var retained = new HashSet<string>();
                    Action<string> retain = null;
                    retain = id => { if (!owned.Contains(id) || !retained.Add(id)) return; foreach (var e in graph.Connections.Where(e => e.FromNodeId == id)) retain(e.ToNodeId); };
                    foreach (string id in incomingElsewhere) retain(id);
                    owned.ExceptWith(retained);
                    foreach (var binding in graph.Nodes.Where(n => owned.Contains(n.Id)).SelectMany(n => n.Payload.Definitions))
                        if (!string.IsNullOrEmpty(binding.LocalId)) candidates.Add(binding.LocalId);
                    graph.Nodes.RemoveAll(n => owned.Contains(n.Id));
                    graph.Connections.RemoveAll(e => owned.Contains(e.FromNodeId) || owned.Contains(e.ToNodeId));
                    graph.Timelines.Remove(timeline); changed = true;
                }
                foreach (var definition in graph.LocalDefinitions.Where(d => candidates.Contains(d.Id) && !used.Contains(d.Id)).ToArray())
                {
                    foreach(var edge in graph.Connections.Where(e=>e.FromNodeId==definition.Id&&e.InputPortKey==GameplayGraphEffectCallbacks.Input).ToArray()){
                        var receiver=graph.Nodes.Find(n=>n.Id==edge.ToNodeId);
                        if(receiver?.CallbackOwnerDefinitionId==definition.Id&&!graph.Connections.Any(e=>e.ToNodeId==edge.ToNodeId&&e.FromNodeId!=definition.Id)&&!graph.Timelines.SelectMany(t=>t.Events).Any(e=>e.EntryNodeId==edge.ToNodeId))DeleteFromGraph(graph,new[]{edge.ToNodeId});
                    }
                    var actionRoots = graph.Connections.Where(e => e.FromNodeId == definition.Id && GameplayGraphEffectCallbacks.IsActionPort(e.OutputPortKey))
                        .Select(e => e.ToNodeId).ToArray();
                    foreach (var binding in definition.Definitions) if (!string.IsNullOrEmpty(binding.LocalId)) candidates.Add(binding.LocalId);
                    graph.Connections.RemoveAll(e => e.FromNodeId == definition.Id);
                    graph.LocalDefinitions.Remove(definition); changed = true;
                    if (actionRoots.Length > 0) CollectEffectActionNodes(graph, definition.Id, actionRoots);
                    foreach(var receiver in graph.Nodes.Where(n=>n.CallbackOwnerDefinitionId==definition.Id))receiver.CallbackOwnerDefinitionId=null;
                }
            } while (changed);
        }

        public static void Clear(GameplaySkillDocumentAsset document)
        {
            Transaction(document, "清空技能内容", graph =>
            {
                graph.Nodes.Clear(); graph.Connections.Clear(); graph.Timelines.Clear(); graph.LocalDefinitions.Clear();
                var ability = new GameplayAuthoringNode { Title = "技能施放", Payload = new GameplayAbilityNodePayload { Settings = new GameplayAbilityDefinitionData() } };
                graph.Nodes.Add(ability); graph.AbilityNodeId = ability.Id; graph.PrimaryRuleNodeId = null; graph.AdditionalRuleNodeIds.Clear();
            });
        }

        public static void Copy(GameplaySkillDocumentAsset document, IEnumerable<string> nodeIds)
        {
            var selected = new HashSet<string>(nodeIds);
            if(selected.Contains(document.AuthoringGraph.AbilityNodeId)||document.AuthoringGraph.Nodes.Any(n=>selected.Contains(n.Id)&&n.Payload is GameplayAbilityNodePayload))throw new InvalidOperationException("主施放卡不能复制为第二个根；请选择行为或触发规则。");
            var graph = (GameplayAuthoringGraph)GameplayGraphFields.Clone(document.AuthoringGraph);
            var needed = new HashSet<string>(selected);
            var defs = new HashSet<string>(); var timelines = new HashSet<string>();
            bool changed;
            do
            {
                int count = needed.Count + defs.Count + timelines.Count;
                foreach (var node in graph.Nodes.Where(n => needed.Contains(n.Id)))
                    foreach (var binding in node.Payload.Definitions.Where(b => !string.IsNullOrEmpty(b.LocalId)))
                    { if (graph.Timelines.Any(t => t.Id == binding.LocalId)) timelines.Add(binding.LocalId); else defs.Add(binding.LocalId); }
                foreach (var definition in graph.LocalDefinitions.Where(d => defs.Contains(d.Id)))
                    foreach (var binding in definition.Definitions.Where(b => !string.IsNullOrEmpty(b.LocalId))) defs.Add(binding.LocalId);
                foreach (var timeline in graph.Timelines.Where(t => timelines.Contains(t.Id))) foreach (var entry in timeline.Events) needed.Add(entry.EntryNodeId);
                foreach (var edge in graph.Connections.Where(e => (needed.Contains(e.FromNodeId) || defs.Contains(e.FromNodeId)) && e.OutputPortKey != "Then")) needed.Add(edge.ToNodeId);
                foreach (var edge in graph.Connections.Where(e => needed.Contains(e.FromNodeId) && e.OutputPortKey == "Then" && !selected.Contains(e.FromNodeId))) needed.Add(edge.ToNodeId);
                changed = count != needed.Count + defs.Count + timelines.Count;
            } while (changed);
            graph.Nodes.RemoveAll(n => !needed.Contains(n.Id)); graph.LocalDefinitions.RemoveAll(d => !defs.Contains(d.Id));
            graph.Timelines.RemoveAll(t => !timelines.Contains(t.Id));
            graph.Connections.RemoveAll(e => !(needed.Contains(e.FromNodeId) || defs.Contains(e.FromNodeId)) || !needed.Contains(e.ToNodeId));
            graph.LegacyResources.Clear(); _clipboard = graph;
        }

        public static string[] Paste(GameplaySkillDocumentAsset document, Vector2 offset,bool activateRules=true)
        {
            if (_clipboard == null) return Array.Empty<string>();
            var copy = (GameplayAuthoringGraph)GameplayGraphFields.Clone(_clipboard);
            var map = new Dictionary<string, string>();
            foreach (string id in copy.Nodes.Select(n => n.Id).Concat(copy.LocalDefinitions.Select(d => d.Id)).Concat(copy.Timelines.Select(t => t.Id))
                .Concat(copy.Timelines.SelectMany(t => t.Events).Select(e => e.Id))) map[id] = GameplayAuthoringGraph.NewId();
            foreach (var node in copy.Nodes)
            {
                node.Id = map[node.Id]; node.Position += offset; node.SourceActionId = -1; node.SourceDefinitionId = document.DefinitionId;
                node.CallbackOwnerDefinitionId=!string.IsNullOrEmpty(node.CallbackOwnerDefinitionId)&&map.TryGetValue(node.CallbackOwnerDefinitionId,out var owner)?owner:null;
                foreach (var b in node.Payload.Definitions) if (!string.IsNullOrEmpty(b.LocalId)) b.LocalId = map[b.LocalId];
            }
            foreach (var d in copy.LocalDefinitions) { d.Id = map[d.Id]; d.SourceDefinitionId = document.DefinitionId; foreach (var b in d.Definitions) if (!string.IsNullOrEmpty(b.LocalId)) b.LocalId = map[b.LocalId]; }
            foreach (var timeline in copy.Timelines) { timeline.Id = map[timeline.Id]; foreach (var e in timeline.Events) { e.Id = map[e.Id]; e.EntryNodeId = map[e.EntryNodeId]; } }
            foreach (var edge in copy.Connections) { edge.Id = GameplayAuthoringGraph.NewId(); edge.FromNodeId = map[edge.FromNodeId]; edge.ToNodeId = map[edge.ToNodeId]; }
            Transaction(document, activateRules?"粘贴为正式规则 / 行为":"粘贴为未启用规则 / 未接入行为", graph => { graph.Nodes.AddRange(copy.Nodes); graph.LocalDefinitions.AddRange(copy.LocalDefinitions); graph.Timelines.AddRange(copy.Timelines); graph.Connections.AddRange(copy.Connections);
                if(activateRules)foreach(var rule in copy.Nodes.Where(n=>n.Payload is GameplayRuleNodePayload))ActivateRule(graph,rule.Id,true);
            });
            return copy.Nodes.Select(n => n.Id).ToArray();
        }
        public static void SetRuleActive(GameplaySkillDocumentAsset document,string nodeId,bool active)=>Transaction(document,active?"启用正式触发规则":"停用触发规则",graph=>ActivateRule(graph,nodeId,active));
        private static void ActivateRule(GameplayAuthoringGraph graph,string nodeId,bool active)
        {
            if(!(graph.Nodes.Single(n=>n.Id==nodeId).Payload is GameplayRuleNodePayload))throw new InvalidOperationException("请选择触发规则。");
            if(!active){if(graph.PrimaryRuleNodeId==nodeId)throw new InvalidOperationException("主施放入口不能停用；可清空技能内容。");graph.AdditionalRuleNodeIds.Remove(nodeId);return;}
            if(!graph.Connections.Any(e=>e.FromNodeId==nodeId&&e.OutputPortKey=="Actions"))throw new InvalidOperationException("请先连接真实行为，再启用此规则。");
            if(graph.PrimaryRuleNodeId==nodeId||graph.AdditionalRuleNodeIds.Contains(nodeId))return;
            if(string.IsNullOrEmpty(graph.PrimaryRuleNodeId))graph.PrimaryRuleNodeId=nodeId;else graph.AdditionalRuleNodeIds.Add(nodeId);
        }
        public static int LocalUseCount(GameplayAuthoringGraph graph,string id)=>graph.Nodes.SelectMany(n=>n.Payload.Definitions).Concat(graph.LocalDefinitions.SelectMany(d=>d.Definitions)).Count(b=>b.LocalId==id);
        public static void DeleteLocalDefinition(GameplaySkillDocumentAsset document,string id)=>Transaction(document,"删除未使用局部配置",graph=>{
            int count=LocalUseCount(graph,id);if(count>0)throw new InvalidOperationException("此配置仍有 "+count+" 处引用；先解除引用。");
            CollectOwned(graph,new HashSet<string>{id});
        });
        internal static void CollectReleased(GameplayAuthoringGraph graph,IEnumerable<string> ids)=>CollectOwned(graph,new HashSet<string>(ids.Where(id=>!string.IsNullOrEmpty(id))));
        internal static void ConnectInGraph(GameplayAuthoringGraph graph,string owner,string port,string target,string input="Execute")
        {
            if (input == "Execute" && (port == GameplayGraphEffectCallbacks.AddCompletionActions || port == GameplayGraphEffectCallbacks.AddOverflowActions))
            {
                var effect = (GameplayEffectDefinitionData)graph.LocalDefinitions.Single(d => d.Id == owner).Payload;
                string reason = GameplayGraphPortPolicy.Unavailable(effect, port);
                if (reason != null) throw new InvalidOperationException(reason);
                if (port == GameplayGraphEffectCallbacks.AddOverflowActions) effect.Stacking.Overflow = GameplayEffectStackOverflowPolicy.ExecuteOverflow;
                port = GameplayGraphEffectCallbacks.AddActions(effect, port);
            }
            if(input==GameplayGraphEffectCallbacks.Input){
                var source=graph.LocalDefinitions.Single(d=>d.Id==owner);var effect=source.Payload as GameplayEffectDefinitionData;
                if(effect==null||GameplayGraphEffectCallbacks.EffectBinding(graph.Nodes.Single(n=>n.Id==target))==null)throw new InvalidOperationException("生命周期端口需要真实 Effect 定义输入。");
                string blocked=GameplayGraphPortPolicy.Unavailable(effect,port);if(blocked!=null)throw new InvalidOperationException(blocked);
                if(port==GameplayGraphEffectCallbacks.Completion||port==GameplayGraphEffectCallbacks.Overflow)port=GameplayGraphEffectCallbacks.Add(effect,port==GameplayGraphEffectCallbacks.Completion);
                else if(graph.Connections.Any(e=>e.FromNodeId==owner&&e.OutputPortKey==port))throw new InvalidOperationException("回调已有连接；请先断开。");
            }
            var peers=graph.Connections.Where(e=>e.FromNodeId==owner&&e.OutputPortKey==port).ToArray();if(peers.Any(e=>e.ToNodeId==target))throw new InvalidOperationException("执行关系已存在。");
            graph.Connections.Add(new GameplayAuthoringConnection{FromNodeId=owner,OutputPortKey=port,ToNodeId=target,InputPortKey=input,Order=peers.Length==0?0:peers.Max(e=>e.Order)+1});
        }
        internal static void DisconnectInGraph(GameplayAuthoringGraph graph,string id)
        {
            var edge=graph.Connections.SingleOrDefault(e=>e.Id==id);if(edge==null)return;
            graph.Connections.Remove(edge);
        }
    }
}
