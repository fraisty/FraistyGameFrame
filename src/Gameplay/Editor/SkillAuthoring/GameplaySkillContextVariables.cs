using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using FRFrame.Gameplay.Standard;
using UnityEditor;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Editor projection of scalar bindings along the same ordered arrays the compiler builds.
    /// A binding is available only on every invocation of the field. Producer paths are retained
    /// to distinguish an unambiguous rename from a same-name write on an independent path.</summary>
    internal sealed class GameplaySkillContextVariables
    {
        private static ConditionalWeakTable<GameplaySkillDocumentAsset, GameplaySkillContextVariables> Cache = new();
        private static readonly Regex FormulaTokens = new(@"(?<![\w.])(?:Context|Caller)\.([\p{L}\p{Nd}_.]+)");
        private static readonly IReadOnlyDictionary<string, HashSet<string>> Empty =
            new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        private readonly Dictionary<string, IReadOnlyDictionary<string, HashSet<string>>> _inputs = new();
        private readonly Dictionary<string, (string Key, string Title)> _producers = new();
        private readonly Dictionary<string, string> _references = new();
        private readonly Dictionary<string, string> _locations = new();
        private readonly HashSet<string> _formulaPaths = new();

        static GameplaySkillContextVariables()
        {
            GameplaySkillAuthoringCommands.DetailedChanged += (document, change) => { if (!change.Layout) Cache.Remove(document); };
            Undo.undoRedoPerformed += () => Cache = new();
        }

        internal static GameplaySkillContextVariables For(GameplaySkillDocumentAsset document) =>
            Cache.GetValue(document, source => new GameplaySkillContextVariables(source));

        internal static bool IsProducer(Type type) =>
            type == typeof(GameplayCaptureValueActionAuthoring) || type == typeof(GameplaySkillSetContextValueActionAuthoring);

        internal static bool IsReader(Type type) =>
            type == typeof(GameplaySkillContextValueConditionAuthoring) || type == typeof(GameplayContextFloatValueAuthoring) ||
            type == typeof(GameplaySetByCallerFloatValueAuthoring);

        internal static Type FieldOwner(SerializedProperty property)
        {
            if (property.name != "Key" || property.serializedObject.isEditingMultipleObjects ||
                !(property.serializedObject.targetObject is GameplaySkillDocumentAsset document) ||
                document.AuthoringSchemaVersion != 2) return null;
            string path = property.propertyPath;
            var owner = property.serializedObject.FindProperty(path.Substring(0, path.Length - 4));
            return owner?.propertyType == SerializedPropertyType.ManagedReference ? owner.managedReferenceValue?.GetType() : null;
        }

        private GameplaySkillContextVariables(GameplaySkillDocumentAsset document)
        {
            var graph = document.AuthoringGraph;
            var nodes = graph.Nodes.Select((node, i) => (node, path: $"authoringGraph.Nodes.Array.data[{i}].Payload"))
                .ToDictionary(item => item.node.Id);
            var definitions = graph.LocalDefinitions.Select((definition, i) => (definition, path: $"authoringGraph.LocalDefinitions.Array.data[{i}].Payload"))
                .ToDictionary(item => item.definition.Id);
            var timelines = graph.Timelines.Select((timeline, i) => (timeline, path: $"authoringGraph.Timelines.Array.data[{i}].Events"))
                .ToDictionary(item => item.timeline.Id);
            var edges = graph.Connections.ToLookup(edge => (edge.FromNodeId, edge.OutputPortKey));
            var active = new HashSet<string>();
            foreach (var (node, path) in nodes.Values)
            {
                var action = (node.Payload as GameplayActionNodePayload)?.Action;
                var key = action is GameplayCaptureValueActionAuthoring capture ? capture.Key.Name :
                    (action as GameplaySkillSetContextValueActionAuthoring)?.Key.Name;
                if (action != null && IsProducer(action.GetType()))
                    _producers[path + ".Action.Key"] = (key, node.Title);
            }

            var defaults = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            for (int i = 0; i < document.Defaults.Length; i++)
            {
                string key = document.Defaults[i].Key.Name;
                if (string.IsNullOrEmpty(key)) continue;
                string path = $"Defaults.Array.data[{i}].Key";
                _producers[path] = (key, "技能默认输入");
                defaults[key] = new HashSet<string> { path };
            }
            string triggerMagnitude = GameplayContextValues.TriggerMagnitudeKey.Name;
            _producers[triggerMagnitude] = (triggerMagnitude, "触发事件强度");
            defaults[triggerMagnitude] = new HashSet<string> { triggerMagnitude };

            foreach (var id in graph.AdditionalRuleNodeIds.Prepend(graph.PrimaryRuleNodeId))
            {
                if (string.IsNullOrEmpty(id) || !nodes.TryGetValue(id, out var rule)) continue;
                // Rule conditions see the incoming trigger. Installed defaults are merged for Actions.
                Observe(rule.path, id, rule.node.Payload, Empty);
                RunOutput(id, "Actions", defaults);
            }

            IReadOnlyDictionary<string, HashSet<string>> RunOutput(string id, string port,
                IReadOnlyDictionary<string, HashSet<string>> input)
            {
                foreach (var edge in edges[(id, port)].OrderBy(edge => edge.Order))
                    if (edge.InputPortKey == "Execute") input = RunAction(edge.ToNodeId, input);
                return input;
            }

            IReadOnlyDictionary<string, HashSet<string>> RunAction(string id, IReadOnlyDictionary<string, HashSet<string>> input)
            {
                if (!nodes.TryGetValue(id, out var item) || !(item.node.Payload is GameplayActionNodePayload payload) ||
                    payload.Action == null || !active.Add(id)) return input;
                string path = item.path + ".Action";
                Observe(path, id, payload.Action, input);
                foreach (var binding in payload.Definitions) VisitDefinition(binding.LocalId, input);
                if (_producers.TryGetValue(path + ".Key", out var producer) && !string.IsNullOrEmpty(producer.Key))
                    input = new Dictionary<string, HashSet<string>>(input, StringComparer.Ordinal)
                        { [producer.Key] = new HashSet<string> { path + ".Key" } };
                else if (payload.Action is GameplayCaptureCombatRollActionAuthoring roll)
                {
                    var captured = new Dictionary<string, HashSet<string>>(input, StringComparer.Ordinal);
                    foreach (string key in new[] { GameplayDamageExecution.CriticalKey.Name, GameplayDamageExecution.MissKey.Name })
                    {
                        if (roll.PreserveExisting && captured.ContainsKey(key)) continue;
                        string source = path + "." + key;
                        _producers[source] = (key, "捕获暴击 / 未命中");
                        captured[key] = new HashSet<string> { source };
                    }
                    input = captured;
                }
                else if (payload.Action is GameplaySkillBranchActionAuthoring)
                    input = Merge(RunOutput(id, "TrueActions", input), RunOutput(id, "FalseActions", input));
                else if (payload.Action is GameplaySearchTargetActionAuthoring search)
                {
                    RunOutput(id, "OnBegin", input);
                    var each = input;
                    if (search.RepeatFromResult)
                    {
                        string key = GameplaySearchTargetAction.SearchMultiplierKey.Name;
                        string source = path + ".RepeatMultiplier";
                        _producers[source] = (key, "逐次搜索倍率");
                        each = new Dictionary<string, HashSet<string>>(input, StringComparer.Ordinal) { [key] = new HashSet<string> { source } };
                    }
                    RunOutput(id, "EachTarget", each);
                    RunOutput(id, "NoTargets", input);
                    var completed = RunOutput(id, "Completed", input);
                    if (search.HopIntervalSeconds == 0) input = completed;
                }
                else
                    foreach (var slot in GameplayGraphFields.ActionArrays(payload.Action))
                        RunOutput(id, slot.Path, payload.Action is GameplayMoveTargetsActionAuthoring ||
                            payload.Action is GameplayGrantTimedShieldActionAuthoring ? input : Empty);

                input = RunOutput(id, "Then", input);
                active.Remove(id);
                return input;
            }

            void VisitDefinition(string id, IReadOnlyDictionary<string, HashSet<string>> input)
            {
                if (string.IsNullOrEmpty(id) || !active.Add(id)) return;
                if (timelines.TryGetValue(id, out var schedule))
                    for (int i = 0; i < schedule.timeline.Events.Count; i++)
                    {
                        var entry = schedule.timeline.Events[i];
                        // Each marker starts with the Context captured when the sequence started.
                        Observe($"{schedule.path}.Array.data[{i}]", entry.Id, entry, input);
                        if (!string.IsNullOrEmpty(entry.EntryNodeId)) RunAction(entry.EntryNodeId, input);
                    }
                else if (definitions.TryGetValue(id, out var item))
                {
                    var payload = item.definition.Payload;
                    Observe(item.path, id, payload, input);
                    foreach (var binding in item.definition.Definitions) VisitDefinition(binding.LocalId, input);
                    bool spatial = payload is GameplayAreaDefinitionData || payload is GameplayProjectileDefinitionData ||
                        payload is GameplaySummonDefinitionData;
                    foreach (var slot in GameplayGraphFields.ActionArrays(payload))
                        RunOutput(id, GameplayGraphEffectCallbacks.ActionKey(slot), spatial || payload is GameplayEffectDefinitionData ? input : Empty);
                    foreach (var slot in GameplayGraphEffectCallbacks.Slots(payload))
                        foreach (var edge in edges[(id, slot.Key)])
                            VisitDefinition(GameplayGraphEffectCallbacks.EffectBinding(nodes[edge.ToNodeId].node)?.LocalId, input);
                }
                active.Remove(id);
            }
        }

        private void Observe(string root, string id, object payload, IReadOnlyDictionary<string, HashSet<string>> input)
        {
            if (_inputs.TryGetValue(root, out var previous))
            {
                _inputs[root] = Merge(previous, input);
                return;
            }
            _inputs[root] = input;
            foreach (var pair in GameplayGraphFields.ObjectPaths(payload))
            {
                string path = string.IsNullOrEmpty(pair.Value) ? root : root + "." + pair.Value;
                if (IsReader(pair.Key.GetType()))
                    _locations[path] = string.IsNullOrEmpty(pair.Value) ? id : id + "." + pair.Value;
                switch (pair.Key)
                {
                    case GameplaySkillContextValueConditionAuthoring condition: _references[path + ".Key"] = condition.Key.Name; break;
                    case GameplayContextFloatValueAuthoring context: _references[path + ".Key"] = context.Key.Name; break;
                    case GameplaySetByCallerFloatValueAuthoring caller: _references[path + ".Key"] = caller.Key.Name; break;
                    case GameplayFormulaFloatValueAuthoring _: _formulaPaths.Add(path + ".Expression"); break;
                }
            }
        }

        private static IReadOnlyDictionary<string, HashSet<string>> Merge(
            IReadOnlyDictionary<string, HashSet<string>> left, IReadOnlyDictionary<string, HashSet<string>> right) =>
            left.Where(pair => right.ContainsKey(pair.Key)).ToDictionary(pair => pair.Key,
                pair => new HashSet<string>(pair.Value.Concat(right[pair.Key])), StringComparer.Ordinal);

        private IReadOnlyDictionary<string, HashSet<string>> Input(string path)
        {
            for (int end = path.LastIndexOf('.'); end >= 0; end = path.LastIndexOf('.', end - 1))
                if (_inputs.TryGetValue(path.Substring(0, end), out var result)) return result;
            return Empty;
        }

        internal (string Key, string Label)[] Choices(string path) => Input(path)
            .Where(pair => GameplayReferenceDiagnostics.IsValid(GameplayReferenceKind.Tag, pair.Key))
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair =>
            {
                string name = ObjectNames.NicifyVariableName(pair.Key.Substring(pair.Key.LastIndexOf('.') + 1));
                string source = string.Join(" / ", pair.Value.Select(id => _producers[id].Title).Where(title => !string.IsNullOrEmpty(title)).Distinct());
                return (pair.Key, source.Length == 0 ? name : name + " · " + source);
            }).ToArray();

        internal string Warning(string path, string key)
        {
            if (!GameplayReferenceDiagnostics.IsValid(GameplayReferenceKind.Tag, key)) return "请输入有效的 Context 键。";
            if (Input(path).ContainsKey(key)) return null;
            return _producers.Values.Any(producer => producer.Key == key)
                ? "此键在技能中有定义，但并非每条到达路径都携带该值。"
                : "此处没有可达的生产者。若由外部传入，需由调用方保证有值。";
        }

        internal IEnumerable<GameplayAuthoringValidationIssue> Diagnostics()
        {
            foreach (var pair in _references)
            {
                if (!GameplayReferenceDiagnostics.IsValid(GameplayReferenceKind.Tag, pair.Value)) continue;
                string warning = Warning(pair.Key, pair.Value);
                if (warning != null)
                    yield return new GameplayAuthoringValidationIssue(GameplayAuthoringIssueSeverity.Warning,
                        _locations[pair.Key.Substring(0, pair.Key.Length - 4)] + ".Key", pair.Value + " · " + warning);
            }
        }

        /// <summary>Stages the declaration and its uniquely bound reads in the same Undo edit.</summary>
        internal void StageRenames(SerializedObject serialized, HashSet<string> paths)
        {
            foreach (string changed in paths.ToArray())
            {
                string path = changed.EndsWith(".Name", StringComparison.Ordinal) ? changed.Substring(0, changed.Length - 5) : changed;
                if (!_producers.TryGetValue(path, out var producer) || !path.StartsWith("authoringGraph.Nodes.", StringComparison.Ordinal)) continue;
                string key = serialized.FindProperty(path + ".Name").stringValue;
                if (key == producer.Key) continue;
                if (!GameplayReferenceDiagnostics.IsValid(GameplayReferenceKind.Tag, key))
                    throw new InvalidOperationException("请输入有效的 Context 键。");
                if (string.IsNullOrEmpty(producer.Key)) continue;
                bool Bound(string reader) => Input(reader).TryGetValue(producer.Key, out var sources) &&
                    sources.Count == 1 && sources.Contains(path);
                var readers = _references.Where(pair => pair.Value == producer.Key && Bound(pair.Key)).Select(pair => pair.Key + ".Name").ToArray();
                var formulas = _formulaPaths.Where(Bound).Select(serialized.FindProperty).Where(property => property != null &&
                    FormulaTokens.Matches(property.stringValue).Cast<Match>().Any(match => match.Groups[1].Value == producer.Key)).ToArray();
                if (readers.Length == 0 && formulas.Length == 0) continue;
                if (_producers.Any(pair => pair.Key != path && pair.Value.Key == key))
                    throw new InvalidOperationException("已有同名定义。请先选一个新键，避免改名后读取其它来源。");
                if (formulas.Length != 0 && key.Any(c => !char.IsLetterOrDigit(c) && c != '_' && c != '.'))
                    throw new InvalidOperationException("此键用于公式，改名时只可使用字母、数字、下划线和点。");
                foreach (string reader in readers)
                {
                    serialized.FindProperty(reader).stringValue = key;
                    paths.Add(reader);
                }
                foreach (var formula in formulas)
                {
                    formula.stringValue = FormulaTokens.Replace(formula.stringValue, match => match.Groups[1].Value == producer.Key
                        ? match.Value.Substring(0, match.Value.IndexOf('.') + 1) + key : match.Value);
                    paths.Add(formula.propertyPath);
                }
            }
        }
    }
}
