# Pool 模块新功能详细设计

## 设计边界

`Pool` 模块继续保持三类池职责：`ReferencePoolModule` 负责轻量引用对象，`ObjectPoolModule` 负责命名通用对象池，`GameObjectPoolModule` 负责 prefab 实例复用。

与 Resource 模块的边界保持不变：`GameObjectPoolModule` 只管理实例复用，不直接释放 YooAsset 资源句柄；资源引用计数、资源卸载和 package 级资源清理由 `ResourceManager` 负责。

## 当前基础

当前 Pool 模块已具备：

- `PoolManager` 统一初始化和销毁三类池。
- `ReferencePoolModule` 支持按类型获取和回收 `IReference`。
- `ObjectPoolModule` 支持按字符串 name 创建、获取、清理通用对象池。
- `ObjectPool<T>` 支持 factory、onGet、onRelease、onDestroy 和最大 inactive 数量。
- `GameObjectPoolModule` 使用 `ResourceKey` 分桶，支持取出、回收、清理 key、清理 package、清理全部。
- `GameObjectPool` 与 Resource 模块通过 `AssetInstanceTracker` 建立实例来源追踪。

后续新增功能重点是安全性、容量控制、生命周期回调、诊断快照和统一清理策略。

## 功能一：重复回收检测

### 目标

避免同一个对象被重复放入池中，导致后续多次取出同一实例。

### ObjectPool 设计

