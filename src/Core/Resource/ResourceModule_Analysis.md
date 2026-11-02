# Resource 模块新功能详细设计

## 设计边界

`ResourceManager` 仍保持当前职责边界：框架初始化阶段只初始化 `DefaultPackage`，其它游戏 package 由对应业务初始化流程自行调用单包初始化、版本更新、清单更新和下载接口。

本设计不改变默认初始化策略，重点补强运行期资源生命周期、多 package API、诊断、清理策略和下载流程封装。

## 当前基础

当前 Resource 模块已经具备以下基础能力：

- 基于 YooAsset 的默认包初始化流程。
- 单 package 初始化、版本请求、清单更新、下载器创建入口。
- `ResourceKey(PackageName, Location)` 作为资源唯一标识。
- 同步加载、异步加载、预加载、实例化、场景加载。
- 资源缓存、引用计数、驻留类型、同 key 并发加载合并。
- 与 `GameObjectPool` 联动的 prefab 实例回收。
- 资源事件、基础统计、基础诊断。

后续新增功能应尽量围绕这些已有结构扩展，不重写 YooAsset 适配层，不让 Pool 模块直接接管资源句柄生命周期。

## 功能一：资源句柄 ResourceHandle

### 目标

解决“加载返回资源对象后，调用方忘记释放或释放责任不清晰”的问题。

### 新增类型

建议新增文件：

- `Assets/FRFrame/Core/Resource/Runtime/ResourceHandle.cs`
- `Assets/FRFrame/Core/Resource/Runtime/ResourceHandleState.cs`

```csharp
public enum ResourceHandleState
{
    None,
    Valid,
    Released
}

public readonly struct ResourceHandle<T> : IDisposable where T : UnityEngine.Object
{
    public ResourceKey Key { get; }
    public T Asset { get; }
    public bool IsValid { get; }
    public ResourceHandleState State { get; }
    public void Release();
    public void Dispose();
}
```

`ResourceHandle<T>` 不直接持有 YooAsset 的 `AssetHandle`，而是持有 `ResourceKey` 和资源对象，通过 `ResourceManager.ReleaseAssetReference(Key)` 释放引用。这样可以保持 YooAsset handle 仍由 `AssetEntry` 统一管理。

### ResourceManager 新增 API

```csharp
public async UniTask<ResourceHandle<T>> LoadAssetHandleAsync<T>(ResourceKey key, ResourceLoadOptions options = null) where T : Object;
public async UniTask<ResourceHandle<T>> LoadAssetHandleAsync<T>(string location, ResourceLoadOptions options = null) where T : Object;
public ResourceHandle<T> LoadAssetHandle<T>(ResourceKey key) where T : Object;
public ResourceHandle<T> LoadAssetHandle<T>(string location, string packageName = null) where T : Object;
```

### 生命周期规则

- 加载成功后，引用计数与现有 `LoadAssetAsync` 一致增加。
- `Release` 只减少引用计数，不强制卸载资源。
- 多次 `Release` 应只生效一次，重复释放输出 warning。
- 如果资源加载失败，返回 invalid handle。
- `Dispose` 等价于 `Release`。

### 与旧 API 的关系

旧的 `LoadAssetAsync<T>` 保留，用于简单场景。新业务推荐使用 `LoadAssetHandleAsync<T>`，特别是 UI、战斗、剧情、关卡等生命周期明确的系统。

## 功能二：资源作用域 ResourceScope

### 目标

为 UI、战斗、关卡、剧情等业务提供统一资源释放边界，减少手动释放遗漏。

### 新增类型

建议新增文件：

- `Assets/FRFrame/Core/Resource/Runtime/ResourceScope.cs`
- `Assets/FRFrame/Core/Resource/Runtime/ResourceScopeState.cs`

```csharp
public enum ResourceScopeState
{
    Active,
    Releasing,
    Released
}

public sealed class ResourceScope : IDisposable
{
    public string Name { get; }
    public ResourceScopeState State { get; }
    public IReadOnlyList<ResourceKey> Keys { get; }
    public UniTask<T> LoadAssetAsync<T>(ResourceKey key, ResourceLoadOptions options = null) where T : Object;
    public UniTask<ResourceHandle<T>> LoadAssetHandleAsync<T>(ResourceKey key, ResourceLoadOptions options = null) where T : Object;
    public UniTask<GameObject> InstantiateAsync(ResourceKey key, Transform parent = null, ResourceLoadOptions options = null);
    public void Release(ResourceKey key);
    public void ReleaseAll();
    public void Dispose();
}
```

