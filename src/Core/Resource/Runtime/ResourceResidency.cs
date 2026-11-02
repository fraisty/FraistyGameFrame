namespace FRFrame
{
    /// <summary>
    /// 资源驻留语义。
    /// </summary>
    public enum ResourceResidency
    {
        /// <summary>
        /// 普通资源。
        /// 由引用计数与清理策略决定释放时机。
        /// </summary>
        Normal = 0,

        /// <summary>
        /// 常驻资源。
        /// 不参与普通清理。
        /// </summary>
        Resident = 1,

        /// <summary>
        /// 短生命周期资源。
        /// 通常使用完即释放，或不进入长缓存。
        /// </summary>
        Transient = 2,
        
        /// <summary>
        /// 场景作用域资源。
        /// 通常在主场景切换后自动清理。
        /// </summary>
        SceneScoped = 3
    }
}
