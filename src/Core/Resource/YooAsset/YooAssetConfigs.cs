using Cysharp.Threading.Tasks;
using System.Collections.Generic;
using UnityEngine;

namespace FRFrame
{
    /// <summary>
    /// YooAsset 配置访问器。
    /// </summary>
    public static class YooAssetConfigs
    {
        private static YooAssetPackagesConfig _packagesConfig;
        
        /// <summary>
        /// 异步加载 json 配置
        /// </summary>
        public static async UniTask InitializeFromJsonAsync()
        {
            _packagesConfig = await YooAssetPackagesJsonLoader.LoadAsync();
            if (_packagesConfig == null)
            {
                Debug.LogError("YooAsset Packages Config json 加载失败。");
            }
        }
        
        private static void PrepareYooPackagesConfig()
        {
            if (_packagesConfig != null)
                return;

            _packagesConfig = ScriptableObject.CreateInstance<YooAssetPackagesConfig>();
        }

        /// <summary>
        /// 配置对象。
        /// </summary>
        public static YooAssetPackagesConfig Config
        {
            get
            {
                PrepareYooPackagesConfig();
                return _packagesConfig;
            }
        }

        /// <summary>
        /// 主服务器地址。
        /// </summary>
        public static string ResUrl
        {
            get
            {
                PrepareYooPackagesConfig();
                return _packagesConfig != null ? _packagesConfig.hostServerURL : string.Empty;
            }
        }

        /// <summary>
        /// 备用服务器地址。
        /// </summary>
        public static string FallbackResUrl
        {
            get
            {
                PrepareYooPackagesConfig();
                return _packagesConfig != null ? _packagesConfig.fallbackHostServerURL : string.Empty;
            }
        }

        /// <summary>
        /// 包配置列表。
        /// </summary>
        public static List<PackageConfig> PackageConfigs
        {
            get
            {
                PrepareYooPackagesConfig();
                return _packagesConfig != null ? _packagesConfig.packages : null;
            }
        }

        /// <summary>
        /// 最大下载并发数。
        /// </summary>
        public static int DownloadingMaxNum
        {
            get
            {
                PrepareYooPackagesConfig();
                return _packagesConfig != null ? _packagesConfig.downloadingMaxNum : 10;
            }
        }

        /// <summary>
        /// 下载失败重试次数。
        /// </summary>
        public static int FailedTryAgain
        {
            get
            {
                PrepareYooPackagesConfig();
                return _packagesConfig != null ? _packagesConfig.failedTryAgain : 3;
            }
        }

        /// <summary>
        /// 默认包配置。
        /// 优先找 isDefaultPackage=true，否则取第一个。
        /// </summary>
        public static PackageConfig DefaultPackageConfig
        {
            get
            {
                var list = PackageConfigs;
                if (list == null || list.Count == 0)
                    return null;

                for (int i = 0; i < list.Count; i++)
                {
                    if (list[i] != null && list[i].isDefaultPackage)
                        return list[i];
                }

                return list[0];
            }
        }

        public static PackageConfig GetPackageConfig(string name)
        {
            var list = PackageConfigs;
            if (list == null || list.Count == 0)
                return null;

            foreach (PackageConfig config in list)
            {
                if (config.name == name)
                    return config;
            }

            return null;
        }

        public static async UniTask ReloadFromJsonAsync()
        {
            await InitializeFromJsonAsync();
        }

        public static void SaveCurrentJson()
        {
            PrepareYooPackagesConfig();
            if (_packagesConfig == null)
            {
                return;
            }

            YooAssetPackagesJsonLoader.Save(_packagesConfig);
        }

        public static void UpsertPackageConfig(PackageConfig packageConfig)
        {
            if (packageConfig == null || string.IsNullOrEmpty(packageConfig.name))
            {
                return;
            }

            PrepareYooPackagesConfig();
            if (_packagesConfig == null)
            {
                _packagesConfig = ScriptableObject.CreateInstance<YooAssetPackagesConfig>();
            }

            if (_packagesConfig.packages == null)
            {
                _packagesConfig.packages = new List<PackageConfig>();
            }

            int index = _packagesConfig.packages.FindIndex(config => config != null && config.name == packageConfig.name);
            if (index >= 0)
            {
                _packagesConfig.packages[index] = packageConfig;
            }
            else
            {
                _packagesConfig.packages.Add(packageConfig);
            }

            YooAssetPackagesJsonLoader.Save(_packagesConfig);
        }

        /// <summary>
        /// 获取当前平台名。
        /// </summary>
        public static string GetPlatformName()
        {
#if UNITY_ANDROID
            return "Android";
#elif UNITY_IOS
            return "iOS";
#elif UNITY_WEBGL
            return "WebGL";
#elif UNITY_STANDALONE_WIN
            return "Windows";
#elif UNITY_STANDALONE_OSX
            return "MacOS";
#elif UNITY_STANDALONE_LINUX
            return "Linux";
#else
            return "Default";
#endif
        }
    }
}