### ResourceManager 新增 API

```csharp
public ResourceScope CreateScope(string name);
public bool TryGetScope(string name, out ResourceScope scope);
public bool ReleaseScope(string name);
public void ReleaseAllScopes();
```

### 行为规则

- `ResourceScope` 内部记录加载成功并增加引用的 `ResourceKey`。
- 同一 scope 多次加载同一 key，需要记录引用次数，而不是只记录 key。
- `ReleaseAll` 按记录次数调用 `ReleaseAssetReference`。
- scope 释放后不允许继续加载，调用时输出 error 并返回 null。
- scope 可绑定业务对象生命周期，例如 UIForm 关闭时释放。

### 与 Residency 的关系

`ResourceScope` 是引用释放边界，`ResourceResidency` 是资源清理策略。二者不互斥。

- scope 释放会减少引用计数。
- 是否立即卸载仍由资源驻留类型和清理策略决定。
- `Resident` 资源即使 scope 释放也不会被普通清理卸载。

## 功能三：取消加载 CancellationToken

### 目标

支持 UI 快速关闭、场景切换、业务中断时取消等待加载结果，避免加载完成后产生无用引用或实例。

### Options 扩展

在 `ResourceLoadOptions` 增加：

```csharp
public CancellationToken CancellationToken;
public bool ReleaseOnCanceled = true;
```

在 `SceneLoadOptions` 增加：

```csharp
public CancellationToken CancellationToken;
```

### 加载流程设计

`LoadAssetInternalAsync` 中关键节点检查取消：

1. 发起加载前检查，已取消则直接返回 null。
2. YooAsset handle 完成后检查，若已取消且 `ReleaseOnCanceled` 为 true，则不增加引用并释放 handle。
3. 并发加载合并时，每个调用方独立响应自己的 token，但底层共享加载任务不因为单个调用方取消而取消。

### 实例化取消规则

- prefab 加载前已取消：返回 null。
- prefab 加载后实例化前取消：释放本次引用，返回 null。
- 实例化后通常不再响应取消，由调用方回收或销毁。

### 场景取消规则

YooAsset 场景加载底层不一定支持真正取消，因此这里只取消等待和后续处理。若 handle 已完成但 token 取消，按失败路径返回 default，并记录 warning。

## 功能四：Package 状态查询

### 目标

让业务初始化流程可以可靠判断某个 package 的创建、初始化、失败状态，避免重复初始化和错误调用。

### 新增类型

建议新增文件：

- `Assets/FRFrame/Core/Resource/YooAsset/PackageRuntimeState.cs`

```csharp
public enum PackageRuntimeState
{
    None,
    Created,
    Initializing,
    Initialized,
    Failed
}

public sealed class PackageRuntimeInfo
{
    public string PackageName;
    public bool IsDefaultPackage;
    public PackageRuntimeState State;
    public string Version;
    public string LastError;
}
```

### YooAssetExtension 扩展

维护：

```csharp
private readonly Dictionary<string, PackageRuntimeInfo> _packageInfos;
```

新增：

```csharp
public bool IsPackageCreated(string packageName);
public bool IsPackageInitialized(string packageName);
public bool IsDefaultPackage(string packageName);
public PackageRuntimeState GetPackageState(string packageName);
public PackageRuntimeInfo GetPackageInfo(string packageName);
public IReadOnlyList<PackageRuntimeInfo> GetAllPackageInfos();
```

### 状态流转

- `GetOrCreatePackage` 成功：`None -> Created`。
- `InitializePackageAsync` 开始：`Created -> Initializing`。
- 初始化成功：`Initializing -> Initialized`。
- 初始化失败：`Initializing -> Failed`，记录 `LastError`。
- `SetDefaultPackage` 更新 `IsDefaultPackage`。

## 功能五：Package 下载任务与进度事件

### 目标

在不强制业务使用统一下载 UI 的前提下，提供标准下载任务封装和进度事件。

### 新增类型

建议新增文件：

