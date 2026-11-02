using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay
{
    /// <summary>
    /// 由调用者组装的有序计算管线，无内建默认阶段，不执行命中、扣血或死亡处理。
    /// 同步调用；计算期间不得更改阶段列表或 Context 引用的属性。
    /// </summary>
    public sealed class DamagePipeline
    {
        private readonly List<IDamageModifier> _modifiers = new List<IDamageModifier>();

        /// <summary>追加一个非空阶段；添加顺序就是计算顺序，重复添加会执行多次。</summary>
        public void AddModifier(IDamageModifier modifier)
        {
            if (modifier == null) throw new ArgumentNullException(nameof(modifier));
            _modifiers.Add(modifier);
        }

        /// <summary>
        /// 从 BaseDamage 开始依次计算，空管线原样返回。
        /// null Context 抛 ArgumentNullException；阶段输出负数或非有限数时抛 InvalidOperationException，
        /// 停止后续阶段且不产生结果。阶段自身的异常直接传播。
        /// </summary>
        public DamageResult Calculate(DamageContext context)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));

            float damage = context.BaseDamage;
            float absorbed = 0; DamageResultFlags flags = DamageResultFlags.None;
            foreach (IDamageModifier modifier in _modifiers)
            {
                bool stop = false;
                if (modifier is IDamageResultModifier rich)
                {
                    var stage = rich.Calculate(context, damage); damage = stage.Damage;
                    absorbed += stage.Absorbed; flags |= stage.Flags; stop = stage.Stop;
                }
                else damage = modifier.Modify(context, damage);
                if (float.IsNaN(damage) || float.IsInfinity(damage) || damage < 0f)
                    throw new InvalidOperationException("Damage modifier returned an invalid value: " +
                        modifier.GetType().Name);
                if (!float.IsFinite(absorbed)) throw new InvalidOperationException("Absorption must be finite.");
                if (stop) break;
            }

            return new DamageResult(context.BaseDamage, damage, absorbed, flags);
        }
    }
}
