using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;
using Object = UnityEngine.Object;
using SceneHandle = YooAsset.SceneHandle;

namespace FRFrame
{
    /// <summary>
    /// 资源管理器。
    /// 负责：
    /// 1. Package 初始化与准备流程
    /// 2. 普通资源加载与缓存
    /// 3. 场景加载与记录
    /// 4. GameObject 实例复用与回收
    /// 5. 资源清理与诊断
    /// </summary>
    public sealed class ResourceManager : IManagerBase
    {
        public override int Priority => 10;

        /// <summary>
        /// 资源事件。
        /// </summary>
        public ResourceEvents Events { get; } = new ResourceEvents();

        /// <summary>
        /// YooAsset 适配层。
        /// </summary>
        public YooAssetExtension YooExtension { get; private set; }

        /// <summary>
        /// 资源诊断。
        /// </summary>
        public ResourceDiagnostics Diagnostics { get; private set; }

        /// <summary>
        /// 是否已初始化完成。
        /// </summary>
        private bool _isReady;
        public bool IsReady => _isReady;
        public bool IsProcessLoading => _loadingAssets.Count > 0;

        /// <summary>
        /// 普通资源缓存表。
        /// </summary>
        private readonly Dictionary<ResourceKey, AssetEntry> _assetEntries = new Dictionary<ResourceKey, AssetEntry>();

        /// <summary>
        /// 场景资源缓存表。
        /// </summary>
        private readonly Dictionary<ResourceKey, SceneEntry> _sceneEntries = new Dictionary<ResourceKey, SceneEntry>();

        /// <summary>
        /// 正在加载中的资源 key 集合。
        /// </summary>
        private readonly HashSet<ResourceKey> _loadingAssets = new HashSet<ResourceKey>();

        /// <summary>
        /// 同 key 并发加载合并表。
        /// </summary>
        private readonly Dictionary<ResourceKey, UniTaskCompletionSource<Object>> _assetLoadingTasks =
            new Dictionary<ResourceKey, UniTaskCompletionSource<Object>>();

        private readonly Dictionary<string, ResourceScope> _scopes = new Dictionary<string, ResourceScope>();
        private ResourceCleanupOptions _autoCleanupOptions;
        private float _lastUnloadUnusedAssetsTime = -999f;
        public bool EnableSyncLoadWarning { get; set; } = true;
        public bool AllowSyncLoadMissCache { get; set; } = true;

        /// <summary>
        /// 当前主场景 key。
        /// </summary>
        private ResourceKey? _currentMainSceneKey;

        #region Initialize / Destroy

        public override async UniTask InitializeAsync()
        {
            YooExtension = new YooAssetExtension();
            YooExtension.InitializeFramework();

            Diagnostics = new ResourceDiagnostics(this);

            // 读取默认包配置
            await YooAssetConfigs.InitializeFromJsonAsync();
            PackageConfig defaultPackageConfig = YooAssetConfigs.DefaultPackageConfig;
            if (defaultPackageConfig == null)
            {
                FRLog.Error("ResourceManager initialize failed, default package config is null.");
                return;
            }

            // 依赖 FsmManager。请确保 FsmManager 的 Priority 小于 ResourceManager。
            if (FRFM.Fsm == null)
            {
                FRLog.Error("ResourceManager initialize failed, FsmManager is null.");
                return;
            }

            UniTaskCompletionSource<bool> prepareDone = new UniTaskCompletionSource<bool>();

            AssetsPrepareFsmController fsm = FRFM.Fsm.CreateFsm<AssetsPrepareFsmController>(
                new FsmCreateOption
                {
                    Owner = this,
                    AutoRemoveWhenTaskDone = true,
                    EnableLog = true,
                });

            fsm.SetData("packageConfig", defaultPackageConfig);
            fsm.SetData("prepareDone", prepareDone);
            fsm.Start();

            bool ok = await prepareDone.Task;
            if (!ok)
            {
                FRLog.Error("ResourceManager initialize failed, assets prepare FSM failed.");
                return;
            }

            _isReady = true;
            FRLog.Info("ResourceManager initialized and package prepared.");
        }

        public override void Destroy()
        {
            ClearAllAssets();
            ClearAllScenes();
            ReleaseAllScopes();
            _isReady = false;
            FRLog.Info("ResourceManager destroyed.");
        }

        #endregion

        #region Package

        public bool IsPackageInitialized(string packageName)
        {
            return YooExtension != null && YooExtension.IsPackageInitialized(packageName);
        }

        public bool IsPackageCreated(string packageName)
        {
            return YooExtension != null && YooExtension.IsPackageCreated(packageName);
        }

        public bool IsDefaultPackage(string packageName)
        {
            return YooExtension != null && YooExtension.IsDefaultPackage(packageName);
        }

        public PackageRuntimeState GetPackageState(string packageName)
        {
            return YooExtension != null ? YooExtension.GetPackageState(packageName) : PackageRuntimeState.None;
        }

        public PackageRuntimeInfo GetPackageInfo(string packageName)
        {
            return YooExtension?.GetPackageInfo(packageName);
        }

        public IReadOnlyList<PackageRuntimeInfo> GetAllPackageInfos()
        {
            return YooExtension != null ? YooExtension.GetAllPackageInfos() : new List<PackageRuntimeInfo>();
        }

        public bool CheckLocationValid(ResourceKey key)
        {
            key = MakeKey(key);
            if (YooExtension == null || !YooExtension.IsPackageInitialized(key.PackageName))
                return false;
            ResourcePackage package = YooExtension.GetPackage(key.PackageName);
            return package != null && package.GetAssetInfo(key.Location).IsValid;
        }

        /// <summary>
        /// 初始化指定资源包。
        /// 一般由 FSM 状态内部调用。
        /// </summary>
        public async UniTask<bool> InitializePackageAsync(string packageName, InitializePackageOptions initializeParameters, bool setAsDefault = false)
        {
            if (YooExtension == null)
            {
                FRLog.Error("InitializePackageAsync failed, YooExtension is null.");
                return false;
            }

            return await YooExtension.InitializePackageAsync(packageName, initializeParameters, setAsDefault);
        }

        /// <summary>
        /// 请求包版本。
        /// 注意：当前按你旧工程实际接口使用 RequestPackageVersionAsync。
        /// </summary>
        public UniTask<string> RequestPackageVersionAsync(string packageName)
        {
            return YooExtension.RequestPackageVersionAsync(packageName);
        }

