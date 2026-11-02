using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FRFrame.Gameplay
{
    /// <summary>
    /// 不可变的 Effect 静态定义，不保存目标、Handle 或剩余时间。
    /// Duration 的单位由推进容器的宿主时钟决定；Infinite/Instant 将 Duration 规范为 0。
    /// </summary>
    public sealed class GameplayEffectDefinition
    {
        public GameplayEffectDurationPolicy DurationPolicy { get; }
        public float Duration { get; }
        /// <summary>0 表示非周期；正数表示两次 execution 之间的宿主规则时间。</summary>
        public float Period { get; }
        /// <summary>Optional per-application values, frozen before any persistent state is committed.</summary>
        public IGameplayFloatValue DurationValue { get; }
        public IGameplayFloatValue PeriodValue { get; }
        public GameplayEffectPeriodicStartPolicy PeriodicStart { get; }
        /// <summary>What this effect is; never contributed to the target tag container.</summary>
        public IReadOnlyList<GameplayTag> EffectTags { get; }
        /// <summary>null 表示每次 Apply 独立成一个实例；否则按显式 StackKey 聚合。</summary>
        public GameplayEffectStackingDefinition Stacking { get; }
        /// <summary>每次 Apply 前检查目标标签；拒绝时不分配来源或修改现有 stack。</summary>
        public GameplayTagQuery ApplicationRequirements { get; }
        /// <summary>持续 Effect 每次规则推进前检查；失配时移除整个活动 Entry。</summary>
        public GameplayTagQuery OngoingRequirements { get; }
        public IReadOnlyList<GameplayEffectAttributeModifierDefinition> Modifiers { get; }
        /// <summary>按定义顺序执行的行为；Instant 执行一次，周期 Effect 每到 Period 执行一次。</summary>
        public IReadOnlyList<IGameplayEffectExecution> Executions { get; }
        /// <summary>Authored order, executed once after natural Duration expiry cleanup; interruptions never run these.</summary>
        public IReadOnlyList<IGameplayEffectCompletionExecution> CompletionExecutions { get; }

        /// <summary>
        /// 此定义在应用期间提供的 exact 标签。集合只读，来源由每个 Effect 实例独立分配。
        /// </summary>
        public IReadOnlyList<GameplayTag> GrantedTags { get; }

        public GameplayEffectDefinition(
            GameplayEffectDurationPolicy durationPolicy,
            float duration,
            IEnumerable<GameplayEffectAttributeModifierDefinition> modifiers)
            : this(durationPolicy, duration, modifiers, new GameplayTag[0])
        {
        }

        /// <summary>
        /// 创建同时提供属性修饰与标签贡献的不可变定义。两组输入都会被防御性复制；
        /// 标签来源不保存在 Definition 中，而由每次 Apply 的 Effect 实例分配。
        /// </summary>
        public GameplayEffectDefinition(
            GameplayEffectDurationPolicy durationPolicy,
            float duration,
            IEnumerable<GameplayEffectAttributeModifierDefinition> modifiers,
            IEnumerable<GameplayTag> grantedTags)
            : this(durationPolicy, duration, modifiers, grantedTags, Array.Empty<IGameplayEffectExecution>())
        {
        }

        public GameplayEffectDefinition(
            GameplayEffectDurationPolicy durationPolicy,
            float duration,
            IEnumerable<GameplayEffectAttributeModifierDefinition> modifiers,
            IEnumerable<GameplayTag> grantedTags,
            IEnumerable<IGameplayEffectExecution> executions)
            : this(durationPolicy, duration, 0f, modifiers, grantedTags, executions)
        {
        }

        /// <summary>带周期的不可变定义；旧构造器的 Period 默认为 0。</summary>
        public GameplayEffectDefinition(
            GameplayEffectDurationPolicy durationPolicy,
            float duration,
            float period,
            IEnumerable<GameplayEffectAttributeModifierDefinition> modifiers,
            IEnumerable<GameplayTag> grantedTags,
            IEnumerable<IGameplayEffectExecution> executions)
            : this(durationPolicy, duration, period, modifiers, grantedTags, executions, null)
        {
        }

        /// <summary>带可选聚合堆叠策略的不可变定义。</summary>
        public GameplayEffectDefinition(
            GameplayEffectDurationPolicy durationPolicy,
            float duration,
            float period,
            IEnumerable<GameplayEffectAttributeModifierDefinition> modifiers,
            IEnumerable<GameplayTag> grantedTags,
            IEnumerable<IGameplayEffectExecution> executions,
            GameplayEffectStackingDefinition stacking,
            GameplayTagQuery applicationRequirements = null,
            GameplayTagQuery ongoingRequirements = null,
            IEnumerable<IGameplayEffectCompletionExecution> completionExecutions = null,
            IEnumerable<GameplayTag> effectTags = null,
            GameplayEffectPeriodicStartPolicy periodicStart = GameplayEffectPeriodicStartPolicy.AfterFirstPeriod,
            IGameplayFloatValue durationValue = null, IGameplayFloatValue periodValue = null)
        {
            if (durationPolicy != GameplayEffectDurationPolicy.Duration &&
                durationPolicy != GameplayEffectDurationPolicy.Infinite &&
                durationPolicy != GameplayEffectDurationPolicy.Instant)
                throw new ArgumentOutOfRangeException(nameof(durationPolicy));
            if (durationPolicy == GameplayEffectDurationPolicy.Duration &&
                (float.IsNaN(duration) || float.IsInfinity(duration) || duration <= 0f))
                throw new ArgumentOutOfRangeException(nameof(duration), "Duration must be finite and greater than zero.");
            if (!float.IsFinite(period) || period < 0f)
                throw new ArgumentOutOfRangeException(nameof(period), "Period must be finite and nonnegative.");
            if (modifiers == null)
                throw new ArgumentNullException(nameof(modifiers));
            if (grantedTags == null)
                throw new ArgumentNullException(nameof(grantedTags));
            if (executions == null)
                throw new ArgumentNullException(nameof(executions));
            if (!Enum.IsDefined(typeof(GameplayEffectPeriodicStartPolicy), periodicStart))
                throw new ArgumentOutOfRangeException(nameof(periodicStart));
            if (durationValue != null && durationPolicy != GameplayEffectDurationPolicy.Duration)
                throw new ArgumentException("DurationValue requires Duration policy.");
            if ((periodValue != null || periodicStart != GameplayEffectPeriodicStartPolicy.AfterFirstPeriod) &&
                (durationPolicy == GameplayEffectDurationPolicy.Instant || period <= 0f))
                throw new ArgumentException("Dynamic period and immediate start require a periodic persistent effect.");
            if (stacking?.Lifetime == GameplayEffectStackLifetimePolicy.IndependentLayerLifetime &&
                durationPolicy != GameplayEffectDurationPolicy.Duration)
                throw new ArgumentException("Independent layer lifetime requires Duration policy.");
            var classification = new List<GameplayTag>(effectTags ?? Array.Empty<GameplayTag>());
            foreach (var tag in classification) if (!tag.IsValid) throw new ArgumentException("Invalid EffectTag.");

            var modifierCopy = new List<GameplayEffectAttributeModifierDefinition>();
            foreach (GameplayEffectAttributeModifierDefinition modifier in modifiers)
            {
                if (modifier == null)
                    throw new ArgumentException("Modifiers must not contain null elements.", nameof(modifiers));
                modifierCopy.Add(modifier);
            }

            var tagCopy = new List<GameplayTag>();
            foreach (GameplayTag tag in grantedTags)
            {
                if (!tag.IsValid)
                    throw new ArgumentException("Granted tags must be valid.", nameof(grantedTags));
                tagCopy.Add(tag);
            }
            var executionCopy = new List<IGameplayEffectExecution>(executions);
            var completionCopy = new List<IGameplayEffectCompletionExecution>(completionExecutions ??
                Array.Empty<IGameplayEffectCompletionExecution>());
            if (completionCopy.Contains(null))
                throw new ArgumentException("Completion executions must not contain null.", nameof(completionExecutions));
            if (completionCopy.Count > 0 && durationPolicy != GameplayEffectDurationPolicy.Duration)
                throw new ArgumentException("Only Duration effects can have natural completion executions.", nameof(completionExecutions));
            if (executionCopy.Contains(null))
                throw new ArgumentException("Executions must not contain null.", nameof(executions));
            if (durationPolicy == GameplayEffectDurationPolicy.Instant)
            {
                if (ongoingRequirements != null && !ongoingRequirements.IsEmpty)
                    throw new ArgumentException("Instant effects cannot have ongoing requirements.", nameof(ongoingRequirements));
                if (period != 0f || stacking != null || modifierCopy.Count != 0 || tagCopy.Count != 0 || executionCopy.Count == 0)
                    throw new ArgumentException("Instant effects require executions and reject persistent modifiers or tags.");
            }
            else if ((period == 0f && executionCopy.Count != 0) ||
                     (period > 0f && executionCopy.Count == 0))
                throw new ArgumentException("Persistent effects require executions exactly when Period is positive.", nameof(executions));

            DurationPolicy = durationPolicy;
            Duration = durationPolicy == GameplayEffectDurationPolicy.Duration ? duration : 0f;
            Period = period;
            DurationValue = durationValue;
            PeriodValue = periodValue;
            PeriodicStart = periodicStart;
            EffectTags = classification.AsReadOnly();
            Stacking = stacking;
            ApplicationRequirements = applicationRequirements ?? GameplayTagQuery.Empty;
            OngoingRequirements = ongoingRequirements ?? GameplayTagQuery.Empty;
            Modifiers = new ReadOnlyCollection<GameplayEffectAttributeModifierDefinition>(modifierCopy);
            GrantedTags = new ReadOnlyCollection<GameplayTag>(tagCopy);
            Executions = new ReadOnlyCollection<IGameplayEffectExecution>(executionCopy);
            CompletionExecutions = new ReadOnlyCollection<IGameplayEffectCompletionExecution>(completionCopy);
        }
    }
}
