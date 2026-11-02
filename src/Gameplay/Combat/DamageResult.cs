namespace FRFrame.Gameplay
{
    /// <summary>
    /// 管线产出的不可变计算结果。FinalDamage 是应造成的伤害，不是实际扣除的生命值。
    /// 真正应用到生命、限制剩余生命和处理死亡属于调用方的后续职责。
    /// </summary>
    public readonly struct DamageResult
    {
        /// <summary>本次 Context 的原始伤害，不受 Modifier 修改。</summary>
        public float BaseDamage { get; }
        /// <summary>按顺序经过全部 Modifier 后的有限、非负伤害；不做取整或最低伤害保底。</summary>
        public float FinalDamage { get; }
        public float AbsorbedDamage { get; }
        public DamageResultFlags Flags { get; }
        public bool IsCritical => (Flags & DamageResultFlags.Critical) != 0;
        public bool IsMiss => (Flags & DamageResultFlags.Miss) != 0;
        public bool IsImmune => (Flags & DamageResultFlags.Immune) != 0;

        internal DamageResult(float baseDamage, float finalDamage)
            : this(baseDamage, finalDamage, 0, DamageResultFlags.None) { }

        internal DamageResult(float baseDamage, float finalDamage, float absorbed, DamageResultFlags flags)
        {
            BaseDamage = baseDamage;
            FinalDamage = finalDamage;
            AbsorbedDamage = absorbed; Flags = flags;
        }
    }
}
