using System;
using FRFrame.Gameplay.Authoring;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor
{
    /// <summary>Minimal production Inspector type selection for Effect timing/stack/overflow nodes, including game extensions.</summary>
    public abstract class GameplayEffectCapabilityDrawer : PropertyDrawer
    {
        protected abstract Type NodeType { get; }
        private static float Line => EditorGUIUtility.singleLineHeight;
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            float height = Line;
            if (property.isExpanded && (property.propertyType!=SerializedPropertyType.ManagedReference || property.managedReferenceValue != null))
                foreach (var child in Children(property)) height += GameplayAuthoringInspector.PropertyHeight(child) + 2;
            return height;
        }
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if(label.text.StartsWith("Element",StringComparison.Ordinal)) {
                int left=property.propertyPath.LastIndexOf('['),right=property.propertyPath.LastIndexOf(']');
                if(left>=0&&right>left&&int.TryParse(property.propertyPath.Substring(left+1,right-left-1),out int index))
                    label=new GUIContent((property.propertyPath.StartsWith("Markers",StringComparison.Ordinal)?"标记 ":"配置 ")+(index+1),property.propertyPath);
            }
            EditorGUI.BeginProperty(position, label, property);
            var row = new Rect(position.x, position.y, position.width, Line);
            float selectorWidth=Mathf.Min(180,Mathf.Max(80,row.width*.55f));
            var selector = new Rect(row.xMax-selectorWidth,row.y,selectorWidth,Line);
            row.width-=selectorWidth+4;
            property.isExpanded = EditorGUI.Foldout(row, property.isExpanded, label, true);
            bool managed=property.propertyType==SerializedPropertyType.ManagedReference;
            if(managed){
                string caption = property.managedReferenceValue == null ? "选择配置类型" :
                    SkillAuthoring.GameplaySkillNodeDescriptors.Get(property.managedReferenceValue.GetType()).DisplayName;
                if (GUI.Button(selector, caption, EditorStyles.popup))GameplayAuthoringTypePicker.Property(NodeType, property);
            }
            if (property.isExpanded && (!managed || property.managedReferenceValue != null))
            {
                EditorGUI.indentLevel++;
                float y = position.y + Line + 2;
                foreach (var child in Children(property))
                {
                    float height = GameplayAuthoringInspector.PropertyHeight(child);
                    GameplayAuthoringInspector.DrawProperty(new Rect(position.x,y,position.width,height), child, GameplayAuthoringInspector.Label(child)); y += height + 2;
                }
                EditorGUI.indentLevel--;
            }
            EditorGUI.EndProperty();
        }
        private static System.Collections.Generic.IEnumerable<SerializedProperty> Children(SerializedProperty property)
        {
            var child = property.Copy(); var end = child.GetEndProperty();
            if (!child.NextVisible(true)) yield break;
            do { if (SerializedProperty.EqualContents(child,end)) yield break;
                if(child.name=="Value"&&property.propertyType==SerializedPropertyType.ManagedReference&&property.managedReferenceValue is GameplayCooldownActionAuthoring cooldown&&cooldown.Operation==Standard.GameplayCooldownOperation.Restart)continue;
                if(!GameplayAuthoringFieldSchema.Visible(property.propertyType==SerializedPropertyType.ManagedReference?property.managedReferenceValue:null,child.name))continue;
                yield return child.Copy(); } while (child.NextVisible(false));
        }
    }
    [CustomPropertyDrawer(typeof(GameplayFloatValueAuthoring), true)]
    public sealed class GameplayEffectTimingValueDrawer : GameplayEffectCapabilityDrawer { protected override Type NodeType => typeof(GameplayFloatValueAuthoring); }
    [CustomPropertyDrawer(typeof(GameplayEffectOverflowAuthoring), true)]
    public sealed class GameplayEffectOverflowDrawer : GameplayEffectCapabilityDrawer { protected override Type NodeType => typeof(GameplayEffectOverflowAuthoring); }
    [CustomPropertyDrawer(typeof(GameplayEffectStackingAuthoring))]
    public sealed class GameplayEffectStackingDrawer : GameplayEffectCapabilityDrawer { protected override Type NodeType => typeof(GameplayEffectStackingAuthoring); }
}
