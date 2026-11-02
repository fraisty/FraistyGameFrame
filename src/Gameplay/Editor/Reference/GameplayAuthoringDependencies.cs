using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
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
        public bool Missing => Target == null;
        public Action Open { get; }
        public Action Ping { get; }
        /// <summary>Optional provider repair. Must record Undo, touch only this reference and never SaveAll.</summary>
        public Action Fix { get; }
        public GameplayAuthoringDependencyEdge(Object source, Object target, string path, string label,
            Type expectedType, Action open = null, Action ping = null, Action fix = null)
        {
            Source = source; Target = target; PropertyPath = path; Label = label; ExpectedType = expectedType;
            Open = open; Ping = ping; Fix = fix;
        }
    }

    /// <summary>One canonical asset with multiple incoming reference locations.</summary>
    public sealed class GameplayAuthoringDependencyNode
    {
        public Object Asset { get; }
        public IReadOnlyList<GameplayAuthoringDependencyEdge> Edges => _edges;
        public IReadOnlyList<string> InlineAuthoringPaths => _inlinePaths;
        internal readonly List<GameplayAuthoringDependencyEdge> _edges = new List<GameplayAuthoringDependencyEdge>();
        internal readonly List<string> _inlinePaths = new List<string>();
        internal GameplayAuthoringDependencyNode(Object asset) { Asset = asset; }
    }

    /// <summary>Cycle-safe gameplay dependency projection. Ordinary resources are leaves unless a provider opts in.</summary>
    public sealed class GameplayAuthoringDependencyGraph
    {
        private readonly Dictionary<Object, GameplayAuthoringDependencyNode> _nodes = new Dictionary<Object, GameplayAuthoringDependencyNode>();
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
            if (asset is GameplayDefinitionAsset)
            {
                using (var serialized = new SerializedObject(asset))
                foreach (var property in GameplayAuthoringProperties.Walk(serialized))
                {
                    if (property.propertyType == SerializedPropertyType.ManagedReference && property.managedReferenceValue != null)
                        node._inlinePaths.Add(property.propertyPath + " · " + property.managedReferenceValue.GetType().Name);
                    if (property.propertyType != SerializedPropertyType.ObjectReference || property.name == "m_Script") continue;
                    if(asset is GameplaySkillDocumentAsset skill && skill.AuthoringSchemaVersion==2 && property.propertyPath.StartsWith("authoringGraph.LegacyResources",StringComparison.Ordinal))continue;
                    Type expected = GameplayAuthoringProperties.ReferenceType(property);
                    Object target = property.objectReferenceValue;
                    if (!(target is GameplayDefinitionAsset) &&
                        (expected == null || !typeof(GameplayDefinitionAsset).IsAssignableFrom(expected))) continue;
                    node._edges.Add(new GameplayAuthoringDependencyEdge(asset, target, property.propertyPath,
                        property.propertyPath, expected ?? target?.GetType()));
                }
            }
            else foreach (var provider in GameplayAuthoringProviders.Dependencies)
                if (provider.Supports(asset)) node._edges.AddRange(provider.Dependencies(asset));
            foreach (var edge in node._edges) if (edge.Target != null) Visit(edge.Target);
            return node;
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
                return TypeCache.GetTypesDerivedFrom<ScriptableObject>().FirstOrDefault(t => t.Name == name);
            }
            return property.objectReferenceValue?.GetType();
        }
    }

    /// <summary>Explicit repair writes one serialized reference. Assignment is undoable; destination creation never saves unrelated assets.</summary>
    public static class GameplayAuthoringDependencyRepair
    {
        public static void Assign(GameplayAuthoringDependencyEdge edge, Object target)
        {
            if (edge.Source == null) throw new ArgumentException("Missing source.");
            if (target != null && edge.ExpectedType != null && !edge.ExpectedType.IsInstanceOfType(target))
                throw new ArgumentException("Incompatible reference type.");
            using (var serialized = new SerializedObject(edge.Source))
            {
                var property = serialized.FindProperty(edge.PropertyPath);
                if (property == null || property.propertyType != SerializedPropertyType.ObjectReference)
                    throw new ArgumentException("Reference location no longer exists.");
                Type currentType = GameplayAuthoringProperties.ReferenceType(property);
                if (target != null && currentType != null && !currentType.IsInstanceOfType(target))
                    throw new ArgumentException("Reference type changed; refresh dependencies first.");
                property.objectReferenceValue = target;
                serialized.ApplyModifiedProperties();
            }
        }
        public static ScriptableObject CreateAndAssign(GameplayAuthoringDependencyEdge edge, string destination)
        {
            Type type = edge.ExpectedType;
            if (type == null || type.IsAbstract || !typeof(ScriptableObject).IsAssignableFrom(type))
                throw new ArgumentException("This edge requires a provider repair or an existing resource.");
            if (string.IsNullOrEmpty(destination) || !destination.StartsWith("Assets/", StringComparison.Ordinal) ||
                destination.Contains("../") || destination.Contains("\\") ||
                !destination.EndsWith(".asset", StringComparison.OrdinalIgnoreCase) || System.IO.File.Exists(destination))
                throw new ArgumentException("Choose a new .asset destination inside Assets.");
            // Validate the source before creating anything.
            using (var serialized = new SerializedObject(edge.Source))
                if (serialized.FindProperty(edge.PropertyPath)?.propertyType != SerializedPropertyType.ObjectReference)
                    throw new ArgumentException("Reference location no longer exists.");
            int group = Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName("Create & Assign dependency");
            var asset = ScriptableObject.CreateInstance(type);
            if (asset is GameplayDefinitionAsset)
            {
                using (var serialized = new SerializedObject(asset))
                {
                    serialized.FindProperty("definitionId").stringValue = Guid.NewGuid().ToString("N");
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            }
            AssetDatabase.CreateAsset(asset, destination);
            Undo.RegisterCreatedObjectUndo(asset, "Create dependency");
            Assign(edge, asset);
            Undo.CollapseUndoOperations(group);
            return asset;
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
