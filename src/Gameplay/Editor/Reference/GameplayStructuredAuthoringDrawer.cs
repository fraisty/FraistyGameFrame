using FRFrame.Gameplay.Authoring;
using UnityEditor;
using UnityEngine;
namespace FRFrame.Gameplay.Editor
{
    /// <summary>Localizes ordinary authoring children without introducing another managed selector.</summary>
    [CustomPropertyDrawer(typeof(GameplayEffectModifierAuthoring))]
    [CustomPropertyDrawer(typeof(GameplayAbilityChargeAuthoring))]
    [CustomPropertyDrawer(typeof(GameplayAttributeAbilityCostAuthoring.Entry))]
    public class GameplayStructuredAuthoringDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property,GUIContent label)
        {
            float height=EditorGUIUtility.singleLineHeight;
            if(property.isExpanded) foreach(var child in Children(property)) height+=EditorGUI.GetPropertyHeight(child,true)+2;
            return height;
        }
        public override void OnGUI(Rect rect,SerializedProperty property,GUIContent label)
        {
            if(label.text.StartsWith("Element",System.StringComparison.Ordinal))label=GameplayAuthoringInspector.Label(property);
            EditorGUI.BeginProperty(rect,label,property);
            property.isExpanded=EditorGUI.Foldout(new Rect(rect.x,rect.y,rect.width,EditorGUIUtility.singleLineHeight),property.isExpanded,label,true);
            if(property.isExpanded) {
                float y=rect.y+EditorGUIUtility.singleLineHeight+2;EditorGUI.indentLevel++;
                foreach(var child in Children(property)) {
                    float height=EditorGUI.GetPropertyHeight(child,true);
                    EditorGUI.PropertyField(new Rect(rect.x,y,rect.width,height),child,GameplayAuthoringInspector.Label(child),true);y+=height+2;
                }
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }
        private static System.Collections.Generic.IEnumerable<SerializedProperty> Children(SerializedProperty property)
        {
            var child=property.Copy();var end=child.GetEndProperty();
            if(!child.NextVisible(true)) yield break;
            do {if(SerializedProperty.EqualContents(child,end))yield break;yield return child.Copy();}while(child.NextVisible(false));
        }
    }
}
