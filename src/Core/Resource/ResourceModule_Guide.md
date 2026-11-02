# Resource 模块详细说明

## 模块定位

`Assets/FRFrame/Core/Resource` 是 FRFrame 对 YooAsset 的资源管理封装层，负责默认包初始化、业务包辅助接口、资源加载、引用计数、Prefab 实例化、场景加载、资源作用域、下载进度、诊断与清理。

模块设计边界：框架初始化阶段只初始化 `DefaultPackage`。其它游戏 package 由对应业务初始化流程自行初始化、更新清单、下载资源。本模块提供多 package API 和必要状态查询，但不批量接管所有业务包初始化。

## 核心入口

### ResourceManager

文件：`Assets/FRFrame/Core/Resource/ResourceManager.cs`

常用入口通常通过 `FRFM.Resource` 访问。

核心职责：

- 初始化 YooAsset 与默认包。
- 管理 YooAsset package 状态和下载流程。
- 以 `ResourceKey` 管理多 package 资源唯一标识。
- 管理资源缓存、引用计数、驻留策略和清理。
- 管理 Prefab 实例化与 `GameObjectPool` 联动。
- 管理场景加载、卸载和主场景切换清理。
- 提供资源诊断、统计和事件。

### YooAssetExtension

文件：`Assets/FRFrame/Core/Resource/YooAsset/YooAssetExtension.cs`

职责：

- 包装 YooAsset 初始化。
- 获取或创建 `ResourcePackage`。
- 设置 `DefaultPackage`。
- 创建初始化参数。
- 初始化单个 package。
- 请求版本、更新清单、创建下载器。
- 维护 `PackageRuntimeInfo` 状态。

业务初始化其它 package 时优先使用 `ResourceManager` 对外 API，只有框架内部或特殊流程才直接访问 `YooAssetExtension`。

## 资源标识 ResourceKey

文件：`Assets/FRFrame/Core/Resource/Runtime/ResourceKey.cs`

`ResourceKey` 由 `PackageName` 和 `Location` 组成，用于避免多 package 下相同 location 冲突。

```csharp
var key = new ResourceKey("BattlePackage", "Assets/Game/Battle/Prefabs/Hero.prefab");
```

规则：

- 推荐业务代码在多 package 场景下显式传入 `ResourceKey`。
- 如果 `PackageName` 为空，ResourceManager 会尝试解析为默认包名。
- 非默认 package 的资源不要只传 location，否则会被当作默认包资源处理。

## 初始化与 Package 流程

### 框架初始化

`ResourceManager.InitializeAsync()` 会：

1. 创建 `YooAssetExtension`。
2. 调用 `YooAssets.Initialize()`。
3. 加载 YooAsset package 配置。
4. 获取默认 package 配置。
5. 通过资源准备 FSM 初始化默认包。
6. 初始化成功后设置 `IsReady = true`。

### 业务 package 初始化建议流程

```csharp
PackageConfig config = YooAssetConfigs.GetPackageConfig("BattlePackage");
InitializeParameters parameters = FRFM.Resource.YooExtension.CreateInitializeParameters(config);
bool initialized = await FRFM.Resource.InitializePackageAsync(config.name, parameters, setAsDefault: false);

if (!initialized)
    return;

string version = await FRFM.Resource.RequestPackageVersionAsync(config.name);
bool manifestOk = await FRFM.Resource.UpdatePackageManifestAsync(config.name, version);
```

如果业务需要下载资源：

```csharp
var result = await FRFM.Resource.DownloadPackageAsync("BattlePackage", new PackageDownloadOptions
{
    DownloadingMaxNum = 10,
    FailedTryAgain = 3,
    ProgressCallback = progress =>
    {
        float value = progress.Progress;
    }
});

if (!result.Succeed)
{
    FRLog.Error(result.Error);
}
```

### Package 状态 API

```csharp
bool created = FRFM.Resource.IsPackageCreated(packageName);
bool initialized = FRFM.Resource.IsPackageInitialized(packageName);
bool isDefault = FRFM.Resource.IsDefaultPackage(packageName);
PackageRuntimeState state = FRFM.Resource.GetPackageState(packageName);
PackageRuntimeInfo info = FRFM.Resource.GetPackageInfo(packageName);
IReadOnlyList<PackageRuntimeInfo> infos = FRFM.Resource.GetAllPackageInfos();
```

