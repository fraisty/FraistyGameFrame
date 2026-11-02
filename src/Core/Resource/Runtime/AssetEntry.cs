using YooAsset;

namespace FRFrame
{
    /// <summary>
    /// 普通资源条目。
    /// 记录 YooAsset 的 AssetHandle 及资源状态。
    /// </summary>
    public sealed class AssetEntry : ResourceEntryBase
    {
        /// <summary>
        /// YooAsset 资源句柄。
        /// </summary>
        public AssetHandle Handle;

        /// <summary>
        /// 是否允许实例池复用。
        /// 通常 prefab 资源会使用。
        /// </summary>
        public bool AllowGameObjectPool;
    }
}
