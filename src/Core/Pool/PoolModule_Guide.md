# Pool 模块详细说明

## 模块定位

`Assets/FRFrame/Core/Pool` 是 FRFrame 的池化管理模块，提供三类对象复用能力：

- `ReferencePoolModule`：轻量引用对象池，适合事件参数、临时上下文、DTO、请求对象等。
- `ObjectPoolModule`：通用命名对象池，适合普通 class 对象、命令对象、运行时数据对象。
- `GameObjectPoolModule`：Prefab 实例池，按 `ResourceKey` 分桶，与 Resource 模块的 Prefab 实例化和回收流程联动。

Pool 模块只负责对象复用，不直接管理 YooAsset 资源句柄生命周期。资源引用计数、资源卸载、package 级资源清理由 `ResourceManager` 负责。

## 核心入口 PoolManager

文件：`Assets/FRFrame/Core/Pool/PoolManager.cs`

常用入口通常通过 `FRFM.Pool` 访问。

初始化时：

1. 创建 `ReferencePoolModule`。
2. 创建 `ObjectPoolModule`。
3. 创建 `GameObjectPoolModule`。
4. 创建常驻根节点 `[FRFrame_Pools]`。
5. 将根节点传入 GameObjectPool。

销毁时：

1. 清空 GameObjectPool。
2. 清空 ReferencePool。
3. 清空 ObjectPool。
4. 清理业务 pool group。
5. 销毁 `[FRFrame_Pools]`。

## ReferencePool

### 适用场景

`ReferencePool` 适合池化轻量、可重置的引用对象。例如：

- 事件参数。
- 临时计算上下文。
- Request / Response 对象。
- 不继承 UnityEngine.Object 的小型运行时对象。

池化对象必须实现 `IReference`。

文件：`Assets/FRFrame/Core/Pool/ReferencePool/IReference.cs`

```csharp
public sealed class DamageEventArgs : IReference
{
    public int Damage;
    public string Source;

    public void Clear()
    {
        Damage = 0;
        Source = null;
    }
}
```

### 获取与回收

```csharp
DamageEventArgs args = FRFM.Pool.ReferencePool.Acquire<DamageEventArgs>();
args.Damage = 100;
args.Source = "Skill";

// 使用完成
FRFM.Pool.ReferencePool.Release(args);
```

回收时会先调用 `reference.Clear()`，再放回对应类型的栈中。

### 容量控制

```csharp
FRFM.Pool.ReferencePool.SetDefaultMaxCachedCount(200);
FRFM.Pool.ReferencePool.SetMaxCachedCount<DamageEventArgs>(50);

int max = FRFM.Pool.ReferencePool.GetMaxCachedCount<DamageEventArgs>();
```

规则：

- `MaxCachedCount <= 0` 表示不限制。
- 类型级上限优先于全局默认上限。
- 达到上限后，回收对象会被丢弃，不再入池。

### 清理与裁剪

```csharp
FRFM.Pool.ReferencePool.Clear<DamageEventArgs>();
FRFM.Pool.ReferencePool.Trim<DamageEventArgs>(10);
FRFM.Pool.ReferencePool.TrimAll(0.5f);
FRFM.Pool.ReferencePool.ClearAll();
```

`TrimAll(keepPercent)` 会按比例保留各类型缓存。

### 查询与快照

```csharp
bool hasPool = FRFM.Pool.ReferencePool.HasPool<DamageEventArgs>();
int cached = FRFM.Pool.ReferencePool.GetCachedCount<DamageEventArgs>();
int total = FRFM.Pool.ReferencePool.GetTotalCachedCount();
IReadOnlyList<ReferencePoolSnapshot> snapshots = FRFM.Pool.ReferencePool.GetSnapshots();
```

`ReferencePoolSnapshot` 包含：

- `Type`
- `CachedCount`
- `MaxCachedCount`

## ObjectPool

### 适用场景

`ObjectPoolModule` 适合池化普通 class 对象，但这些对象不一定需要实现 `IReference`。例如：

