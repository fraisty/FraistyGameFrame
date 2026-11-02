namespace FRFrame.Gameplay
{
    /// <summary>
    /// 一个同步伤害计算阶段；按管线注册顺序接收上一阶段的输出。
    /// 实现只能读取 Context 并返回伤害，不能修改属性、扣血、播放表现或发送 UI/游戏事件。
    /// 管线不负责撤销自定义实现的副作用，调用者必须遵守此计算契约。
    /// </summary>
    public interface IDamageModifier
    {
        /// <summary>
        /// 将 currentDamage 变换为本阶段结果。输入和返回值均应为有限、非负数。
        /// 顺序有语义；应使用 currentDamage 串接计算，而不是重新从 BaseDamage 起算。
        /// </summary>
        float Modify(DamageContext context, float currentDamage);
    }
}