        /// <summary>
        /// 更新包清单。
        /// </summary>
        public UniTask<bool> UpdatePackageManifestAsync(string packageName, string packageVersion)
        {
            return YooExtension.UpdatePackageManifestAsync(packageName, packageVersion);
        }

        /// <summary>
        /// 创建下载器。
        /// </summary>
        public ResourceDownloaderOperation CreateDownloader(string packageName, int downloadingMaxNum = 10, int failedTryAgain = 3)
        {
            return YooExtension.CreateDownloader(packageName, downloadingMaxNum, failedTryAgain);
        }

        public async UniTask<PackageDownloadResult> DownloadPackageAsync(string packageName, PackageDownloadOptions options = null)
        {
            options ??= new PackageDownloadOptions();
            var result = new PackageDownloadResult { PackageName = packageName };
            if (options.CancellationToken.IsCancellationRequested)
            {
                result.Error = "canceled";
                Events.RaisePackageDownloadFinished(result);
                return result;
            }

            ResourceDownloaderOperation downloader = CreateDownloader(packageName, options.DownloadingMaxNum, options.FailedTryAgain);
            if (downloader == null)
            {
                result.Error = "downloader is null";
                Events.RaisePackageDownloadFinished(result);
                return result;
            }

            if (downloader.TotalDownloadCount == 0)
            {
                result.Succeed = true;
                Events.RaisePackageDownloadFinished(result);
                return result;
            }

            downloader.DownloadProgressChanged += data =>
            {
                var progress = new PackageDownloadProgress
                {
                    PackageName = packageName,
                    TotalDownloadCount = data.TotalDownloadCount,
                    CurrentDownloadCount = data.CurrentDownloadCount,
                    TotalDownloadBytes = data.TotalDownloadBytes,
                    CurrentDownloadBytes = data.CurrentDownloadBytes,
                    Progress = data.Progress
                };
                options.ProgressCallback?.Invoke(progress);
                Events.RaisePackageDownloadProgress(progress);
            };
            downloader.DownloadError += data => result.Error = data.ErrorInfo;
            downloader.DownloadCompleted += data => result.Succeed = data.Succeeded;
            downloader.StartDownload();
            await downloader.ToUniTask();

            if (options.CancellationToken.IsCancellationRequested)
            {
                result.Succeed = false;
                result.Error = "canceled";
            }
            else if (downloader.Status != EOperationStatus.Succeeded)
            {
                result.Succeed = false;
                if (string.IsNullOrEmpty(result.Error))
                    result.Error = downloader.Error;
            }
            else
            {
                result.Succeed = true;
                await YooExtension.ClearUnusedCacheFilesAsync(packageName);
            }

            Events.RaisePackageDownloadFinished(result);
            return result;
        }

        #endregion

        #region Asset Load

        public async UniTask<ResourceHandle<T>> LoadAssetHandleAsync<T>(ResourceKey key, ResourceLoadOptions options = null) where T : Object
        {
            key = MakeKey(key);
            T asset = await LoadAssetAsync<T>(key, options);
            return new ResourceHandle<T>(this, key, asset);
        }

        public async UniTask<ResourceHandle<T>> LoadAssetHandleAsync<T>(string location, ResourceLoadOptions options = null) where T : Object
        {
            options ??= ResourceLoadOptions.Default;
            ResourceKey key = MakeKey(location, options.PackageName);
            T asset = await LoadAssetAsync<T>(location, options);
            return new ResourceHandle<T>(this, key, asset);
        }

        public async UniTask<ResourceRawFileHandle> LoadRawFileHandleAsync(ResourceKey key)
        {
            key = MakeKey(key);
            if (string.IsNullOrEmpty(key.Location))
            {
                FRLog.Error("LoadRawFileHandleAsync failed, location is null or empty.");
                return null;
            }

            EnsureBundleFileOperation operation = YooExtension.EnsureBundleFileAsync(key.Location, key.PackageName);
            if (operation == null)
                return null;

            await operation.ToUniTask();
            if (operation.Status != EOperationStatus.Succeeded)
            {
                FRLog.Error($"LoadRawFileHandleAsync failed : {key}, error : {operation.Error}");
                return null;
            }

            string filePath = operation.Detail.BundleFilePath;
            if (string.IsNullOrEmpty(filePath))
            {
                FRLog.Error($"LoadRawFileHandleAsync got empty file path : {key}");
                return null;
            }

            return new ResourceRawFileHandle(key, filePath);
        }

        public UniTask<ResourceRawFileHandle> LoadRawFileHandleAsync(string location, string packageName = null)
        {
            return LoadRawFileHandleAsync(MakeKey(location, packageName));
        }

        public ResourceHandle<T> LoadAssetHandle<T>(ResourceKey key) where T : Object
        {
            key = MakeKey(key);
            T asset = LoadAsset<T>(key);
            return new ResourceHandle<T>(this, key, asset);
        }

        public ResourceHandle<T> LoadAssetHandle<T>(string location, string packageName = null) where T : Object
        {
            ResourceKey key = MakeKey(location, packageName);
            T asset = LoadAsset<T>(location, packageName);
            return new ResourceHandle<T>(this, key, asset);
        }

        /// <summary>
        /// 异步加载强类型资源。
        /// </summary>
        public async UniTask<T> LoadAssetAsync<T>(ResourceKey key, ResourceLoadOptions options = null) where T : Object
        {
            key = MakeKey(key);
            var obj = await LoadAssetInternalAsync(key.Location, typeof(T), WithPackage(options, key));
            return obj as T;
        }

        public async UniTask<T> LoadAssetAsync<T>(string location, ResourceLoadOptions options = null) where T : Object
        {
            var obj = await LoadAssetInternalAsync(location, typeof(T), options);
            return obj as T;
        }

        /// <summary>
        /// 异步加载无类型资源。
        /// </summary>
        public async UniTask<Object> LoadAssetAsync(ResourceKey key, ResourceLoadOptions options = null)
        {
            key = MakeKey(key);
            return await LoadAssetInternalAsync(key.Location, null, WithPackage(options, key));
        }

        public async UniTask<Object> LoadAssetAsync(string location, ResourceLoadOptions options = null)
        {
            return await LoadAssetInternalAsync(location, null, options);
        }