`PackageRuntimeState`：

- `None`
- `Created`
- `Initializing`
- `Initialized`
- `Failed`

## 资源加载 API

### 异步加载

```csharp
var key = new ResourceKey("UIPackage", "Assets/Game/UI/Main.prefab");
GameObject prefab = await FRFM.Resource.LoadAssetAsync<GameObject>(key);
```

使用 options：

```csharp
var asset = await FRFM.Resource.LoadAssetAsync<Texture2D>("Assets/Game/Icon.png", new ResourceLoadOptions
{
    PackageName = "UIPackage",
    Residency = ResourceResidency.Normal,
    TrackReference = true,
    AllowCache = true,
    CancellationToken = cancellationToken
});
```

### 同步加载

```csharp
Texture2D icon = FRFM.Resource.LoadAsset<Texture2D>(new ResourceKey("UIPackage", "Assets/Game/Icon.png"));
```

同步加载策略：

- 优先从已缓存资源中返回。
- 缓存 miss 时可直接调用 YooAsset 同步加载。
- `EnableSyncLoadWarning` 控制是否对同步 miss 输出 warning。
- `AllowSyncLoadMissCache` 为 false 时，同步 miss 会返回 null。

建议：运行期主流程优先使用异步加载；同步加载适合确定已预加载的资源或编辑器/调试场景。

### 预加载与 TryGet

```csharp
await FRFM.Resource.PreloadAssetAsync<GameObject>(key);

if (FRFM.Resource.TryGetAsset<GameObject>(key, out var prefab))
{
    // 使用已加载资源
}
```

### ResourceLoadOptions

文件：`Assets/FRFrame/Core/Resource/Runtime/ResourceLoadOptions.cs`

字段：

- `PackageName`：资源包名。
- `Residency`：驻留策略。
- `TrackReference`：是否增加引用计数。
- `AllowCache`：是否写入资源缓存。
- `AllowGameObjectPool`：Prefab 实例化是否允许对象池。
- `CancellationToken`：取消等待加载结果。
- `ReleaseOnCanceled`：取消后是否释放本次加载引用。
- `WarnIfSyncLoadMissCache`：同步加载缓存 miss 时是否警告。

## 资源驻留策略

文件：`Assets/FRFrame/Core/Resource/Runtime/ResourceResidency.cs`

常见策略：

- `Normal`：普通资源，可在引用归零后被清理。
- `Transient`：临时资源，适合短生命周期加载。
- `SceneScoped`：场景级资源，适合场景切换时清理。
- `Resident`：常驻资源，不被普通清理移除。

设置方式：

```csharp
await FRFM.Resource.LoadAssetAsync<AudioClip>(key, new ResourceLoadOptions
{
    Residency = ResourceResidency.Resident
});
```

也可以显式标记常驻：

```csharp
FRFM.Resource.AddResidentAsset(key);
```

## ResourceHandle

文件：

- `Assets/FRFrame/Core/Resource/Runtime/ResourceHandle.cs`
- `Assets/FRFrame/Core/Resource/Runtime/ResourceHandleState.cs`

用途：让调用方明确拥有一次资源引用，并通过 `Dispose` / `Release` 释放。

```csharp
using ResourceHandle<Texture2D> handle = await FRFM.Resource.LoadAssetHandleAsync<Texture2D>(key);
if (handle.IsValid)
{
    Texture2D texture = handle.Asset;
}
```

手动释放：

```csharp
ResourceHandle<GameObject> handle = await FRFM.Resource.LoadAssetHandleAsync<GameObject>(key);
// 使用资源
handle.Release();
```

规则：

- 加载成功后引用计数增加。
- `Release()` 会调用 `ReleaseAssetReference(Key)`。
- 重复释放只输出 warning，不会重复减少引用计数。
- `Dispose()` 等价于 `Release()`。

## ResourceScope

文件：

- `Assets/FRFrame/Core/Resource/Runtime/ResourceScope.cs`
- `Assets/FRFrame/Core/Resource/Runtime/ResourceScopeState.cs`

用途：为 UI、关卡、战斗、剧情等业务建立资源生命周期边界。Scope 可以绑定默认 package，适合每个业务 package 像独立游戏一样管理自身资源的场景。

