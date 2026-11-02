# Event 模块详细说明

## 模块定位

`Assets/FRFrame/Core/Event` 是 FRFrame 的事件系统模块，提供同步强类型事件总线、延迟事件队列、异步事件通道、订阅生命周期管理、ScriptableObject 事件资产、资产事件桥接和运行时诊断能力。

Event 模块的核心模型是：同步、强类型、可预测。默认 `Publish` / `Subscribe` 语义保持同步立即分发；需要等待异步监听器时使用独立的 `PublishAsync` / `SubscribeAsync`，不要把同步事件和异步流程混在一起。

常见适用场景：

- UI、玩法、资源、战斗等系统间解耦通信。
- UI View / HUD / 场景对象的短生命周期订阅。
- 需要跨帧延迟派发的事件。
- 需要等待多个系统异步准备完成的流程。
- 使用 ScriptableObject 事件资产进行可视化触发和桥接。
- 运行时诊断订阅泄漏、owner 未解绑、Post 队列堆积。

## 核心类型

### EventManager

文件：`Assets/FRFrame/Core/Event/EventManager.cs`

事件系统运行时宿主，由框架初始化流程统一创建，通常通过 `FRFM.Event` 访问。

常用 API：

```csharp
IDisposable Subscribe<TEvent>(Action<TEvent> handler, object owner = null, int priority = 0);
IDisposable SubscribeOnce<TEvent>(Action<TEvent> handler, object owner = null, int priority = 0);
IDisposable SubscribeAsync<TEvent>(Func<TEvent, UniTask> handler, object owner = null, int priority = 0);
IDisposable SubscribeAsyncOnce<TEvent>(Func<TEvent, UniTask> handler, object owner = null, int priority = 0);
int Unsubscribe<TEvent>(Action<TEvent> handler);
int UnsubscribeAll(object owner);
void Publish<TEvent>(TEvent evt);
UniTask PublishAsync<TEvent>(TEvent evt, CancellationToken cancellationToken = default);
void Post<TEvent>(TEvent evt);
void Flush();
int Flush(int maxCount);
int ClearPostQueue();
int ClearPostQueue<TEvent>();
int ClearPostQueue(Type eventType);
int GetListenerCount<TEvent>();
int GetPostQueueCount();
EventBusSnapshot CreateSnapshot();
void ClearAll();
```

`EventManager.Update()` 会按 `Bus.Options.MaxFlushCountPerFrame` 自动 Flush 延迟队列。

### EventBus

文件：`Assets/FRFrame/Core/Event/EventBus.cs`

强类型事件总线核心实现。一般业务使用 `FRFM.Event`，只有需要独立事件通道时才直接创建 `EventBus`。

特性：

- 强类型事件订阅和分发。
- once 订阅。
- owner 批量解绑。
- priority 优先级。
- 分发期间安全增删监听。
- Post / Flush 延迟分发。
- 独立异步监听器通道。
- 监听器异常策略。
- 运行时统计和快照。

## 基础同步事件

### 定义事件

推荐使用 struct 或小型 class 表达事件参数：

```csharp
public readonly struct PlayerLevelUpEvent
{
    public readonly int PlayerId;
    public readonly int Level;

    public PlayerLevelUpEvent(int playerId, int level)
    {
        PlayerId = playerId;
        Level = level;
    }
}
```

### 订阅与发布

```csharp
private readonly EventSubscriptionScope _scope = new EventSubscriptionScope();

private void OnEnable()
{
    FRFM.Event.Subscribe<PlayerLevelUpEvent>(OnPlayerLevelUp, owner: this)
        .AddTo(_scope);
}

private void OnDisable()
{
    _scope.Clear();
}

private void OnPlayerLevelUp(PlayerLevelUpEvent evt)
{
    FRLog.Info("Event", $"Player {evt.PlayerId} level up to {evt.Level}");
}

private void LevelUp()
{
    FRFM.Event.Publish(new PlayerLevelUpEvent(1001, 5));
}
```

### 一次性订阅

```csharp
FRFM.Event.SubscribeOnce<TutorialFinishedEvent>(OnTutorialFinished, owner: this)
    .AddTo(_scope);
```

首次触发后自动 Dispose。

### 优先级

```csharp
FRFM.Event.Subscribe<DamageEvent>(ApplyShield, priority: 100);
FRFM.Event.Subscribe<DamageEvent>(ApplyHpDamage, priority: 0);
```

priority 越大越先执行；同 priority 按订阅顺序执行。

## 订阅生命周期

### EventSubscriptionScope

