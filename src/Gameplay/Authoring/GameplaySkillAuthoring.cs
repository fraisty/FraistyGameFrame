using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Serializable static trigger node; Build returns a shareable Definition, while Bind owns per-owner state.</summary>
    [Serializable]
    public abstract class GameplaySkillTriggerAuthoring
    {
        public abstract void Validate(GameplayAuthoringValidationContext validation, string path);
        public abstract IGameplaySkillTriggerDefinition Build();
    }
    /// <summary>Serializable condition node; validation must report path-aware content errors before Build.</summary>
    [Serializable]
    public abstract class GameplaySkillConditionAuthoring
    {
        public abstract void Validate(GameplayAuthoringValidationContext validation, string path);
        public abstract IGameplaySkillCondition Build();
    }
    /// <summary>Serializable target node; spatial resolution, when needed, remains in a game-specific resolver.</summary>
    [Serializable]
    public abstract class GameplaySkillTargetAuthoring
    {
        public abstract void Validate(GameplayAuthoringValidationContext validation, string path);
        public abstract IGameplaySkillTarget Build();
    }
    /// <summary>Serializable action node; use the supplied BuildContext for referenced assets to preserve Definition identity.</summary>
    [Serializable]
    public abstract class GameplaySkillActionAuthoring
    {
        public abstract void Validate(GameplayAuthoringValidationContext validation, string path);
        public abstract IGameplaySkillAction Build(GameplayAuthoringBuildContext context);
        public virtual void ValidateReferences(GameplayAuthoringValidationContext validation, string path) { }
    }

    [Serializable, Preserve]
    public sealed class GameplaySkillEventTriggerAuthoring : GameplaySkillTriggerAuthoring
    {
        public GameplayTagReference EventTag;
        public override void Validate(GameplayAuthoringValidationContext v, string p) => v.Tag(EventTag, p + ".EventTag");
        public override IGameplaySkillTriggerDefinition Build() => new GameplaySkillEventTriggerDefinition(EventTag.Build());
    }
    [Serializable, Preserve]
    public sealed class GameplaySkillOwnerEventCountTriggerAuthoring : GameplaySkillTriggerAuthoring
    {
        public GameplayTagReference EventTag;
        public int RequiredCount = 1;
        public override void Validate(GameplayAuthoringValidationContext v, string p)
        { v.Tag(EventTag, p + ".EventTag"); if (RequiredCount < 1) v.Error(p + ".RequiredCount", "Count must be positive."); }
        public override IGameplaySkillTriggerDefinition Build() => new GameplaySkillOwnerEventCountTriggerDefinition(EventTag.Build(), RequiredCount);
    }
    [Serializable, Preserve]
    public sealed class GameplaySkillIntervalTriggerAuthoring : GameplaySkillTriggerAuthoring
    {
        public GameplayTagReference SignalTag;
        public float Interval = 1f;
        public override void Validate(GameplayAuthoringValidationContext v, string p)
        { v.Tag(SignalTag, p + ".SignalTag"); v.Finite(Interval, p + ".Interval"); if (Interval <= 0) v.Error(p + ".Interval", "Interval must be positive."); }
        public override IGameplaySkillTriggerDefinition Build() => new GameplaySkillIntervalTriggerDefinition(SignalTag.Build(), Interval);
    }
    [Serializable, Preserve]
    public sealed class GameplaySkillTagConditionAuthoring : GameplaySkillConditionAuthoring
    {
        public GameplayTagReference[] Required = Array.Empty<GameplayTagReference>();
        public GameplayTagReference[] Blocked = Array.Empty<GameplayTagReference>();
        public override void Validate(GameplayAuthoringValidationContext v, string p)
        {
            if (Required == null) v.Error(p + ".Required", "Tag list is null.");
            else for (int i = 0; i < Required.Length; i++) v.Tag(Required[i], p + $".Required[{i}]");
            if (Blocked == null) v.Error(p + ".Blocked", "Tag list is null.");
            else for (int i = 0; i < Blocked.Length; i++) v.Tag(Blocked[i], p + $".Blocked[{i}]");
        }
        public override IGameplaySkillCondition Build() => new GameplaySkillTagCondition(
            GameplayTagQueryAuthoring.BuildTags(Required), GameplayTagQueryAuthoring.BuildTags(Blocked));
    }
    [Serializable, Preserve]
    public sealed class GameplaySkillAttributeConditionAuthoring : GameplaySkillConditionAuthoring
    {
        public GameplayAttributeReference Attribute;
        public GameplaySkillAttributeComparison Comparison;
        public float Constant;
        public override void Validate(GameplayAuthoringValidationContext v, string p)
        { v.Attribute(Attribute, p + ".Attribute"); v.Enum(Comparison, p + ".Comparison"); v.Finite(Constant, p + ".Constant"); }
        public override IGameplaySkillCondition Build() => new GameplaySkillAttributeCondition(Attribute.Build(), Comparison, Constant);
    }
    [Serializable, Preserve]
    public sealed class GameplaySkillSelfTargetAuthoring : GameplaySkillTargetAuthoring
    {
        public override void Validate(GameplayAuthoringValidationContext v, string p) { }
        public override IGameplaySkillTarget Build() => new GameplaySkillSelfTarget();
    }
    [Serializable, Preserve]
    public sealed class GameplaySkillApplyEffectActionAuthoring : GameplaySkillActionAuthoring
    {
        public GameplayEffectDefinitionAsset Effect;
        public override void Validate(GameplayAuthoringValidationContext v, string p) => ValidateReferences(v, p);
        public override void ValidateReferences(GameplayAuthoringValidationContext v, string p) => v.Asset(Effect, p + ".Effect");
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context) => new GameplaySkillApplyEffectAction(context.ResolveDefinition(Effect, this, nameof(Effect), () => Effect.Build(context)));
    }

}