- 战斗指令对象。
- AI 行为节点运行时数据。
- 寻路请求对象。
- 临时 List 包装对象。
- 业务层可复用服务对象。

### 创建对象池

```csharp
var pool = FRFM.Pool.ObjectPool.CreatePool<BattleCommand>(
    "Battle.Command",
    factory: () => new BattleCommand(),
    onGet: command => command.ResetRuntimeState(),
    onRelease: command => command.Clear(),
    onDestroy: command => command.Dispose(),
    maxCount: 128);
```

如果同名池已存在且类型一致，会返回已有池；如果同名但类型不一致，会输出 error。

### 使用 ObjectPoolKey

文件：`Assets/FRFrame/Core/Pool/ObjectPool/ObjectPoolKey.cs`

```csharp
public static class PoolKeys
{
    public static readonly ObjectPoolKey<BattleCommand> BattleCommand = new ObjectPoolKey<BattleCommand>("Battle.Command");
}

var pool = FRFM.Pool.ObjectPool.CreatePool(
    PoolKeys.BattleCommand,
    () => new BattleCommand(),
    onRelease: command => command.Clear(),
    maxCount: 128);
```

推荐大型业务模块使用 `ObjectPoolKey<T>`，避免字符串散落和类型误取。

### 获取对象池

```csharp
IObjectPool<BattleCommand> pool = FRFM.Pool.ObjectPool.GetPool<BattleCommand>("Battle.Command");
IObjectPool<BattleCommand> typedPool = FRFM.Pool.ObjectPool.GetPool(PoolKeys.BattleCommand);
```

### 获取或创建默认类型池

```csharp
IObjectPool<BattleCommand> pool = FRFM.Pool.ObjectPool.GetOrCreatePool(
    () => new BattleCommand(),
    maxCount: 128);
```

默认名称为 `typeof(T).FullName`。

### Acquire / Release

```csharp
BattleCommand command = pool.Acquire();
command.SkillId = 1001;

// 使用完成
pool.Release(command);
```

规则：

- `Acquire()` 优先从 inactive 栈取对象，否则调用 factory 创建。
- `Release()` 先执行 onRelease。
- inactive 数量超过 `maxCount` 时执行 onDestroy，不入池。
- 编辑器或 Development Build 下会检测重复回收，重复回收会 warning 并忽略。

### 清理对象池

```csharp
FRFM.Pool.ObjectPool.ClearPool("Battle.Command");
FRFM.Pool.ObjectPool.ClearPool(PoolKeys.BattleCommand);
FRFM.Pool.ObjectPool.ClearPool("Battle.Command", remove: false);
FRFM.Pool.ObjectPool.RemovePoolOnly("Battle.Command");
FRFM.Pool.ObjectPool.ClearAll();
```

说明：

- `ClearPool(name)` 默认清空并移除池定义。
- `remove: false` 只清空缓存，保留池定义。
- `RemovePoolOnly` 只移除池定义，不调用 Clear，通常不推荐。

### 统计与快照

```csharp
int poolCount = FRFM.Pool.ObjectPool.GetPoolCount();
int inactive = FRFM.Pool.ObjectPool.GetInactiveCount("Battle.Command");
IReadOnlyList<ObjectPoolSnapshot> snapshots = FRFM.Pool.ObjectPool.GetSnapshots();
```

`ObjectPoolSnapshot` 包含：

- `Name`
- `ObjectTypeName`
- `CountAll`
- `CountActive`
- `CountInactive`
- `TotalCreated`
- `TotalAcquired`
- `TotalReleased`
- `HitCount`
- `MissCount`

## GameObjectPool

### 模块边界

`GameObjectPoolModule` 只负责 Prefab 实例复用，不负责资源引用计数。正常业务不应直接调用 `GameObjectPool.Release`，而应通过 `FRFM.Resource.RecycleInstance(instance)`，让 ResourceManager 先处理引用计数，再放入 GameObjectPool。

### 分桶规则

文件：`Assets/FRFrame/Core/Pool/GameObjectPool/GameObjectPoolModule.cs`

