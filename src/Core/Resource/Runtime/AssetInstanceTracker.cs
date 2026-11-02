using UnityEngine;

namespace FRFrame
{
    /// <summary>
    /// 挂在实例对象上的追踪器。
    /// 用于回收时反查该实例对应的资源 key。
    /// </summary>
    public sealed class AssetInstanceTracker : MonoBehaviour
    {
        /// <summary>
        /// 对应资源 key。
        /// </summary>
        public ResourceKey ResourceKey;

        /// <summary>
        /// 原始资源地址，仅用于调试。
        /// </summary>
        public string Location;
    }
}
