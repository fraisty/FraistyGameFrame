using System;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Layout primitives for the Workbench's IMGUI renderer.</summary>
    internal static class GameplayInspectorIMGUI
    {
        private static float _width;
        private static float Width => _width > 0 ? _width : EditorGUIUtility.currentViewWidth;
        internal static Color PanelColor => EditorGUIUtility.isProSkin ? GameplayEditorStyles.Panel : new Color(.80f, .80f, .80f);
        internal readonly struct Pane : IDisposable
        {
            private readonly float _previousWidth, _previousLabel;
            private readonly int _previousIndent;
            internal Pane(float width)
            {
                _previousWidth = _width;
                _previousLabel = EditorGUIUtility.labelWidth;
                _previousIndent = EditorGUI.indentLevel;
                _width = width;
                EditorGUIUtility.labelWidth = Mathf.Clamp(width * .35f, 95, 165);
                EditorGUI.indentLevel = 0;
            }
            public void Dispose()
            {
                _width = _previousWidth;
                EditorGUIUtility.labelWidth = _previousLabel;
                EditorGUI.indentLevel = _previousIndent;
            }
        }

        internal static bool Section(string title, string help, bool open)
        {
            GUILayout.Space(9);
            using (new EditorGUILayout.HorizontalScope())
            {
                open = EditorGUILayout.Foldout(open, new GUIContent(title, help), true, EditorStyles.foldoutHeader);
                if (!string.IsNullOrEmpty(help)) GUILayout.Label(new GUIContent("?", help), EditorStyles.miniButton, GUILayout.Width(24));
            }
            var rule = GUILayoutUtility.GetRect(0, 1, GUILayout.ExpandWidth(true));
            if (Event.current.type == EventType.Repaint)
                EditorGUI.DrawRect(rule, EditorGUIUtility.isProSkin ? new Color(1, 1, 1, .12f) : new Color(0, 0, 0, .17f));
            if (open) GUILayout.Space(4);
            return open;
        }

        internal static void Field(string label, string help, bool fullWidth, Action<GUIContent> control)
        {
            var caption = new GUIContent(label, help);
            if (label.Length == 0 || !fullWidth && Width >= 390)
            {
                control(caption);
                return;
            }
            EditorGUILayout.LabelField(caption, EditorStyles.miniLabel);
            control(GUIContent.none);
        }

        internal static bool Button(GameplayInspectorPresentationSpec.Action action)
        {
            using (new EditorGUI.DisabledScope(!action.Enabled))
                return GUILayout.Button(new GUIContent(action.Label, action.Help), EditorStyles.miniButton,
                    GUILayout.Width(action.Label.Length == 1 ? 26 : 42), GUILayout.Height(20));
        }
    }
}
