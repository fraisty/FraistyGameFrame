using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    public enum GameplayDependencyState { Linked, Inline, Optional, Unset, Missing, Invalid }
    /// <summary>Explicit game-side dependency declaration. Framework definitions use serialized traversal instead.</summary>
    public interface IGameplayAuthoringDependencyProvider
    {
        bool Supports(Object source);
        IEnumerable<GameplayAuthoringDependencyEdge> Dependencies(Object source);
    }

    /// <summary>A reference location, not a copied definition. Callbacks are editor-only, explicit user actions.</summary>
    public sealed class GameplayAuthoringDependencyEdge
    {
        public Object Source { get; }
        public Object Target { get; }
        public string PropertyPath { get; }
        public string Label { get; }
        public Type ExpectedType { get; }
        public bool Missing => State is GameplayDependencyState.Missing or GameplayDependencyState.Invalid;
        public GameplayDependencyState State { get; internal set; }
        public string Issue { get; internal set; }
        internal string GraphOwner, SlotPath, LocalDefinitionId;
        internal GameplaySourceHandle Handle;
        internal string SourceSnapshot;
        internal bool ProviderOwned;
        public Action Open { get; }
        public Action Ping { get; }
        /// <summary>Optional provider repair. Must record Undo, touch only this reference and never SaveAll.</summary>
        public Action Fix { get; }
        public GameplayAuthoringDependencyEdge(Object source, Object target, string path, string label,
            Type expectedType, Action open = null, Action ping = null, Action fix = null)
        {
            Source = source; Target = target; PropertyPath = path; Label = label; ExpectedType = expectedType;
            Open = open; Ping = ping; Fix = fix;
            State = target ? GameplayDependencyState.Linked : GameplayDependencyState.Missing;
        }
    }

    /// <summary>One canonical asset with multiple incoming reference locations.</summary>
    public sealed class GameplayAuthoringDependencyNode
    {
        public Object Asset { get; }
        public IReadOnlyList<GameplayAuthoringDependencyEdge> Edges => _edges;
        public IReadOnlyList<string> InlineAuthoringPaths => _inlinePaths;
        public IReadOnlyList<GameplayAuthoringValidationIssue> Issues => _issues;
        internal bool HasActions { get; set; }
        public int UnusedDefinitions { get; internal set; }
        internal readonly List<GameplayAuthoringDependencyEdge> _edges = new List<GameplayAuthoringDependencyEdge>();
        internal readonly List<string> _inlinePaths = new List<string>();
        internal readonly List<GameplayAuthoringValidationIssue> _issues = new List<GameplayAuthoringValidationIssue>();
        internal GameplayAuthoringDependencyNode(Object asset) { Asset = asset; }
    }

    /// <summary>Cycle-safe gameplay dependency projection. Ordinary resources are leaves unless a provider opts in.</summary>
    public sealed class GameplayAuthoringDependencyGraph
    {
        private readonly Dictionary<Object, GameplayAuthoringDependencyNode> _nodes = new Dictionary<Object, GameplayAuthoringDependencyNode>();
        private readonly HashSet<Object> _visiting = new HashSet<Object>();
        public IReadOnlyCollection<GameplayAuthoringDependencyNode> Nodes => _nodes.Values;
        public GameplayAuthoringDependencyNode Root { get; private set; }
        public GameplayAuthoringDependencyNode Find(Object asset) => asset != null && _nodes.TryGetValue(asset, out var node) ? node : null;
        /// <summary>Traverses only a consumed definition's reference closure. Editor workspace
        /// banks and unconnected drafts must not select a runtime host or animation controller.</summary>
        public static GameplayAuthoringDependencyGraph BuildDefinition(GameplayDefinitionAsset definition)
        {
            var graph=new GameplayAuthoringDependencyGraph();
            if(definition!=null)graph.Root=graph.Visit(definition);
            return graph;
        }
        private GameplayAuthoringDependencyNode Visit(Object asset)
        {
            if (_nodes.TryGetValue(asset, out var existing)) return existing;
            var node = new GameplayAuthoringDependencyNode(asset);
            _nodes.Add(asset, node);
            _visiting.Add(asset);
            if (asset is GameplaySkillDocumentAsset document && document.AuthoringSchemaVersion == 2)
                ReadGraph(node, document);
            else if (asset is GameplayDefinitionAsset definition)
            {
                var required = Requirements(node, definition, "");
                using (var serialized = new SerializedObject(asset))
                foreach (var property in GameplayAuthoringProperties.Walk(serialized))
                {
                    if (property.propertyType == SerializedPropertyType.ManagedReference && property.managedReferenceValue != null)
                    {
                        node._inlinePaths.Add(property.propertyPath + " · " + property.managedReferenceValue.GetType().Name);
                        node.HasActions |= property.managedReferenceValue is GameplaySkillActionAuthoring;
                    }
                    if (property.propertyType != SerializedPropertyType.ObjectReference || property.name == "m_Script") continue;
                    Type expected = GameplayAuthoringProperties.ReferenceType(property);
                    AddProperty(node, property, expected, IsRequired(required,property.propertyPath), required != null);
                }
            }
            else foreach (var provider in GameplayAuthoringProviders.Dependencies)
                try
                {
                    if (!provider.Supports(asset)) continue;
                    foreach (var edge in provider.Dependencies(asset).ToArray())
                    {
                        edge.ProviderOwned = true;
                        node._edges.Add(edge);
                    }
                }
                catch (Exception error)
                {
                    node._issues.Add(new GameplayAuthoringValidationIssue(GameplayAuthoringIssueSeverity.Error,
                        provider.GetType().Name, error.Message));
                }
            string snapshot = null;
            foreach (var edge in node._edges)
            {
                if (_visiting.Contains(edge.Target))
                {
                    edge.State = GameplayDependencyState.Invalid;
                    edge.Issue = "循环引用。";
                }
                else if (edge.Target) Visit(edge.Target);
                if (!edge.Missing || edge.ProviderOwned) continue;
                edge.Handle = GameplaySourceHandle.Capture(asset, edge.PropertyPath);
                edge.SourceSnapshot = snapshot ??= EditorJsonUtility.ToJson(asset);
            }
            _visiting.Remove(asset);
            return node;
        }

        private static void ReadGraph(GameplayAuthoringDependencyNode target, GameplaySkillDocumentAsset document)
        {
            var graph = document.AuthoringGraph;
            var active = GameplayGraphContextPolicy.Reachable(graph);
            using var serialized = new SerializedObject(document);
            var ordinary = GameplayAuthoringProperties.Walk(serialized).Where(p =>
                p.propertyType == SerializedPropertyType.ObjectReference).ToArray();
            for (int i = 0; i < graph.Nodes.Count; i++)
            {
                var node = graph.Nodes[i];
                if (!active.Contains(node.Id)) { target.UnusedDefinitions++; continue; }
                string prefix = $"authoringGraph.Nodes.Array.data[{i}].Payload";
                if (node.Payload is GameplayActionNodePayload action)
                {
                    target.HasActions |= action.Action != null;
                    ReadOwner(node.Id, action.Action, node.Payload.Definitions, prefix + ".Action");
                }
                else if (node.Payload is GameplayAbilityNodePayload ability) ReadOwner(node.Id, ability.Settings, node.Payload.Definitions, prefix + ".Settings");
            }
            for (int i = 0; i < graph.LocalDefinitions.Count; i++)
            {
                var local = graph.LocalDefinitions[i];
                if (!active.Contains(local.Id)) { target.UnusedDefinitions++; continue; }
                ReadOwner(local.Id, local.Payload, local.Definitions, $"authoringGraph.LocalDefinitions.Array.data[{i}].Payload");
            }

            void ReadOwner(string id, object payload, List<GameplayDefinitionBinding> bindings, string prefix)
            {
                var slots = GameplayGraphFields.ResourceSlots(payload);
                var required = Requirements(target,payload,prefix);
                var resourcePaths = slots.Select(slot => prefix + "." + slot.Path).ToHashSet();
                foreach (var slot in slots)
                {
                    var binding = bindings.Find(b => b.PropertyPath == slot.Path);
                    GraphSlot(id, prefix + "." + slot.Path, slot.Path, slot.ValueType, binding,
                        IsRequired(required,slot.Path),
                        GameplayGraphEffectCallbacks.Slots(payload).Any(s => s.Path == slot.Path && graph.Connections.Any(e => e.FromNodeId == id && e.OutputPortKey == s.Key)),
                        required != null);
                }
                foreach (var binding in bindings.Where(b => !slots.Any(slot => slot.Path == b.PropertyPath)))
                {
                    Type expected = binding.PropertyPath == "Settings" ? typeof(GameplayAbilityDefinitionAsset) : null;
                    GraphSlot(id, prefix, binding.PropertyPath, expected, binding, true, false, true);
                }
                foreach (var p in ordinary.Where(p => p.propertyPath.StartsWith(prefix + ".", StringComparison.Ordinal) && !resourcePaths.Contains(p.propertyPath)))
                    AddProperty(target, p, GameplayAuthoringProperties.ReferenceType(p),
                        IsRequired(required,p.propertyPath.Substring(prefix.Length + 1)), required != null);
            }

            void GraphSlot(string id, string path, string slot, Type expected, GameplayDefinitionBinding binding, bool required, bool callback, bool validated)
            {
                var edge = new GameplayAuthoringDependencyEdge(document, binding?.External, path, slot, expected)
                {
                    GraphOwner = id, SlotPath = slot, LocalDefinitionId = binding?.LocalId
                };
                if (expected == null) { edge.State = GameplayDependencyState.Invalid; edge.Issue = "原声明字段已不存在。"; }
                else if (!string.IsNullOrEmpty(binding?.LocalId))
                {
                    var local = graph.LocalDefinitions.Find(d => d.Id == binding.LocalId);
                    bool timeline = expected == typeof(GameplaySequenceDefinitionAsset) && graph.Timelines.Any(t => t.Id == binding.LocalId);
                    bool valid = timeline || local?.Payload != null && GameplaySkillFieldSchema.Values(expected).Any(t => t.IsInstanceOfType(local.Payload));
                    edge.State = valid ? GameplayDependencyState.Inline : GameplayDependencyState.Invalid;
                    edge.Issue = valid ? null : "内嵌定义缺失或类型不匹配。";
                }
                else if (callback) edge.State = GameplayDependencyState.Inline;
                else if (edge.Target)
                {
                    edge.Issue = !expected.IsInstanceOfType(edge.Target) ? "引用类型不匹配。" : GameplayActionArrayPolicy.ScopeReason(edge.Target, graph.Scope);
                    edge.State = edge.Issue == null ? GameplayDependencyState.Linked : GameplayDependencyState.Invalid;
                }
                else if (required || binding != null) { edge.State = GameplayDependencyState.Missing; edge.Issue = "需要实际的内嵌定义或共享引用。"; }
                else edge.State = validated ? GameplayDependencyState.Optional : GameplayDependencyState.Unset;
                target._edges.Add(edge);
            }
        }

        private static IReadOnlyCollection<string> Requirements(GameplayAuthoringDependencyNode node, object payload, string path)
        {
            try { return GameplayGraphFields.RequiredResources(payload); }
            catch (Exception error)
            {
                node._issues.Add(new GameplayAuthoringValidationIssue(GameplayAuthoringIssueSeverity.Error,
                    path, $"未能判定 {payload.GetType().Name} 的必填引用 · {error.Message}"));
                return null;
            }
        }

        private static bool IsRequired(IReadOnlyCollection<string> required, string path) =>
            required != null && required.Any(declaration => declaration == path ||
                path.StartsWith(declaration + ".Array.data[", StringComparison.Ordinal));

        private static void AddProperty(GameplayAuthoringDependencyNode owner, SerializedProperty property, Type expected, bool required, bool validated)
        {
            var target = property.objectReferenceValue;
            var edge = new GameplayAuthoringDependencyEdge(owner.Asset, target, property.propertyPath, property.propertyPath, expected);
            if (!target && !required && property.objectReferenceInstanceIDValue == 0)
                edge.State = validated && expected != null && typeof(GameplayDefinitionAsset).IsAssignableFrom(expected)
                    ? GameplayDependencyState.Optional : GameplayDependencyState.Unset;
            if (edge.Missing) edge.Issue = "声明的资源未绑定或已丢失。";
            owner._edges.Add(edge);
        }
    }

    /// <summary>Serialized traversal preserves source paths, including nested managed references, without recursing a managed cycle.</summary>
    public static class GameplayAuthoringProperties
    {
        public static IEnumerable<SerializedProperty> Walk(SerializedObject serialized)
        {
            var property = serialized.GetIterator();
            var ancestors = new List<(int depth, long id)>();
            bool enter = true;
            while (property.NextVisible(enter))
            {
                ancestors.RemoveAll(x => x.depth >= property.depth);
                enter = property.propertyType != SerializedPropertyType.String;
                if (property.propertyType == SerializedPropertyType.ManagedReference && property.managedReferenceValue != null)
                {
                    long id = property.managedReferenceId;
                    enter = !ancestors.Any(x => x.id == id);
                    if (enter) ancestors.Add((property.depth, id));
                }
                yield return property.Copy();
            }
        }
        public static Type ReferenceType(SerializedProperty property)
        {
            string type = property.type;
            if (type.StartsWith("PPtr<$", StringComparison.Ordinal) && type.EndsWith(">", StringComparison.Ordinal))
            {
                string name = type.Substring(6, type.Length - 7);
                if (name == "Object") return typeof(Object);
                if (name == "GameObject") return typeof(GameObject);
                if (name == "Sprite") return typeof(Sprite);
                if (name == "AnimationClip") return typeof(AnimationClip);
                if (name == "AudioClip") return typeof(AudioClip);
                if (name == "Material") return typeof(Material);
                return TypeCache.GetTypesDerivedFrom<ScriptableObject>().FirstOrDefault(t => t.Name == name) ?? property.objectReferenceValue?.GetType();
            }
            return property.objectReferenceValue?.GetType();
        }
    }

    /// <summary>Explicit repair writes one serialized reference. Assignment is undoable; destination creation never saves unrelated assets.</summary>
    public static class GameplayAuthoringDependencyRepair
    {
        public static void Assign(GameplayAuthoringDependencyEdge edge, Object target)
        {
            if (target is GameplayDefinitionAsset { MigrationHistoryOnly: true })
                throw new InvalidOperationException("只读迁移记录不能用作正式共享引用。");
            string path = RequireCurrent(edge);
            if (!target || !EditorUtility.IsPersistent(target) || !edge.ExpectedType.IsInstanceOfType(target))
                throw new ArgumentException("请选择类型匹配的项目资源。");
            var document = edge.Source as GameplaySkillDocumentAsset;
            string scope = document ? document.AuthoringGraph.Scope : (edge.Source as IGameplayScopedAuthoring)?.RequiredScope;
            string restriction = GameplayActionArrayPolicy.ScopeReason(target, scope);
            if (restriction != null) throw new InvalidOperationException(restriction);
            if (target is GameplayDefinitionAsset definition && GameplayAuthoringDependencyGraph.BuildDefinition(definition).Find(edge.Source) != null)
                throw new InvalidOperationException("此绑定会形成共享资源循环。");
            if (edge.GraphOwner != null)
            {
                GameplayInspectorOperations.BindResource(document, edge.GraphOwner, edge.SlotPath, null, null, (GameplayDefinitionAsset)target);
                return;
            }
            using var serialized = new SerializedObject(edge.Source);
            serialized.FindProperty(path).objectReferenceValue = target;
            serialized.ApplyModifiedProperties();
            GameplayAuthoringSourceChanges.Notify(edge.Source, path);
        }

        public static void AssignInline(GameplayAuthoringDependencyEdge edge, Type type)
        {
            RequireCurrent(edge);
            if (edge.GraphOwner == null || !GameplaySkillFieldSchema.Values(edge.ExpectedType).Contains(type))
                throw new ArgumentException("该字段无法创建所选内嵌类型。");
            GameplayInspectorOperations.BindResource((GameplaySkillDocumentAsset)edge.Source, edge.GraphOwner, edge.SlotPath, type, null, null);
        }

        private static string RequireCurrent(GameplayAuthoringDependencyEdge edge)
        {
            var source = edge.Source;
            if (!source || EditorApplication.isPlayingOrWillChangePlaymode ||
                source is GameplayDefinitionAsset { MigrationHistoryOnly: true } ||
                source is GameplaySkillDocumentAsset d && GameplaySkillAuthoringCommands.IsReadOnly(d) ||
                EditorUtility.IsPersistent(source) && !AssetDatabase.IsOpenForEdit(source))
                throw new InvalidOperationException("原来源不可编辑。请退出 Play 或打开可写来源。");
            if (!edge.Missing || edge.ExpectedType == null || edge.Handle?.Owner != source ||
                !edge.Handle.TryResolve(out string path) || EditorJsonUtility.ToJson(source) != edge.SourceSnapshot)
                throw new InvalidOperationException("原引用位置或配置已改变。请刷新依赖后重新选择。");
            if (edge.GraphOwner != null)
            {
                var graph = ((GameplaySkillDocumentAsset)source).AuthoringGraph;
                var node = graph.Nodes.Find(n => n.Id == edge.GraphOwner);
                object payload = node?.Payload is GameplayActionNodePayload a ? a.Action :
                    node?.Payload is GameplayAbilityNodePayload b ? b.Settings : graph.LocalDefinitions.Find(d => d.Id == edge.GraphOwner)?.Payload;
                if (node?.Payload is GameplayAbilityNodePayload && edge.SlotPath == "Settings" ||
                    GameplayGraphFields.ResourceSlots(payload).Any(s => s.Path == edge.SlotPath && s.ValueType == edge.ExpectedType)) return path;
            }
            else
            {
                using var serialized = new SerializedObject(source);
                var property = serialized.FindProperty(path);
                if (property?.propertyType == SerializedPropertyType.ObjectReference &&
                    GameplayAuthoringProperties.ReferenceType(property) == edge.ExpectedType) return path;
            }
            throw new InvalidOperationException("原字段类型或用途已改变。请重新定位。");
        }

        public static ScriptableObject CreateAndAssign(GameplayAuthoringDependencyEdge edge, string destination)
        {
            Type type = edge.ExpectedType;
            if (type == null || type.IsAbstract || !typeof(ScriptableObject).IsAssignableFrom(type))
                throw new ArgumentException("This edge requires a provider repair or an existing resource.");
            if (string.IsNullOrEmpty(destination) || !destination.StartsWith("Assets/", StringComparison.Ordinal) ||
                destination.Contains("../") || destination.Contains("\\") ||
                !destination.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) ||
                System.IO.File.Exists(destination) || System.IO.File.Exists(destination+".meta") ||
                System.IO.Directory.Exists(destination) || !AssetDatabase.IsValidFolder(System.IO.Path.GetDirectoryName(destination).Replace('\\','/')))
                throw new ArgumentException("Choose a new .asset destination inside Assets.");
            RequireCurrent(edge);
            Undo.IncrementCurrentGroup();
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("新建并绑定共享定义");
            var asset = ScriptableObject.CreateInstance(type);
            if (asset is GameplayDefinitionAsset)
            {
                using (var serialized = new SerializedObject(asset))
                {
                    serialized.FindProperty("definitionId").stringValue = Guid.NewGuid().ToString("N");
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            try
            {
                AssetDatabase.CreateAsset(asset, destination);
                Assign(edge, asset);
                Undo.CollapseUndoOperations(group);
                return asset;
            }
            catch
            {
                Undo.RevertAllDownToGroup(group);
                if (AssetDatabase.GetAssetPath(asset) == destination) AssetDatabase.DeleteAsset(destination);
                else if (asset) Object.DestroyImmediate(asset);
                throw;
            }
        }
    }

    internal static class GameplayAuthoringProviders
    {
        internal static readonly IGameplayAuthoringDependencyProvider[] Dependencies = Discover<IGameplayAuthoringDependencyProvider>();
        internal static readonly IGameplayRangePreviewProvider[] Ranges = Discover<IGameplayRangePreviewProvider>();
        private static T[] Discover<T>() => TypeCache.GetTypesDerivedFrom<T>()
            .Where(t => !t.IsAbstract && !t.IsInterface && !t.Assembly.GetName().Name.EndsWith(".Tests", StringComparison.Ordinal))
            .OrderBy(t => t.FullName, StringComparer.Ordinal).Select(t => (T)Activator.CreateInstance(t)).ToArray();
    }
}
