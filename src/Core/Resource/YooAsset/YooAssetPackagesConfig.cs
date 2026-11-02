using System;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame
{
    [CreateAssetMenu(menuName = "FRFrame/YooAssetPackagesCreate", fileName = "FRFMYooAssetPackages")]
    public sealed class YooAssetPackagesConfig : ScriptableObject
    {
        /// <summary>
        /// 主服务器地址。
        /// Host / Web 模式下会使用。
        /// </summary>
        public string hostServerURL = "http://127.0.0.1/CDN/Android/v1.0";

        /// <summary>
        /// 备用服务器地址。
        /// </summary>
        public string fallbackHostServerURL = "http://127.0.0.1/CDN/Android/v1.0";

        /// <summary>
        /// 包列表。
        /// </summary>
        public List<PackageConfig> packages = new List<PackageConfig>
        {
            new PackageConfig
            {
                name = "DefaultPackage",
                playMode = YooAsset.EPlayMode.OfflinePlayMode,
                isDefaultPackage = true,
            }
        };

        /// <summary>
        /// 下载最大并发数。
        /// </summary>
        public int downloadingMaxNum = 10;

        /// <summary>
        /// 下载失败重试次数。
        /// </summary>
        public int failedTryAgain = 3;
    }
}