- `Assets/FRFrame/Core/Resource/YooAsset/PackageDownloadOptions.cs`
- `Assets/FRFrame/Core/Resource/YooAsset/PackageDownloadProgress.cs`
- `Assets/FRFrame/Core/Resource/YooAsset/PackageDownloadResult.cs`

```csharp
public sealed class PackageDownloadOptions
{
    public int DownloadingMaxNum = 10;
    public int FailedTryAgain = 3;
    public CancellationToken CancellationToken;
    public Action<PackageDownloadProgress> ProgressCallback;
}

public sealed class PackageDownloadProgress
{
    public string PackageName;
    public int TotalDownloadCount;
    public int CurrentDownloadCount;
    public long TotalDownloadBytes;
    public long CurrentDownloadBytes;
    public float Progress;
}

public sealed class PackageDownloadResult
{
    public string PackageName;
    public bool Succeed;
    public string Error;
}
```

### ResourceManager 新增 API

```csharp
public UniTask<PackageDownloadResult> DownloadPackageAsync(string packageName, PackageDownloadOptions options = null);
```

### ResourceEvents 扩展

```csharp
public event Action<PackageDownloadProgress> PackageDownloadProgress;
public event Action<PackageDownloadResult> PackageDownloadFinished;
```

### 行为规则

- 内部仍通过 `CreateDownloader` 获取 YooAsset downloader。
- 没有下载内容时直接返回成功。
- 进度事件和 options 回调都触发。
- 下载失败返回失败结果，不抛异常。
- token 取消时返回失败结果，错误信息为 canceled。

## 功能六：诊断快照增强

### 目标

让编辑器调试面板和运行期日志能定位资源泄漏、引用异常、package 状态异常。

### 新增类型

建议新增文件：

- `Assets/FRFrame/Core/Resource/Runtime/ResourceDiagnosticSnapshot.cs`
- `Assets/FRFrame/Core/Resource/Runtime/AssetEntrySnapshot.cs`
- `Assets/FRFrame/Core/Resource/Runtime/SceneEntrySnapshot.cs`
- `Assets/FRFrame/Core/Resource/Runtime/PackageResourceSnapshot.cs`

```csharp
public sealed class ResourceDiagnosticSnapshot
{
    public ResourceStatistics Statistics;
    public List<PackageResourceSnapshot> Packages;
    public List<AssetEntrySnapshot> Assets;
    public List<SceneEntrySnapshot> Scenes;
    public List<string> Warnings;
}
```

`AssetEntrySnapshot` 字段建议包含：

- `ResourceKey Key`
- `string PackageName`
- `string Location`
- `int RefCount`
- `ResourceResidency Residency`
- `ResourceEntryState State`
- `float LastUseTime`
- `bool HandleValid`
- `bool IsResident`

### ResourceDiagnostics 新增 API

```csharp
public ResourceDiagnosticSnapshot CreateSnapshot();
public string DumpDetail();
public IReadOnlyList<AssetEntrySnapshot> GetAssetSnapshots(string packageName = null);
public IReadOnlyList<SceneEntrySnapshot> GetSceneSnapshots(string packageName = null);
```

### 异常检测规则

- entry 状态为 Loaded 但 handle 无效。
- entry 状态为 Failed 但仍留在缓存中。
- refCount 长时间大于 0 且 LastUseTime 很久未更新。
- package 未初始化但存在资源 entry。
- GameObjectPool 中存在资源 entry 不存在的桶。

## 功能七：资源清理策略增强

### 目标

从“手动清理函数”升级为“可配置清理策略”，减少内存峰值和频繁 `Resources.UnloadUnusedAssets` 带来的卡顿。

### 新增类型

建议新增文件：

- `Assets/FRFrame/Core/Resource/Runtime/ResourceCleanupOptions.cs`
- `Assets/FRFrame/Core/Resource/Runtime/ResourceCleanupResult.cs`

```csharp
public sealed class ResourceCleanupOptions
{
    public string PackageName;
    public ResourceResidency? Residency;
    public float MinUnusedSeconds = 0f;
    public int MaxRemoveCount = 0;
    public bool IncludeResident = false;
    public bool UnloadUnusedAssets = true;
    public float UnloadUnusedAssetsMinInterval = 5f;
}

public sealed class ResourceCleanupResult
{
    public int RemovedAssetCount;
    public int ClearedPoolBucketCount;
    public bool CalledUnloadUnusedAssets;
}
```