        /// <summary>
        /// 同步加载资源。
        /// 兼容你旧版 ResourceManager 的使用习惯。
        /// </summary>
        public T LoadAsset<T>(ResourceKey key) where T : Object
        {
            return LoadAsset<T>(key.Location, key.PackageName);
        }

        public T LoadAsset<T>(string location, string packageName = null) where T : Object
        {
            var key = MakeKey(location, packageName);

            if (_assetEntries.TryGetValue(key, out var cachedEntry) &&
                cachedEntry.IsLoaded &&
                cachedEntry.Handle.IsValid)
            {
                cachedEntry.RefCount++;
                cachedEntry.LastUseTime = Time.unscaledTime;
                return cachedEntry.Handle.AssetObject as T;
            }

            var package = YooExtension.GetPackage(key.PackageName);
            if (package == null)
            {
                FRLog.Error($"LoadAsset sync failed, package not found : {key.PackageName}");
                return null;
            }

            if (EnableSyncLoadWarning && AllowSyncLoadMissCache)
            {
                FRLog.Warning($"LoadAsset sync miss cache : {key}");
            }

            if (!AllowSyncLoadMissCache)
                return null;

            AssetHandle handle = package.LoadAssetSync<T>(location);
            if (handle.Status != EOperationStatus.Succeeded)
            {
                FRLog.Error($"LoadAsset sync failed : {location}, error : {handle.Error}");
                return null;
            }

            var entry = new AssetEntry
            {
                Key = key,
                Location = location,
                PackageName = key.PackageName,
                RefCount = 1,
                LastUseTime = Time.unscaledTime,
                Residency = ResourceResidency.Normal,
                State = ResourceEntryState.Loaded,
                Handle = handle,
                AllowGameObjectPool = true
            };

            _assetEntries[key] = entry;

            Events.RaiseAssetLoaded(new ResourceEventArgs
            {
                Key = key,
                Location = location,
                PackageName = key.PackageName,
                AssetObject = handle.AssetObject
            });

            return handle.AssetObject as T;
        }

        private async UniTask<Object> LoadAssetInternalAsync(string location, System.Type assetType, ResourceLoadOptions options)
        {
            if (string.IsNullOrEmpty(location))
            {
                FRLog.Error("LoadAssetInternalAsync failed, location is null or empty.");
                return null;
            }

            options ??= ResourceLoadOptions.Default;
            if (options.CancellationToken.IsCancellationRequested)
                return null;

            ResourceKey key = MakeKey(location, options.PackageName);

            // 已缓存并已加载完成
            if (_assetEntries.TryGetValue(key, out var cachedEntry) &&
                cachedEntry.IsLoaded &&
                cachedEntry.Handle.IsValid)
            {
                TouchAssetEntry(cachedEntry, options);
                return cachedEntry.Handle.AssetObject;
            }

            // 同 Key 并发加载合并
            if (_assetLoadingTasks.TryGetValue(key, out var existingTcs))
            {
                Object loadedObj = await existingTcs.Task;
                if (options.CancellationToken.IsCancellationRequested)
                    return null;

                if (_assetEntries.TryGetValue(key, out var existingEntry))
                {
                    TouchAssetEntry(existingEntry, options);
                }

                return loadedObj;
            }

            UniTaskCompletionSource<Object> tcs = new UniTaskCompletionSource<Object>();
            _assetLoadingTasks[key] = tcs;

            AssetEntry entry;
            if (!_assetEntries.TryGetValue(key, out entry))
            {
                entry = new AssetEntry
                {
                    Key = key,
                    Location = location,
                    PackageName = key.PackageName,
                    Residency = options.Residency,
                    AllowGameObjectPool = options.AllowGameObjectPool,
                    RefCount = 0,
                    State = ResourceEntryState.None
                };

                if (options.AllowCache)
                    _assetEntries[key] = entry;
            }

            entry.State = ResourceEntryState.Loading;
            entry.LastUseTime = Time.unscaledTime;
            _loadingAssets.Add(key);

            try
            {
                AssetHandle handle = assetType == null
                    ? YooExtension.LoadAssetAsync(location, key.PackageName)
                    : YooExtension.LoadAssetAsync(location, assetType, key.PackageName);

                entry.Handle = handle;
                await handle.ToUniTask();

                _loadingAssets.Remove(key);

                if (options.CancellationToken.IsCancellationRequested)
                {
                    if (options.ReleaseOnCanceled && handle.IsValid)
                        handle.Release();
                    entry.State = ResourceEntryState.None;
                    _assetEntries.Remove(key);
                    tcs.TrySetResult(null);
                    _assetLoadingTasks.Remove(key);
                    return null;
                }

                if (handle.Status != EOperationStatus.Succeeded)
                {
                    entry.State = ResourceEntryState.Failed;

                    Events.RaiseAssetLoadFailed(new ResourceEventArgs
                    {
                        Key = key,
                        Location = location,
                        PackageName = key.PackageName,
                        Error = handle.Error
                    });

                    tcs.TrySetResult(null);
                    _assetLoadingTasks.Remove(key);

                    FRLog.Error($"LoadAsset failed : {key}, error : {handle.Error}");
                    return null;
                }

                entry.State = ResourceEntryState.Loaded;
                TouchAssetEntry(entry, options);

                Object obj = handle.AssetObject;
                tcs.TrySetResult(obj);
                _assetLoadingTasks.Remove(key);

                Events.RaiseAssetLoaded(new ResourceEventArgs
                {
                    Key = key,
                    Location = location,
                    PackageName = key.PackageName,
                    AssetObject = obj
                });

                return obj;
            }
            catch (System.Exception e)
            {
                _loadingAssets.Remove(key);
                _assetLoadingTasks.Remove(key);
                entry.State = ResourceEntryState.Failed;
                tcs.TrySetException(e);

                Events.RaiseAssetLoadFailed(new ResourceEventArgs
                {
                    Key = key,
                    Location = location,
                    PackageName = key.PackageName,
                    Error = e.Message
                });

                FRLog.Error($"LoadAsset exception : {key}, {e}");
                return null;
            }
        }

        /// <summary>
        /// 预加载资源。
        /// 默认不增加引用计数。
        /// </summary>
        public async UniTask<T> PreloadAssetAsync<T>(ResourceKey key, ResourceLoadOptions options = null) where T : Object
        {
            key = MakeKey(key);
            options ??= new ResourceLoadOptions
            {
                TrackReference = false,
                AllowCache = true,
                Residency = ResourceResidency.Normal
            };

            return await LoadAssetAsync<T>(key, options);
        }

