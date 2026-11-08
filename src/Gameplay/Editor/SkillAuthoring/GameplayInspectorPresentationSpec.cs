using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using UnityEditor;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    internal enum InspectorFieldKind { Value, Structure, List, Reference }
    internal enum InspectorCommand { Add, ChooseType, Move, Remove, Navigate, Disconnect, MoveConnection }

    /// <summary>Presentation metadata for the current source, with no writable value or retained SerializedProperty.
    /// The renderer resolves paths against its current serialized buffer.</summary>
    internal static class GameplayInspectorPresentationSpec
    {
        internal readonly struct Field
        {
            internal readonly string Path, Name, Label, Help, RelativePath, Port;
            internal readonly Type Type, ResourceType;
            internal readonly InspectorFieldKind Kind;
            internal readonly bool ReadOnly;
            internal readonly IReadOnlyList<string> Choices;
            internal Field(string path, string name, string label, string help, Type type, InspectorFieldKind kind,
                bool readOnly, IReadOnlyList<string> choices, string relativePath, Type resourceType, string port)
            {
                Path = path; Name = name; Label = label; Help = help; Type = type; Kind = kind; ReadOnly = readOnly; Choices = choices;
                RelativePath = relativePath; ResourceType = resourceType; Port = port;
            }
        }
        internal readonly struct Section
        {
            internal readonly string Key, Title, Help;
            internal readonly bool Advanced;
            internal readonly IReadOnlyList<Field> Fields;
            internal Section(string key, string title, string help, bool advanced, IReadOnlyList<Field> fields)
            { Key = key; Title = title; Help = help; Advanced = advanced; Fields = fields; }
        }
        internal readonly struct Action
        {
            internal readonly InspectorCommand Command;
            internal readonly string Label, Help, Target;
            internal readonly int Offset;
            internal readonly bool Enabled;
            internal Action(InspectorCommand command, string label, string help, string target = null, int offset = 0, bool enabled = true)
            { Command = command; Label = label; Help = help; Target = target; Offset = offset; Enabled = enabled; }
        }
        internal readonly struct Item
        {
            internal readonly string Key, Title, Path;
            internal readonly IReadOnlyList<Action> Actions;
            internal Item(string key, string title, string path, IReadOnlyList<Action> actions)
            { Key = key; Title = title; Path = path; Actions = actions; }
        }

        internal sealed class Source
        {
            private readonly Dictionary<string, Type> _resources;
            private readonly Dictionary<string, string> _ports;

            internal Source(object value, bool graphBindings = true)
            {
                _resources = graphBindings ? GameplayGraphFields.ResourceSlots(value).ToDictionary(slot => slot.Path, slot => slot.ValueType) :
                    new Dictionary<string, Type>();
                _ports = graphBindings ? GameplayGraphFields.ActionArrays(value).ToDictionary(slot => slot.Path, GameplayGraphEffectCallbacks.ActionKey) :
                    new Dictionary<string, string>();
            }

            internal IEnumerable<Section> Sections(SerializedProperty parent, object owner, string relative, string key, bool showInactive)
                => DescribeSections(parent.serializedObject, parent, owner, relative, key, showInactive, _resources, _ports);

            internal IEnumerable<Section> SharedSections(SerializedObject serialized, string key) =>
                DescribeSections(serialized, null, serialized.targetObject, "", key, false, _resources, _ports);
        }

        private static IEnumerable<Section> DescribeSections(SerializedObject serialized, SerializedProperty parent, object owner, string relative, string key,
            bool showInactive, IReadOnlyDictionary<string, Type> resources, IReadOnlyDictionary<string, string> ports)
        {
            var children = new Dictionary<string, SerializedProperty>();
            var cursor = parent == null ? serialized.GetIterator() : parent.Copy();
            var end = parent?.GetEndProperty();
            if (cursor.NextVisible(true))
                do
                {
                    if (end != null && SerializedProperty.EqualContents(cursor, end)) break;
                    if (cursor.name != "Definitions" && cursor.name != "m_Script" && cursor.name != "definitionId" &&
                        cursor.name != "migrationHistoryOnly" && GameplaySkillFieldSchema.IsValueFieldVisible(owner, cursor.name))
                        children.Add(cursor.name, cursor.Copy());
                } while (cursor.NextVisible(false));

            var groups = GameplaySkillFieldSchema.DescribeSections(owner).ToList();
            var covered = new HashSet<string>(groups.SelectMany(group => group.Paths));
            var rest = children.Keys.Where(name => !covered.Contains(name)).ToArray();
            if (rest.Length > 0) groups.Add(new GameplayBusinessInspectorGroup(null, groups.Count == 0 ? "业务配置" : "其它配置", null, rest));
            var types = GameplayGraphFields.SerializedFields(owner.GetType());
            foreach (var group in groups)
            {
                var fields = new List<Field>();
                foreach (string name in group.Paths)
                {
                    if (!children.TryGetValue(name, out var property) || !showInactive && !GameplaySkillFieldSchema.Visible(owner, name)) continue;
                    var caption = GameplayAuthoringInspector.DescribeLabel(property);
                    string path = relative.Length == 0 ? name : relative + "." + name;
                    resources.TryGetValue(path, out Type resource);
                    ports.TryGetValue(path, out string port);
                    var type = types.FirstOrDefault(field => field.Name == name)?.FieldType;
                    var kind = property.isArray && property.propertyType != SerializedPropertyType.String ? InspectorFieldKind.List :
                        type == typeof(GameplayTagReference) || type == typeof(GameplayAttributeReference) ||
                        property.propertyType == SerializedPropertyType.ObjectReference ? InspectorFieldKind.Reference :
                        property.propertyType == SerializedPropertyType.ManagedReference || property.propertyType == SerializedPropertyType.Generic
                            ? InspectorFieldKind.Structure : InspectorFieldKind.Value;
                    fields.Add(new Field(property.propertyPath, name, group.Labels.TryGetValue(name, out var label) ? label : caption.Text,
                        string.IsNullOrEmpty(property.tooltip) ? caption.Help : property.tooltip, type, kind, group.ReadOnly.Contains(name),
                        group.Choices.TryGetValue(name, out var choices) ? Array.AsReadOnly(choices) : null, path, resource, port));
                }
                if (fields.Count > 0)
                    yield return new Section(key + "/" + group.Title, group.Title, group.Note, group.Advanced, fields.AsReadOnly());
            }
        }

        internal static Item ArrayItem(SerializedProperty array, int index, string arrayKey, string label)
        {
            var item = array.GetArrayElementAtIndex(index);
            string id = item.FindPropertyRelative("AuthoringId")?.stringValue;
            string key = arrayKey + (string.IsNullOrEmpty(id) ? "/index:" + index : "/id:" + id);
            string title = item.propertyType == SerializedPropertyType.ManagedReference && item.managedReferenceValue is object managed
                ? GameplaySkillNodeDescriptors.Get(managed.GetType()).DisplayName : label;
            if (item.FindPropertyRelative("Kind") is SerializedProperty kind && kind.propertyType == SerializedPropertyType.Enum)
            {
                var options = GameplaySkillBusinessLabels.Options(kind);
                if (kind.enumValueIndex >= 0 && kind.enumValueIndex < options.Count) title = options[kind.enumValueIndex];
            }
            var actions = new List<Action>();
            if (item.propertyType == SerializedPropertyType.ManagedReference)
                actions.Add(new Action(InspectorCommand.ChooseType, "换类型", "选择此条目的配置类型"));
            actions.AddRange(new[]
            {
                new Action(InspectorCommand.Move, "↑", "上移", offset: -1, enabled: index > 0),
                new Action(InspectorCommand.Move, "↓", "下移", offset: 1, enabled: index + 1 < array.arraySize),
                new Action(InspectorCommand.Remove, "×", "删除"),
            });
            return new Item(key, title + " #" + (index + 1), item.propertyPath, actions.AsReadOnly());
        }

        internal static IEnumerable<Item> Connections(GameplayAuthoringGraph graph, string owner, string port)
        {
            var steps = graph.Connections.Where(edge => edge.FromNodeId == owner && edge.OutputPortKey == port)
                .OrderBy(edge => edge.Order).ToArray();
            for (int i = 0; i < steps.Length; i++)
            {
                var step = steps[i];
                var target = graph.Nodes.Find(node => node.Id == step.ToNodeId);
                yield return new Item(step.Id, target == null ? "缺失目标" : GameplaySkillNodeDisplay.Name(target, graph),
                    null, Array.AsReadOnly(new[]
                    {
                        new Action(InspectorCommand.Navigate, "定位", "在画布定位", step.ToNodeId, enabled: target != null),
                        new Action(InspectorCommand.MoveConnection, "↑", "上移", step.Id, -1, i > 0),
                        new Action(InspectorCommand.MoveConnection, "↓", "下移", step.Id, 1, i + 1 < steps.Length),
                        new Action(InspectorCommand.Disconnect, "×", "断开，保留行为卡及其资源作为草稿", step.Id)
                    }));
            }
        }

        internal static IEnumerable<string> CallbackSummary(GameplayAuthoringGraph graph, string owner, GameplayEffectDefinitionData effect)
        {
            if (effect.Stacking != null)
                yield return Format("溢出", effect.Stacking.OverflowExecutions.SelectMany(item => Step(item,
                    item is GameplayEffectOverflowActionsAuthoring ? GameplayGraphEffectCallbacks.OverflowActionPrefix + item.AuthoringId :
                    item is GameplayApplyEffectOnOverflowAuthoring ? "Overflow/" + item.AuthoringId : null)));
            if (effect.DurationPolicy == GameplayEffectDurationPolicy.Duration)
                yield return Format("自然到期", effect.CompletionExecutions.SelectMany(item => Step(item,
                    item is GameplayEffectCompletionActionsAuthoring ? GameplayGraphEffectCallbacks.CompletionActionPrefix + item.AuthoringId :
                    item is GameplayApplyEffectOnCompletionAuthoring ? "Completion/" + item.AuthoringId : null)));

            IEnumerable<string> Step(object item, string port)
            {
                if (item == null) return new[] { "缺失步骤" };
                if (port == null) return new[] { GameplaySkillNodeDescriptors.Get(item.GetType()).DisplayName };
                var names = Connections(graph, owner, port).Select(step => step.Title).ToArray();
                return names.Length == 0 ? new[] { "待连接" } : names;
            }

            string Format(string title, IEnumerable<string> steps)
            {
                string ordered = string.Join(" → ", steps);
                return title + " · " + (ordered.Length == 0 ? "无额外行为" : ordered);
            }
        }
    }
}
