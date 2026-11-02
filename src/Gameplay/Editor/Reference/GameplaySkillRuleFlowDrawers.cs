using System;
using FRFrame.Gameplay.Authoring;
using UnityEditor;

namespace FRFrame.Gameplay.Editor
{
    /// <summary>Minimal nested action/condition selectors; full branch graph presentation belongs to R1-D.</summary>
    [CustomPropertyDrawer(typeof(GameplaySkillActionAuthoring), true)]
    public sealed class GameplaySkillActionTreeDrawer : GameplayEffectCapabilityDrawer { protected override Type NodeType => typeof(GameplaySkillActionAuthoring); }
    [CustomPropertyDrawer(typeof(GameplaySkillConditionAuthoring), true)]
    public sealed class GameplaySkillConditionTreeDrawer : GameplayEffectCapabilityDrawer { protected override Type NodeType => typeof(GameplaySkillConditionAuthoring); }
    [CustomPropertyDrawer(typeof(GameplaySkillTriggerAuthoring), true)]
    public sealed class GameplaySkillTriggerDrawer : GameplayEffectCapabilityDrawer { protected override Type NodeType => typeof(GameplaySkillTriggerAuthoring); }
    [CustomPropertyDrawer(typeof(GameplaySkillTargetAuthoring), true)]
    public sealed class GameplaySkillTargetDrawer : GameplayEffectCapabilityDrawer { protected override Type NodeType => typeof(GameplaySkillTargetAuthoring); }
    [CustomPropertyDrawer(typeof(GameplayAbilityCostAuthoring), true)]
    public sealed class GameplayCostDrawer : GameplayEffectCapabilityDrawer { protected override Type NodeType => typeof(GameplayAbilityCostAuthoring); }
    [CustomPropertyDrawer(typeof(GameplaySkillLearningRequirementAuthoring), true)]
    public sealed class GameplayLearningRequirementDrawer : GameplayEffectCapabilityDrawer { protected override Type NodeType => typeof(GameplaySkillLearningRequirementAuthoring); }
    [CustomPropertyDrawer(typeof(Standard.GameplayCooldownOperation))]
    public sealed class GameplayCooldownOperationDrawer:PropertyDrawer
    {
        private static readonly string[] Names={"设置剩余秒数（0清零）","增减剩余秒数","按捕获时长重新计时","设置充能数","增减充能数"};
        public override void OnGUI(UnityEngine.Rect position,SerializedProperty property,UnityEngine.GUIContent label)
        {EditorGUI.BeginProperty(position,label,property);property.enumValueIndex=EditorGUI.Popup(position,label.text,property.enumValueIndex,property.enumDisplayNames.Length==Names.Length?Names:property.enumDisplayNames);EditorGUI.EndProperty();}
    }
    [CustomPropertyDrawer(typeof(Standard.GameplaySearchDirection))]
    public sealed class GameplaySearchDirectionDrawer:PropertyDrawer
    {
        private static readonly string[] Names={"角色朝向 + 角度偏移","指向当前目标 + 偏移","指向捕获地点 + 偏移","绝对角度"};
        public override void OnGUI(UnityEngine.Rect position,SerializedProperty property,UnityEngine.GUIContent label)
        {EditorGUI.BeginProperty(position,label,property);property.enumValueIndex=EditorGUI.Popup(position,label.text,property.enumValueIndex,property.enumDisplayNames.Length==Names.Length?Names:property.enumDisplayNames);EditorGUI.EndProperty();}
    }
    [CustomPropertyDrawer(typeof(Standard.GameplayStraightDestinationMode))]
    public sealed class GameplayStraightDestinationDrawer:PropertyDrawer
    {
        // Popup treats '/' as a submenu separator; keep these choices on one visible level.
        private static readonly string[] Names={"方向射击（射程或寿命结束）","固定终点（捕获目标或点位）"};
        public override void OnGUI(UnityEngine.Rect position,SerializedProperty property,UnityEngine.GUIContent label)
        {EditorGUI.BeginProperty(position,label,property);property.enumValueIndex=EditorGUI.Popup(position,"直线终点模式",property.enumValueIndex,Names);EditorGUI.EndProperty();}
    }
    [CustomPropertyDrawer(typeof(GameplayEffectExecutionAuthoring), true)]
    public sealed class GameplayExecutionDrawer : GameplayEffectCapabilityDrawer { protected override Type NodeType => typeof(GameplayEffectExecutionAuthoring); }
    [CustomPropertyDrawer(typeof(GameplayEffectCompletionAuthoring), true)]
    public sealed class GameplayCompletionDrawer : GameplayEffectCapabilityDrawer { protected override Type NodeType => typeof(GameplayEffectCompletionAuthoring); }
    [CustomPropertyDrawer(typeof(GameplaySequenceMarkerAuthoring), true)]
    public sealed class GameplayMarkerDrawer : GameplayEffectCapabilityDrawer { protected override Type NodeType => typeof(GameplaySequenceMarkerAuthoring); }
    [CustomPropertyDrawer(typeof(GameplayTagQueryAuthoring))]
    public sealed class GameplayTagQueryDrawer : GameplayEffectCapabilityDrawer { protected override Type NodeType => typeof(GameplayTagQueryAuthoring); }
}
