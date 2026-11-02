using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FRFrame.Gameplay
{
    /// <summary>Effect execution 对目标的最小通用规则视图；不假定任何生命资源模型。</summary>
    public interface IGameplayEffectExecutionTarget
    {
        GameplaySourceId TargetId { get; }
        AttributeSet Attributes { get; }
        GameplayTagContainer Tags { get; }
    }

    /// <summary>Instant 或周期 Effect 的同步行为；异常向调用方传播，已执行行为不回滚。</summary>
    public interface IGameplayEffectExecution
    {
        void Execute(GameplayEffectApplicationContext application);
    }

    /// <summary>精确 GameplayTag 键到有限 float 的不可变本次调用值快照。</summary>
    public sealed class GameplaySetByCallerValues
    {
        /// <summary>供无需运行值的 context-aware Effect application 共用的不可变空快照。</summary>
        public static GameplaySetByCallerValues Empty { get; } =
            new GameplaySetByCallerValues(Array.Empty<KeyValuePair<GameplayTag, float>>());
        private readonly IReadOnlyDictionary<GameplayTag, float> _values;
        public int Count => _values.Count;

        public GameplaySetByCallerValues(IEnumerable<KeyValuePair<GameplayTag, float>> values)
        {
            if (values == null) throw new ArgumentNullException(nameof(values));
            var copy = new Dictionary<GameplayTag, float>();
            foreach (KeyValuePair<GameplayTag, float> pair in values)
            {
                if (!pair.Key.IsValid) throw new ArgumentException("SetByCaller key must be valid.", nameof(values));
                if (!float.IsFinite(pair.Value)) throw new ArgumentOutOfRangeException(nameof(values));
                if (copy.ContainsKey(pair.Key)) throw new ArgumentException("Duplicate SetByCaller key.", nameof(values));
                copy.Add(pair.Key, pair.Value);
            }
            _values = new ReadOnlyDictionary<GameplayTag, float>(copy);
        }

        public bool TryGetValue(GameplayTag key, out float value)
        {
            if (!key.IsValid) throw new ArgumentException("SetByCaller key must be valid.", nameof(key));
            return _values.TryGetValue(key, out value);
        }

        public float GetRequiredValue(GameplayTag key)
        {
            if (!TryGetValue(key, out float value)) throw new KeyNotFoundException("Missing SetByCaller key: " + key);
            return value;
        }
    }

    /// <summary>引用已有 GameplayContext 身份快照，只补 execution target 与本次运行值。</summary>
    public sealed class GameplayEffectApplicationContext
    {
        public GameplayContext Context { get; }
        public IGameplayEffectExecutionTarget Target { get; }
        public GameplaySetByCallerValues SetByCaller { get; }
        /// <summary>真实 Instigator 的属性视图；与 EffectiveSourceId 无关。</summary>
        public AttributeSet InstigatorAttributes { get; }
        public GameplaySourceId InstigatorAttributeOwnerId { get; }
        /// <summary>Optional actual instigator capability; permits resource-backed values without duplicating balances.</summary>
        public IGameplayEffectExecutionTarget InstigatorTarget { get; }
        public int StackCount { get; }

        public GameplayEffectApplicationContext(GameplayContext context, IGameplayEffectExecutionTarget target,
            GameplaySetByCallerValues setByCaller, AttributeSet instigatorAttributes = null,
            GameplaySourceId instigatorAttributeOwnerId = default, int stackCount = 1,
            IGameplayEffectExecutionTarget instigatorTarget = null)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            SetByCaller = setByCaller ?? throw new ArgumentNullException(nameof(setByCaller));
            if (!target.TargetId.IsValid || context.TargetData.PrimaryTargetId != target.TargetId)
                throw new ArgumentException("Effect execution target must match the context primary target.", nameof(target));
            if (target.Attributes == null || target.Tags == null)
                throw new ArgumentException("Effect execution target must provide attributes and tags.", nameof(target));
            if (stackCount < 1) throw new ArgumentOutOfRangeException(nameof(stackCount));
            if (instigatorTarget == null && context.InstigatorId == target.TargetId) instigatorTarget = target;
            if (instigatorTarget != null && instigatorAttributes == null)
            {
                instigatorAttributes = instigatorTarget.Attributes; instigatorAttributeOwnerId = instigatorTarget.TargetId;
            }
            if (instigatorAttributes == null && context.InstigatorId == target.TargetId)
            {
                instigatorAttributes = target.Attributes;
                instigatorAttributeOwnerId = target.TargetId;
            }
            if (instigatorAttributes != null && instigatorAttributeOwnerId != context.InstigatorId)
                throw new ArgumentException("Instigator attribute owner must match context InstigatorId.",
                    nameof(instigatorAttributeOwnerId));
            if (instigatorAttributes == null && instigatorAttributeOwnerId.IsValid)
                throw new ArgumentException("Instigator attribute owner requires attributes.", nameof(instigatorAttributeOwnerId));
            InstigatorAttributes = instigatorAttributes;
            if (instigatorTarget != null && (instigatorTarget.TargetId != context.InstigatorId ||
                !ReferenceEquals(instigatorTarget.Attributes, instigatorAttributes)))
                throw new ArgumentException("Instigator capability must match the captured owner and attribute view.", nameof(instigatorTarget));
            InstigatorTarget = instigatorTarget;
            InstigatorAttributeOwnerId = instigatorAttributeOwnerId;
            StackCount = stackCount;
        }

        internal GameplayEffectApplicationContext WithStackCount(int count) => count == StackCount ? this :
            new GameplayEffectApplicationContext(Context, Target, SetByCaller,
                InstigatorAttributes, InstigatorAttributeOwnerId, count, InstigatorTarget);
    }

    /// <summary>Effect 申请的执行、聚合或上限处理结果。</summary>
    public enum GameplayEffectApplicationOutcome
    {
        InstantExecuted,
        NewActiveEffect,
        StackIncreased,
        RefreshedAtLimit,
        RejectedAtLimit,
        RejectedByRequirements,
        OverflowExecuted
    }

    /// <summary>一次 Effect 申请的明确结果；拒绝叠层仍指向原活动 Handle。</summary>
    public readonly struct GameplayEffectApplicationResult
    {
        public GameplayEffectApplicationOutcome Outcome { get; }
        public bool Succeeded { get; }
        /// <summary>Whether a handle was returned. Reentrant executions may remove it; query Container.IsActive for live state.</summary>
        public bool HasActiveHandle { get; }
        public GameplayEffectHandle ActiveHandle { get; }
        public int StackCount { get; }

        /// <summary>Only a real newly-created contribution produces a layer handle.</summary>
        public GameplayEffectLayerHandle LayerHandle { get; }

        internal GameplayEffectApplicationResult(GameplayEffectApplicationOutcome outcome,
            GameplayEffectHandle handle, int stackCount, GameplayEffectLayerHandle layerHandle = default)
        {
            Outcome = outcome;
            Succeeded = outcome != GameplayEffectApplicationOutcome.RejectedAtLimit &&
                outcome != GameplayEffectApplicationOutcome.RejectedByRequirements;
            ActiveHandle = handle;
            HasActiveHandle = handle.IsValid;
            StackCount = stackCount;
            LayerHandle = layerHandle;
        }
    }
}
