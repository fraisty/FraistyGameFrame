using System.Collections.Generic;
using System.IO;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;
using SceneHandle = YooAsset.SceneHandle;

namespace FRFrame
{
    /// <summary>
    /// YooAsset 扩展层。
    /// 这次以你旧版逻辑为主：
    /// 1. 初始化 YooAssets
    /// 2. 管理 DefaultPackage
    /// 3. 提供包获取与资源加载统一入口
    /// 4. 为 AssetsProcess / Operations 提供辅助方法
    /// </summary>
    public sealed class YooAssetExtension
    {
        /// <summary>
        /// 默认包。
        /// </summary>
        public ResourcePackage DefaultPackage { get; private set; }

        private readonly Dictionary<string, ResourcePackage> _packages = new Dictionary<string, ResourcePackage>();
        private readonly HashSet<string> _initializedPackages = new HashSet<string>();
        private readonly Dictionary<string, PackageRuntimeInfo> _packageInfos = new Dictionary<string, PackageRuntimeInfo>();

        public async UniTask CreateGamePackageDefault(PackageConfig packageConfig)
        {
            UniTaskCompletionSource<bool> prepaerDone = new UniTaskCompletionSource<bool>();
            AssetsPrepareFsmController fsm = FRFM.Fsm.CreateFsm<AssetsPrepareFsmController>(
                new FsmCreateOption()
                {
                    Owner = this,
                    AutoRemoveWhenTaskDone = true,
                    EnableLog = true,
                });

            fsm.SetData("packageConfig", packageConfig);
            fsm.SetData("prepareDone", prepaerDone);
            fsm.Start();

            bool ok = await prepaerDone.Task;
            if (!ok)
            {
                FRLog.Info("CreateGamePackageDefault " + packageConfig.name + " Failed");
            }

            FRLog.Info("CreateGamePackageDefault " + packageConfig.name + " Finish");
        }
        
        
        /// <summary>
        /// 初始化 YooAssets 框架。
        /// </summary>
        public void InitializeFramework()
        {
            YooAssets.Initialize();
            FRLog.Info("YooAssets.Initialize completed.");
        }
        
        /// <summary>
        /// 获取或创建包。
        /// </summary>
        public ResourcePackage GetOrCreatePackage(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                FRLog.Error("GetOrCreatePackage failed, packageName is null or empty.");
                return null;
            }

            if (_packages.TryGetValue(packageName, out var package))
                return package;

            if (!YooAssets.TryGetPackage(packageName, out package))
                package = YooAssets.CreatePackage(packageName);

            _packages[packageName] = package;
            GetOrCreatePackageInfo(packageName).State = PackageRuntimeState.Created;
            return package;
        }

        /// <summary>
        /// 获取包。
        /// </summary>
        public ResourcePackage GetPackage(string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
                return DefaultPackage;

            if (_packages.TryGetValue(packageName, out ResourcePackage package))
                return package;

            if (YooAssets.TryGetPackage(packageName, out package))
            {
                _packages[packageName] = package;
                GetOrCreatePackageInfo(packageName).State = _initializedPackages.Contains(packageName) ? PackageRuntimeState.Initialized : PackageRuntimeState.Created;
            }

            return package;
        }

        /// <summary>
        /// 设置默认包。
        /// </summary>
        public void SetDefaultPackage(ResourcePackage package)
        {
            if (package == null)
            {
                FRLog.Error("SetDefaultPackage failed, package is null.");
                return;
            }

            DefaultPackage = package;
            foreach (var pair in _packageInfos)
                pair.Value.IsDefaultPackage = false;
            GetOrCreatePackageInfo(package.PackageName).IsDefaultPackage = true;
            FRLog.Info($"Default package set : {package.PackageName}");
        }