适合 OnEnable / OnDisable 生命周期：

```csharp
private readonly EventSubscriptionScope _scope = new EventSubscriptionScope();

private void OnEnable()
{
    FRFM.Event.Subscribe<ItemChangedEvent>(OnItemChanged).AddTo(_scope);
}

private void OnDisable()
{
    _scope.Clear();
}

private void OnDestroy()
{
    _scope.Dispose();
}
```

`Clear()` 会释放已收集订阅，但 scope 可继续复用。`Dispose()` 后 scope 进入终态，不允许继续 Add。

### AddTo(MonoBehaviour)

语法糖和防漏工具：

```csharp
FRFM.Event.Subscribe<ItemChangedEvent>(OnItemChanged)
    .AddTo(this);
```

该扩展会自动给当前 GameObject 添加 `EventSubscriptionScopeComponent`，并在 `OnDestroy` 时统一 Dispose。

注意：如果订阅发生在 `OnEnable`，但只用 `.AddTo(this)`，对象 Disable 后仍会收到事件，直到 Destroy。需要 Disable 即解绑时，应使用显式 `EventSubscriptionScope` 并在 `OnDisable` 调用 `Clear()`。

### owner 批量解绑

```csharp
FRFM.Event.Subscribe<BattleEndEvent>(OnBattleEnd, owner: this);
FRFM.Event.UnsubscribeAll(this);
```

适合系统、服务或长期对象按 owner 管理订阅。

## 延迟事件 Post / Flush

`Post` 会把事件加入队列，等待 `Flush` 时分发：

```csharp
FRFM.Event.Post(new GoldChangedEvent(100));
```

默认 `EventManager.Update()` 每帧 Flush。手动控制：

```csharp
FRFM.Event.Flush();
FRFM.Event.Flush(maxCount: 20);
```

行为规则：

- `Flush()` 只处理本轮开始时已有事件。
- Flush 期间新增的 Post 留到下一次 Flush。
- `Flush(int maxCount)` 最多处理指定数量。
- 可通过 `ClearPostQueue<TEvent>()` 清理指定类型延迟事件。

## 队列治理配置

`EventBusOptions`：

```csharp
FRFM.Event.Bus.Options.MaxPostQueueCount = 1000;
FRFM.Event.Bus.Options.MaxFlushCountPerFrame = 100;
FRFM.Event.Bus.Options.OverflowPolicy = EventPostOverflowPolicy.WarningAndDropNewest;
```

溢出策略：

```csharp
public enum EventPostOverflowPolicy
{
    WarningAndDropNewest,
    DropNewest,
    DropOldest,
    Throw
}
```

默认不限制队列数量。高频事件或线上环境建议设置队列上限和单帧 Flush 上限。

## 异步事件通道

异步事件独立于同步 `Publish`：

```csharp
FRFM.Event.SubscribeAsync<LoginSuccessEvent>(PrepareInventoryAsync, owner: this, priority: 100)
    .AddTo(_scope);

await FRFM.Event.PublishAsync(new LoginSuccessEvent(userId), cancellationToken);
```

执行规则：

- 按 priority 降序执行。
- 同 priority 按订阅顺序执行。
- 当前实现按顺序 await，一个监听器完成后再执行下一个。
- `SubscribeAsyncOnce` 首次执行后自动移除。
- 异步监听器同样支持 owner 批量解绑。

适合场景：

- 登录成功后等待背包、任务、邮件等系统准备完成。
- 切场景前等待存档、网络同步、资源释放完成。
- 新手引导等待弹窗、剧情播放完成后继续。

不需要等待结果的普通通知继续使用同步 `Publish`。

## 监听器异常策略

配置：

```csharp
FRFM.Event.Bus.Options.ExceptionPolicy = EventExceptionPolicy.LogAndContinue;
```

策略：

```csharp
public enum EventExceptionPolicy
{
    Throw,
    LogAndContinue,
    CollectAndThrow
}
```

- `Throw`：默认行为，监听器异常直接向上传播。
- `LogAndContinue`：用 `FRLog.Exception("Event", ...)` 记录并继续后续监听器。
- `CollectAndThrow`：执行完所有监听器后抛出 `EventDispatchAggregateException`。

开发期建议使用默认 `Throw` 及时暴露问题；运行期关键通道可按需要使用 `LogAndContinue`。

## ScriptableObject 事件资产

### 无参 GameEventAsset

```csharp
[CreateAssetMenu(menuName = "Events/Void Game Event")]
public sealed class VoidGameEventAsset : GameEventAsset
{
}
```

API：