```csharp
ResourceScope scope = FRFM.Resource.CreateScope("UI.MainMenu", "UIPackage");

GameObject prefab = await scope.LoadAssetAsync<GameObject>("Assets/Game/UI/MainMenu.prefab");
GameObject instance = await scope.InstantiateAsync("Assets/Game/UI/MainMenu.prefab");
string videoPath = await scope.LoadRawFilePathAsync("Assets/Game/Video/intro.mp4");

scope.ReleaseAll();
```

跨 package 资源仍然显式使用 `ResourceKey`：

```csharp
GameObject shared = await scope.LoadAssetAsync<GameObject>(
    new ResourceKey("SharedPackage", "Assets/Game/Common/Shared.prefab"));
```

推荐用法：

```csharp
public sealed class BattleLoader : System.IDisposable
{
    private readonly ResourceScope _scope = FRFM.Resource.CreateScope("Battle", "BattlePackage");

    public async UniTask LoadAsync(Transform parent)
    {
        await _scope.LoadAssetAsync<GameObject>("Assets/Game/Battle/Hero.prefab");
        await _scope.InstantiateAsync("Assets/Game/Battle/Bullet.prefab", parent);
    }

    public void Dispose()
    {
        _scope.Dispose();
    }
}
```

API：

```csharp
ResourceScope CreateScope(string name, string packageName = null);
UniTask<T> LoadAssetAsync<T>(string location, ResourceLoadOptions options = null);
UniTask<T> LoadAssetAsync<T>(ResourceKey key, ResourceLoadOptions options = null);
UniTask<ResourceHandle<T>> LoadAssetHandleAsync<T>(string location, ResourceLoadOptions options = null);
UniTask<ResourceHandle<T>> LoadAssetHandleAsync<T>(ResourceKey key, ResourceLoadOptions options = null);
UniTask<string> LoadRawFilePathAsync(string location);
UniTask<string> LoadRawFilePathAsync(ResourceKey key);
void ReleaseRawFile(string location);
void ReleaseRawFile(ResourceKey key);
UniTask<GameObject> InstantiateAsync(string location, Transform parent = null, ResourceLoadOptions options = null);
UniTask<GameObject> InstantiateAsync(ResourceKey key, Transform parent = null, ResourceLoadOptions options = null);
```

规则：

- `CreateScope(name, packageName)` 的 `packageName` 是 scope 内 string location API 的默认包名。
- 未绑定 package 的 scope 仍按 ResourceManager 默认包解析 string location。
- 同一 scope 多次加载同一 key 会记录次数。
- 原生文件句柄由 scope 缓存，`ReleaseRawFile` 或 `ReleaseAll` 时释放。
- `Release(key)` 释放一次引用。
- `ReleaseAll()` 按记录次数释放所有引用。
- scope 释放后继续加载会输出 error 并返回 null。
- scope 默认 package 只是默认值，不限制该 scope 只能加载这个包；跨包资源使用 `ResourceKey` 明确表达。

## Prefab 实例化与回收

### 实例化

```csharp
GameObject instance = await FRFM.Resource.InstantiateAsync(
    new ResourceKey("BattlePackage", "Assets/Game/Battle/Hero.prefab"),
    parent);
```

流程：

1. 先尝试从 `FRFM.Pool.GameObjectPool` 按 `ResourceKey` 获取 inactive 实例。
2. 如果池中没有，则加载 prefab。
3. `Object.Instantiate` 创建实例。
4. 添加或更新 `AssetInstanceTracker`。
5. 触发资源池相关事件。

### 预热

```csharp
await FRFM.Resource.WarmupGameObjectAsync(key, 10, new ResourceLoadOptions
{
    PackageName = "BattlePackage"
});
```

### 回收

```csharp
bool recycled = FRFM.Resource.RecycleInstance(instance);
```

回收流程：

- 通过 `AssetInstanceTracker` 找到来源 `ResourceKey`。
- 减少资源引用计数。
- 放入 `GameObjectPool`。
- 如果缺少 tracker，则销毁实例并输出 warning。

### 销毁实例

```csharp
FRFM.Resource.DestroyInstance(instance);
```

适合不希望入池复用的实例。

## 场景加载

### SceneLoadOptions

文件：`Assets/FRFrame/Core/Resource/Scene/SceneLoadOptions.cs`

