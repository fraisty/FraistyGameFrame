#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Live2D.Editor
{
    [CustomPropertyDrawer(typeof(Live2DMotionReference))]
    public class Live2DMotionReferenceDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);
            position = EditorGUI.PrefixLabel(position, label);

            var libraryProperty = property.FindPropertyRelative("library");
            var motionIdProperty = property.FindPropertyRelative("motionId");
            float libraryWidth = Mathf.Max(120f, position.width * 0.45f);
            var libraryRect = new Rect(position.x, position.y, libraryWidth - 4f, EditorGUIUtility.singleLineHeight);
            var motionRect = new Rect(position.x + libraryWidth, position.y, position.width - libraryWidth, EditorGUIUtility.singleLineHeight);

            EditorGUI.PropertyField(libraryRect, libraryProperty, GUIContent.none);
            DrawMotionPopup(motionRect, libraryProperty.objectReferenceValue as Live2DMotionLibrary, motionIdProperty);
            EditorGUI.EndProperty();
        }

        private static void DrawMotionPopup(Rect rect, Live2DMotionLibrary library, SerializedProperty motionIdProperty)
        {
            if (library == null || library.clips == null || library.clips.Count == 0)
            {
                EditorGUI.PropertyField(rect, motionIdProperty, GUIContent.none);
                return;
            }

            var options = new List<string> { "<None>" };
            int selected = 0;
            for (int i = 0; i < library.clips.Count; i++)
            {
                var clip = library.clips[i];
                if (clip == null || string.IsNullOrEmpty(clip.motionId)) continue;
                string label = string.IsNullOrEmpty(clip.displayName) ? clip.motionId : $"{clip.displayName} ({clip.motionId})";
                options.Add(label);
                if (clip.motionId == motionIdProperty.stringValue) selected = options.Count - 1;
            }

            int next = EditorGUI.Popup(rect, selected, options.ToArray());
            if (next == selected) return;
            motionIdProperty.stringValue = next <= 0 ? string.Empty : library.clips[next - 1].motionId;
        }
    }
}
#endif