        public async UniTask<T> PreloadAssetAsync<T>(string location, ResourceLoadOptions options = null) where T : Object
        {
            options ??= new ResourceLoadOptions
            {
                TrackReference = false,
                AllowCache = true,
                Residency = ResourceResidency.Normal
            };

            return await LoadAssetAsync<T>(location, options);
        }

        /// <summary>
        /// 尝试获取已缓存资源。
        /// </summary>
        public bool TryGetAsset<T>(ResourceKey key, out T asset) where T : Object
        {
            return TryGetAsset(key.Location, out asset, key.PackageName);
        }

        public bool TryGetAsset<T>(string location, out T asset, string packageName = null) where T : Object
        {
            asset = null;
            var key = MakeKey(location, packageName);

            if (_assetEntries.TryGetValue(key, out var entry) &&
                entry.IsLoaded &&
                entry.Handle.IsValid)
            {
                asset = entry.Handle.AssetObject as T;
                return asset != null;
            }

            return false;
        }

        #endregion

        #region Scope

        public ResourceScope CreateScope(string name, string packageName = null)
        {
            if (string.IsNullOrEmpty(name))
            {
                FRLog.Error("CreateScope failed, name is null or empty.");
                return null;
            }

            if (_scopes.TryGetValue(name, out var scope) && scope.State == ResourceScopeState.Active)
            {
                if (!string.Equals(scope.PackageName, packageName, System.StringComparison.Ordinal))
                    FRLog.Warning($"CreateScope got existing scope with different package. name:{name}, current:{scope.PackageName}, requested:{packageName}");
                return scope;
            }

            scope = new ResourceScope(this, name, packageName);
            _scopes[name] = scope;
            return scope;
        }

        public bool TryGetScope(string name, out ResourceScope scope)
        {
            return _scopes.TryGetValue(name, out scope) && scope.State == ResourceScopeState.Active;
        }

        public bool ReleaseScope(string name)
        {
            if (!_scopes.TryGetValue(name, out var scope))
                return false;

            scope.ReleaseAll();
            _scopes.Remove(name);
            return true;
        }

        public void ReleaseAllScopes()
        {
            foreach (var pair in _scopes)
                pair.Value.ReleaseAll();
            _scopes.Clear();
        }

        #endregion

        #region Instantiate / Recycle

        /// <summary>
        /// 异步实例化 prefab。
        /// 优先尝试从对象池中获取。
        /// </summary>
        public async UniTask<GameObject> InstantiateAsync(ResourceKey key, Transform parent = null, ResourceLoadOptions options = null)
        {
            key = MakeKey(key);
            return await InstantiateAsync(key.Location, parent, WithPackage(options, key));
        }

        public async UniTask<GameObject> InstantiateAsync(string location, Transform parent = null, ResourceLoadOptions options = null)
        {
            options ??= ResourceLoadOptions.Default;
            options.AllowGameObjectPool = true;

            var key = MakeKey(location, options.PackageName);

            // 先从池取
            var pooled = FRFM.Pool?.GameObjectPool?.TryGet(key);
            if (pooled != null)
            {
                if (parent != null)
                    pooled.transform.SetParent(parent, false);

                var tracker = pooled.GetComponent<AssetInstanceTracker>();
                if (tracker == null)
                    tracker = pooled.AddComponent<AssetInstanceTracker>();

                tracker.ResourceKey = key;
                tracker.Location = location;

                if (_assetEntries.TryGetValue(key, out var pooledEntry))
                {
                    pooledEntry.RefCount++;
                    pooledEntry.LastUseTime = Time.unscaledTime;
                }

                return pooled;
            }

            // 池里没有，先加载 prefab
            GameObject prefab = await LoadAssetAsync<GameObject>(location, options);
            if (prefab == null)
                return null;

            if (options.CancellationToken.IsCancellationRequested)
            {
                ReleaseAssetReference(key);
                return null;
            }

            GameObject instance = Object.Instantiate(prefab, parent);
            instance.name = prefab.name;

            AssetInstanceTracker instanceTracker = instance.GetComponent<AssetInstanceTracker>();
            if (instanceTracker == null)
                instanceTracker = instance.AddComponent<AssetInstanceTracker>();

            instanceTracker.ResourceKey = key;
            instanceTracker.Location = location;

            Events.RaisePoolObjectCreated(new ResourceEventArgs
            {
                Key = key,
                Location = location,
                PackageName = key.PackageName,
                Instance = instance
            });

            return instance;
        }

        /// <summary>
        /// 预热 GameObject 池。
        /// </summary>
        public async UniTask WarmupGameObjectAsync(ResourceKey key, int count, ResourceLoadOptions options = null)
        {
            key = MakeKey(key);
            await WarmupGameObjectAsync(key.Location, count, WithPackage(options, key));
        }

        public async UniTask WarmupGameObjectAsync(string location, int count, ResourceLoadOptions options = null)
        {
            if (count <= 0)
                return;

            options ??= ResourceLoadOptions.Default;
            options.AllowGameObjectPool = true;

            var instances = new List<GameObject>(count);

            for (int i = 0; i < count; i++)
            {
                if (options.CancellationToken.IsCancellationRequested)
                    break;

                var instance = await InstantiateAsync(location, null, options);
                if (instance != null)
                    instances.Add(instance);
            }

            for (int i = 0; i < instances.Count; i++)
            {
                RecycleInstance(instances[i]);
            }

            FRLog.Info($"WarmupGameObjectAsync finished : {location}, count : {count}");
        }

        /// <summary>
        /// 回收实例到池。
        /// </summary>
        public bool RecycleInstance(GameObject instance)
        {
            if (instance == null)
                return false;

            var tracker = instance.GetComponent<AssetInstanceTracker>();
            if (tracker == null)
            {
                FRLog.Warning($"RecycleInstance failed, tracker not found : {instance.name}");
                Object.Destroy(instance);
                return false;
            }

            var key = tracker.ResourceKey;
            if (!_assetEntries.TryGetValue(key, out var entry))
            {
                FRLog.Warning($"RecycleInstance failed, entry not found : {key}");
                Object.Destroy(instance);
                return false;
            }

            entry.RefCount = Mathf.Max(0, entry.RefCount - 1);
            entry.LastUseTime = Time.unscaledTime;

            FRFM.Pool?.GameObjectPool?.Release(key, tracker.Location, instance);

            Events.RaiseInstanceRecycled(new ResourceEventArgs
            {
                Key = key,
                Location = tracker.Location,
                PackageName = key.PackageName,
                Instance = instance
            });

            Events.RaisePoolObjectReleased(new ResourceEventArgs
            {
                Key = key,
                Location = tracker.Location,
                PackageName = key.PackageName,
                Instance = instance
            });

            return true;
        }