在 `ObjectPool<T>` 中增加调试期 inactive set：

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
private readonly HashSet<T> _inactiveSet = new HashSet<T>();
#endif
```

`Release` 流程：

1. obj 为 null 直接返回。
2. 调试期检查 `_inactiveSet.Contains(obj)`。
3. 已存在则输出 error 或 warning，并拒绝重复入池。
4. 正常入池时同时加入 `_inactiveSet`。
5. `Acquire` 从 stack 取出后从 `_inactiveSet` 移除。
6. `Clear` 时清空 `_inactiveSet`。

### GameObjectPool 设计

在 `GameObjectPoolBucket` 中增加：

```csharp
#if UNITY_EDITOR || DEVELOPMENT_BUILD
public readonly HashSet<GameObject> InactiveSet = new HashSet<GameObject>();
#endif
```

`GameObjectPoolModule.Release` 中检查重复回收。重复回收时不再次 `SetActive(false)` 或 push stack，直接输出 warning。

### 配置项

可在 `PoolManager` 暴露：

```csharp
public bool EnableDuplicateReleaseCheck { get; set; } = true;
```

该配置仅控制调试期检查是否启用。

## 功能二：GameObjectPool 桶容量上限

### 目标

限制每个 prefab 资源桶保留的 inactive 实例数量，避免高峰后长期占用大量内存。

### 新增类型

建议新增文件：

- `Assets/FRFrame/Core/Pool/GameObjectPool/GameObjectPoolOptions.cs`

```csharp
public sealed class GameObjectPoolOptions
{
    public int MaxInactiveCount = 0;
    public bool UseGlobalDefaultMaxInactiveCount = true;
    public bool DestroyOverflow = true;
    public bool ResetTransformOnRelease = true;
}
```

### GameObjectPoolModule 新增字段

```csharp
private int _defaultMaxInactiveCount;
private readonly Dictionary<ResourceKey, GameObjectPoolOptions> _options = new Dictionary<ResourceKey, GameObjectPoolOptions>();
```

### 新增 API

```csharp
public void SetDefaultMaxInactiveCount(int count);
public void SetOptions(ResourceKey key, GameObjectPoolOptions options);
public GameObjectPoolOptions GetOptions(ResourceKey key);
public int GetMaxInactiveCount(ResourceKey key);
```

### 回收规则

- `MaxInactiveCount <= 0` 表示不限制。
- 回收前检查当前桶 inactive 数量。
- 如果已达到上限，调用销毁流程，不入池。
- 销毁溢出实例时调用 `IPoolableGameObject.OnDestroyFromPool`。
- 不改变 ResourceManager 的资源引用计数，因为引用计数已在回收实例前由 ResourceManager 递减。

### 预热规则

`ResourceManager.WarmupGameObjectAsync` 或未来的 `GameObjectPoolModule.Warmup` 应读取桶容量，预热数量不应超过上限。

## 功能三：GameObject 池生命周期回调

### 目标

让 prefab 自身有机会在取出、回收、销毁时重置状态或释放内部资源。

### 新增接口

建议新增文件：

- `Assets/FRFrame/Core/Pool/GameObjectPool/IPoolableGameObject.cs`

```csharp
public interface IPoolableGameObject
{
    void OnSpawnFromPool();
    void OnRecycleToPool();
    void OnDestroyFromPool();
}
```

可选拆分为三个小接口：

```csharp
public interface IPoolSpawnHandler { void OnSpawnFromPool(); }
public interface IPoolRecycleHandler { void OnRecycleToPool(); }
public interface IPoolDestroyHandler { void OnDestroyFromPool(); }
```

拆分接口更灵活，推荐采用拆分形式。

### 调用时机

- `TryGet` 成功取出对象，`SetActive(true)` 后调用 `OnSpawnFromPool`。
- `Release` 入池前调用 `OnRecycleToPool`，再 `SetActive(false)`。
- `Clear`、`ClearPackage`、`ClearAll`、容量溢出销毁时调用 `OnDestroyFromPool`。

### 查询方式

建议使用 `GetComponentsInChildren<T>(true)`，确保子节点组件也能收到回调。为避免频繁分配，可后续缓存组件列表，但第一版可以保持简单。

## 功能四：ReferencePool 容量限制和裁剪

### 目标

避免高峰期创建的大量引用对象长期滞留。

### 新增配置

```csharp
private int _defaultMaxCachedCount;
private readonly Dictionary<Type, int> _maxCachedCounts = new Dictionary<Type, int>();
```

### 新增 API

```csharp
public void SetDefaultMaxCachedCount(int count);
public void SetMaxCachedCount<T>(int count) where T : class, IReference, new();
public int GetMaxCachedCount<T>() where T : class, IReference, new();
public void Clear<T>() where T : class, IReference, new();
public int Trim<T>(int targetCount) where T : class, IReference, new();
public int TrimAll(float keepPercent);
public IReadOnlyList<ReferencePoolSnapshot> GetSnapshots();
```

### 新增快照类型

```csharp
public sealed class ReferencePoolSnapshot
{
    public Type Type;
    public int CachedCount;
    public int MaxCachedCount;
}
```

### Release 规则

- `Release` 前仍调用 `reference.Clear()`。
- 若对应类型缓存数量已达上限，则丢弃对象，不入池。
- `MaxCachedCount <= 0` 表示不限制。

## 功能五：ObjectPool 统计增强

### 目标

让通用对象池能观察创建数、活跃数、命中率和释放情况。

### IObjectPoolBase 扩展

```csharp
public interface IObjectPoolBase
{
    string Name { get; }
    int CountInactive { get; }
    int CountActive { get; }
    int CountAll { get; }
    int TotalCreated { get; }
    int TotalAcquired { get; }
    int TotalReleased { get; }
    int HitCount { get; }
    int MissCount { get; }
    void Clear();
}
```

### ObjectPool<T> 计数规则

- factory 创建新对象时 `TotalCreated++`、`MissCount++`。
- 从 stack 取对象时 `HitCount++`。
- 每次 Acquire 成功 `TotalAcquired++`、`CountActive++`。
- 每次 Release 成功 `TotalReleased++`、`CountActive--`。
- 超过 maxCount 被销毁时，`CountAll--`。

### ObjectPoolModule 新增快照

```csharp
public sealed class ObjectPoolSnapshot
{
    public string Name;
    public string ObjectTypeName;
    public int CountAll;
    public int CountActive;
    public int CountInactive;
    public int TotalCreated;
    public int HitCount;
    public int MissCount;
}