```csharp
IDisposable Register(Action listener, object owner = null, int priority = 0);
IDisposable RegisterOnce(Action listener, object owner = null, int priority = 0);
int Unregister(Action listener);
int UnregisterAll(object owner);
void Raise();
void Post();
void Flush();
int GetListenerCount();
GameEventAssetSnapshot CreateSnapshot();
void ClearAll();
```

### 单参数 GameEventAsset<T>

```csharp
[CreateAssetMenu(menuName = "Events/Int Game Event")]
public sealed class IntGameEventAsset : GameEventAsset<int>
{
}
```

API：

```csharp
IDisposable Register(Action<T> listener, object owner = null, int priority = 0);
IDisposable RegisterOnce(Action<T> listener, object owner = null, int priority = 0);
int Unregister(Action<T> listener);
int UnregisterAll(object owner);
void Raise(T value);
void Post(T value);
void Flush();
int GetListenerCount();
GameEventAssetSnapshot CreateSnapshot();
void ClearAll();
```

快照字段：事件资产名、监听数量、Post 队列数量、Raise 次数、最近 Raise 时间。

## AssetEventBridge

资产事件桥接到代码事件总线：

```csharp
FRFM.Event.Bridge.Bind(playerDeadAsset, () => new PlayerDeadEvent(1)).AddTo(_scope);
FRFM.Event.Bridge.Bind<int, GoldChangedEvent>(goldChangedAsset, value => new GoldChangedEvent(value)).AddTo(_scope);
```

推荐在需要策划或场景资产触发代码事件时使用。桥接后业务系统仍订阅强类型代码事件。

## 事件录制

`EventRecorder` 用于调试关键事件流：

```csharp
var recorder = new EventRecorder();
recorder.Start();
recorder.Record(new BattleStartEvent());
IReadOnlyList<EventRecord> records = recorder.Stop();
```

记录字段：事件类型、类型名、Json、时间、帧号。该功能用于调试，不保证完整复现所有副作用。

## 诊断 API

### EventBusSnapshot

```csharp
EventBusSnapshot snapshot = FRFM.Event.CreateSnapshot();
```

包含：

- 总监听数量。
- 事件类型数量。
- owner 数量。
- Post 队列长度。
- 各事件类型 listener count、once count、优先级范围。
- 各 owner subscription count。

### 常用统计查询

```csharp
int count = FRFM.Event.GetListenerCount<PlayerLevelUpEvent>();
int queueCount = FRFM.Event.GetPostQueueCount();
int ownerCount = FRFM.Event.Bus.GetOwnerCount();
bool hasListener = FRFM.Event.Bus.HasListener<PlayerLevelUpEvent>();
```

### Editor 窗口

菜单：`FRFrame/Diagnostics/Event Diagnostics`

可查看监听数量、事件类型、owner、Post 队列和各类快照，并可清理 Post 队列。

## 推荐工作流

### 普通业务通知

1. 定义强类型事件。
2. 发布方调用 `FRFM.Event.Publish(evt)`。
3. 订阅方在 OnEnable Subscribe，在 OnDisable Clear。
4. 使用 `owner` 或 scope 管理生命周期。

### UI / 场景对象订阅

- Disable 后不应接收事件：使用 `EventSubscriptionScope` + `OnDisable.Clear()`。
- 只需 Destroy 自动释放：使用 `.AddTo(this)`。

### 需要跨帧或削峰

- 使用 `Post`，配置 `MaxFlushCountPerFrame`。
- 高频事件设置 `MaxPostQueueCount` 和溢出策略。

### 需要等待监听器

- 使用 `SubscribeAsync` / `PublishAsync`。
- 保持异步事件语义清晰，不要让同步事件承担等待流程。

### ScriptableObject 事件

- 资产事件用于 Inspector 连接和调试触发。
- 复杂业务仍桥接到强类型代码事件，避免业务散落在资产监听中。

## 使用规范

- 业务不要直接 `new EventManager()`，使用 `FRFM.Event`。
- 事件类型应表达清晰含义，避免用 `object` 或字符串事件名。
- 高频事件参数可结合 Pool 模块复用，但发布后不要再修改被监听器使用的数据。
- 订阅必须有释放策略：scope、owner 或 AddTo(MonoBehaviour)。
- 同步 `Publish` 默认可抛异常，监听器内异常会影响发布方。
- 异步事件只用于发布方确实需要等待的流程。
- Post 队列不是无限缓存，高频使用时必须考虑上限和清理。
- ScriptableObject 事件适合桥接和调试，不替代核心代码事件模型。
