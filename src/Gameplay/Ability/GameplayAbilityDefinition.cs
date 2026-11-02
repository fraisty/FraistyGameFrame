using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FRFrame.Gameplay
{
    /// <summary>
    /// 不可变的 Ability 静态定义，只描述激活条件、激活期间拥有的标签与互斥的冷却/充能策略。
    /// Definition 不保存 Grant Handle、激活来源或活动状态，可以被多个 Container 共享。
    /// </summary>
    public sealed class GameplayAbilityDefinition
    {
        /// <summary>激活前必须全部满足的标签；每项均使用层级查询。</summary>
        public IReadOnlyList<GameplayTag> RequiredTags { get; }

        /// <summary>激活前任意命中即阻止激活的标签；每项均使用层级查询。</summary>
        public IReadOnlyList<GameplayTag> BlockedTags { get; }

        /// <summary>本次 Ability 处于 Active 期间，由独立 Activation Source 提供的 exact 标签。</summary>
        public IReadOnlyList<GameplayTag> ActivationOwnedTags { get; }
        /// <summary>Ability 定义身份，不写入 owner 的状态标签容器。</summary>
        public IReadOnlyList<GameplayTag> AbilityTags { get; }
        /// <summary>成功激活后，取消本 owner 中身份匹配的其他活动 Ability。</summary>
        public IReadOnlyList<GameplayTag> CancelAbilitiesWithTags { get; }
        /// <summary>活动期间阻止身份匹配的其他 Ability 激活。</summary>
        public IReadOnlyList<GameplayTag> BlockAbilitiesWithTags { get; }
        /// <summary>活动后命中任一 owner 标签时，由 Advance 取消本 Ability。</summary>
        public IReadOnlyList<GameplayTag> OngoingBlockedTags { get; }
        public IGameplayAbilityCost Cost { get; }
        /// <summary>Allowed grant level range is 1..MaxLevel; the current level belongs to each grant.</summary>
        public int MaxLevel { get; }
        /// <summary>由一次活动生命周期独占的持续 Effect；End/Cancel/Remove 清理。</summary>
        public IReadOnlyList<GameplayEffectDefinition> ActivationEffects { get; }

        /// <summary>
        /// 激活提交时应用的可选冷却 Effect。其 GrantedTags 同时是 CanActivate 查询的冷却标签。
        /// 冷却必须是无属性修饰、至少提供一个标签的常量 Duration Effect，不允许 DurationValue override。
        /// </summary>
        public GameplayEffectDefinition CooldownEffect { get; }

        /// <summary>Optional per-grant sequential charge policy, mutually exclusive with CooldownEffect.</summary>
        public GameplayAbilityChargeDefinition Charge { get; }

        /// <summary>
        /// 创建不可变 Ability 定义。三组标签都会被防御性复制并以只读集合公开；
        /// null 集合、无效标签及同时配置两种时间门禁会被拒绝。
        /// </summary>
        public GameplayAbilityDefinition(
            IEnumerable<GameplayTag> requiredTags,
            IEnumerable<GameplayTag> blockedTags,
            IEnumerable<GameplayTag> activationOwnedTags,
            GameplayEffectDefinition cooldownEffect = null,
            GameplayAbilityChargeDefinition charge = null,
            IEnumerable<GameplayTag> abilityTags = null,
            IEnumerable<GameplayTag> cancelAbilitiesWithTags = null,
            IEnumerable<GameplayTag> blockAbilitiesWithTags = null,
            IEnumerable<GameplayTag> ongoingBlockedTags = null,
            IGameplayAbilityCost cost = null,
            IEnumerable<GameplayEffectDefinition> activationEffects = null,
            int maxLevel = 1)
        {
            if (maxLevel < 1) throw new ArgumentOutOfRangeException(nameof(maxLevel));
            MaxLevel = maxLevel;
            RequiredTags = CopyTags(requiredTags, nameof(requiredTags));
            BlockedTags = CopyTags(blockedTags, nameof(blockedTags));
            ActivationOwnedTags = CopyTags(activationOwnedTags, nameof(activationOwnedTags));
            AbilityTags = CopyTags(abilityTags ?? Array.Empty<GameplayTag>(), nameof(abilityTags));
            CancelAbilitiesWithTags = CopyTags(cancelAbilitiesWithTags ?? Array.Empty<GameplayTag>(), nameof(cancelAbilitiesWithTags));
            BlockAbilitiesWithTags = CopyTags(blockAbilitiesWithTags ?? Array.Empty<GameplayTag>(), nameof(blockAbilitiesWithTags));
            OngoingBlockedTags = CopyTags(ongoingBlockedTags ?? Array.Empty<GameplayTag>(), nameof(ongoingBlockedTags));
            var effectCopy = new List<GameplayEffectDefinition>();
            if (activationEffects != null)
                foreach (GameplayEffectDefinition effect in activationEffects)
                {
                    if (effect == null || effect.DurationPolicy == GameplayEffectDurationPolicy.Instant ||
                        effect.Stacking != null)
                        throw new ArgumentException("Activation effects must be unstacked persistent effects.", nameof(activationEffects));
                    effectCopy.Add(effect);
                }
            ActivationEffects = new ReadOnlyCollection<GameplayEffectDefinition>(effectCopy);
            Cost = cost;

            if (cooldownEffect != null)
            {
                if (cooldownEffect.DurationPolicy != GameplayEffectDurationPolicy.Duration)
                    throw new ArgumentException("Cooldown effect must use the Duration policy.", nameof(cooldownEffect));
                if (cooldownEffect.GrantedTags.Count == 0)
                    throw new ArgumentException("Cooldown effect must grant at least one tag.", nameof(cooldownEffect));
                if (cooldownEffect.Modifiers.Count != 0)
                    throw new ArgumentException("Cooldown effect must not contain attribute modifiers.", nameof(cooldownEffect));
                if (cooldownEffect.Period != 0f || cooldownEffect.PeriodValue!=null || cooldownEffect.Executions.Count != 0 || cooldownEffect.CompletionExecutions.Count != 0 ||
                    cooldownEffect.Stacking != null || !cooldownEffect.ApplicationRequirements.IsEmpty ||
                    !cooldownEffect.OngoingRequirements.IsEmpty)
                    throw new ArgumentException("Cooldown must be a simple Duration and GrantedTag effect.", nameof(cooldownEffect));
            }

            if (cooldownEffect != null && charge != null)
                throw new ArgumentException("Charge and normal cooldown cannot be configured together.", nameof(charge));

            CooldownEffect = cooldownEffect;
            Charge = charge;
        }

        private static IReadOnlyList<GameplayTag> CopyTags(
            IEnumerable<GameplayTag> tags,
            string parameterName)
        {
            if (tags == null)
                throw new ArgumentNullException(parameterName);

            var copy = new List<GameplayTag>();
            foreach (GameplayTag tag in tags)
            {
                if (!tag.IsValid)
                    throw new ArgumentException("Ability tags must be valid.", parameterName);
                copy.Add(tag);
            }

            return new ReadOnlyCollection<GameplayTag>(copy);
        }
    }

    /// <summary>Immutable sequential recharge policy; mutable charge counts live in each granted ability.</summary>
    public sealed class GameplayAbilityChargeDefinition
    {
        public int MaxCharges { get; }
        public float RechargeDuration { get; }

        public GameplayAbilityChargeDefinition(int maxCharges, float rechargeDuration)
        {
            if (maxCharges <= 0) throw new ArgumentOutOfRangeException(nameof(maxCharges));
            if (!float.IsFinite(rechargeDuration) || rechargeDuration <= 0f)
                throw new ArgumentOutOfRangeException(nameof(rechargeDuration));
            MaxCharges = maxCharges;
            RechargeDuration = rechargeDuration;
        }
    }
}