GameObjectPool 以 `ResourceKey` 分桶：

```csharp
var key = new ResourceKey("BattlePackage", "Assets/Game/Battle/Hero.prefab");
```

每个桶包含：

- `ResourceKey`
- `Location`
- `Root`
- inactive 实例栈
- 调试期 inactive set

### 推荐实例化流程

```csharp
GameObject instance = await FRFM.Resource.InstantiateAsync(
    new ResourceKey("BattlePackage", "Assets/Game/Battle/Hero.prefab"),
    parent);
```

ResourceManager 会自动：

1. 优先从 GameObjectPool 取对象。
2. 池中没有则加载 prefab 并实例化。
3. 设置 `AssetInstanceTracker`。
4. 管理资源引用计数。

### 推荐回收流程

```csharp
FRFM.Resource.RecycleInstance(instance);
```

ResourceManager 会自动：

1. 从 `AssetInstanceTracker` 读取 `ResourceKey`。
2. 减少资源引用计数。
3. 调用 `GameObjectPool.Release(key, location, instance)`。

### 预热

```csharp
await FRFM.Resource.WarmupGameObjectAsync(
    new ResourceKey("BattlePackage", "Assets/Game/Battle/Bullet.prefab"),
    30);
```

预热会实例化指定数量对象后立即回收进池。

### 容量配置

全局默认容量：

```csharp
FRFM.Pool.GameObjectPool.SetDefaultMaxInactiveCount(50);
```

按 key 配置：

```csharp
var key = new ResourceKey("BattlePackage", "Assets/Game/Battle/Bullet.prefab");
FRFM.Pool.GameObjectPool.SetOptions(key, new GameObjectPoolOptions
{
    UseGlobalDefaultMaxInactiveCount = false,
    MaxInactiveCount = 100,
    ResetTransformOnRelease = true,
    ResetLocalPosition = true,
    ResetLocalRotation = true,
    ResetLocalScale = false
});
```

字段说明：

- `MaxInactiveCount`：最大 inactive 数量，<= 0 表示不限制。
- `UseGlobalDefaultMaxInactiveCount`：是否使用全局默认上限。
- `DestroyOverflow`：表示溢出时销毁的策略意图，当前实现溢出时会销毁。
- `ResetTransformOnRelease`：回收时是否重置 transform。
- `ResetLocalPosition` / `ResetLocalRotation` / `ResetLocalScale`：控制重置项。
- `DefaultLocalPosition` / `DefaultLocalRotation` / `DefaultLocalScale`：重置目标值。

查询容量：

```csharp
int max = FRFM.Pool.GameObjectPool.GetMaxInactiveCount(key);
GameObjectPoolOptions options = FRFM.Pool.GameObjectPool.GetOptions(key);
```

### 生命周期回调

文件：`Assets/FRFrame/Core/Pool/GameObjectPool/IPoolableGameObject.cs`

接口：

```csharp
public interface IPoolSpawnHandler
{
    void OnSpawnFromPool();
}

public interface IPoolRecycleHandler
{
    void OnRecycleToPool();
}

public interface IPoolDestroyHandler
{
    void OnDestroyFromPool();
}
```

使用示例：

```csharp
public sealed class BulletView : MonoBehaviour, IPoolSpawnHandler, IPoolRecycleHandler, IPoolDestroyHandler
{
    public void OnSpawnFromPool()
    {
        // 激活特效、重置运行时状态
    }

    public void OnRecycleToPool()
    {
        // 停止粒子、清空目标、解绑临时事件
    }

    public void OnDestroyFromPool()
    {
        // 释放仅属于实例的内部对象
    }
}
```

调用时机：

- 从池取出并 `SetActive(true)` 后调用 `OnSpawnFromPool`。
- 入池前调用 `OnRecycleToPool`。
- 清池、容量溢出销毁时调用 `OnDestroyFromPool`。

会通过 `GetComponentsInChildren<T>(true)` 通知子节点组件。

### 重复回收检测

编辑器或 Development Build 下，GameObjectPool 会记录 inactive set。