        /// <summary>
        /// 兼容旧版接口命名。
        /// </summary>
        public void Recycle(GameObject instanceObject, bool forceDestroy = false)
        {
            if (instanceObject == null)
                return;

            if (forceDestroy)
            {
                DestroyInstance(instanceObject);
                return;
            }

            RecycleInstance(instanceObject);
        }

        /// <summary>
        /// 销毁实例，不回池。
        /// </summary>
        public bool DestroyInstance(GameObject instance)
        {
            if (instance == null)
                return false;

            var tracker = instance.GetComponent<AssetInstanceTracker>();
            if (tracker != null && _assetEntries.TryGetValue(tracker.ResourceKey, out var entry))
            {
                entry.RefCount = Mathf.Max(0, entry.RefCount - 1);
                entry.LastUseTime = Time.unscaledTime;
            }

            Object.Destroy(instance);
            return true;
        }

        #endregion

        #region Scene

        /// <summary>
        /// 异步加载场景。
        /// </summary>
        public async UniTask<SceneHandle> LoadSceneAsync(ResourceKey key, SceneLoadOptions options = null)
        {
            key = MakeKey(key);
            return await LoadSceneAsync(key.Location, WithPackage(options, key));
        }

        public async UniTask<SceneHandle> LoadSceneAsync(string location, SceneLoadOptions options = null)
        {
            options ??= new SceneLoadOptions();
            if (options.CancellationToken.IsCancellationRequested)
                return default;

            ResourceKey key = MakeKey(location, options.PackageName);

            if (_sceneEntries.TryGetValue(key, out SceneEntry existing) &&
                existing.IsLoaded &&
                existing.Handle.IsValid)
            {
                existing.RefCount++;
                existing.LastUseTime = Time.unscaledTime;
                return existing.Handle;
            }

            SceneEntry entry = new SceneEntry
            {
                Key = key,
                Location = location,
                PackageName = key.PackageName,
                Residency = options.Residency,
                RefCount = 1,
                State = ResourceEntryState.Loading,
                IsMainScene = options.IsMainScene
            };

            _sceneEntries[key] = entry;

            SceneHandle handle = YooExtension.LoadSceneAsync(location, options.LoadSceneMode, options.ActivateOnLoad, key.PackageName);
            entry.Handle = handle;
            
            await handle.ToUniTask();

            if (options.CancellationToken.IsCancellationRequested)
            {
                entry.State = ResourceEntryState.Failed;
                FRLog.Warning($"LoadSceneAsync canceled : {key}");
                return default;
            }

            if (handle.Status != EOperationStatus.Succeeded)
            {
                entry.State = ResourceEntryState.Failed;

                Events.RaiseAssetLoadFailed(new ResourceEventArgs
                {
                    Key = key,
                    Location = location,
                    PackageName = key.PackageName,
                    Error = handle.Error
                });

                FRLog.Error($"LoadSceneAsync failed : {key}, error : {handle.Error}");
                return default;
            }

            entry.State = ResourceEntryState.Loaded;
            entry.LastUseTime = Time.unscaledTime;

            // Single 模式视为主场景切换
            if (options.LoadSceneMode == LoadSceneMode.Single)
            {
                _currentMainSceneKey = key;

                if (options.CleanupSceneScopedAssetsAfterLoad)
                    CleanupSceneScopedAssets();

                if (options.CleanupNormalAssetsAfterLoad)
                    CleanupUnusedNormalAssets();
            }

            Events.RaiseSceneLoaded(new ResourceEventArgs
            {
                Key = key,
                Location = location,
                PackageName = key.PackageName
            });

            FRLog.Info($"Scene loaded : {key}");
            return handle;
        }

        /// <summary>
        /// 卸载场景。
        /// </summary>
        public async UniTask<bool> UnloadSceneAsync(ResourceKey key)
        {
            return await UnloadSceneAsync(key.Location, key.PackageName);
        }

        public async UniTask<bool> UnloadSceneAsync(string location, string packageName = null)
        {
            var key = MakeKey(location, packageName);

            if (!_sceneEntries.TryGetValue(key, out var entry))
            {
                FRLog.Warning($"UnloadSceneAsync ignored, scene entry not found : {key}");
                return false;
            }

            if (!entry.Handle.IsValid)
            {
                _sceneEntries.Remove(key);
                return false;
            }

            entry.State = ResourceEntryState.Unloading;

            var operation = entry.Handle.UnloadSceneAsync();
            await operation.ToUniTask();

            entry.RefCount = 0;
            entry.State = ResourceEntryState.None;

            if (_currentMainSceneKey.HasValue && _currentMainSceneKey.Value.Equals(key))
            {
                _currentMainSceneKey = null;
            }

            _sceneEntries.Remove(key);

            Events.RaiseSceneUnloaded(new ResourceEventArgs
            {
                Key = key,
                Location = location,
                PackageName = key.PackageName
            });

            FRLog.Info($"Scene unloaded : {key}");
            return true;
        }

        /// <summary>
        /// 判断场景是否已加载。
        /// </summary>
        public bool IsSceneLoaded(ResourceKey key)
        {
            return IsSceneLoaded(key.Location, key.PackageName);
        }

        public bool IsSceneLoaded(string location, string packageName = null)
        {
            var key = MakeKey(location, packageName);
            return _sceneEntries.TryGetValue(key, out var entry) &&
                   entry.IsLoaded &&
                   entry.Handle.IsValid;
        }

        #endregion

        #region Release / Cleanup

        /// <summary>
        /// 手动减少资源引用。
        /// 常用于非实例资源。
        /// </summary>
        public bool ReleaseAssetReference(ResourceKey key)
        {
            return ReleaseAssetReference(key.Location, key.PackageName);
        }

