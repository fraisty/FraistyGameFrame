using System;

namespace FRFrame.Gameplay
{
    /// <summary>
    /// 一次伤害计算的输入，由调用者提供来源、基础伤害和属性容器。
    /// 属性容器是当前运行状态的引用，不是快照；计算期间调用者和 Modifier 都不得修改它们。
    /// Context 不执行命中判定、扣血或生命周期管理。
    /// </summary>
    public sealed class DamageContext
    {
        /// <summary>有效的运行时来源 ID，不是配置表 ID。</summary>
        public GameplaySourceId SourceId { get; }
        /// <summary>未经管线处理的有限、非负伤害；0 是合法输入，负数不用于表达治疗。</summary>
        public float BaseDamage { get; }
        /// <summary>可选来源属性；没有来源单位属性的伤害可以传 null。</summary>
        public AttributeSet SourceAttributes { get; }
        /// <summary>必需的目标属性容器，仅供计算读取。</summary>
        public AttributeSet TargetAttributes { get; }
        /// <summary>Optional execution context; when present it is the authority for SourceId.</summary>
        public GameplayContext GameplayContext { get; }

        /// <summary>构造输入；无效来源、负数/非有限伤害或空目标会被拒绝。</summary>
        public DamageContext(GameplaySourceId sourceId, float baseDamage, AttributeSet targetAttributes,
            AttributeSet sourceAttributes = null)
        {
            if (!sourceId.IsValid)
                throw new ArgumentException("Source ID must be valid.", nameof(sourceId));
            if (float.IsNaN(baseDamage) || float.IsInfinity(baseDamage) || baseDamage < 0f)
                throw new ArgumentOutOfRangeException(nameof(baseDamage), "Damage must be finite and nonnegative.");
            if (targetAttributes == null)
                throw new ArgumentNullException(nameof(targetAttributes));

            SourceId = sourceId;
            BaseDamage = baseDamage;
            TargetAttributes = targetAttributes;
            SourceAttributes = sourceAttributes;
        }

        /// <summary>Use an execution context as the single source authority for this damage calculation.</summary>
        public DamageContext(GameplayContext gameplayContext, float baseDamage, AttributeSet targetAttributes,
            AttributeSet sourceAttributes = null)
            : this((gameplayContext ?? throw new ArgumentNullException(nameof(gameplayContext))).EffectiveSourceId,
                baseDamage, targetAttributes, sourceAttributes)
        {
            GameplayContext = gameplayContext;
        }
    }
}