字段：

- `PackageName`
- `LoadSceneMode`
- `ActivateOnLoad`
- `IsMainScene`
- `Residency`
- `CleanupNormalAssetsAfterLoad`
- `CleanupSceneScopedAssetsAfterLoad`
- `CancellationToken`

### 加载场景

```csharp
SceneHandle handle = await FRFM.Resource.LoadSceneAsync(
    new ResourceKey("ScenePackage", "Assets/Game/Scenes/Battle.unity"),
    new SceneLoadOptions
    {
        LoadSceneMode = LoadSceneMode.Single,
        IsMainScene = true,
        CleanupNormalAssetsAfterLoad = true,
        CleanupSceneScopedAssetsAfterLoad = true
    });
```

主场景加载成功后，会记录当前主场景 key，并按 options 触发普通资源和场景级资源清理。

### 卸载场景

```csharp
await FRFM.Resource.UnloadSceneAsync(new ResourceKey("ScenePackage", "Assets/Game/Scenes/Battle.unity"));
```

### 查询场景

```csharp
bool loaded = FRFM.Resource.IsSceneLoaded(key);
int count = FRFM.Resource.GetLoadedSceneCount("ScenePackage");
```

## 清理策略

### 引用释放

```csharp
FRFM.Resource.ReleaseAssetReference(key);
```

兼容旧 API：

```csharp
FRFM.Resource.ReleaseReference(key);
```

### 卸载资源

```csharp
FRFM.Resource.UnloadAsset(key, force: false);
```

兼容旧 API：

```csharp
FRFM.Resource.UnLoadAsset(key);
```

### 按 package 清理

```csharp
FRFM.Resource.CleanupPackageAssets("BattlePackage", includeResident: false);
FRFM.Resource.ClearPackageGameObjectPool("BattlePackage");
```

### 策略清理

```csharp
ResourceCleanupResult result = FRFM.Resource.CleanupAssets(new ResourceCleanupOptions
{
    PackageName = "BattlePackage",
    Residency = ResourceResidency.Normal,
    MinUnusedSeconds = 30f,
    MaxRemoveCount = 20,
    IncludeResident = false,
    UnloadUnusedAssets = true,
    UnloadUnusedAssetsMinInterval = 5f
});
```

快捷方式：

```csharp
FRFM.Resource.CleanupLeastRecentlyUsed(20, "BattlePackage");
FRFM.Resource.CleanupUnusedOlderThan(30f, "BattlePackage");
```

### 常用清理 API

```csharp
FRFM.Resource.CleanupUnusedNormalAssets();
FRFM.Resource.CleanupSceneScopedAssets();
await FRFM.Resource.CleanUpAsync();
FRFM.Resource.CleanUp();
FRFM.Resource.ClearAllAssets();
FRFM.Resource.ClearAllScenes();
```

## 诊断与统计

### ResourceStatistics

```csharp
ResourceStatistics stats = FRFM.Resource.GetStatistics();
```

### ResourceDiagnostics

```csharp
ResourceDiagnosticSnapshot snapshot = FRFM.Resource.Diagnostics.CreateSnapshot();
string summary = FRFM.Resource.Diagnostics.DumpSummary();
string detail = FRFM.Resource.Diagnostics.DumpDetail();
```

诊断信息包括：

- 资源统计。
- Asset 快照。
- Scene 快照。
- Package 资源快照。
- warning 列表。

典型 warning：

- 已加载资源 handle 无效。
- 失败资源仍在缓存中。
- 资源所属 package 未初始化。
- GameObjectPool bucket 找不到对应资源 entry。

### Editor 诊断窗口

文件：`Assets/FRFrame/Editor/Diagnostics/FRFrameRuntimeDiagnosticsWindow.cs`

菜单：`FRFrame/Diagnostics/Runtime Diagnostics`

可查看：

- Resource
- Packages
- GameObject Pools
- Object Pools
- Reference Pools
- Warnings

## 事件系统

文件：`Assets/FRFrame/Core/Resource/Events/ResourceEvents.cs`

旧事件：