        public bool ReleaseAssetReference(string location, string packageName = null)
        {
            var key = MakeKey(location, packageName);
            if (!_assetEntries.TryGetValue(key, out var entry))
                return false;

            entry.RefCount = Mathf.Max(0, entry.RefCount - 1);
            entry.LastUseTime = Time.unscaledTime;
            return true;
        }

        /// <summary>
        /// 兼容旧版命名。
        /// </summary>
        public void ReleaseReference(ResourceKey key)
        {
            ReleaseReference(key.Location, key.PackageName);
        }

        public void ReleaseReference(string location)
        {
            ReleaseReference(location, null);
        }

        public void ReleaseReference(string location, string packageName)
        {
            var key = MakeKey(location, packageName);
            if (_assetEntries.TryGetValue(key, out var entry))
            {
                entry.RefCount = Mathf.Max(0, entry.RefCount - 1);
                entry.LastUseTime = Time.unscaledTime;

                if (entry.RefCount <= 0 && entry.Residency != ResourceResidency.Resident)
                {
                    UnloadAsset(location, key.PackageName);
                }
            }
        }

        /// <summary>
        /// 卸载指定资源。
        /// </summary>
        public bool UnloadAsset(ResourceKey key, bool clearGameObjectPool = true)
        {
            return UnloadAsset(key.Location, key.PackageName, clearGameObjectPool);
        }

        public bool UnloadAsset(string location, string packageName = null, bool clearGameObjectPool = true)
        {
            var key = MakeKey(location, packageName);
            if (!_assetEntries.TryGetValue(key, out var entry))
                return false;

            if (entry.State == ResourceEntryState.Loading || _loadingAssets.Contains(key))
            {
                FRLog.Warning($"UnloadAsset ignored, asset is loading : {key}");
                return false;
            }

            if (entry.IsResident)
            {
                FRLog.Warning($"UnloadAsset ignored, asset is resident : {key}");
                return false;
            }

            if (entry.RefCount > 0)
            {
                FRLog.Warning($"UnloadAsset ignored, asset is still referenced : {key}, refCount : {entry.RefCount}");
                return false;
            }

            if (clearGameObjectPool)
            {
                FRFM.Pool?.GameObjectPool?.Clear(key);
            }

            if (entry.Handle.IsValid)
            {
                entry.Handle.Release();
            }

            _assetEntries.Remove(key);

            Events.RaiseAssetReleased(new ResourceEventArgs
            {
                Key = key,
                Location = location,
                PackageName = key.PackageName
            });

            FRLog.Info($"Asset unloaded : {key}");
            return true;
        }

        /// <summary>
        /// 兼容旧版命名。
        /// </summary>
        public void UnLoadAsset(ResourceKey key)
        {
            UnloadAsset(key);
        }

        public void UnLoadAsset(string location)
        {
            UnloadAsset(location);
        }

        /// <summary>
        /// 添加常驻资源。
        /// 如果资源已加载则直接修改条目驻留语义；
        /// 如果未加载，则在下次加载时应通过 options 指定 Resident 更稳妥。
        /// </summary>
        public void AddResidentAsset(ResourceKey key)
        {
            AddResidentAsset(key.Location, key.PackageName);
        }

        public void AddResidentAsset(string location, string packageName = null)
        {
            var key = MakeKey(location, packageName);
            if (_assetEntries.TryGetValue(key, out var entry))
            {
                entry.Residency = ResourceResidency.Resident;
                FRLog.Info($"Asset set to resident : {key}");
            }
            else
            {
                FRLog.Warning($"AddResidentAsset ignored, asset not loaded yet : {key}");
            }
        }

        public void CleanupPackageAssets(string packageName, bool includeResident = false)
        {
            var pendingRemove = new List<ResourceKey>();

            foreach (var pair in _assetEntries)
            {
                var entry = pair.Value;
                if (entry.PackageName != packageName)
                    continue;

                if (entry.State == ResourceEntryState.Loading || _loadingAssets.Contains(entry.Key))
                    continue;

                if (!includeResident && entry.IsResident)
                    continue;

                if (entry.RefCount > 0)
                    continue;

                FRFM.Pool?.GameObjectPool?.Clear(entry.Key);

                if (entry.Handle.IsValid)
                {
                    entry.Handle.Release();
                }

                pendingRemove.Add(entry.Key);

                Events.RaiseAssetReleased(new ResourceEventArgs
                {
                    Key = entry.Key,
                    Location = entry.Location,
                    PackageName = entry.PackageName
                });
            }

            for (int i = 0; i < pendingRemove.Count; i++)
            {
                _assetEntries.Remove(pendingRemove[i]);
            }

            Resources.UnloadUnusedAssets();
            FRLog.Info($"CleanupPackageAssets finished : {packageName}, removed count : {pendingRemove.Count}");
        }

        public void ClearPackageGameObjectPool(string packageName)
        {
            FRFM.Pool?.GameObjectPool?.ClearPackage(packageName);
        }

        /// <summary>
        /// 清空指定资源键对应的实例池桶。
        /// </summary>
        public int ClearGameObjectPool(ResourceKey key)
        {
            return FRFM.Pool?.GameObjectPool?.Clear(MakeKey(key)) ?? 0;
        }

        public ResourceCleanupResult CleanupAssets(ResourceCleanupOptions options)
        {
            options ??= new ResourceCleanupOptions();
            var result = new ResourceCleanupResult();
            var pendingRemove = new List<ResourceKey>();
            float now = Time.unscaledTime;

            foreach (var pair in _assetEntries)
            {
                var entry = pair.Value;
                if (!string.IsNullOrEmpty(options.PackageName) && entry.PackageName != options.PackageName)
                    continue;
                if (entry.State == ResourceEntryState.Loading || _loadingAssets.Contains(entry.Key))
                    continue;
                if (options.Residency.HasValue && entry.Residency != options.Residency.Value)
                    continue;
                if (!options.IncludeResident && entry.IsResident)
                    continue;
                if (entry.RefCount > 0)
                    continue;
                if (options.MinUnusedSeconds > 0f && now - entry.LastUseTime < options.MinUnusedSeconds)
                    continue;

                pendingRemove.Add(entry.Key);
            }

            pendingRemove.Sort((a, b) => _assetEntries[a].LastUseTime.CompareTo(_assetEntries[b].LastUseTime));
            int removeCount = options.MaxRemoveCount > 0
                ? Mathf.Min(options.MaxRemoveCount, pendingRemove.Count)
                : pendingRemove.Count;
            for (int i = 0; i < removeCount; i++)
            {
                if (!_assetEntries.TryGetValue(pendingRemove[i], out var entry))
                    continue;
                result.ClearedPoolBucketCount += FRFM.Pool?.GameObjectPool?.ContainsBucket(entry.Key) == true ? 1 : 0;
                FRFM.Pool?.GameObjectPool?.Clear(entry.Key);
                if (entry.Handle.IsValid)
                    entry.Handle.Release();
                _assetEntries.Remove(entry.Key);
                result.RemovedAssetCount++;
                Events.RaiseAssetReleased(new ResourceEventArgs { Key = entry.Key, Location = entry.Location, PackageName = entry.PackageName });
            }

            if (options.UnloadUnusedAssets && now - _lastUnloadUnusedAssetsTime >= options.UnloadUnusedAssetsMinInterval)
            {
                Resources.UnloadUnusedAssets();
                _lastUnloadUnusedAssetsTime = now;
                result.CalledUnloadUnusedAssets = true;
            }

            return result;
        }