        /// <summary>
        /// 根据资源包配置创建初始化参数。
        /// 这部分严格沿用你旧版思路，只做结构整理。
        /// </summary>
        public InitializePackageOptions CreateInitializeParameters(PackageConfig packageConfig)
        {
            if (packageConfig == null)
            {
                FRLog.Error("YooEx, CreateInitializeParameters failed, packageConfig is null.");
                return null;
            }

            EPlayMode playMode = packageConfig.playMode;

            // Older persisted configs may contain EditorSimulateMode. YooAsset 3.x keeps
            // the editor API in player builds as a throwing stub, so never invoke it on device.
            if (!Application.isEditor && playMode == EPlayMode.EditorSimulateMode)
            {
                FRLog.Warning($"Package {packageConfig.name} uses EditorSimulateMode on player; falling back to OfflinePlayMode.");
                playMode = EPlayMode.OfflinePlayMode;
            }

#if UNITY_EDITOR
            if (Application.isEditor &&
                (playMode == EPlayMode.HostPlayMode || playMode == EPlayMode.OfflinePlayMode || playMode == EPlayMode.WebPlayMode))
            {
                playMode = EPlayMode.EditorSimulateMode;
            }
#endif

            switch (playMode)
            {
                case EPlayMode.EditorSimulateMode:
                {
                    PackageBuildResult buildResult = EditorSimulateBuildInvoker.Build(packageConfig.name, (int)EBundleType.VirtualAssetBundle);
                    string packageRoot = buildResult.PackageRootDirectory;
                    EditorSimulateModeOptions editorParams = new EditorSimulateModeOptions
                    {
                        EditorFileSystemParameters = FileSystemParameters.CreateDefaultEditorFileSystemParameters(packageRoot)
                    };
                    return editorParams;
                }

                case EPlayMode.OfflinePlayMode:
                {
                    string packagesRoot = packageConfig.isDefaultPackage ? null :
                        PathExtension.GetPackagesExtensionPath(packageConfig.name);
                    FRLog.Info($"YooAssetExtension package {packageConfig.name} use package root path {packagesRoot}");
                    
                    FileSystemParameters buildinFileSystemParams = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(packagesRoot);
                    
                    OfflinePlayModeOptions offlineParams = new OfflinePlayModeOptions
                    {
                        BuiltinFileSystemParameters = buildinFileSystemParams,
                    };
                    return offlineParams;
                }

                case EPlayMode.HostPlayMode:
                {
                    string defaultHostServer = GetHostServerURL();
                    string fallbackHostServer = GetFallbackHostServerURL();
                    IRemoteService remoteServices = new RemoteServices(defaultHostServer, fallbackHostServer);
                    HostPlayModeOptions hostParams = new HostPlayModeOptions
                    {
                        BuiltinFileSystemParameters = FileSystemParameters.CreateDefaultBuiltinFileSystemParameters(),
                        CacheFileSystemParameters = FileSystemParameters.CreateDefaultSandboxFileSystemParameters(remoteServices)
                    };
                    return hostParams;
                }

                case EPlayMode.WebPlayMode:
                {
#if UNITY_WEBGL && WEIXINMINIGAME && !UNITY_EDITOR
                    string defaultHostServer = GetHostServerURL();
                    string fallbackHostServer = GetFallbackHostServerURL();
                    string packageRoot = $"{WeChatWASM.WX.env.USER_DATA_PATH}/__GAME_FILE_CACHE";
                    IRemoteService remoteServices = new RemoteServices(defaultHostServer, fallbackHostServer);
                    WebPlayModeOptions webParams = new WebPlayModeOptions();
                    webParams.WebNetworkFileSystemParameters = WechatFileSystemCreater.CreateFileSystemParameters(packageRoot, remoteServices);
                    return webParams;
#else
                    WebPlayModeOptions webParams = new WebPlayModeOptions
                    {
                        WebServerFileSystemParameters = FileSystemParameters.CreateDefaultWebServerFileSystemParameters(),
                    };
                    return webParams;
#endif
                }

                default:
                    FRLog.Error($"Unsupported play mode : {playMode}");
                    return null;
            }
        }

