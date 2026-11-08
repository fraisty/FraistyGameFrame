using System;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Workbench Inspector commands shared by field controls. Structural edits commit
    /// through the existing document transaction; delayed choices resolve their original source.</summary>
    public static class GameplayInspectorOperations
    {
        internal static bool Flush(SerializedObject serialized, GameplaySkillDocumentAsset document) =>
            !serialized.hasModifiedProperties || GameplaySkillFieldSchema.ApplyFields(serialized, document);

        internal static void PickType(SerializedObject serialized, GameplaySkillDocumentAsset document,
            string path, Type type, bool append)
        {
            if (type == null || !Flush(serialized, document)) return;
            var source = GameplaySourceHandle.Capture(document, path);
            string expected = append ? null : serialized.FindProperty(path).managedReferenceFullTypename;
            var sourceGuard = GameplaySkillFieldSchema.CurrentSourceGuard;
            GameplayAuthoringTypePicker.Show(type, document,
                selected => SetType(document, source, expected, type, selected, append, sourceGuard), document.AuthoringGraph.Scope);
        }

        internal static void SetType(GameplaySkillDocumentAsset document, GameplaySourceHandle source, string expected,
            Type type, Type selected, bool append, Func<bool> sourceGuard)
        {
            if (append && selected == null) return;
            if (sourceGuard != null && !sourceGuard()) throw new InvalidOperationException("来源已切换，请重新选择配置。");
            using var serialized = new SerializedObject(document);
            var target = serialized.FindProperty(source.ResolvePath());
            if (!append && target.managedReferenceFullTypename != expected)
                throw new InvalidOperationException("字段已被替换，请重新选择配置。");
            if (selected != null && !GameplayAuthoringTypeCatalog.Query(type, document.AuthoringGraph.Scope).Contains(selected))
                throw new InvalidOperationException("此类型在当前 Scope 不可用。");
            if (!append && selected == null && (target.managedReferenceValue is GameplaySkillTriggerAuthoring ||
                target.managedReferenceValue is GameplaySkillTargetAuthoring))
                throw new InvalidOperationException("这是必需的触发或目标配置，请选择替代类型。");
            if (!append && target.managedReferenceValue is object current)
            {
                if (current.GetType() == selected) return;
                if (!EditorUtility.DisplayDialog("替换业务配置",
                    "将替换「" + GameplaySkillNodeDescriptors.Get(current.GetType()).DisplayName + "」及其字段。一次 Undo 可恢复原配置。",
                    "替换", "取消")) return;
            }
            if (append) target = target.GetArrayElementAtIndex(target.arraySize++);
            target.managedReferenceValue = selected == null ? null : Activator.CreateInstance(selected);
            GameplaySkillFieldSchema.ApplyFields(serialized, document);
        }

        internal static bool Array(SerializedObject serialized, GameplaySkillDocumentAsset document, string owner,
            string path, int index, Type elementType, GameplayInspectorPresentationSpec.Action action)
        {
            if (!Flush(serialized, document)) return false;
            var array = serialized.FindProperty(path);
            switch (action.Command)
            {
                case InspectorCommand.Move:
                    array.MoveArrayElement(index, index + action.Offset);
                    break;
                case InspectorCommand.Remove:
                    var item = array.GetArrayElementAtIndex(index);
                    var callback = item.propertyType == SerializedPropertyType.ManagedReference ? item.managedReferenceValue switch
                    {
                        GameplayApplyEffectOnCompletionAuthoring completion => "Completion/" + completion.AuthoringId,
                        GameplayApplyEffectOnOverflowAuthoring overflow => "Overflow/" + overflow.AuthoringId,
                        GameplayEffectCompletionActionsAuthoring completion => GameplayGraphEffectCallbacks.CompletionActionPrefix + completion.AuthoringId,
                        GameplayEffectOverflowActionsAuthoring overflow => GameplayGraphEffectCallbacks.OverflowActionPrefix + overflow.AuthoringId,
                        _ => null
                    } : null;
                    if (callback != null)
                    {
                        GameplaySkillAuthoringCommands.RemoveEffectCallback(document, owner, callback);
                        return true;
                    }
                    int count = array.arraySize;
                    array.DeleteArrayElementAtIndex(index);
                    // Unity clears a populated object slot on the first delete.
                    if (array.arraySize == count) array.DeleteArrayElementAtIndex(index);
                    break;
                case InspectorCommand.Add:
                    if (elementType == null || elementType.IsAbstract)
                    {
                        PickType(serialized, document, path, elementType, true);
                        return false;
                    }
                    var entry = array.GetArrayElementAtIndex(array.arraySize++);
                    if (entry.propertyType == SerializedPropertyType.ManagedReference)
                        entry.managedReferenceValue = Activator.CreateInstance(elementType);
                    else if (entry.propertyType == SerializedPropertyType.ObjectReference)
                        entry.objectReferenceValue = null;
                    break;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
            return GameplaySkillFieldSchema.ApplyFields(serialized, document, true, new[] { path });
        }

        internal static void Graph(SerializedObject serialized, GameplaySkillDocumentAsset document,
            GameplayInspectorPresentationSpec.Action action)
        {
            if (!Flush(serialized, document)) return;
            switch (action.Command)
            {
                case InspectorCommand.Navigate: Navigate(document, action.Target); break;
                case InspectorCommand.MoveConnection: GameplaySkillAuthoringCommands.ReorderEffectAction(document, action.Target, action.Offset); break;
                case InspectorCommand.Disconnect: GameplaySkillAuthoringCommands.Disconnect(document, action.Target); break;
                default: throw new ArgumentOutOfRangeException(nameof(action));
            }
        }

        internal static void Navigate(GameplaySkillDocumentAsset document, string node) => GameplaySkillWorkbench.Open(document, node);

        internal static void ChooseReference(GameplaySourceHandle[] sources, GameplayReferenceKind kind, string value, Func<bool> sourceGuard)
        {
            if (sourceGuard != null && !sourceGuard())
                throw new InvalidOperationException("原资源绑定已改变，请重新打开选择器。");
            var paths = new string[sources.Length];
            for (int i = 0; i < sources.Length; i++)
            {
                var handle = sources[i];
                paths[i] = handle.ResolvePath();
                if (kind == GameplayReferenceKind.Tag && !GameplayReferenceCatalogProvider.Visible(GameplayReferenceCatalogProvider.Scope(handle.Owner))
                    .Any(catalog => catalog.Tags.Contains(value, StringComparer.Ordinal)))
                    throw new InvalidOperationException("此 Tag 已不在当前 Scope 中。");
                using var source = new SerializedObject(handle.Owner);
                if (source.FindProperty(paths[i])?.propertyType != SerializedPropertyType.String)
                    throw new InvalidOperationException("原引用字段已改变，请重新选择。");
            }
            if (sources.Length == 1 && sources[0].Owner is GameplaySkillDocumentAsset document)
            {
                using var source = new SerializedObject(document);
                source.FindProperty(paths[0]).stringValue = value;
                GameplaySkillFieldSchema.ApplyFields(source, document, false, paths);
                return;
            }
            GameplaySharedAuthoringCommands.Transaction("选择正式 Gameplay 引用", sources.Select(handle => handle.Owner).ToArray(), () =>
            {
                for (int i = 0; i < sources.Length; i++)
                {
                    using var source = new SerializedObject(sources[i].Owner);
                    source.FindProperty(paths[i]).stringValue = value;
                    source.ApplyModifiedPropertiesWithoutUndo();
                }
            });
        }

        internal static void BindResource(GameplaySkillDocumentAsset document, string owner, string path, Type create,
            string localId, GameplayDefinitionAsset external, bool keepReleasedDraft = false) =>
            BindValue(document, owner, path, create == null ? null : (GameplayLocalDefinitionPayload)Activator.CreateInstance(create),
                create == null ? null : GameplaySkillFieldSchema.Name(create), localId, external, keepReleasedDraft);

        internal static void BindValue(GameplaySkillDocumentAsset document, string owner, string path,
            GameplayLocalDefinitionPayload value, string name, string localId, GameplayDefinitionAsset external, bool keepReleasedDraft)
        {
            GameplaySkillAuthoringCommands.Transaction(document, "配置正式资源", graph =>
            {
                var node = graph.Nodes.Find(candidate => candidate.Id == owner);
                var bindings = node?.Payload.Definitions ?? graph.LocalDefinitions.Single(definition => definition.Id == owner).Definitions;
                var released = bindings.Where(binding => binding.PropertyPath == path).Select(binding => binding.LocalId)
                    .Where(id => !string.IsNullOrEmpty(id)).ToArray();
                bindings.RemoveAll(binding => binding.PropertyPath == path);
                if (path == "Settings" && node?.Payload is GameplayAbilityNodePayload ability)
                {
                    ability.Settings = value;
                    if (external) bindings.Add(new GameplayDefinitionBinding { PropertyPath = path, External = external });
                    return;
                }
                if (value != null)
                {
                    var local = new GameplayLocalDefinition { Name = name, Payload = value, SourceDefinitionId = document.DefinitionId };
                    graph.LocalDefinitions.Add(local);
                    localId = local.Id;
                }
                if (localId != null || external)
                    bindings.Add(new GameplayDefinitionBinding { PropertyPath = path, LocalId = localId, External = external });
                if (!keepReleasedDraft) GameplaySkillAuthoringCommands.CollectReleased(graph, released);
            });
        }

        /// <summary>Highlights the existing resource without changing its binding.</summary>
        public static void LocateResource(UnityEngine.Object source) => EditorGUIUtility.PingObject(source);
        /// <summary>Opens an explicit editor for the actual shared or Unity resource.</summary>
        public static void EditResource(UnityEngine.Object source)
        {
            if (source is GameplayDefinitionAsset definition) GameplaySkillWorkbench.EditSharedSource(definition);
            else AssetDatabase.OpenAsset(source);
        }
    }
}
