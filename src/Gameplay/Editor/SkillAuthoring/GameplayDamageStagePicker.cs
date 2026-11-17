using System;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Stages use the existing searchable menu and the same serialized row in both skill inspectors.</summary>
    internal static class GameplayDamageStagePicker
    {
        internal const string HistoricalValueTitle = "高级 / 历史 Value";
        internal const string RestoreHistoricalValueTitle = "恢复默认 1";

        private static readonly string[] Names = { "免疫", "未命中", "防御", "暴击", "护盾", "最低伤害", "伤害倍率" };
        private static readonly string[] Notes =
        {
            "命中免疫时停止后续阶段。", "未命中时停止后续阶段。", "读取伤害类型对应的防御。真实伤害跳过。",
            "读取已捕获的暴击值，有效时按指定倍率计算。", "最多一次。全吸收后停止，结算成功后才提交护盾。",
            "尚未停止时可抬升当前伤害，包括已经被固定格挡减到零的伤害。", "将当前伤害乘以指定数值。"
        };

        internal static string Caption(GameplayDamageStageAuthoring stage)
        {
            if (stage.Source == GameplayDamageStageSource.Extension)
                return stage.Extension == null ? "扩展类型丢失" : GameplaySkillNodeDescriptors.Get(stage.Extension.GetType()).DisplayName;
            int kind = (int)stage.Kind;
            return kind >= 0 && kind < Names.Length ? Names[kind] : "未知阶段";
        }

        internal static bool HasHistoricalValue(GameplayDamageStageAuthoring stage) =>
            stage.Source == GameplayDamageStageSource.Builtin &&
            stage.Kind is not (GameplayDamageStageKind.Critical or GameplayDamageStageKind.Minimum or GameplayDamageStageKind.Scale);

        internal static string HistoricalValueNote(GameplayDamageStageAuthoring stage) =>
            stage.Kind == GameplayDamageStageKind.Defense
                ? "防御阶段读取对应防御属性。历史 Value 仍作配置校验，运行时不求值。"
                : "历史 Value 不参与阶段计算，但仍会校验和求值。缺少数值来源或运行时 Caller 等输入会导致失败。";

        internal static void RestoreHistoricalValue(SerializedProperty value)
        {
            value.managedReferenceValue = new GameplayConstantFloatValueAuthoring { Value = 1 };
            GUI.changed = true;
        }

        internal static void Show(SerializedProperty stages, int index)
        {
            var serialized = stages.serializedObject;
            var source = serialized.targetObject;
            var document = source as GameplaySkillDocumentAsset;
            if (document != null && !GameplayInspectorOperations.Flush(serialized, document)) return;
            if (document == null) serialized.ApplyModifiedProperties();
            var handle = GameplaySourceHandle.Capture(source, stages.propertyPath);
            var sourceGuard = GameplaySkillFieldSchema.CurrentSourceGuard;
            var window = EditorWindow.focusedWindow;
            if (window == null) return;
            string scope = document?.AuthoringGraph.Scope;
            var expected = index < 0 ? null : StageOrder(stages);
            var content = index < 0 ? default : stages.contentHash;
            GameplaySkillCreationMenu.Show(window.rootVisualElement, index < 0 ? "添加结算阶段" : "替换结算阶段 · 可撤销", menu =>
            {
                foreach (GameplayDamageStageKind kind in Enum.GetValues(typeof(GameplayDamageStageKind)))
                {
                    menu.AddItem(new GUIContent("标准阶段/" + Names[(int)kind], Notes[(int)kind]), false,
                        () => Write(new GameplayDamageStageAuthoring
                        {
                            Kind = kind,
                            Value = new GameplayConstantFloatValueAuthoring { Value = kind == GameplayDamageStageKind.Critical ? 2 : 1 }
                        }));
                }
                foreach (var type in GameplayAuthoringTypeCatalog.Types(typeof(GameplayDamageCalculationStageAuthoring)))
                {
                    var extension = (GameplayDamageCalculationStageAuthoring)Activator.CreateInstance(type);
                    if (!extension.IsAvailableInScope(scope)) continue;
                    var description = GameplaySkillNodeDescriptors.Get(type);
                    menu.AddItem(new GUIContent("计算扩展/" + description.DisplayName, description.Summary), false,
                        () => Write(new GameplayDamageStageAuthoring { Source = GameplayDamageStageSource.Extension, Extension = extension }));
                }
            });

            void Write(GameplayDamageStageAuthoring stage)
            {
                if (!source || sourceGuard != null && !sourceGuard() || document?.AuthoringGraph.Scope != scope ||
                    !handle.TryResolve(out string path))
                {
                    NotifyExpired();
                    return;
                }
                using var editing = new SerializedObject(source);
                var array = editing.FindProperty(path);
                if (index >= 0 && (index >= array.arraySize || array.contentHash != content ||
                    !expected.SequenceEqual(StageOrder(array))))
                {
                    NotifyExpired();
                    return;
                }
                int at = index < 0 ? array.arraySize++ : index;
                var row = array.GetArrayElementAtIndex(at);
                row.FindPropertyRelative(nameof(stage.Source)).enumValueIndex = (int)stage.Source;
                row.FindPropertyRelative(nameof(stage.Kind)).enumValueIndex = (int)stage.Kind;
                row.FindPropertyRelative(nameof(stage.Value)).managedReferenceValue = stage.Value;
                row.FindPropertyRelative(nameof(stage.Extension)).managedReferenceValue = stage.Extension;
                Undo.SetCurrentGroupName(index < 0 ? "添加结算阶段" : "替换结算阶段");
                if (document != null) GameplaySkillFieldSchema.ApplyFields(editing, document);
                else if (editing.ApplyModifiedProperties()) GameplayAuthoringSourceChanges.Notify(source);
            }

            void NotifyExpired()
            {
                if (window) window.ShowNotification(new GUIContent("原结算阶段或来源已改变，请重新选择。"));
            }
        }

        // Rows are inline. Child reference IDs detect reordered or replaced rows even when
        // their payload types and field values are identical.
        private static (int Source, int Kind, long Value, long Extension)[] StageOrder(SerializedProperty stages)
        {
            var order = new (int, int, long, long)[stages.arraySize];
            for (int i = 0; i < order.Length; i++)
            {
                var row = stages.GetArrayElementAtIndex(i);
                order[i] = (row.FindPropertyRelative(nameof(GameplayDamageStageAuthoring.Source)).intValue,
                    row.FindPropertyRelative(nameof(GameplayDamageStageAuthoring.Kind)).intValue,
                    row.FindPropertyRelative(nameof(GameplayDamageStageAuthoring.Value)).managedReferenceId,
                    row.FindPropertyRelative(nameof(GameplayDamageStageAuthoring.Extension)).managedReferenceId);
            }
            return order;
        }
    }
}
