using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using UnityEngine;

namespace FRFrame.Gameplay.Authoring
{
    [Serializable]
    public sealed class GameplayEffectModifierAuthoring
    {
        public GameplayAttributeReference Attribute;
        public GameplayModifierOperation Operation;
        [SerializeReference] public GameplayFloatValueAuthoring Magnitude;
        public void Validate(GameplayAuthoringValidationContext v, string p)
        {
            v.Attribute(Attribute, p + ".Attribute"); v.Enum(Operation, p + ".Operation");
            if (Magnitude == null) v.Error(p + ".Magnitude", "Missing magnitude.");
            else Magnitude.Validate(v, p + ".Magnitude");
        }
        public GameplayEffectAttributeModifierDefinition Build() =>
            new GameplayEffectAttributeModifierDefinition(Attribute.Build(), Operation, Magnitude.Build());
    }

    [Serializable]
    public sealed class GameplayTagQueryAuthoring
    {
        public GameplayTagReference[] All = Array.Empty<GameplayTagReference>();
        public GameplayTagReference[] Any = Array.Empty<GameplayTagReference>();
        public GameplayTagReference[] None = Array.Empty<GameplayTagReference>();
        public void Validate(GameplayAuthoringValidationContext v, string p)
        { ValidateList(v, p + ".All", All); ValidateList(v, p + ".Any", Any); ValidateList(v, p + ".None", None); }
        private static void ValidateList(GameplayAuthoringValidationContext v, string p, GameplayTagReference[] values)
        {
            if (values == null) { v.Error(p, "Tag list is null."); return; }
            for (int i = 0; i < values.Length; i++) v.Tag(values[i], p + "[" + i + "]");
        }
        public GameplayTagQuery Build() => new GameplayTagQuery(BuildTags(All), BuildTags(Any), BuildTags(None));
        internal static GameplayTag[] BuildTags(GameplayTagReference[] values)
        {
            var tags = new GameplayTag[values.Length];
            for (int i = 0; i < tags.Length; i++) tags[i] = values[i].Build();
            return tags;
        }
    }

    [Serializable]
    public sealed class GameplayEffectStackingAuthoring
    {
        public GameplayTagReference StackKey;
        public GameplayEffectStackAggregation Aggregation;
        public int MaxStacks = 1;
        public GameplayEffectStackDurationRefreshPolicy DurationRefresh;
        public GameplayEffectStackPeriodResetPolicy PeriodReset;
        public GameplayEffectStackOverflowPolicy Overflow;
        public GameplayEffectStackLifetimePolicy Lifetime;
        [SerializeReference] public GameplayEffectOverflowAuthoring[] OverflowExecutions = Array.Empty<GameplayEffectOverflowAuthoring>();
        public void Validate(GameplayAuthoringValidationContext v, string p)
        {
            v.Tag(StackKey, p + ".StackKey"); v.Enum(Aggregation, p + ".Aggregation");
            if (MaxStacks < 1) v.Error(p + ".MaxStacks", "MaxStacks must be positive.");
            v.Enum(DurationRefresh, p + ".DurationRefresh"); v.Enum(PeriodReset, p + ".PeriodReset"); v.Enum(Overflow, p + ".Overflow");
            v.Enum(Lifetime, p + ".Lifetime");
            if (Lifetime == GameplayEffectStackLifetimePolicy.IndependentLayerLifetime && DurationRefresh != GameplayEffectStackDurationRefreshPolicy.NeverRefresh)
                v.Error(p + ".DurationRefresh", "Independent layer lifetime requires NeverRefresh.");
            if (OverflowExecutions == null) v.Error(p + ".OverflowExecutions", "Null overflow list.");
            else
            {
                if ((Overflow == GameplayEffectStackOverflowPolicy.ExecuteOverflow) != (OverflowExecutions.Length > 0))
                    v.Error(p + ".OverflowExecutions", "Only ExecuteOverflow requires nonempty rules.");
                for (int i = 0; i < OverflowExecutions.Length; i++)
                {
                    string path = p + $".OverflowExecutions[{i}]";
                    if (OverflowExecutions[i] == null) v.Error(path, "Null overflow execution.");
                    else { OverflowExecutions[i].Validate(v, path); OverflowExecutions[i].ValidateReferences(v, path); }
                }
            }
        }
        public GameplayEffectStackingDefinition Build(GameplayAuthoringBuildContext context = null)
        {
            var rules = new List<IGameplayEffectOverflowExecution>();
            foreach (var item in OverflowExecutions) rules.Add(item.Build(context));
            return new GameplayEffectStackingDefinition(StackKey.Build(), Aggregation, MaxStacks, DurationRefresh, PeriodReset, Overflow, Lifetime, rules);
        }
    }

    /// <summary>Game-specific executions derive here without extending FRFrame's runtime model.</summary>
    [Serializable]
    public abstract class GameplayEffectExecutionAuthoring
    {
        public abstract void Validate(GameplayAuthoringValidationContext validation, string path);
        public abstract IGameplayEffectExecution Build();
    }

}