```csharp
FRFM.Resource.Events.AssetLoaded += OnAssetLoaded;
FRFM.Resource.Events.AssetLoadFailed += OnAssetLoadFailed;
FRFM.Resource.Events.AssetReleased += OnAssetReleased;
FRFM.Resource.Events.SceneLoaded += OnSceneLoaded;
FRFM.Resource.Events.SceneUnloaded += OnSceneUnloaded;
```

V2 强类型事件：

```csharp
FRFM.Resource.Events.AssetLoadedV2 += args => { };
FRFM.Resource.Events.AssetLoadFailedV2 += args => { };
FRFM.Resource.Events.AssetReleasedV2 += args => { };
FRFM.Resource.Events.SceneLoadedV2 += args => { };
FRFM.Resource.Events.SceneUnloadedV2 += args => { };
FRFM.Resource.Events.InstanceRecycledV2 += args => { };
FRFM.Resource.Events.PoolObjectCreatedV2 += args => { };
FRFM.Resource.Events.PoolObjectReleasedV2 += args => { };
FRFM.Resource.Events.PackageDownloadProgress += progress => { };
FRFM.Resource.Events.PackageDownloadFinished += result => { };
```

新业务优先订阅 V2 事件。

## 推荐工作流

### UI 界面资源

1. 创建 UI scope。
2. 使用 `ResourceKey` 加载 prefab、图集、音效。
3. 界面关闭时释放 scope。
4. 对频繁打开的 UI prefab 可使用 `WarmupGameObjectAsync`。

```csharp
ResourceScope scope = FRFM.Resource.CreateScope("UI.Inventory");
GameObject view = await scope.InstantiateAsync(new ResourceKey("UIPackage", "Assets/Game/UI/Inventory.prefab"));

// UI 关闭
scope.ReleaseAll();
FRFM.Resource.RecycleInstance(view);
```

### 战斗 package

1. 业务初始化阶段初始化 `BattlePackage`。
2. 下载缺失资源。
3. 战斗开始创建 `Battle` scope。
4. 加载角色、技能、特效、音效。
5. 战斗结束释放 scope，清理 `SceneScoped` 或 package 资源。

```csharp
ResourceScope battleScope = FRFM.Resource.CreateScope("Battle");
await battleScope.LoadAssetAsync<GameObject>(new ResourceKey("BattlePackage", "Assets/Game/Battle/Hero.prefab"));

battleScope.ReleaseAll();
FRFM.Resource.CleanupUnusedOlderThan(0f, "BattlePackage");
```

### 场景切换

1. 用 `LoadSceneAsync` 加载新主场景。
2. 设置 `CleanupNormalAssetsAfterLoad` 和 `CleanupSceneScopedAssetsAfterLoad`。
3. 对上一场景 package 执行 package 级清理。

## 使用规范

- 多 package 资源优先使用 `ResourceKey`。
- 默认包资源可以传 location，但长期建议也使用 `ResourceKey` 保持一致。
- 资源所有权明确的业务优先使用 `ResourceHandle<T>` 或 `ResourceScope`。
- 不要绕过 ResourceManager 直接持有 YooAsset handle，除非在 YooAsset 适配层内部。
- Prefab 实例回收优先使用 `RecycleInstance`，不要直接丢进 `GameObjectPool`。
- 常驻资源使用 `Resident`，不要依赖“永不释放引用”实现常驻。
- 同步加载应限于已预加载资源或调试场景。
- Package 初始化由业务模块负责，不要把所有 package 加入框架初始化流程。
- 清理 package 资源时同步考虑 GameObjectPool bucket。
- 订阅事件时优先使用 V2 强类型事件。

## 常见问题

### 资源加载到了错误 package

检查是否只传了 location。非默认包资源必须设置 `PackageName` 或使用 `ResourceKey`。

### 引用计数没有下降

检查是否忘记调用 `ResourceHandle.Release()`、`ResourceScope.ReleaseAll()` 或 `ReleaseAssetReference()`。

### Prefab 回收后资源仍未卸载

GameObjectPool 只管理实例复用。资源卸载仍由 ResourceManager 引用计数和清理策略决定。需要调用清理 API。

### 下载进度没有回调

确认 `DownloadPackageAsync` 创建的 downloader 不是 null，且 package manifest 已正确更新。

### 诊断窗口看到 GameObjectPool bucket 无 asset entry

通常表示池中还有实例缓存，但资源 entry 已被清理。可清理对应 package 的 GameObjectPool。