        /// <summary>
        /// 初始化包。
        /// </summary>
        public async UniTask<bool> InitializePackageAsync(string packageName, InitializePackageOptions parameters, bool setAsDefault)
        {
            ResourcePackage package = GetOrCreatePackage(packageName);
            if (package == null || parameters == null)
                return false;

            var info = GetOrCreatePackageInfo(packageName);
            if (_initializedPackages.Contains(packageName))
            {
                if (setAsDefault)
                {
                    SetDefaultPackage(package);
                }

                info.State = PackageRuntimeState.Initialized;
                FRLog.Info($"Package already initialized : {packageName}");
                return true;
            }

            info.State = PackageRuntimeState.Initializing;
            info.LastError = null;
            InitializePackageOperation operation = package.InitializePackageAsync(parameters);
            await operation.ToUniTask();

            if (operation.Status != EOperationStatus.Succeeded)
            {
                info.State = PackageRuntimeState.Failed;
                info.LastError = operation.Error;
                FRLog.Error($"Package initialize failed : {packageName}, error : {operation.Error}");
                return false;
            }

            if (setAsDefault)
                SetDefaultPackage(package);

            _initializedPackages.Add(packageName);
            info.State = PackageRuntimeState.Initialized;
            FRLog.Info($"Package initialize success : {packageName}");
            return true;
        }

        /// <summary>
        /// 请求包版本。
        /// 注意：这里按你旧版接口改为 RequestPackageVersionAsync。
        /// </summary>
        public async UniTask<string> RequestPackageVersionAsync(string packageName)
        {
            ResourcePackage package = GetPackage(packageName);
            if (package == null)
            {
                FRLog.Error($"RequestPackageVersionAsync failed, package not found : {packageName}");
                return null;
            }

            RequestPackageVersionOperation operation = package.RequestPackageVersionAsync();
            await operation.ToUniTask();

            if (operation.Status != EOperationStatus.Succeeded)
            {
                FRLog.Error($"RequestPackageVersionAsync failed : {packageName}, error : {operation.Error}");
                return null;
            }

            FRLog.Info($"Request package version success : {packageName}, version : {operation.PackageVersion}");
            GetOrCreatePackageInfo(package.PackageName).Version = operation.PackageVersion;
            return operation.PackageVersion;
        }

        /// <summary>
        /// 更新包清单。
        /// </summary>
        public async UniTask<bool> UpdatePackageManifestAsync(string packageName, string packageVersion)
        {
            ResourcePackage package = GetPackage(packageName);
            if (package == null)
            {
                FRLog.Error($"UpdatePackageManifestAsync failed, package not found : {packageName}");
                return false;
            }

            LoadPackageManifestOperation operation = package.LoadPackageManifestAsync(new LoadPackageManifestOptions(packageVersion, 60));
            await operation.ToUniTask();

            if (operation.Status != EOperationStatus.Succeeded)
            {
                FRLog.Error($"UpdatePackageManifestAsync failed : {packageName}, error : {operation.Error}");
                return false;
            }

            FRLog.Info($"UpdatePackageManifest success : {packageName}, version : {packageVersion}");
            return true;
        }

        /// <summary>
        /// 创建下载器。
        /// </summary>
        public ResourceDownloaderOperation CreateDownloader(string packageName, int downloadingMaxNum, int failedTryAgain)
        {
            ResourcePackage package = GetPackage(packageName);
            if (package == null)
            {
                FRLog.Error($"CreateDownloader failed, package not found : {packageName}");
                return null;
            }

            return package.CreateResourceDownloader(new ResourceDownloaderOptions(downloadingMaxNum, failedTryAgain));
        }

        public AssetHandle LoadAssetAsync<T>(string location, string packageName = null) where T : UnityEngine.Object
        {
            var package = GetPackage(packageName);
            if (package == null)
            {
                FRLog.Error($"LoadAssetAsync<T> failed, package not found : {packageName}, location : {location}");
                return default;
            }

            return package.LoadAssetAsync<T>(location);
        }

        public AssetHandle LoadAssetAsync(string location, string packageName = null)
        {
            var package = GetPackage(packageName);
            if (package == null)
            {
                FRLog.Error($"LoadAssetAsync failed, package not found : {packageName}, location : {location}");
                return default;
            }

            return package.LoadAssetAsync(location);
        }

