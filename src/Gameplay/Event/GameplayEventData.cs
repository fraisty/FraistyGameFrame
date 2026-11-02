using System;

namespace FRFrame.Gameplay
{
    /// <summary>
    /// 一次同步 Gameplay Event 的不可变规则数据。
    /// InstigatorId 与 TargetId 可为 default，表示事件没有对应的 Gameplay 来源；Magnitude 可为任意有限值。
    /// </summary>
    public readonly struct GameplayEventData
    {
        public GameplayTag EventTag { get; }
        public GameplaySourceId InstigatorId { get; }
        public GameplaySourceId TargetId { get; }
        public float Magnitude { get; }
        /// <summary>Optional immutable child/hit envelope. Legacy events leave this null.</summary>
        public GameplayContext Context { get; }
        public bool HasContext => Context != null;

        /// <summary>Preserves the full source, target geometry and scalar snapshot with consistent event IDs.</summary>
        public GameplayEventData(GameplayTag eventTag, GameplayContext context, float magnitude)
            : this(eventTag, (context ?? throw new ArgumentNullException(nameof(context))).InstigatorId,
                context.TargetData.PrimaryTargetId, magnitude)
        { Context = context; }


        /// <summary>
        /// 创建事件数据。eventTag 必须有效，magnitude 必须为有限值；来源身份允许为 default。
        /// </summary>
        public GameplayEventData(
            GameplayTag eventTag,
            GameplaySourceId instigatorId,
            GameplaySourceId targetId,
            float magnitude)
        {
            if (!eventTag.IsValid)
                throw new ArgumentException("Event tag must be valid.", nameof(eventTag));
            if (float.IsNaN(magnitude) || float.IsInfinity(magnitude))
                throw new ArgumentOutOfRangeException(nameof(magnitude), "Event magnitude must be finite.");

            Context = null;
            EventTag = eventTag;
            InstigatorId = instigatorId;
            TargetId = targetId;
            Magnitude = magnitude;
        }
    }
}
