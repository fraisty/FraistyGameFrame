# Event 模块新功能详细设计

## 设计边界

`EventManager` 由 FRFrame 框架初始化流程统一创建和初始化，业务层不直接 `new EventManager()`。因此本设计不把运行时未初始化保护作为核心需求，只优化 `Test` 示例代码，避免示例误导业务绕过框架生命周期。

Event 模块继续保持当前核心模型：同步、强类型、可预测的事件总线。新增能力应围绕诊断、生命周期防漏、延迟队列治理、异步事件独立通道和 ScriptableObject 事件调试展开，不改变现有 `Publish` / `Subscribe` 语义。

## 当前基础

当前 Event 模块已具备：

- `EventBus` 强类型事件总线。
- `Publish` 同步分发。
- `Post` / `Flush` 延迟分发。
- `SubscribeOnce` 一次性订阅。
- 订阅优先级。
- owner 批量解绑。
- 分发期间安全增删监听。
- `EventSubscriptionScope` 订阅作用域。
- `GameEventAsset` / `GameEventAsset<T>` ScriptableObject 事件通道。
- `AssetEventBridge` 资产事件到代码事件桥接。
- `EventManager` 接入框架生命周期并每帧 Flush。

## 功能一：优化示例代码

### 目标

确保 `Assets/FRFrame/Core/Event/Test` 下示例体现真实框架使用方式，避免示例直接 `new EventManager()` 导致未初始化误用。

### 调整范围

- `Assets/FRFrame/Core/Event/Test/EventBusExample.cs`
- `Assets/FRFrame/Core/Event/Test/AssetBridgeExample.cs`

### 设计方案

示例统一改为以下两种之一：

1. 通过 `FRFM.Event` 演示事件订阅和发布。
2. 如果需要演示独立 `EventBus`，则明确示例对象是 `EventBus`，不使用 `EventManager`。

推荐示例：

```csharp
public sealed class EventBusExample : MonoBehaviour
{
    private readonly EventSubscriptionScope _scope = new EventSubscriptionScope();

    private void OnEnable()
    {
        FRFM.Event.Subscribe<PlayerLevelUpEvent>(OnPlayerLevelUp, owner: this)
            .AddTo(_scope);
    }

    private void OnDisable()
    {
        _scope.Dispose();
    }

    private void OnPlayerLevelUp(PlayerLevelUpEvent evt)
    {
        FRLog.Info("Event", $"Player level up : {evt.Level}");
    }
}
```

### 验收标准

- 示例代码不再直接 `new EventManager()`。
- 示例能体现订阅释放方式。
- 示例中桥接逻辑通过 `FRFM.Event.Bridge` 表达。

## 功能二：事件诊断与统计 API

### 目标

让框架和业务能在运行期观察事件系统状态，定位订阅泄漏、延迟队列堆积、owner 未解绑等问题。

### 新增类型

建议新增文件：

- `Assets/FRFrame/Core/Event/EventBusSnapshot.cs`
- `Assets/FRFrame/Core/Event/EventListenerSnapshot.cs`
- `Assets/FRFrame/Core/Event/EventOwnerSnapshot.cs`

```csharp
public sealed class EventBusSnapshot
{
    public int TotalListenerCount;
    public int EventTypeCount;
    public int OwnerCount;
    public int PostQueueCount;
    public IReadOnlyList<EventListenerSnapshot> Listeners;
    public IReadOnlyList<EventOwnerSnapshot> Owners;
}

public sealed class EventListenerSnapshot
{
    public Type EventType;
    public int ListenerCount;
    public int OnceListenerCount;
    public int MaxPriority;
    public int MinPriority;
}

public sealed class EventOwnerSnapshot
{
    public string OwnerName;
    public Type OwnerType;
    public int SubscriptionCount;
}
```

### EventBus 新增 API

```csharp
public int GetListenerCount<TEvent>();
public int GetListenerCount(Type eventType);
public int GetTotalListenerCount();
public int GetEventTypeCount();
public int GetOwnerCount();
public int GetOwnerSubscriptionCount(object owner);
public int GetPostQueueCount();
public bool HasListener<TEvent>();
public EventBusSnapshot CreateSnapshot();
```

### 内部设计

当前 `EventBus` 已有：

- `_listenerLists`
- `_subscriptionsByOwner`
- `_postQueue`

诊断 API 可基于这三个结构生成快照。需要给 `IListenerList` 补充只读统计能力：

```csharp
int Count { get; }
int OnceCount { get; }
int MaxPriority { get; }
int MinPriority { get; }
```

### EventManager 转发 API

```csharp
public int GetListenerCount<TEvent>();
public int GetPostQueueCount();
public EventBusSnapshot CreateSnapshot();
```

### 验收标准

- 可查询单事件类型监听数量。
- 可查询延迟队列长度。
- 可查询 owner 订阅数量。
- 快照不暴露可修改内部集合。

## 功能三：延迟队列治理

### 目标

避免 `Post` 队列无限增长，支持清理指定类型延迟事件，并允许控制单帧 Flush 开销。

### 新增配置

建议新增：