```csharp
FRFM.Pool.EnableDuplicateReleaseCheck = true;
```

重复回收同一实例会输出 warning 并忽略，避免同一对象被多次压入池中。

### 查询与诊断

```csharp
bool exists = FRFM.Pool.GameObjectPool.ContainsBucket(key);
int bucketCount = FRFM.Pool.GameObjectPool.GetBucketCount();
int packageBucketCount = FRFM.Pool.GameObjectPool.GetBucketCount("BattlePackage");
int inactive = FRFM.Pool.GameObjectPool.GetInactiveCount(key);
int packageInactive = FRFM.Pool.GameObjectPool.GetInactiveCountByPackage("BattlePackage");
int totalInactive = FRFM.Pool.GameObjectPool.GetTotalInactiveCount();

IReadOnlyList<GameObjectPoolBucketSnapshot> buckets = FRFM.Pool.GameObjectPool.GetBucketSnapshots();
IReadOnlyList<GameObjectPoolPackageSnapshot> packages = FRFM.Pool.GameObjectPool.GetPackageSnapshots();
```

`GameObjectPoolBucketSnapshot` 包含：

- `Key`
- `PackageName`
- `Location`
- `InactiveCount`
- `MaxInactiveCount`
- `RootExists`
- `Warning`

`GameObjectPoolPackageSnapshot` 包含：

- `PackageName`
- `BucketCount`
- `InactiveCount`

### 清理 GameObjectPool

```csharp
FRFM.Pool.GameObjectPool.Clear(key);
FRFM.Pool.GameObjectPool.ClearPackage("BattlePackage");
FRFM.Pool.GameObjectPool.Trim(maxRemoveCount: 20, packageName: "BattlePackage");
FRFM.Pool.GameObjectPool.ClearAll();
```

清理会销毁 inactive 实例，并触发 `IPoolDestroyHandler.OnDestroyFromPool()`。

## PoolManager 统一清理

### TrimAll

```csharp
PoolCleanupResult result = FRFM.Pool.TrimAll(new PoolCleanupOptions
{
    IncludeReferencePool = true,
    IncludeObjectPool = false,
    IncludeGameObjectPool = true,
    PackageName = "BattlePackage",
    ReferenceKeepPercent = 0.5f,
    GameObjectMaxRemoveCount = 20
});
```

字段说明：

- `IncludeReferencePool`：是否裁剪 ReferencePool。
- `IncludeObjectPool`：是否清理 ObjectPool。
- `IncludeGameObjectPool`：是否裁剪或清理 GameObjectPool。
- `PackageName`：仅作用于 GameObjectPool package 清理。
- `ReferenceKeepPercent`：ReferencePool 保留比例。
- `GameObjectMaxRemoveCount`：GameObjectPool 最大移除数量。

返回 `PoolCleanupResult`，包含：

- `ReferenceRemovedCount`
- `ObjectPoolClearedCount`
- `GameObjectBucketClearedCount`
- `GameObjectDestroyedCount`

### Package 清理

```csharp
FRFM.Pool.ClearPackage("BattlePackage");
```

该方法清理对应 package 的 GameObjectPool bucket。

### 场景级清理

```csharp
FRFM.Pool.ClearSceneScopedPools();
```

当前实现会裁剪 GameObjectPool inactive 实例。

## Pool Group

用于把若干 ObjectPool 按业务分组，便于批量清理。

```csharp
FRFM.Pool.RegisterPoolGroup("Battle", "Battle.Command");
FRFM.Pool.RegisterPoolGroup("Battle", "Battle.PathRequest");

FRFM.Pool.ClearPoolGroup("Battle");
```

规则：

- group 只记录 ObjectPool 名称。
- `ClearPoolGroup` 会调用 `ObjectPool.ClearPool(name)`，默认清空并移除池。
- 清理后 group 会被移除。

## Editor 诊断窗口

文件：`Assets/FRFrame/Editor/Diagnostics/FRFrameRuntimeDiagnosticsWindow.cs`

菜单：`FRFrame/Diagnostics/Runtime Diagnostics`

