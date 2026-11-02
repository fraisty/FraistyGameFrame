namespace FRFrame
{
    /// <summary>
    /// 资源条目基类。
    /// AssetEntry 和 SceneEntry 共享公共字段。
    /// </summary>
    public abstract class ResourceEntryBase
    {
        /// <summary>
        /// 资源键。
        /// </summary>
        public ResourceKey Key;

        /// <summary>
        /// 资源地址。
        /// </summary>
        public string Location;

        /// <summary>
        /// 所属包名。
        /// </summary>
        public string PackageName;

        /// <summary>
        /// 当前引用计数。
        /// </summary>
        public int RefCount;

        /// <summary>
        /// 最后使用时间。
        /// 可用于清理策略。
        /// </summary>
        public float LastUseTime;

        /// <summary>
        /// 常驻类型。
        /// </summary>
        public ResourceResidency Residency;

        /// <summary>
        /// 当前状态。
        /// </summary>
        public ResourceEntryState State;

        /// <summary>
        /// 是否已完成加载。
        /// </summary>
        public bool IsLoaded => State == ResourceEntryState.Loaded;

        /// <summary>
        /// 是否是常驻资源。
        /// </summary>
        public bool IsResident => Residency == ResourceResidency.Resident;
    }
}