public IReadOnlyList<ObjectPoolSnapshot> GetSnapshots();
```

## 功能六：GameObjectPool 诊断增强

### 目标

支持按 key、按 package 和全量桶快照查看 GameObject 池状态。

### 新增快照类型

建议新增文件：

- `Assets/FRFrame/Core/Pool/GameObjectPool/GameObjectPoolSnapshot.cs`

```csharp
public sealed class GameObjectPoolBucketSnapshot
{
    public ResourceKey Key;
    public string PackageName;
    public string Location;
    public int InactiveCount;
    public int MaxInactiveCount;
    public bool RootExists;
}

public sealed class GameObjectPoolPackageSnapshot
{
    public string PackageName;
    public int BucketCount;
    public int InactiveCount;
}
```

### GameObjectPoolModule 新增 API

```csharp
public int GetInactiveCount(ResourceKey key);
public int GetBucketCount(string packageName);
public int GetInactiveCountByPackage(string packageName);
public IReadOnlyList<GameObjectPoolBucketSnapshot> GetBucketSnapshots(string packageName = null);
public IReadOnlyList<GameObjectPoolPackageSnapshot> GetPackageSnapshots();
```

### 异常检测

快照可附带 warning：

- bucket root 为 null。
- inactive stack 中存在 null 对象。
- inactive set 与 stack 数量不一致。
- bucket 为空但长期未清理。

## 功能七：PoolManager 统一清理策略

### 目标

为场景切换、业务阶段切换、内存压力处理提供统一池清理入口。

### 新增类型

建议新增文件：

- `Assets/FRFrame/Core/Pool/PoolCleanupOptions.cs`
- `Assets/FRFrame/Core/Pool/PoolCleanupResult.cs`

```csharp
public sealed class PoolCleanupOptions
{
    public bool IncludeReferencePool = true;
    public bool IncludeObjectPool = true;
    public bool IncludeGameObjectPool = true;
    public string PackageName;
    public float ReferenceKeepPercent = 1f;
    public int GameObjectMaxRemoveCount = 0;
}

