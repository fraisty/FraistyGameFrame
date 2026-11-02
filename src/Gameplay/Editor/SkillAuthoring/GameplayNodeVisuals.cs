using FRFrame.Gameplay.Authoring;
using UnityEngine;
namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Transient semantic role; containers never inherit their children's execution role.</summary>
    public enum GameplayNodeVisualKind { Neutral, Ability, Condition, Sequence, Target, Effect, Presentation, Continuation, World, Action, End }
    public static class GameplayNodeVisuals
    {
        public static readonly string[] Labels={"配置","释放 / 消耗 / 冷却","条件 / 分支","编排 / 动画","目标搜索","伤害 / 效果","表现","后续 / 完成","世界行为","规则动作","结束技能 / 终止"};
        private static readonly Color[] Colors={new Color(.48f,.52f,.58f),new Color(.95f,.57f,.25f),new Color(.94f,.78f,.27f),new Color(.30f,.61f,.94f),new Color(.22f,.76f,.88f),new Color(.91f,.35f,.39f),new Color(.36f,.78f,.43f),new Color(.72f,.48f,.92f),new Color(.20f,.70f,.65f),new Color(.42f,.58f,.84f),new Color(.83f,.47f,.62f)};
        public static Color Color(GameplayNodeVisualKind kind)=>Colors[(int)kind];
        public static GameplayNodeVisualKind Classify(object value)
        {
            if(value is GameplaySkillConditionAuthoring || value is GameplaySkillBranchActionAuthoring) return GameplayNodeVisualKind.Condition;
            if(value is GameplaySkillTargetAuthoring || value is GameplaySearchTargetActionAuthoring) return GameplayNodeVisualKind.Target;
            if(value is GameplayEndAbilityActionAuthoring) return GameplayNodeVisualKind.End;
            if(value is GameplayProjectileDefinitionAsset || value is GameplayAreaDefinitionAsset || value is GameplaySummonDefinitionAsset || value is GameplaySpawnSummonActionAuthoring || value is GameplaySpawnProjectileActionAuthoring || value is GameplaySpawnAreaActionAuthoring || value is GameplayMoveTargetsActionAuthoring || value is GameplayRotateToPointActionAuthoring) return GameplayNodeVisualKind.World;
            if(value is GameplayAbilityDefinitionAsset || value is GameplayAbilityCostAuthoring || value is GameplayAbilityChargeAuthoring) return GameplayNodeVisualKind.Ability;
            if(value is GameplayContinuationDefinitionAsset || value is GameplayEffectCompletionAuthoring || value is GameplayEffectOverflowAuthoring) return GameplayNodeVisualKind.Continuation;
            if(value is GameplayEffectDefinitionAsset || value is GameplayEffectExecutionAuthoring || value is GameplaySkillApplyEffectActionAuthoring || value is GameplayApplyToTargetsActionAuthoring || value is GameplayDispelEffectsActionAuthoring || value is GameplaySequenceApplyEffectMarkerAuthoring || value is GameplayEffectOverflowAuthoring) return GameplayNodeVisualKind.Effect;
            if(value is GameplaySequenceCueMarkerAuthoring) return GameplayNodeVisualKind.Presentation;
            if(value is GameplaySequenceDefinitionAsset || value is GameplaySequenceSkillStepMarkerAuthoring || value is GameplaySkillPlaySequenceActionAuthoring || value is GameplayOwnedSequenceActionAuthoring || value is GameplaySkillTriggerAuthoring) return GameplayNodeVisualKind.Sequence;
            if(value is GameplaySkillActionAuthoring) return GameplayNodeVisualKind.Action;
            return GameplayNodeVisualKind.Neutral;
        }
    }
}