```csharp
public sealed class EventBusOptions
{
    public int MaxPostQueueCount = 0;
    public int MaxFlushCountPerFrame = 0;
    public EventPostOverflowPolicy OverflowPolicy = EventPostOverflowPolicy.WarningAndDropNewest;
}

public enum EventPostOverflowPolicy
{
    WarningAndDropNewest,
    DropNewest,
    DropOldest,
    Throw
}
```

### EventBus 新增 API

```csharp
public EventBusOptions Options { get; }
public int ClearPostQueue();
public int ClearPostQueue<TEvent>();
public int ClearPostQueue(Type eventType);
public int Flush(int maxCount);
```

### 行为规则

- `MaxPostQueueCount <= 0` 表示不限制。
- `Post` 时如果队列超限，根据策略处理。
- `Flush()` 维持当前语义：只处理本轮开始时已有事件。
- `Flush(maxCount)` 最多处理指定数量。
- `EventManager.Update()` 可使用 `Options.MaxFlushCountPerFrame` 控制每帧分发上限。

### 清理指定类型事件

当前 `_postQueue` 是 `Queue<IQueuedEvent>`。要支持指定类型清理，`IQueuedEvent` 需暴露：

```csharp
Type EventType { get; }
```

清理时重建队列，保留其它类型事件。

### 验收标准

- 队列超限时行为符合配置。
- 可清理某类型延迟事件。
- 可限制单次 Flush 数量。
- 默认配置保持现有行为。

## 功能四：监听器异常策略

### 目标

让事件系统可在开发期严格暴露异常，也可在运行期避免一个监听器异常中断后续监听器。

### 新增类型

```csharp
public enum EventExceptionPolicy
{
    Throw,
    LogAndContinue,
    CollectAndThrow
}

public sealed class EventDispatchException
{
    public Type EventType;
    public object Owner;
    public Exception Exception;
}

public sealed class EventDispatchAggregateException : Exception
{
    public IReadOnlyList<EventDispatchException> Exceptions { get; }
}
```

### EventBusOptions 扩展

```csharp
public EventExceptionPolicy ExceptionPolicy = EventExceptionPolicy.Throw;
```

### 行为规则

- `Throw`：保持当前行为。
- `LogAndContinue`：使用 `FRLog.Exception` 记录，继续执行后续监听器。
- `CollectAndThrow`：执行全部监听器后抛出聚合异常。

### 验收标准

- 默认策略保持当前行为。
- `LogAndContinue` 下单个监听器异常不会中断其它监听器。
- 异常日志包含事件类型、owner、handler 信息。

## 功能五：MonoBehaviour 生命周期绑定语法糖

### 目标

为大量短生命周期 MonoBehaviour 订阅场景提供自动解绑能力，降低 UI、HUD、场景对象、临时 View 忘记释放订阅的风险。

该功能只作为语法糖和防漏解绑工具，不改变 EventBus 核心。

### 新增类型

建议新增文件：

- `Assets/FRFrame/Core/Event/EventSubscriptionScopeComponent.cs`
- `Assets/FRFrame/Core/Event/EventMonoBehaviourExtensions.cs`

```csharp
public sealed class EventSubscriptionScopeComponent : MonoBehaviour
{
    private readonly EventSubscriptionScope _scope = new EventSubscriptionScope();

    public void Add(IDisposable subscription);
    private void OnDestroy();
}
```

扩展 API：

```csharp
public static IDisposable AddTo(this IDisposable subscription, MonoBehaviour owner);
```

### 使用示例

```csharp
public sealed class InventoryView : MonoBehaviour
{
    private void OnEnable()
    {
        FRFM.Event.Subscribe<ItemChangedEvent>(OnItemChanged)
            .AddTo(this);
    }
}
```

### 注意事项

如果订阅发生在 `OnEnable`，而自动解绑发生在 `OnDestroy`，对象 Disable 后仍会收到事件。若需要 Disable 即解绑，仍应使用显式 `EventSubscriptionScope` 并在 `OnDisable` Dispose。

### 验收标准

- MonoBehaviour 销毁时自动 Dispose 所有 AddTo 的订阅。
- 多次 AddTo 复用同一个组件。
- 不影响现有 `EventSubscriptionScope` 用法。

## 功能六：独立异步事件通道

### 目标

支持发布方需要等待监听器完成的流程，但不改变现有同步 `Publish` 语义。

典型场景：

- 登录成功后等待多个系统异步准备完成再进入大厅。
- 场景切换前等待存档、网络同步、资源释放完成。
- 新手引导事件等待弹窗和剧情播放完成再继续流程。

### 新增 API

```csharp
public IDisposable SubscribeAsync<TEvent>(Func<TEvent, UniTask> handler, object owner = null, int priority = 0);
public UniTask PublishAsync<TEvent>(TEvent evt, CancellationToken cancellationToken = default);
```

可选 once：

```csharp
public IDisposable SubscribeAsyncOnce<TEvent>(Func<TEvent, UniTask> handler, object owner = null, int priority = 0);
```

### 执行顺序

建议第一版采用顺序 await：