        public ResourceCleanupResult CleanupLeastRecentlyUsed(int maxRemoveCount, string packageName = null)
        {
            return CleanupAssets(new ResourceCleanupOptions { PackageName = packageName, MaxRemoveCount = maxRemoveCount });
        }

        public ResourceCleanupResult CleanupUnusedOlderThan(float seconds, string packageName = null)
        {
            return CleanupAssets(new ResourceCleanupOptions { PackageName = packageName, MinUnusedSeconds = seconds });
        }

        public void SetAutoCleanupOptions(ResourceCleanupOptions options)
        {
            _autoCleanupOptions = options;
        }

        /// <summary>
        /// 清理未被引用的普通资源。
        /// 不清理 Resident。
        /// </summary>
        public void CleanupUnusedNormalAssets()
        {
            var pendingRemove = new List<ResourceKey>();

            foreach (var pair in _assetEntries)
            {
                var entry = pair.Value;

                if (entry.State != ResourceEntryState.Loaded)
                    continue;

                if (entry.Residency != ResourceResidency.Normal &&
                    entry.Residency != ResourceResidency.Transient)
                    continue;

                if (entry.RefCount > 0)
                    continue;

                FRFM.Pool?.GameObjectPool?.Clear(entry.Key);

                if (entry.Handle.IsValid)
                {
                    entry.Handle.Release();
                }

                pendingRemove.Add(entry.Key);

                Events.RaiseAssetReleased(new ResourceEventArgs
                {
                    Key = entry.Key,
                    Location = entry.Location,
                    PackageName = entry.PackageName
                });
            }

            for (int i = 0; i < pendingRemove.Count; i++)
            {
                _assetEntries.Remove(pendingRemove[i]);
            }

            Resources.UnloadUnusedAssets();
            FRLog.Info($"CleanupUnusedNormalAssets finished, removed count : {pendingRemove.Count}");
        }

        /// <summary>
        /// 清理场景作用域资源。
        /// 主场景切换时调用。
        /// </summary>
        public void CleanupSceneScopedAssets()
        {
            var pendingRemove = new List<ResourceKey>();

            foreach (var pair in _assetEntries)
            {
                var entry = pair.Value;

                if (entry.State != ResourceEntryState.Loaded)
                    continue;

                if (entry.Residency != ResourceResidency.SceneScoped)
                    continue;

                if (entry.RefCount > 0)
                    continue;

                FRFM.Pool?.GameObjectPool?.Clear(entry.Key);

                if (entry.Handle.IsValid)
                {
                    entry.Handle.Release();
                }

                pendingRemove.Add(entry.Key);

                Events.RaiseAssetReleased(new ResourceEventArgs
                {
                    Key = entry.Key,
                    Location = entry.Location,
                    PackageName = entry.PackageName
                });
            }

            for (int i = 0; i < pendingRemove.Count; i++)
            {
                _assetEntries.Remove(pendingRemove[i]);
            }

            FRLog.Info($"CleanupSceneScopedAssets finished, removed count : {pendingRemove.Count}");
        }

        /// <summary>
        /// 异步等待不在加载中，然后执行清理。
        /// 兼容旧版 cleanUpAsync。
        /// </summary>
        public async UniTask CleanUpAsync()
        {
            await UniTask.WaitUntil(() => _loadingAssets.Count <= 0);
            CleanUp();
        }

        /// <summary>
        /// 兼容旧版命名。
        /// </summary>
        public async UniTask cleanUpAsync()
        {
            await CleanUpAsync();
        }

        /// <summary>
        /// 清理全部非常驻普通资源。
        /// 兼容旧版 CleanUp 行为。
        /// </summary>
        public void CleanUp()
        {
            CleanupUnusedNormalAssets();
        }

        /// <summary>
        /// 清理全部非 Resident 资源。
        /// </summary>
        public void ClearAllAssets()
        {
            var keys = new List<ResourceKey>(_assetEntries.Keys);

            for (int i = 0; i < keys.Count; i++)
            {
                var key = keys[i];
                if (!_assetEntries.TryGetValue(key, out var entry))
                    continue;

                if (entry.IsResident)
                    continue;

                FRFM.Pool?.GameObjectPool?.Clear(key);

                if (entry.Handle.IsValid)
                {
                    entry.Handle.Release();
                }

                Events.RaiseAssetReleased(new ResourceEventArgs
                {
                    Key = key,
                    Location = entry.Location,
                    PackageName = entry.PackageName
                });

                _assetEntries.Remove(key);
            }

            Resources.UnloadUnusedAssets();
        }

        /// <summary>
        /// 清理全部场景记录。
        /// </summary>
        public void ClearAllScenes()
        {
            _sceneEntries.Clear();
            _currentMainSceneKey = null;
        }

        #endregion

        #region Diagnostics

        /// <summary>
        /// 当前资源统计。
        /// </summary>
        public ResourceStatistics GetStatistics()
        {
            return new ResourceStatistics
            {
                LoadedAssetCount = _assetEntries.Count,
                LoadedSceneCount = _sceneEntries.Count,
                LoadingAssetCount = _loadingAssets.Count,
                ReferencePoolCachedCount = FRFM.Pool?.ReferencePool?.GetTotalCachedCount() ?? 0,
                ObjectPoolCount = FRFM.Pool?.ObjectPool?.GetPoolCount() ?? 0,
                GameObjectPoolBucketCount = FRFM.Pool?.GameObjectPool?.GetBucketCount() ?? 0,
                GameObjectPoolInactiveCount = FRFM.Pool?.GameObjectPool?.GetTotalInactiveCount() ?? 0
            };
        }