### ResourceManager 新增 API

```csharp
public ResourceCleanupResult CleanupAssets(ResourceCleanupOptions options);
public ResourceCleanupResult CleanupLeastRecentlyUsed(int maxRemoveCount, string packageName = null);
public ResourceCleanupResult CleanupUnusedOlderThan(float seconds, string packageName = null);
public void SetAutoCleanupOptions(ResourceCleanupOptions options);
```

### 清理规则

- 只清理 `RefCount <= 0` 的资源。
- 默认不清理 `Resident`。
- 清理资源前先清理对应 `GameObjectPool` 桶。
- LRU 清理按 `LastUseTime` 从旧到新排序。
- `Resources.UnloadUnusedAssets` 需要节流，避免连续多次调用。

## 功能八：事件参数标准化

### 目标

让事件字段更清晰，降低监听者误用字段的概率。

### 新增事件参数

建议新增文件：

- `Assets/FRFrame/Core/Resource/Events/AssetLoadEventArgs.cs`
- `Assets/FRFrame/Core/Resource/Events/AssetReleaseEventArgs.cs`
- `Assets/FRFrame/Core/Resource/Events/SceneLoadEventArgs.cs`
- `Assets/FRFrame/Core/Resource/Events/PackageDownloadEventArgs.cs`
- `Assets/FRFrame/Core/Resource/Events/ResourcePoolEventArgs.cs`

### 兼容策略

第一阶段保留现有 `ResourceEventArgs` 和事件，同时新增强类型事件。

```csharp
public event Action<AssetLoadEventArgs> AssetLoadedV2;
public event Action<AssetLoadEventArgs> AssetLoadFailedV2;
public event Action<AssetReleaseEventArgs> AssetReleasedV2;
public event Action<SceneLoadEventArgs> SceneLoadedV2;
public event Action<SceneLoadEventArgs> SceneUnloadedV2;
```

第二阶段业务迁移后，再考虑标记旧事件为 `[Obsolete]`。

## 功能九：同步加载使用收敛

### 目标

保留同步加载兼容能力，同时降低运行期卡顿风险。

### ResourceLoadOptions 扩展

```csharp
public bool WarnIfSyncLoadMissCache = true;
```

### ResourceManager 新增设置

```csharp
public bool EnableSyncLoadWarning { get; set; } = true;
public bool AllowSyncLoadMissCache { get; set; } = true;
```

### 行为规则

- `LoadAsset<T>` 命中缓存时不输出 warning。
- 未命中缓存且需要真正同步 YooAsset 加载时输出 warning。
- 若 `AllowSyncLoadMissCache == false`，未命中缓存直接返回 null。
- 文档推荐运行期优先使用 `PreloadAssetAsync` + `TryGetAsset`。

## 实施顺序建议

### 阶段一

1. 实现 `ResourceHandle<T>`。
2. 实现 `ResourceScope`。
3. 增强诊断快照。
4. 增加 package 状态查询。

### 阶段二

1. 加入取消加载支持。
2. 实现下载任务和进度事件。
3. 实现统一清理策略。

### 阶段三

1. 标准化事件参数。
2. 收敛同步加载策略。
3. 基于诊断快照制作编辑器调试窗口。

## 验收标准

- 默认 Resource 初始化仍只初始化 `DefaultPackage`。
- 业务 package 仍可独立初始化、下载和卸载资源。
- 所有新增 API 都支持 `ResourceKey`。
- `ResourceHandle<T>` 重复释放不会破坏引用计数。
- `ResourceScope.ReleaseAll` 后作用域内资源引用计数正确归零或减少。
- 取消加载不会产生额外引用泄漏。
- 诊断快照能列出每个 package 的资源、场景、引用计数和异常项。
- 清理策略不会卸载仍被引用或默认保护的常驻资源。

## 总结

Resource 模块后续的核心方向是把“能加载资源”升级为“能可靠表达资源生命周期”。最关键的设计是 `ResourceHandle<T>`、`ResourceScope`、取消加载、package 状态查询和诊断快照。它们不会改变当前默认包初始化边界，但能显著降低业务层资源泄漏、重复加载和多 package 使用错误的概率。