public sealed class PoolCleanupResult
{
    public int ReferenceRemovedCount;
    public int ObjectPoolClearedCount;
    public int GameObjectBucketClearedCount;
    public int GameObjectDestroyedCount;
}
```

### PoolManager 新增 API

```csharp
public PoolCleanupResult TrimAll(PoolCleanupOptions options = null);
public void ClearPackage(string packageName);
public void ClearSceneScopedPools();
public void RegisterPoolGroup(string groupName, string poolName);
public bool ClearPoolGroup(string groupName);
```

### 设计规则

- Package 级清理仅作用于 `GameObjectPool`。
- Resource 相关的 package 清理仍建议由 `ResourceManager` 发起。
- ObjectPool 的分组是业务辅助能力，不影响已有 name 查找。

## 功能八：Transform 重置策略

### 目标

统一控制 GameObject 回收时是否重置 Transform，避免部分对象下次取出时携带旧状态。

### 配置项

扩展 `GameObjectPoolOptions`：

```csharp
public bool ResetLocalPosition = true;
public bool ResetLocalRotation = true;
public bool ResetLocalScale = false;
public Vector3 DefaultLocalPosition = Vector3.zero;
public Quaternion DefaultLocalRotation = Quaternion.identity;
public Vector3 DefaultLocalScale = Vector3.one;
```

### 回收流程

1. 调用回收回调。
2. 根据 options 重置 transform。
3. 设置 parent 到 bucket root。
4. `SetActive(false)`。
5. 入栈。

### 注意事项

默认不重置 scale 更稳妥，因为 UI 或特效 prefab 可能依赖原始 scale。需要重置时由业务显式开启。

## 功能九：编辑器调试窗口

### 目标

提供 Resource + Pool 的统一运行期可视化诊断入口。

### 建议路径

- `Assets/FRFrame/Editor/Diagnostics/FRFrameRuntimeDiagnosticsWindow.cs`

### 窗口内容

Tabs：

- Resource Assets
- Resource Scenes
- Packages
- GameObject Pools
- Object Pools
- Reference Pools
- Warnings

### 操作能力

- 刷新快照。
- 搜索 package、location、pool name。
- 清理指定 GameObjectPool bucket。
- 清理指定 package 的 GameObjectPool。
- 清理指定 ObjectPool。
- 清理指定 ReferencePool 类型。
- 导出诊断文本。

### 数据来源

- Resource 使用 `ResourceDiagnostics.CreateSnapshot()`。
- Pool 使用 `PoolManager`、`ObjectPoolModule`、`ReferencePoolModule`、`GameObjectPoolModule` 的快照接口。

## 功能十：类型安全 ObjectPoolKey

### 目标

减少字符串池名拼写错误，同时保留字符串 name 的灵活性。

### 新增类型

建议新增文件：

- `Assets/FRFrame/Core/Pool/ObjectPool/ObjectPoolKey.cs`

```csharp
public readonly struct ObjectPoolKey<T> where T : class
{
    public readonly string Name;
    public ObjectPoolKey(string name);
    public override string ToString();
}
```

### ObjectPoolModule 新增 API

```csharp
public IObjectPool<T> CreatePool<T>(ObjectPoolKey<T> key, Func<T> factory, Action<T> onGet = null, Action<T> onRelease = null, Action<T> onDestroy = null, int maxCount = 0) where T : class;
public IObjectPool<T> GetPool<T>(ObjectPoolKey<T> key) where T : class;
public bool ClearPool<T>(ObjectPoolKey<T> key, bool remove = true) where T : class;
public IObjectPool<T> GetOrCreatePool<T>(Func<T> factory, int maxCount = 0) where T : class;
```

### 默认池命名

`GetOrCreatePool<T>()` 默认 name 可使用：

```csharp
typeof(T).FullName
```

业务也可以定义常量：

```csharp
public static readonly ObjectPoolKey<BattleCommand> BattleCommandPool = new ObjectPoolKey<BattleCommand>("Battle.Command");
```

## 与 Resource 模块协作规则

### GameObjectPool 不释放资源句柄

`GameObjectPool` 销毁 inactive 实例时只销毁 GameObject，不调用 `ResourceManager.UnloadAsset`。资源是否卸载仍由 ResourceManager 的引用计数和清理策略决定。

### ResourceManager 清理资源前先清理池桶

当 ResourceManager 卸载某个 prefab 资源时，应先调用：

```csharp
FRFM.Pool.GameObjectPool.Clear(key);
```

这样可以避免资源 handle 释放后池里仍保留依赖该 prefab 的 inactive 实例。

### Package 清理优先从 ResourceManager 发起

业务希望卸载某个 package 的资源时，推荐调用 ResourceManager 的 package 清理接口，由它协调资源 entry 和 GameObjectPool。`PoolManager.ClearPackage` 更适合只想清理实例缓存、不卸载资源的场景。

## 实施顺序建议

### 阶段一

1. 重复回收检测。
2. GameObjectPool 桶容量上限。
3. GameObject 池生命周期回调。
4. ReferencePool 容量限制和按类型清理。

### 阶段二

1. ObjectPool 统计增强。
2. GameObjectPool 诊断快照。
3. PoolManager 统一清理策略。
4. Transform 重置策略。

### 阶段三

1. 类型安全 `ObjectPoolKey<T>`。
2. 编辑器调试窗口。
3. 与 Resource 诊断快照合并展示。

## 验收标准

- 重复回收不会导致同一对象被重复取出。
- GameObjectPool 超过容量上限时能销毁溢出实例。
- `IPoolSpawnHandler`、`IPoolRecycleHandler`、`IPoolDestroyHandler` 调用时机正确。
- ReferencePool 能按类型设置容量、清理和裁剪。
- ObjectPool 能正确统计 active、inactive、created、hit、miss。
- GameObjectPool 能输出按 key 和 package 的详细快照。
- PoolManager 能统一执行 trim 和 package 级池清理。
- ResourceManager 卸载资源时不会留下对应 inactive 实例。

## 总结

Pool 模块后续的核心方向是从“可复用对象”升级为“可诊断、可限制、可验证的对象复用系统”。最优先的设计是重复回收检测、GameObjectPool 容量上限、池生命周期回调和 ReferencePool 裁剪。完成这些后，Pool 模块会更适合长时间运行、频繁场景切换和复杂战斗对象复用场景。