1. 按 priority 降序。
2. 同 priority 按订阅顺序。
3. 一个 handler 完成后再执行下一个。

原因：顺序执行更可预测，也更符合现有事件优先级语义。

后续可扩展并行策略：

```csharp
public enum EventAsyncDispatchMode
{
    Sequential,
    Parallel
}
```

### 异常策略

异步事件复用 `EventExceptionPolicy`：

- `Throw`：遇到异常立即抛出。
- `LogAndContinue`：记录异常并继续。
- `CollectAndThrow`：全部执行后汇总抛出。

### 生命周期规则

- 异步监听器也支持 owner 批量解绑。
- 分发开始时生成订阅快照。
- 分发过程中 Dispose 的监听器不影响当前快照是否继续执行，第一版可保持和同步事件一致。

### 验收标准

- 同步 `Publish` 行为不变。
- `PublishAsync` 能等待所有异步监听器完成。
- 异步监听器支持 priority、once、owner。
- 异常策略与同步事件一致。

## 功能七：ScriptableObject 事件增强

### 目标

提升 `GameEventAsset` 和 `GameEventAsset<T>` 的调试能力和延迟分发能力。

### 新增 API

无参事件：

```csharp
public void Post();
public void Flush();
public int GetListenerCount();
public GameEventAssetSnapshot CreateSnapshot();
```

单参数事件：

```csharp
public void Post(T value);
public void Flush();
public int GetListenerCount();
public GameEventAssetSnapshot CreateSnapshot();
```

### 新增快照

```csharp
public sealed class GameEventAssetSnapshot
{
    public string Name;
    public int ListenerCount;
    public int PostQueueCount;
    public int RaiseCount;
    public float LastRaiseTime;
}
```

### Editor 调试

可新增 Editor Inspector：

- 显示 listener count。
- 显示 raise count。
- 显示 last raise time。
- 提供无参事件手动 Raise 按钮。

### 验收标准

- ScriptableObject 事件支持延迟分发。
- 可查看监听数量和最近触发信息。
- 无参事件可在 Inspector 手动触发。

## 功能八：Event Editor 诊断窗口

### 目标

提供运行时事件系统可视化，辅助排查订阅泄漏和事件队列堆积。

### 建议路径

`Assets/FRFrame/Editor/Diagnostics/FRFrameEventDiagnosticsWindow.cs`

菜单：`FRFrame/Diagnostics/Event Diagnostics`

### 展示内容

- 总监听数量。
- 事件类型数量。
- owner 数量。
- Post 队列长度。
- 各事件类型 listener count。
- 各 owner subscription count。
- 可筛选事件类型和 owner。

### 操作能力

- Refresh。
- Clear Post Queue。
- Clear 指定事件类型订阅。
- ClearAll 需要二次确认或只在运行时调试阶段提供。

### 验收标准

- Play Mode 下能显示 `FRFM.Event` 快照。
- 不在每帧产生大量 GC。
- 清理操作不会误删工程资产。

## 功能九：事件录制与回放

### 目标

辅助复杂玩法流程调试，记录关键事件流并支持回放。

该功能属于高级调试能力，优先级低于诊断 API 和生命周期绑定。

### 新增类型

```csharp
public sealed class EventRecord
{
    public Type EventType;
    public string EventTypeName;
    public string Json;
    public float Time;
    public int Frame;
}

public sealed class EventRecorder
{
    public bool IsRecording { get; }
    public void Start();
    public IReadOnlyList<EventRecord> Stop();
    public void Clear();
}
```

### 设计限制

- 只记录可序列化事件。
- 默认不启用。
- 高频事件需要过滤。
- 回放只适合调试，不保证完全复现所有副作用。

## 阶段规划

### 第一阶段：稳定性与诊断基础

1. 优化 `Test` 示例代码。
2. 增加 listener count、queue count、owner count。
3. 增加 EventBus snapshot。
4. 增加指定类型 Post 队列清理。

### 第二阶段：生命周期与队列治理

1. 增加 `EventBusOptions`。
2. 增加 Post 队列容量和 Flush 上限。
3. 增加 `AddTo(MonoBehaviour)`。
4. 增加 ScriptableObject 事件 listener count 和调试信息。

### 第三阶段：高级能力

1. 增加异常策略配置。
2. 增加 `SubscribeAsync` / `PublishAsync` 独立异步通道。
3. 增加 Event Editor 诊断窗口。
4. 增加事件录制与回放。

## 推荐实现顺序

优先实现不会改变现有行为的功能：诊断 API、示例优化、MonoBehaviour AddTo。随后处理延迟队列治理。异步事件和异常策略涉及语义设计，应单独实现和测试。

## 测试建议

- Subscribe / Publish 基础分发。
- SubscribeOnce 只触发一次。
- priority 顺序正确。
- owner UnsubscribeAll 正确。
- 分发期间新增/删除安全。
- Post / Flush 只处理本轮开始时队列。
- ClearPostQueue<T> 只清指定类型。
- AddTo(MonoBehaviour) 在 OnDestroy 自动释放。
- PublishAsync 按顺序等待监听器。
- 异常策略在同步和异步下表现一致。