        public AssetEntry GetAssetEntry(ResourceKey key)
        {
            _assetEntries.TryGetValue(key, out var entry);
            return entry;
        }

        public AssetEntry GetAssetEntry(string location, string packageName = null)
        {
            var key = MakeKey(location, packageName);
            _assetEntries.TryGetValue(key, out var entry);
            return entry;
        }

        public SceneEntry GetSceneEntry(ResourceKey key)
        {
            _sceneEntries.TryGetValue(key, out var entry);
            return entry;
        }

        public SceneEntry GetSceneEntry(string location, string packageName = null)
        {
            var key = MakeKey(location, packageName);
            _sceneEntries.TryGetValue(key, out var entry);
            return entry;
        }

        public int GetLoadedAssetCount(string packageName)
        {
            int count = 0;
            foreach (var pair in _assetEntries)
            {
                if (pair.Key.PackageName == packageName)
                    count++;
            }

            return count;
        }

        public int GetLoadedSceneCount(string packageName)
        {
            int count = 0;
            foreach (var pair in _sceneEntries)
            {
                if (pair.Key.PackageName == packageName)
                    count++;
            }

            return count;
        }

        public bool IsAssetLoaded(ResourceKey key)
        {
            return IsAssetLoaded(key.Location, key.PackageName);
        }

        public bool IsAssetLoaded(string location, string packageName = null)
        {
            var key = MakeKey(location, packageName);
            return _assetEntries.TryGetValue(key, out var entry) &&
                   entry.IsLoaded &&
                   entry.Handle.IsValid;
        }

        internal IReadOnlyList<AssetEntrySnapshot> GetAssetSnapshots(string packageName = null)
        {
            var snapshots = new List<AssetEntrySnapshot>();
            foreach (var pair in _assetEntries)
            {
                var entry = pair.Value;
                if (!string.IsNullOrEmpty(packageName) && entry.PackageName != packageName)
                    continue;
                snapshots.Add(new AssetEntrySnapshot
                {
                    Key = entry.Key,
                    PackageName = entry.PackageName,
                    Location = entry.Location,
                    RefCount = entry.RefCount,
                    Residency = entry.Residency,
                    State = entry.State,
                    LastUseTime = entry.LastUseTime,
                    HandleValid = entry.Handle.IsValid,
                    IsResident = entry.IsResident
                });
            }
            return snapshots;
        }

        internal IReadOnlyList<SceneEntrySnapshot> GetSceneSnapshots(string packageName = null)
        {
            var snapshots = new List<SceneEntrySnapshot>();
            foreach (var pair in _sceneEntries)
            {
                var entry = pair.Value;
                if (!string.IsNullOrEmpty(packageName) && entry.PackageName != packageName)
                    continue;
                snapshots.Add(new SceneEntrySnapshot
                {
                    Key = entry.Key,
                    PackageName = entry.PackageName,
                    Location = entry.Location,
                    RefCount = entry.RefCount,
                    Residency = entry.Residency,
                    State = entry.State,
                    LastUseTime = entry.LastUseTime,
                    HandleValid = entry.Handle.IsValid,
                    IsMainScene = entry.IsMainScene
                });
            }
            return snapshots;
        }

        internal IReadOnlyList<PackageResourceSnapshot> GetPackageResourceSnapshots()
        {
            var map = new Dictionary<string, PackageResourceSnapshot>();
            foreach (var pair in _assetEntries)
            {
                if (!map.TryGetValue(pair.Key.PackageName, out var snapshot))
                {
                    snapshot = new PackageResourceSnapshot { PackageName = pair.Key.PackageName };
                    map.Add(pair.Key.PackageName, snapshot);
                }
                snapshot.AssetCount++;
            }
            foreach (var pair in _sceneEntries)
            {
                if (!map.TryGetValue(pair.Key.PackageName, out var snapshot))
                {
                    snapshot = new PackageResourceSnapshot { PackageName = pair.Key.PackageName };
                    map.Add(pair.Key.PackageName, snapshot);
                }
                snapshot.SceneCount++;
            }
            foreach (var key in _loadingAssets)
            {
                if (!map.TryGetValue(key.PackageName, out var snapshot))
                {
                    snapshot = new PackageResourceSnapshot { PackageName = key.PackageName };
                    map.Add(key.PackageName, snapshot);
                }
                snapshot.LoadingAssetCount++;
            }
            return new List<PackageResourceSnapshot>(map.Values);
        }

        internal ResourceKey NormalizeKey(ResourceKey key)
        {
            return MakeKey(key);
        }

        #endregion

        #region Helpers

        private ResourceKey MakeKey(ResourceKey key)
        {
            return MakeKey(key.Location, key.PackageName);
        }

        private ResourceKey MakeKey(string location, string packageName)
        {
            if (string.IsNullOrEmpty(packageName))
            {
                var defaultPackage = YooExtension != null && YooExtension.DefaultPackage != null
                    ? YooExtension.DefaultPackage.PackageName
                    : string.Empty;

                packageName = defaultPackage;

                if (string.IsNullOrEmpty(packageName))
                {
                    FRLog.Warning($"MakeKey got empty package name. location : {location}");
                }
            }

            return new ResourceKey(packageName, location);
        }

        private ResourceLoadOptions WithPackage(ResourceLoadOptions options, ResourceKey key)
        {
            options ??= ResourceLoadOptions.Default;
            options.PackageName = key.PackageName;
            return options;
        }

        private SceneLoadOptions WithPackage(SceneLoadOptions options, ResourceKey key)
        {
            options ??= new SceneLoadOptions();
            options.PackageName = key.PackageName;
            return options;
        }

        private void TouchAssetEntry(AssetEntry entry, ResourceLoadOptions options)
        {
            entry.LastUseTime = Time.unscaledTime;
            entry.Residency = options.Residency;
            entry.AllowGameObjectPool = options.AllowGameObjectPool;

            if (options.TrackReference)
            {
                entry.RefCount++;
            }
        }

        #endregion
    }
}
