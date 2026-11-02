using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay
{
    /// <summary>同目标内按 StackKey 或再按有效来源聚合。</summary>
    public enum GameplayEffectStackAggregation
    {
        AggregateByTarget,
        AggregateBySource
    }

    /// <summary>成功重施加时是否重置共享 Duration。</summary>
    public enum GameplayEffectStackDurationRefreshPolicy
    {
        RefreshOnSuccessfulApplication,
        NeverRefresh
    }

    /// <summary>成功重施加时是否重置下一次周期执行计时。</summary>
    public enum GameplayEffectStackPeriodResetPolicy
    {
        ResetOnSuccessfulApplication,
        NeverReset
    }

    /// <summary>达到层数上限时拒绝申请，或接受并刷新原实例。</summary>
    public enum GameplayEffectStackOverflowPolicy
    {
        Reject,
        RefreshExistingAtLimit,
        ExecuteOverflow
    }

    /// <summary>持续 Effect 的不可变聚合身份、层数上限及成功重施加策略。</summary>
    public sealed class GameplayEffectStackingDefinition
    {
        public GameplayTag StackKey { get; }
        public GameplayEffectStackAggregation Aggregation { get; }
        public int MaxStacks { get; }
        public GameplayEffectStackDurationRefreshPolicy DurationRefresh { get; }
        public GameplayEffectStackPeriodResetPolicy PeriodReset { get; }
        public GameplayEffectStackOverflowPolicy Overflow { get; }
        public GameplayEffectStackLifetimePolicy Lifetime { get; }
        public IReadOnlyList<IGameplayEffectOverflowExecution> OverflowExecutions { get; }

        public GameplayEffectStackingDefinition(GameplayTag stackKey,
            GameplayEffectStackAggregation aggregation, int maxStacks,
            GameplayEffectStackDurationRefreshPolicy durationRefresh,
            GameplayEffectStackPeriodResetPolicy periodReset,
            GameplayEffectStackOverflowPolicy overflow,
            GameplayEffectStackLifetimePolicy lifetime = GameplayEffectStackLifetimePolicy.SharedEntryLifetime,
            IEnumerable<IGameplayEffectOverflowExecution> overflowExecutions = null)
        {
            if (!stackKey.IsValid) throw new ArgumentException("Stack key must be valid.", nameof(stackKey));
            if (!Enum.IsDefined(typeof(GameplayEffectStackAggregation), aggregation))
                throw new ArgumentOutOfRangeException(nameof(aggregation));
            if (maxStacks < 1) throw new ArgumentOutOfRangeException(nameof(maxStacks));
            if (!Enum.IsDefined(typeof(GameplayEffectStackDurationRefreshPolicy), durationRefresh))
                throw new ArgumentOutOfRangeException(nameof(durationRefresh));
            if (!Enum.IsDefined(typeof(GameplayEffectStackPeriodResetPolicy), periodReset))
                throw new ArgumentOutOfRangeException(nameof(periodReset));
            if (!Enum.IsDefined(typeof(GameplayEffectStackOverflowPolicy), overflow))
                throw new ArgumentOutOfRangeException(nameof(overflow));
            if (!Enum.IsDefined(typeof(GameplayEffectStackLifetimePolicy), lifetime)) throw new ArgumentOutOfRangeException(nameof(lifetime));
            if (lifetime == GameplayEffectStackLifetimePolicy.IndependentLayerLifetime &&
                durationRefresh != GameplayEffectStackDurationRefreshPolicy.NeverRefresh)
                throw new ArgumentException("Independent layers cannot refresh a shared duration.");
            var rules = new List<IGameplayEffectOverflowExecution>(overflowExecutions ?? Array.Empty<IGameplayEffectOverflowExecution>());
            if (rules.Contains(null) || (overflow == GameplayEffectStackOverflowPolicy.ExecuteOverflow) != (rules.Count > 0))
                throw new ArgumentException("ExecuteOverflow requires rules; other policies reject them.");

            StackKey = stackKey;
            Aggregation = aggregation;
            MaxStacks = maxStacks;
            DurationRefresh = durationRefresh;
            PeriodReset = periodReset;
            Overflow = overflow;
            Lifetime = lifetime;
            OverflowExecutions = rules.AsReadOnly();
        }
    }
}
