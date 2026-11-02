using System;
using YooAsset;

namespace FRFrame
{
    /// <summary>
    /// 单个资源包配置。
    /// 这次按你旧版设计扩展，直接带 playMode。
    /// </summary>
    [Serializable]
    public sealed class PackageConfig
    {
        /// <summary>
        /// 包名。
        /// </summary>
        public string name = "DefaultPackage";

        /// <summary>
        /// 运行模式。
        /// </summary>
        public EPlayMode playMode = EPlayMode.OfflinePlayMode;

        /// <summary>
        /// 是否默认包。
        /// 如果你配置里第一个就是默认包，也可以不显式勾。
        /// </summary>
        public bool isDefaultPackage = true;
    }
}
