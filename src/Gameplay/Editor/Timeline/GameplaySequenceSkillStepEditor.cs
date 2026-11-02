using System;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.Timeline
{
    /// <summary>Reuses Skill node discovery to author nested nodes on a SkillStep marker.</summary>
    internal static class GameplaySequenceSkillStepEditor
    {
        internal static void DrawNodeMenus(SerializedProperty marker)
        {
            GUILayout.BeginHorizontal();
            if (GUILayout.Button("添加条件")) Menu(marker, "Conditions", SkillNodeRole.Condition, true);
            if (GUILayout.Button("选择目标")) Menu(marker, "Target", SkillNodeRole.Target, false);
            if (GUILayout.Button("添加动作")) Menu(marker, "Actions", SkillNodeRole.Action, true);
            GUILayout.EndHorizontal();
        }

        private static void Menu(SerializedProperty marker, string field, SkillNodeRole role, bool append)
        {
            UnityEngine.Object asset = marker.serializedObject.targetObject;
            string path = marker.propertyPath + "." + field;
            GameplayAuthoringTypePicker.Property(GameplayAuthoringTypeCatalog.BaseType(role), marker.serializedObject.FindProperty(path), append);
        }
    }
}
