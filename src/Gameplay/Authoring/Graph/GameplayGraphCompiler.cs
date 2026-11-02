using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Authoring.Graph
{
    /// <summary>Deterministically lowers the persisted behavior graph into the existing immutable
    /// runtime definitions. It creates no SOs, runtime graph interpreter, clock or mutable owner.</summary>
    public sealed class GameplayGraphCompiler
    {
        private readonly GameplaySkillDocumentAsset _document;
        private readonly GameplayAuthoringGraph _graph;
        private readonly GameplayAuthoringBuildContext _context;
        private readonly Dictionary<string, GameplayAuthoringNode> _nodes;
        private readonly Dictionary<string, GameplayLocalDefinition> _definitions;
        private readonly Dictionary<string, GameplayAuthoringTimeline> _timelines;
        private readonly Dictionary<string, List<GameplayAuthoringConnection>> _outputs;
        private readonly Dictionary<string, object> _built = new Dictionary<string, object>();
        private readonly HashSet<string> _active = new HashSet<string>();
        private readonly Dictionary<string,GameplayGraphContextPolicy.Requirement> _conditionalInputs;
        private readonly Stack<BindingScope> _scopes = new Stack<BindingScope>();

        private sealed class BindingScope
        {
            internal string Id;
            internal List<GameplayDefinitionBinding> Bindings;
            internal Dictionary<object, string> Paths;
            internal object Payload;
        }

        private GameplayGraphCompiler(GameplaySkillDocumentAsset document, GameplayAuthoringGraph graph, GameplayAuthoringBuildContext context)
        {
            _document = document; _graph = graph; _context = context;
            _conditionalInputs=GameplayGraphContextPolicy.ConditionalInputs(graph);
            _nodes = graph.Nodes.ToDictionary(n => n.Id, StringComparer.Ordinal);
            _definitions = graph.LocalDefinitions.ToDictionary(n => n.Id, StringComparer.Ordinal);
            _timelines = graph.Timelines.ToDictionary(n => n.Id, StringComparer.Ordinal);
            _outputs = graph.Connections.GroupBy(e => Key(e.FromNodeId, e.OutputPortKey))
                .ToDictionary(g => g.Key, g => g.OrderBy(e => e.Order).ToList(), StringComparer.Ordinal);
        }

        private static string Key(string owner, string port) => owner + "/" + port;
        private IEnumerable<GameplayAuthoringConnection> Edges(string id, string port) =>
            _outputs.TryGetValue(Key(id, port), out var edges) ? edges : Enumerable.Empty<GameplayAuthoringConnection>();

        /// <summary>Checks persisted IDs and typed consumers before any factory or runtime install.</summary>
        public static void ValidateStructure(GameplayAuthoringGraph graph, GameplayAuthoringValidationContext v, string path, bool requireRoots = true)
        {
            if (graph == null) { v.Error(path, "缺少 V2 行为图。"); return; }
            if (graph.Version != GameplayAuthoringGraph.CurrentVersion) v.Error(path, "不支持的行为图版本。");
            if (graph.Nodes == null || graph.Connections == null || graph.Timelines == null || graph.LocalDefinitions == null ||
                graph.AdditionalRuleNodeIds == null || graph.LegacyResources == null) { v.Error(path, "V2 集合不能为 null。"); return; }
            var ids = new HashSet<string>(StringComparer.Ordinal);
            bool Identity(string id, string location)
            {
                if (string.IsNullOrWhiteSpace(id) || !ids.Add(id)) { v.Error(location, "实体 ID 缺失或重复。"); return false; }
                return true;
            }
            var nodes = new Dictionary<string, GameplayAuthoringNode>(StringComparer.Ordinal);
            var definitions = new Dictionary<string, GameplayLocalDefinition>(StringComparer.Ordinal);
            foreach (var node in graph.Nodes)
            {
                if (node == null) { v.Error(path, "缺少节点。"); continue; }
                if (Identity(node.Id, path + ".Nodes")) nodes[node.Id] = node;
                if (!(node.Payload is GameplayActionNodePayload) && !(node.Payload is GameplayRuleNodePayload) && !(node.Payload is GameplayAbilityNodePayload))
                    v.Error(path + "/" + node.Id, "未知或丢失的节点 Payload。");
                if (node.Payload?.Definitions == null) v.Error(path + "/" + node.Id, "缺少定义引用集合。");
            }
            foreach (var definition in graph.LocalDefinitions)
            {
                if (definition == null) { v.Error(path, "缺少局部定义。"); continue; }
                if (Identity(definition.Id, path + ".LocalDefinitions")) definitions[definition.Id] = definition;
                if (definition.Payload == null || definition.Definitions == null) v.Error(path + "/" + definition.Id, "局部定义 Payload 或引用丢失。");
            }
            foreach (var timeline in graph.Timelines)
            {
                if (timeline == null) { v.Error(path, "缺少时间编排。"); continue; }
                Identity(timeline.Id, path + ".Timelines");
                v.Finite(timeline.DurationSeconds, path + "/" + timeline.Id + ".DurationSeconds");
                if (timeline.DurationSeconds < 0 || timeline.FramesPerSecond < 1 || timeline.Events == null)
                { v.Error(path + "/" + timeline.Id, "时间编排的时长、FPS 或事件集合无效。"); continue; }
                var orders = new HashSet<int>();
                foreach (var entry in timeline.Events)
                {
                    if (entry == null) { v.Error(path, "缺少时间事件。"); continue; }
                    Identity(entry.Id, path + ".Events");
                    v.Finite(entry.TimeSeconds, path + "/" + entry.Id + ".TimeSeconds");
                    if (entry.TimeSeconds < 0 || entry.TimeSeconds > timeline.DurationSeconds || entry.Order < 0 || !orders.Add(entry.Order))
                        v.Error(path + "/" + entry.Id, "事件时点越界或顺序重复。");
                    if (!nodes.TryGetValue(entry.EntryNodeId ?? "", out var target) || !(target.Payload is GameplayActionNodePayload))
                        v.Error(path + "/" + entry.Id, "时间事件必须指向一个真实行为节点。");
                }
            }
            if (requireRoots && (!nodes.TryGetValue(graph.AbilityNodeId ?? "", out var ability) || !(ability.Payload is GameplayAbilityNodePayload)))
                v.Error(path, "尚未配置技能施放。");
            if (requireRoots && (!nodes.TryGetValue(graph.PrimaryRuleNodeId ?? "", out var rule) || !(rule.Payload is GameplayRuleNodePayload)))
                v.Error(path, "尚未配置正式触发规则。");
            var ruleIds = new HashSet<string>(StringComparer.Ordinal) { graph.PrimaryRuleNodeId ?? "" };
            foreach (string id in graph.AdditionalRuleNodeIds)
                if (!nodes.TryGetValue(id ?? "", out var extra) || !(extra.Payload is GameplayRuleNodePayload) || !ruleIds.Add(id))
                    v.Error(path, "附加规则缺失、类型错误或重复。");
            foreach (var edge in graph.Connections)
            {
                if (edge == null) { v.Error(path, "缺少连线。"); continue; }
                Identity(edge.Id, path + ".Connections");
                string location = path + "/" + edge.Id;
                string blocked=GameplayGraphPortPolicy.Unavailable(graph,edge.FromNodeId,edge.OutputPortKey);if(blocked!=null)v.Error(location,blocked);
                string[] ports;
                if (nodes.TryGetValue(edge.FromNodeId ?? "", out var from)) ports = GameplayGraphFields.OutputPorts(from);
                else if (definitions.TryGetValue(edge.FromNodeId ?? "", out var source) && source.Payload != null)
                    ports = GameplayGraphFields.ActionArrays(source.Payload).Select(s => s.Path).Concat(GameplayGraphEffectCallbacks.Slots(source.Payload).Select(s=>s.Key)).ToArray();
                else { v.Error(location, "连线来源不存在。"); continue; }
                bool typed=definitions.TryGetValue(edge.FromNodeId??"",out var effectSource)&&GameplayGraphEffectCallbacks.Slots(effectSource.Payload).Any(s=>s.Key==edge.OutputPortKey);
                if (!ports.Contains(edge.OutputPortKey) || edge.InputPortKey != (typed?GameplayGraphEffectCallbacks.Input:"Execute") || edge.Order < 0 ||
                    !nodes.TryGetValue(edge.ToNodeId ?? "", out var to) || !(to.Payload is GameplayActionNodePayload))
                    v.Error(location, "连线不对应合法的唯一 Runtime 消费者。");
                else if(typed&&(GameplayGraphEffectCallbacks.EffectBinding(to)==null||graph.Connections.Count(e=>e.FromNodeId==edge.FromNodeId&&e.OutputPortKey==edge.OutputPortKey)!=1))v.Error(location,"Effect 回调只能连接一个真实 Effect 定义输入。");
            }
            foreach (var group in graph.Connections.Where(e => e != null).GroupBy(e => Key(e.FromNodeId, e.OutputPortKey)))
                if (group.Select(e => e.Order).Distinct().Count() != group.Count()) v.Error(path, "同一动作数组的连接顺序重复。");
            var visiting = new HashSet<string>(); var done = new HashSet<string>();
            Action<string> visit = null;
            visit = id =>
            {
                if (done.Contains(id)) return;
                if (!visiting.Add(id)) { v.Error(path + "/" + id, "执行连线存在循环。"); return; }
                foreach (var edge in graph.Connections.Where(e => e != null && e.FromNodeId == id)) visit(edge.ToNodeId);
                var node=graph.Nodes.Find(n=>n.Id==id);var definition=graph.LocalDefinitions.Find(d=>d.Id==id);var timeline=graph.Timelines.Find(t=>t.Id==id);
                foreach(var binding in node?.Payload?.Definitions??definition?.Definitions??new List<GameplayDefinitionBinding>())if(!string.IsNullOrEmpty(binding.LocalId))visit(binding.LocalId);
                if(timeline!=null&&timeline.Events!=null)foreach(var entry in timeline.Events.Where(e=>e!=null))visit(entry.EntryNodeId??"");
                visiting.Remove(id); done.Add(id);
            };
            foreach (string id in nodes.Keys) visit(id);
            if(!v.HasErrors)GameplayGraphContextPolicy.Validate(graph,v,path);
            foreach(var local in graph.LocalDefinitions)foreach(var slot in GameplayGraphEffectCallbacks.Slots(local.Payload))
                if(!graph.Connections.Any(e=>e.FromNodeId==local.Id&&e.OutputPortKey==slot.Key)&&!local.Definitions.Any(b=>b.PropertyPath==slot.Path))
                    v.Warning(path+"/"+local.Id+"."+slot.Path,"生命周期回调待重连：保留参数，不参与运行；无活动溢出执行器时拒绝溢出。");
        }

        public static GameplaySkillDocumentDefinition Build(GameplaySkillDocumentAsset document, GameplayAuthoringGraph graph, GameplayAuthoringBuildContext context)
        {
            var validation = new GameplayAuthoringValidationContext();
            ValidateStructure(graph, validation, document.name);
            Throw(validation);
            return context.Build(document, () => new GameplayGraphCompiler(document, graph, context).Compile());
        }
        /// <summary>Builds an explicitly requested local library entry for existing hosts such
        /// as the Lab's restore-charge/reduce-CD buttons. It uses this graph's bindings and
        /// provenance, rather than retaining an independently writable legacy continuation SO.</summary>
        public static T BuildLocalResource<T>(GameplaySkillDocumentAsset document,string localId,GameplayAuthoringBuildContext context) where T:class
        {
            var validation=new GameplayAuthoringValidationContext();ValidateStructure(document.AuthoringGraph,validation,document.name,false);Throw(validation);
            var compiler=new GameplayGraphCompiler(document,document.AuthoringGraph,context);
            var previous=context.GraphDefinitionResolver;var previousExists=context.GraphBindingExists;
            context.GraphDefinitionResolver=compiler.Resolve;context.GraphBindingExists=compiler.HasBinding;
            try{return compiler.BuildResource(localId) as T??throw new InvalidOperationException("局部资源类型与宿主不匹配："+localId);}
            finally{context.GraphDefinitionResolver=previous;context.GraphBindingExists=previousExists;}
        }

        /// <summary>Checks the same actual arrays used by compilation before an Editor mutation
        /// commits. Incomplete draft values do not disable structural authoring.</summary>
        public static void ValidateActionOrder(GameplayAuthoringGraph graph)
        {
            var compiler = new GameplayGraphCompiler(null, graph, new GameplayAuthoringBuildContext());
            foreach (var timeline in graph.Timelines)
            {
                var reachable=GameplayGraphContextPolicy.Reachable(graph);
                bool owned=graph.Nodes.Any(n=>reachable.Contains(n.Id)&&n.Payload.Definitions.Any(b=>b.LocalId==timeline.Id)&&(n.Payload as GameplayActionNodePayload)?.Action is GameplayOwnedSequenceActionAuthoring);
                if (owned) foreach (var entry in timeline.Events.Where(e => e.TimeSeconds > 0))
                    compiler.ValidateGuardedArray(new[] { entry.EntryNodeId }, new HashSet<string>());
            }
        }

        private GameplaySkillDocumentDefinition Compile()
        {
            var previousResolver = _context.GraphDefinitionResolver;
            var previousExists = _context.GraphBindingExists;
            _context.GraphDefinitionResolver = Resolve;
            _context.GraphBindingExists = HasBinding;
            try
            {
                // Match the existing document's build/source ordering, including additional rules.
                var additional = _graph.AdditionalRuleNodeIds.Select(BuildRule).ToArray();
                var abilityNode = _nodes[_graph.AbilityNodeId];
                var ability = (GameplayAbilityNodePayload)abilityNode.Payload;
                GameplayAbilityDefinition builtAbility;
                if (ability.Settings is GameplayAbilityDefinitionData)
                    builtAbility = (GameplayAbilityDefinition)BuildPayload(abilityNode.Id, ability.Settings, ability.Definitions);
                else if (ability.Settings == null && ability.Definitions.Count == 1 && ability.Definitions[0].PropertyPath == "Settings" &&
                    ability.Definitions[0].External is GameplayAbilityDefinitionAsset)
                    builtAbility = (GameplayAbilityDefinition)Bound(ability.Definitions[0]);
                else throw new InvalidOperationException("技能施放必须使用内嵌 Ability 值或明确共享的释放配置。");
                var rule = BuildRule(_graph.PrimaryRuleNodeId);
                if ((_document.Mode == GameplayStandardActivationMode.Active || _document.RunRulesOnPassiveActivation) &&
                    (!(((GameplayRuleNodePayload)_nodes[_graph.PrimaryRuleNodeId].Payload).Trigger is GameplaySkillEventTriggerAuthoring trigger) ||
                     trigger.EventTag.Name != _document.CastEvent.Name || ((GameplayRuleNodePayload)_nodes[_graph.PrimaryRuleNodeId].Payload).Once))
                    throw new InvalidOperationException("主动入口必须消费正式 CastEvent 且 Once=false。");
                var values = _document.Defaults.Select(d => new KeyValuePair<GameplayTag, float>(d.Key.Build(), d.Value));
                return new GameplaySkillDocumentDefinition(_document.SkillId, builtAbility, rule,
                    _document.Mode == GameplayStandardActivationMode.Active || _document.RunRulesOnPassiveActivation ? _document.CastEvent.Build() : default,
                    _document.Mode, _document.Subject, new GameplayContextValues(values), additional,
                    Array.ConvertAll(_document.LearningRequirements, r => r.Build()), _context.GetActionSourcesSnapshot(), _document.DefinitionId,
                    _document.RunRulesOnPassiveActivation);
            }
            finally { _context.GraphDefinitionResolver = previousResolver; _context.GraphBindingExists = previousExists; }
        }

        private GameplaySkillDefinition BuildRule(string id)
        {
            if (_built.TryGetValue(id, out var cached)) return (GameplaySkillDefinition)cached;
            var payload = (GameplayRuleNodePayload)_nodes[id].Payload;
            var v = new GameplayAuthoringValidationContext();
            if (payload.Trigger == null || payload.Target == null || payload.Conditions == null) throw new InvalidOperationException("规则缺少触发、目标或条件。");
            payload.Trigger.Validate(v, id + ".Trigger"); payload.Target.Validate(v, id + ".Target");
            foreach (var condition in payload.Conditions) { if (condition == null) v.Error(id, "缺少规则条件。"); else condition.Validate(v, id + ".Conditions"); }
            Throw(v);
            var actions = BuildOutput(id, "Actions", false);
            if (actions.Length == 0) throw new InvalidOperationException("规则至少需要一个正式行为。");
            var result = new GameplaySkillDefinition(payload.Trigger.Build(), payload.Conditions.Select(c => c.Build()), payload.Target.Build(), actions, payload.Once, payload.PreserveTriggerContext);
            _built.Add(id, result); return result;
        }

        private object BuildResource(string id)
        {
            if (_built.TryGetValue(id, out var cached)) return cached;
            if (!_active.Add(id)) throw new InvalidOperationException("定义引用存在循环：" + id);
            try
            {
                object result;
                if (_timelines.TryGetValue(id, out var timeline)) result = BuildTimeline(timeline);
                else if (_definitions.TryGetValue(id, out var definition)) result = BuildPayload(id, definition.Payload, definition.Definitions);
                else throw new InvalidOperationException("局部定义不存在：" + id);
                _built.Add(id, result); return result;
            }
            finally { _active.Remove(id); }
        }

        private GameplaySequenceDefinition BuildTimeline(GameplayAuthoringTimeline timeline)
        {
            if(timeline.Events.Count==0)throw new InvalidOperationException("已执行的时间编排至少需要一个真实事件；请添加行为或结束施放。");
            var reachable=GameplayGraphContextPolicy.Reachable(_graph);
            bool owned=_graph.Nodes.Any(n=>reachable.Contains(n.Id)&&n.Payload.Definitions.Any(b=>b.LocalId==timeline.Id)&&(n.Payload as GameplayActionNodePayload)?.Action is GameplayOwnedSequenceActionAuthoring);
            var markers = new List<GameplaySequenceMarkerDefinition>();
            foreach (var entry in timeline.Events.OrderBy(e => e.Order))
            {
                if (entry.Target == null || entry.Conditions == null) throw new InvalidOperationException("时间事件缺少目标或条件：" + entry.Id);
                var v = new GameplayAuthoringValidationContext(); entry.Target.Validate(v, entry.Id + ".Target");
                foreach (var condition in entry.Conditions) { if (condition == null) v.Error(entry.Id, "缺少事件条件。"); else condition.Validate(v, entry.Id + ".Conditions"); }
                Throw(v);
                var actions = BuildChain(entry.EntryNodeId, owned && entry.TimeSeconds > 0);
                markers.Add(new GameplaySequenceSkillStepMarkerDefinition(entry.TimeSeconds, entry.Conditions.Select(c => c.Build()), entry.Target.Build(), actions));
            }
            return new GameplaySequenceDefinition(timeline.DurationSeconds, markers);
        }

        private IGameplaySkillAction[] BuildOutput(string owner, string port, bool guarded)
        {
            var entries = Edges(owner, port).Select(e => e.ToNodeId).ToArray();
            if (guarded) ValidateGuardedArray(entries, new HashSet<string>());
            return entries.SelectMany(id => BuildChain(id, guarded)).ToArray();
        }

        // Validate the whole real array, including separate ordered roots and cached/shared
        // nodes. Definition memoization must never hide a later guarded use of the same node.
        private void ValidateGuardedArray(IEnumerable<string> entries, HashSet<string> checkedNodes)
        {
            var ids = new List<string>();
            Action<string> append = null;
            append = id => { ids.Add(id); foreach (var edge in Edges(id, "Then")) append(edge.ToNodeId); };
            foreach (string id in entries) append(id);
            for (int i = 0; i < ids.Count - 1; i++)
                if (((GameplayActionNodePayload)_nodes[ids[i]].Payload).Action is GameplayEndAbilityActionAuthoring)
                    throw new InvalidOperationException("已受 OwnedSequence 生命周期守卫的同一动作数组，不能在 End 后追加执行。");
            foreach (string id in ids)
                if (checkedNodes.Add(id))
                    foreach (var port in GameplayGraphFields.ActionArrays(((GameplayActionNodePayload)_nodes[id].Payload).Action))
                        ValidateGuardedArray(Edges(id, port.Path).Select(e => e.ToNodeId), checkedNodes);
        }

        private IGameplaySkillAction[] BuildChain(string id, bool guarded)
        {
            if (guarded) ValidateGuardedArray(new[] { id }, new HashSet<string>());
            var ids = new List<string>();
            Action<string> collect = null;
            collect = nodeId => { ids.Add(nodeId); foreach (var edge in Edges(nodeId, "Then")) collect(edge.ToNodeId); };
            collect(id);
            if (guarded)
                for (int i = 0; i < ids.Count - 1; i++)
                    if (((GameplayActionNodePayload)_nodes[ids[i]].Payload).Action is GameplayEndAbilityActionAuthoring)
                        throw new InvalidOperationException("已受 OwnedSequence 生命周期守卫的同一动作数组，不能在 End 后追加执行。");
            return ids.Select(nodeId => BuildAction(nodeId, guarded)).ToArray();
        }

        private IGameplaySkillAction BuildAction(string id, bool guarded)
        {
            if (_built.TryGetValue(id, out var cached)) return (IGameplaySkillAction)cached;
            if (!_active.Add(id)) throw new InvalidOperationException("行为或回调循环：" + id);
            try
            {
                var record = _nodes[id]; var payload = (GameplayActionNodePayload)record.Payload;
                if (payload.Action == null) throw new InvalidOperationException("丢失行为类型：" + id);
                var action = (GameplaySkillActionAuthoring)GameplayGraphFields.Clone(payload.Action);
                RejectLegacyRelationships(action, id);
                foreach (var slot in GameplayGraphFields.ActionArrays(action))
                    slot.Value = BuildOutput(id, slot.Path, guarded).Select(a => (GameplaySkillActionAuthoring)new CompiledAction(a)).ToArray();
                Push(id, action, payload.Definitions);
                try
                {
                    var v = Validation(id); action.Validate(v, id); action.ValidateReferences(v, id); Throw(v);
                    var result = action.Build(_context);
                    if(_conditionalInputs.TryGetValue(id,out var required))result=new GameplayGraphInputGuard(result,required,
                        new GameplaySkillTraceSource(string.IsNullOrEmpty(record.SourceDefinitionId)?_document.DefinitionId:record.SourceDefinitionId,
                            record.SourceActionId>=0?record.SourceActionId:StableActionId(record.Id),_document.DefinitionId));
                    _context.RegisterGraphActionSource(result, string.IsNullOrEmpty(record.SourceDefinitionId) ? _document.DefinitionId : record.SourceDefinitionId,
                        record.SourceActionId >= 0 ? record.SourceActionId : StableActionId(record.Id));
                    _built.Add(id, result); return result;
                }
                finally { _scopes.Pop(); }
            }
            finally { _active.Remove(id); }
        }

        private object BuildPayload(string id, GameplayLocalDefinitionPayload source, List<GameplayDefinitionBinding> bindings)
        {
            if (source == null) throw new InvalidOperationException("丢失局部定义 Payload：" + id);
            var payload = (GameplayLocalDefinitionPayload)GameplayGraphFields.Clone(source);
            RejectLegacyRelationships(payload, id);
            if(payload is GameplayEffectDefinitionData effect)bindings=ActiveCallbacks(id,effect,bindings);
            foreach (var slot in GameplayGraphFields.ActionArrays(payload))
                slot.Value = BuildOutput(id, slot.Path, false).Select(a => (GameplaySkillActionAuthoring)new CompiledAction(a)).ToArray();
            Push(id, payload, bindings);
            try { var v = Validation(id); payload.Validate(v, id); Throw(v); return payload.BuildDefinition(_context); }
            finally { _scopes.Pop(); }
        }

        // A disconnected executor remains authoring data with its stable identity and flags.
        // Only connected callbacks are lowered. Never rewrite the author's overflow policy.
        private List<GameplayDefinitionBinding> ActiveCallbacks(string id,GameplayEffectDefinitionData effect,List<GameplayDefinitionBinding> bindings)
        {
            var before=GameplayGraphEffectCallbacks.Slots(effect).ToArray();
            var connected=new HashSet<string>(before.Where(s=>Edges(id,s.Key).Any()||bindings.Any(b=>b.PropertyPath==s.Path)).Select(s=>s.Key));
            effect.CompletionExecutions=effect.CompletionExecutions.Where(e=>!(e is GameplayApplyEffectOnCompletionAuthoring)||connected.Contains("Completion/"+e.AuthoringId)).ToArray();
            if(effect.Stacking!=null){
                bool hadExecutors=effect.Stacking.OverflowExecutions.Length>0;
                effect.Stacking.OverflowExecutions=effect.Stacking.OverflowExecutions.Where(e=>!(e is GameplayApplyEffectOnOverflowAuthoring)||connected.Contains("Overflow/"+e.AuthoringId)).ToArray();
                if(hadExecutors&&effect.Stacking.Overflow==GameplayEffectStackOverflowPolicy.ExecuteOverflow&&effect.Stacking.OverflowExecutions.Length==0)effect.Stacking.Overflow=GameplayEffectStackOverflowPolicy.Reject;
            }
            var after=GameplayGraphEffectCallbacks.Slots(effect).ToDictionary(s=>s.Key);
            return bindings.Select(b=>{var old=before.FirstOrDefault(s=>s.Path==b.PropertyPath);return old!=null&&after.TryGetValue(old.Key,out var slot)?new GameplayDefinitionBinding{PropertyPath=slot.Path,LocalId=b.LocalId,External=b.External}:b;}).ToList();
        }

        private static void RejectLegacyRelationships(object payload, string id)
        {
            if (GameplayGraphFields.ResourceSlots(payload).Any(s => s.Value != null))
                throw new InvalidOperationException("行为 Payload 不允许保留历史 SO 执行引用：" + id);
            if (GameplayGraphFields.ActionArrays(payload).Any(s => ((GameplaySkillActionAuthoring[])s.Value)?.Length > 0))
                throw new InvalidOperationException("动作数组必须由显式连接拥有：" + id);
        }

        private void Push(string id, object payload, List<GameplayDefinitionBinding> bindings)
        {
            if (bindings == null || bindings.Any(b => b == null || string.IsNullOrEmpty(b.PropertyPath) ||
                (!string.IsNullOrEmpty(b.LocalId) == (b.External != null))) ||
                bindings.GroupBy(b => b.PropertyPath).Any(g => g.Count() > 1))
                throw new InvalidOperationException("局部/共享定义引用必须唯一且明确：" + id);
            _scopes.Push(new BindingScope { Id = id, Bindings = bindings, Paths = GameplayGraphFields.ObjectPaths(payload),Payload=payload });
        }

        private GameplayAuthoringValidationContext Validation(string id)
        {
            var v = new GameplayAuthoringValidationContext();
            v.GraphAssetValidation = (asset, path) =>
            {
                if (_scopes.Count == 0) return false;
                var scope = _scopes.Peek();
                string relative = GameplayGraphFields.NormalizePath(path.StartsWith(id + ".", StringComparison.Ordinal) ? path.Substring(id.Length + 1) : path);
                var matches = scope.Bindings.Where(b => b.PropertyPath == relative || b.PropertyPath.StartsWith(relative + ".Array.data[", StringComparison.Ordinal)).ToArray();
                if (matches.Length == 0){
                    if(!GameplayGraphEffectCallbacks.Slots(scope.Payload).Any(s=>s.Path==relative&&Edges(scope.Id,s.Key).Any()))return false;
                    try{ResolveBinding(scope,relative);}catch(Exception error){v.Error(path,error.Message);}return true;
                }
                foreach (var binding in matches)
                {
                    try { Bound(binding); } catch (Exception e) { v.Error(path, e.Message); }
                }
                return true;
            };
            return v;
        }

        private bool HasBinding(object owner, string slot)
        {
            if (_scopes.Count == 0 || !_scopes.Peek().Paths.TryGetValue(owner, out var prefix)) return false;
            string path = prefix.Length == 0 ? slot : prefix + "." + slot;
            var scope=_scopes.Peek();path=GameplayGraphFields.NormalizePath(path);
            return scope.Bindings.Any(b => b.PropertyPath == path)||GameplayGraphEffectCallbacks.Slots(scope.Payload).Any(s=>s.Path==path&&Edges(scope.Id,s.Key).Any());
        }

        private object Resolve(GameplayDefinitionAsset legacy, object owner, string slot, Type expected)
        {
            if (_scopes.Count == 0 || !_scopes.Peek().Paths.TryGetValue(owner, out var prefix)) return null;
            string path = GameplayGraphFields.NormalizePath(prefix.Length == 0 ? slot : prefix + "." + slot);
            object result = ResolveBinding(_scopes.Peek(),path);
            if (!expected.IsInstanceOfType(result)) throw new InvalidOperationException("定义引用类型错误：" + path + "，需要 " + expected.Name);
            return result;
        }
        private object ResolveBinding(BindingScope scope,string path)
        {
            var binding=scope.Bindings.SingleOrDefault(b=>b.PropertyPath==path);if(binding!=null)return Bound(binding);
            var callback=GameplayGraphEffectCallbacks.Slots(scope.Payload).SingleOrDefault(s=>s.Path==path);
            if(callback!=null){var edge=Edges(scope.Id,callback.Key).SingleOrDefault();if(edge!=null){
                var target=GameplayGraphEffectCallbacks.EffectBinding(_nodes[edge.ToNodeId]);if(target!=null)return Bound(target);
            }}throw new InvalidOperationException("缺少必需的定义引用："+scope.Id+"."+path);
        }

        private object Bound(GameplayDefinitionBinding binding) => !string.IsNullOrEmpty(binding.LocalId)
            ? BuildResource(binding.LocalId) : binding.External != null ? binding.External.BuildSharedDefinition(_context)
            : throw new InvalidOperationException("定义引用已丢失：" + binding.PropertyPath);

        private static void Throw(GameplayAuthoringValidationContext v)
        { if (v.HasErrors) throw new GameplayGraphCompilationException(v.Issues); }

        public static long StableActionId(string id)
        {
            unchecked { ulong value = 14695981039346656037UL; foreach (char c in id) { value ^= c; value *= 1099511628211UL; } return (long)(value & 0x7fffffffffffffffUL); }
        }

        // Compiler-only bridge for existing nested action factories. Never persisted or installed
        // as an authoring source, and never introduces another executable rule implementation.
        private sealed class CompiledAction : GameplaySkillActionAuthoring
        {
            private readonly IGameplaySkillAction _action;
            internal CompiledAction(IGameplaySkillAction action) { _action = action; }
            public override void Validate(GameplayAuthoringValidationContext validation, string path) { }
            public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context) => _action;
        }
    }
}