Pool 相关页签：

- `GameObject Pools`
- `Object Pools`
- `Reference Pools`

可查看各池快照，并对部分池执行 Clear。

## 推荐工作流

### 事件参数

```csharp
public sealed class ItemChangedEventArgs : IReference
{
    public int ItemId;
    public int Count;

    public void Clear()
    {
        ItemId = 0;
        Count = 0;
    }
}

ItemChangedEventArgs args = FRFM.Pool.ReferencePool.Acquire<ItemChangedEventArgs>();
args.ItemId = 1001;
args.Count = 5;

// 派发完成后
FRFM.Pool.ReferencePool.Release(args);
```

### 战斗命令对象

```csharp
public sealed class BattleCommand
{
    public int SkillId;
    public int TargetId;

    public void Clear()
    {
        SkillId = 0;
        TargetId = 0;
    }
}

static readonly ObjectPoolKey<BattleCommand> BattleCommandPool = new ObjectPoolKey<BattleCommand>("Battle.Command");

IObjectPool<BattleCommand> pool = FRFM.Pool.ObjectPool.CreatePool(
    BattleCommandPool,
    () => new BattleCommand(),
    onRelease: command => command.Clear(),
    maxCount: 256);

BattleCommand command = pool.Acquire();
command.SkillId = 1001;

pool.Release(command);
```

### 子弹 Prefab 池

```csharp
var bulletKey = new ResourceKey("BattlePackage", "Assets/Game/Battle/Bullet.prefab");

FRFM.Pool.GameObjectPool.SetOptions(bulletKey, new GameObjectPoolOptions
{
    UseGlobalDefaultMaxInactiveCount = false,
    MaxInactiveCount = 80,
    ResetTransformOnRelease = true
});

await FRFM.Resource.WarmupGameObjectAsync(bulletKey, 30);

GameObject bullet = await FRFM.Resource.InstantiateAsync(bulletKey);

// 命中或生命周期结束
FRFM.Resource.RecycleInstance(bullet);
```

## 使用规范

- `ReferencePool` 对象必须实现 `IReference.Clear()`，并确保 Clear 后无脏状态。
- `ObjectPool` 必须提供可靠 factory；需要清理状态时使用 `onRelease`。
- `ObjectPool` 的 name 应稳定，跨系统共享时推荐使用 `ObjectPoolKey<T>`。
- 不要用同一个 name 创建不同类型的 ObjectPool。
- `GameObjectPool` 的正常入口是 `ResourceManager.InstantiateAsync` 和 `ResourceManager.RecycleInstance`。
- 不要直接把没有 `AssetInstanceTracker` 的对象塞入 GameObjectPool。
- Prefab 池回调只处理实例内部状态，不处理资源引用计数。
- 高频 Prefab 应设置 `MaxInactiveCount`，避免高峰后长时间占用内存。
- 大型场景或玩法结束时，应结合 Resource 清理和 Pool 清理。
- 对象池不是所有对象都必须使用；创建成本低、生命周期清晰且数量少的对象可以直接 new。

## 常见问题

### ObjectPool 取回对象还有旧数据

检查是否在 `onRelease` 或对象自身方法中清理状态。ObjectPool 不会自动调用统一 Clear，除非你在回调中指定。

### ReferencePool 对象状态错乱

检查 `IReference.Clear()` 是否完整重置所有字段。

### GameObject 重复出现或同一对象被多处使用

检查是否重复回收。编辑器或 Development Build 下启用 `EnableDuplicateReleaseCheck` 可发现问题。

### Prefab 回收入池后资源仍在内存

Pool 只管理实例，不管理 YooAsset handle。需要调用 Resource 模块清理 API。

### GameObjectPool 清了但场景中对象还在

GameObjectPool 只清理 inactive 实例。active 实例仍由业务持有，需要业务回收或销毁。

### Pool 根节点丢失

查看 `GameObjectPoolBucketSnapshot.RootExists` 和 `Warning`。正常情况下根节点由 `PoolManager.InitializeAsync()` 创建。