        public AssetHandle LoadAssetAsync(string location, System.Type assetType, string packageName = null)
        {
            var package = GetPackage(packageName);
            if (package == null)
            {
                FRLog.Error($"LoadAssetAsync(type) failed, package not found : {packageName}, location : {location}");
                return default;
            }

            return package.LoadAssetAsync(location, assetType);
        }

        public EnsureBundleFileOperation EnsureBundleFileAsync(string location, string packageName = null)
        {
            ResourcePackage package = GetPackage(packageName);
            if (package == null)
            {
                FRLog.Error($"EnsureBundleFileAsync failed, package not found : {packageName}, location : {location}");
                return default;
            }

            return package.EnsureBundleFileAsync(new EnsureBundleFileOptions(location));
        }

        /// <summary>
        /// 异步加载场景，activateOnLoad 为 false 时需要后续手动激活。
        /// </summary>
        public SceneHandle LoadSceneAsync(string location, LoadSceneMode loadMode, bool activateOnLoad, string packageName = null)
        {
            ResourcePackage package = GetPackage(packageName);
            if (package == null)
            {
                FRLog.Error($"LoadSceneAsync failed, package not found : {packageName}, location : {location}");
                return default;
            }

            return package.LoadSceneAsync(location, loadMode, allowSceneActivation: activateOnLoad);
        }

        /// <summary>
        /// 清理未使用缓存文件。
        /// </summary>
        public async UniTask ClearUnusedCacheFilesAsync(string packageName)
        {
            var package = GetPackage(packageName);
            if (package == null)
                return;

            var operation = package.ClearCacheAsync(new ClearCacheOptions(ClearCacheMethods.ClearUnusedBundleFiles));
            await operation.ToUniTask();
        }

        public bool IsPackageCreated(string packageName)
        {
            return _packages.ContainsKey(packageName) || YooAssets.TryGetPackage(packageName, out _);
        }

        public bool IsPackageInitialized(string packageName)
        {
            return _initializedPackages.Contains(packageName);
        }

        public bool IsDefaultPackage(string packageName)
        {
            return DefaultPackage != null && DefaultPackage.PackageName == packageName;
        }

        public PackageRuntimeState GetPackageState(string packageName)
        {
            return _packageInfos.TryGetValue(packageName, out var info) ? info.State : PackageRuntimeState.None;
        }

        public PackageRuntimeInfo GetPackageInfo(string packageName)
        {
            return _packageInfos.TryGetValue(packageName, out var info) ? info : null;
        }

        public IReadOnlyList<PackageRuntimeInfo> GetAllPackageInfos()
        {
            return new List<PackageRuntimeInfo>(_packageInfos.Values);
        }

        private PackageRuntimeInfo GetOrCreatePackageInfo(string packageName)
        {
            if (!_packageInfos.TryGetValue(packageName, out var info))
            {
                info = new PackageRuntimeInfo { PackageName = packageName, State = PackageRuntimeState.None };
                _packageInfos.Add(packageName, info);
            }

            return info;
        }

        private string GetHostServerURL()
        {
            return YooAssetConfigs.ResUrl;
        }

        private string GetFallbackHostServerURL()
        {
            return YooAssetConfigs.FallbackResUrl;
        }

        /// <summary>
        /// 远端地址查询服务。
        /// </summary>
        private class RemoteServices : IRemoteService
        {
            private readonly string _defaultHostServer;
            private readonly string _fallbackHostServer;

            public RemoteServices(string defaultHostServer, string fallbackHostServer)
            {
                _defaultHostServer = defaultHostServer;
                _fallbackHostServer = fallbackHostServer;
            }

            public IReadOnlyList<string> GetRemoteUrls(string fileName)
            {
                return new[]
                {
                    $"{_defaultHostServer}/{fileName}",
                    $"{_fallbackHostServer}/{fileName}"
                };
            }
        }
    }
}
