using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using UnityEditor;
using UnityEngine;
using static FRFrame.Gameplay.Editor.SkillAuthoring.GameplayInspectorPresentationSpec;
using InspectorAction = FRFrame.Gameplay.Editor.SkillAuthoring.GameplayInspectorPresentationSpec.Action;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    public static partial class GameplaySkillFieldSchema
    {
        static GameplaySkillFieldSchema()
        {
            Undo.undoRedoPerformed += () =>
            {
                Open.RemoveWhere(key => key.Contains("/index:", StringComparison.Ordinal));
                ClosedSections.RemoveWhere(key => key.Contains("/index:", StringComparison.Ordinal));
            };
        }

        internal static IEnumerable<GameplayBusinessInspectorGroup> DescribeSections(object owner)
        {
            var sections = ValueSections.SelectMany(provider => provider.Sections(owner)).ToArray();
            return sections.Length == 0 ? GameplayStandardBusinessInspector.Describe(owner) : sections;
        }

        private static void DrawValue(GameplaySkillDocumentAsset document, SerializedObject serialized, SerializedProperty root,
            string ownerId, List<GameplayDefinitionBinding> bindings, string bindingPrefix, HashSet<string> visited, EditState edit)
        {
            if (root == null) return;
            object value = root.propertyType == SerializedPropertyType.ManagedReference ? root.managedReferenceValue : root.boxedValue;
            if (value == null) return;
            var source = new Source(value);
            string key = document.GetInstanceID() + ":" + ownerId;
            string inactiveKey = key + "/inactive";
            bool showInactive = Open.Contains(inactiveKey);
            RevealPaths.TryGetValue(document.GetInstanceID(), out string reveal);
            DrawChildren(root, value, "", key, true);
            if (value is GameplayEffectDefinitionData || value is GameplayProjectileDefinitionData || value is GameplayAreaDefinitionData)
                Fold(inactiveKey, "当前策略未启用的配置", "切换策略会保留原参数。可在此清除不再使用的值。", false);

            void DrawChildren(SerializedProperty parent, object owner, string relative, string uiKey, bool top)
            {
                foreach (var section in source.Sections(parent, owner, relative, uiKey, showInactive))
                {
                    if (reveal != null && section.Fields.Any(field => OnRevealPath(field.Path))) Remember(section.Key, true);
                    bool heading = top || section.Advanced;
                    if (heading && !Fold(section.Key, section.Title, section.Help, !section.Advanced)) continue;
                    foreach (var field in section.Fields)
                    {
                        using var readOnly = new EditorGUI.DisabledScope(field.ReadOnly);
                        var property = serialized.FindProperty(field.Path);
                        string path = field.RelativePath;
                        string propertyKey = uiKey + "/" + field.Name;
                        if (_cardPaths != null && owner is GameplayAbilityDefinitionData &&
                            (field.Name == "Cost" || field.Name == "Charge" || field.Name == "CooldownEffect"))
                        {
                            EditorGUILayout.LabelField(field.Label, "在对应卡片编辑", EditorStyles.miniLabel);
                            continue;
                        }
                        if (CardSummary(property))
                        {
                            if (property.propertyType == SerializedPropertyType.ManagedReference && property.managedReferenceValue is GameplaySkillTargetAuthoring target)
                                DrawChildren(property, target, path, propertyKey, false);
                            else if (!Providers.OfType<IGameplaySkillFieldControlProvider>().Any(provider => provider.DrawSourceLink(root, property)) &&
                                (owner is GameplayEffectDefinitionData || owner is GameplayConstantFloatValueAuthoring ||
                                    owner is GameplayDamageExecutionAuthoring))
                                DrawCardSummary(property, field.Label);
                            continue;
                        }
                        if (field.Port != null)
                        {
                            DrawConnections(field.Port, value is GameplayEffectDefinitionData && GameplayGraphEffectCallbacks.IsActionPort(field.Port));
                            continue;
                        }
                        string bindingPath = bindingPrefix.Length == 0 ? path : bindingPrefix + "." + path;
                        if (field.ResourceType != null)
                        {
                            DrawBinding(document, serialized, ownerId, bindings, bindingPath, field.ResourceType, visited, edit);
                            continue;
                        }
                        if (field.Type?.IsArray == true && typeof(GameplayDefinitionAsset).IsAssignableFrom(field.Type.GetElementType()))
                        {
                            DrawResources(property, field, bindingPath);
                            continue;
                        }
                        EditorGUI.BeginChangeCheck();
                        bool provided = Providers.Any(provider => provider.DrawField(root, property));
                        if (EditorGUI.EndChangeCheck()) edit.Paths.Add(field.Path);
                        if (provided) continue;
                        if (field.Kind == InspectorFieldKind.List || field.Kind == InspectorFieldKind.Structure)
                            DrawStructured(property, field.Type, field.Label, field.Help, path, propertyKey);
                        else
                        {
                            bool policy = field.Name == "Motion" || field.Name == "StraightDestination" ||
                                field.Name == "DurationPolicy" || field.Name == "Overflow";
                            if (policy) EditorGUI.BeginChangeCheck();
                            DrawLeaf(property, field.Label, field.Help, field.Kind == InspectorFieldKind.Reference ||
                                owner is GameplayFormulaFloatValueAuthoring, field.Choices);
                            if (policy && EditorGUI.EndChangeCheck()) edit.Structural = true;
                        }
                    }
                    if (top && value is GameplayEffectDefinitionData effect &&
                        section.Fields.Any(field => field.Name == GameplayGraphEffectCallbacks.Period))
                        foreach (var row in CallbackSummary(document.AuthoringGraph, ownerId, effect))
                            GUILayout.Label(row, EditorStyles.wordWrappedMiniLabel);
                }
            }

            void DrawStructured(SerializedProperty property, Type type, string label, string help, string path, string uiKey)
            {
                if (property.isArray)
                {
                    if (OnRevealPath(property.propertyPath)) Remember(uiKey, true);
                    int count = property.arraySize;
                    var elementType = type?.GetElementType();
                    var add = new InspectorAction(InspectorCommand.Add, "添加", "添加 " + label);
                    if (count == 0)
                    {
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label(new GUIContent(label + " · 暂无项目", help), EditorStyles.miniLabel);
                            if (GameplayInspectorIMGUI.Button(add)) RunArray(property, -1, elementType, add, uiKey);
                        }
                        return;
                    }
                    if (!Fold(uiKey, label + " · " + count + " 项", help,
                        property.name != nameof(GameplayDamageExecutionAuthoring.Stages))) return;
                    for (int index = 0; index < count; index++)
                    {
                        var item = ArrayItem(property, index, uiKey, label);
                        if (OnRevealPath(item.Path)) Remember(item.Key, true);
                        var entry = serialized.FindProperty(item.Path);
                        bool structured = entry.propertyType == SerializedPropertyType.ManagedReference ||
                            entry.propertyType == SerializedPropertyType.Generic && elementType != typeof(GameplayTagReference) &&
                            elementType != typeof(GameplayAttributeReference);
                        using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
                        {
                            bool open = true;
                            using (new EditorGUILayout.HorizontalScope())
                            {
                                var title = new GUIContent(item.Title, item.Title);
                                if (structured)
                                {
                                    open = EditorGUILayout.Foldout(!ClosedSections.Contains(item.Key), title, true);
                                    Remember(item.Key, open);
                                }
                                else GUILayout.Label(title, EditorStyles.miniBoldLabel);
                                foreach (var action in item.Actions)
                                    if (GameplayInspectorIMGUI.Button(action))
                                    {
                                        if (action.Command == InspectorCommand.ChooseType)
                                            GameplayInspectorOperations.PickType(serialized, document, item.Path, elementType, false);
                                        else RunArray(property, index, elementType, action, uiKey);
                                    }
                            }
                            if (!open) continue;
                            if (structured)
                                DrawStructured(entry, elementType, "", null, path + ".Array.data[" + index + "]", item.Key);
                            else DrawLeaf(entry, "", help, true, null);
                        }
                    }
                    if (GUILayout.Button("添加 " + label, EditorStyles.miniButton, GUILayout.Height(20)))
                        RunArray(property, -1, elementType, add, uiKey);
                    return;
                }
                if (property.propertyType == SerializedPropertyType.ManagedReference)
                {
                    object current = property.managedReferenceValue;
                    bool item = label.Length == 0;
                    string typeName = current == null ? "未配置" : GameplaySkillNodeDescriptors.Get(current.GetType()).DisplayName;
                    if (!item)
                        using (new EditorGUILayout.HorizontalScope())
                        {
                            GUILayout.Label(new GUIContent(label + " · " + typeName, help), EditorStyles.miniBoldLabel);
                            if (GUILayout.Button(current == null ? "选择配置…" : "换类型…", EditorStyles.miniButton, GUILayout.ExpandWidth(false), GUILayout.Height(20)))
                                GameplayInspectorOperations.PickType(serialized, document, property.propertyPath, type, false);
                        }
                    if (current != null) DrawChildren(property, current, path, uiKey, false);
                    return;
                }
                if (property.propertyType == SerializedPropertyType.Generic && property.boxedValue is object data)
                {
                    if (label.Length != 0) EditorGUILayout.LabelField(new GUIContent(label, help), EditorStyles.miniBoldLabel);
                    DrawChildren(property, data, path, uiKey, false);
                }
                else DrawLeaf(property, label, help, true, null);
            }

            bool OnRevealPath(string path) => reveal != null &&
                (reveal == path || reveal.StartsWith(path + ".", StringComparison.Ordinal));

            void DrawResources(SerializedProperty property, GameplayInspectorPresentationSpec.Field field, string bindingPath)
            {
                string listKey = key + "/resources/" + bindingPath;
                if (!Fold(listKey, field.Label + " · " + property.arraySize + " 项", field.Help, true)) return;
                for (int index = 0; index < property.arraySize; index++)
                {
                    DrawBinding(document, serialized, ownerId, bindings, bindingPath + ".Array.data[" + index + "]",
                        field.Type.GetElementType(), visited, edit, field.Label + " #" + (index + 1));
                    if (GUILayout.Button("移除引用 #" + (index + 1), EditorStyles.miniButton) && GameplayInspectorOperations.Flush(serialized, document))
                    {
                        GameplaySkillAuthoringCommands.RemoveResourceEntry(document, ownerId, bindingPath, index);
                        GUIUtility.ExitGUI();
                    }
                }
                if (GUILayout.Button("添加引用", EditorStyles.miniButton))
                    RunArray(property, -1, field.Type.GetElementType(), new InspectorAction(InspectorCommand.Add, "添加", null), listKey);
            }

            void RunArray(SerializedProperty property, int index, Type type, InspectorAction action, string listKey)
            {
                if (!GameplayInspectorOperations.Array(serialized, document, ownerId, property.propertyPath, index, type, action)) return;
                Open.RemoveWhere(candidate => candidate.StartsWith(listKey + "/index:", StringComparison.Ordinal));
                ClosedSections.RemoveWhere(candidate => candidate.StartsWith(listKey + "/index:", StringComparison.Ordinal));
                Remember(listKey, true);
                GUIUtility.ExitGUI();
            }

            void DrawConnections(string port, bool canReorder)
            {
                var graph = document.AuthoringGraph;
                var steps = Connections(graph, ownerId, port).ToArray();
                string unavailable = GameplayGraphPortPolicy.Unavailable(graph, ownerId, port);
                if (steps.Length == 0)
                {
                    GUILayout.Label(new GUIContent(PortName(port) + " · 未连接", unavailable ?? "可从画布行尾连接行为。"), EditorStyles.miniLabel);
                    return;
                }
                GUILayout.Label(new GUIContent(PortName(port) + " · " + steps.Length + " 步", unavailable), EditorStyles.miniBoldLabel);
                foreach (var step in steps)
                    using (new EditorGUILayout.HorizontalScope())
                    {
                        GUILayout.Label(new GUIContent(step.Title, step.Title), EditorStyles.miniLabel);
                        foreach (var action in step.Actions)
                            if ((canReorder || action.Command == InspectorCommand.Navigate) && GameplayInspectorIMGUI.Button(action))
                            {
                                GameplayInspectorOperations.Graph(serialized, document, action);
                                GUIUtility.ExitGUI();
                            }
                    }
            }
        }

        private static bool Fold(string key, string title, string help, bool defaultOpen)
        {
            bool open = GameplayInspectorIMGUI.Section(title, help, defaultOpen ? !ClosedSections.Contains(key) : Open.Contains(key));
            Remember(key, open);
            return open;
        }

        private static void Remember(string key, bool open)
        {
            if (open) { Open.Add(key); ClosedSections.Remove(key); }
            else { Open.Remove(key); ClosedSections.Add(key); }
        }

        private static void DrawCardSummary(SerializedProperty property, string label, string help = "在画布卡片编辑")
        {
            string text = property.propertyType switch
            {
                SerializedPropertyType.Enum => property.enumValueIndex < 0 ? "无效值" :
                    GameplaySkillBusinessLabels.Display(property.enumNames[property.enumValueIndex]),
                SerializedPropertyType.Float => property.floatValue.ToString("0.###"),
                SerializedPropertyType.Integer => property.intValue.ToString(),
                _ => null
            };
            if (text == null) return;
            GameplayInspectorIMGUI.Field(label, help, false, caption =>
            {
                if (caption == GUIContent.none) GUILayout.Label(text);
                else EditorGUILayout.LabelField(caption, new GUIContent(text));
            });
        }

        private static void DrawSharedPreview(GameplayDefinitionAsset asset, string key)
        {
            using var serialized = new SerializedObject(asset);
            var source = new Source(asset, graphBindings: false);
            foreach (var section in source.SharedSections(serialized, key))
            {
                if (!Fold(section.Key, section.Title, section.Help, !section.Advanced)) continue;
                foreach (var field in section.Fields)
                {
                    var property = serialized.FindProperty(field.Path);
                    string summary = property.propertyType switch
                    {
                        SerializedPropertyType.ManagedReference => property.managedReferenceValue is object value
                            ? GameplaySkillNodeDescriptors.Get(value.GetType()).DisplayName : "未配置",
                        SerializedPropertyType.ObjectReference => property.objectReferenceValue ? property.objectReferenceValue.name : "未指定",
                        SerializedPropertyType.Boolean => property.boolValue ? "是" : "否",
                        SerializedPropertyType.String => property.stringValue,
                        _ => property.isArray ? property.arraySize + " 项" : null
                    };
                    if (summary != null)
                        GUILayout.Label(new GUIContent(field.Label + " · " + summary, field.Help), EditorStyles.wordWrappedMiniLabel);
                    else DrawCardSummary(property, field.Label, "打开共享源编辑");
                }
            }
        }

        private static void DrawLeaf(SerializedProperty property, string label, string help, bool fullWidth, IReadOnlyList<string> choices)
        {
            GameplayInspectorIMGUI.Field(label, help, fullWidth, caption =>
            {
                if (choices != null && property.propertyType == SerializedPropertyType.Integer)
                {
                    EditorGUI.BeginChangeCheck();
                    property.intValue = EditorGUILayout.Popup(caption, property.intValue, choices.Select(choice => new GUIContent(choice)).ToArray());
                    if (EditorGUI.EndChangeCheck()) _fieldChanges?.Add(property.propertyPath);
                }
                else Field(property, caption);
            });
            if (property.propertyType != SerializedPropertyType.ObjectReference) return;
            if (!property.objectReferenceValue)
            {
                if (property.objectReferenceInstanceIDValue != 0) EditorGUILayout.HelpBox("引用的资源已丢失。", MessageType.Error);
                return;
            }
            using (new EditorGUILayout.HorizontalScope())
            {
                if (GameplayInspectorIMGUI.Button(new InspectorAction(InspectorCommand.Navigate, "定位", "在 Project 中定位资源")))
                    GameplayInspectorOperations.LocateResource(property.objectReferenceValue);
                if (GameplayInspectorIMGUI.Button(new InspectorAction(InspectorCommand.Navigate, "编辑", "打开实际资源")))
                    GameplayInspectorOperations.EditResource(property.objectReferenceValue);
            }
        }
    }
}
