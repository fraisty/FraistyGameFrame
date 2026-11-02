# Fsm 模块新功能详细设计

## 设计边界

`Assets/FRFrame/Core/Fsm` 保持轻量、代码驱动、类型安全的有限状态机定位。常规业务状态切换继续以 `ChangeState<T>()` 为主，新增 `ChangeState(Type)` 和可选 `ChangeState(string)` 用于编辑器调试、配置化流程、存档恢复和工具链场景。

Fsm 模块后续增强重点：接入 `FRLog`、提升黑板安全性、完善任务型 FSM 结果语义、提供无 GC 查询、诊断可视化、暂停恢复和状态切换治理能力。

## 当前基础

当前 Fsm 模块已具备：

- `FsmManager` 统一创建、更新、移除 FSM。
- `FsmController` 管理状态集合和当前状态。
- `FsmState` 提供 `OnInit` / `OnEnter` / `OnUpdate` / `OnExit` 生命周期。
- 黑板支持 string key、泛型 tag key、类型 key。
- `FsmTaskController` 支持 `UniTask` 等待和任务完成状态。
- `FsmCreateOption` 支持 Owner、EnableLog、AutoRemoveWhenTaskDone。
- `FsmDebugInfo` 提供基础调试快照。
- Resource 模块 YooAsset 初始化/下载流程已实际使用 FSM。

## 功能一：FSM 日志接入 FRLog

### 目标

统一框架日志输出，修正 warning/error 等级不准确的问题，让 FSM 日志受 Log 模块统一配置、文件输出和编译宏控制。

### 调整范围

- `Assets/FRFrame/Core/Fsm/FsmController.cs`
- `Assets/FRFrame/Core/Fsm/FsmManager.cs`
- 其它直接使用 `Debug.Log` 的 Fsm 文件。

### 设计方案

新增或调整内部日志方法：

```csharp
protected void LogDebug(string message)
{
    if (EnableLog) FRLog.Debug("FSM", message);
}

protected void LogInfo(string message)
{
    if (EnableLog) FRLog.Info("FSM", message);
}

protected void LogWarning(string message)
{
    if (EnableLog) FRLog.Warning("FSM", message);
}

protected void LogError(string message)
{
    if (EnableLog) FRLog.Error("FSM", message);
}
```

`FsmManager` 创建、移除、自动清理任务 FSM 的日志也改为 `FRLog`。

### 行为规则

- `EnableLog` 继续控制单个 FSM 是否输出流程日志。
- warning/error 必须使用对应 `FRLog.Warning` / `FRLog.Error`。
- 不改变 FSM 状态机行为。

### 验收标准

- Fsm 模块不再直接使用 `Debug.Log` 输出框架日志。
- warning/error 在 Unity Console 中等级正确。
- 文件日志开启时可记录 FSM 日志。

## 功能二：黑板安全读取增强

### 目标

降低黑板 key 不存在、类型错误时的定位成本，同时保留严格读取 API。

### 新增 API

```csharp
public bool TryGetData(string key, out object value);
public bool TryGetData(Type type, out object value);
public T GetDataOrDefault<T>(string key, T defaultValue = default);
public T GetDataOrDefault<T>(T defaultValue = default);
public bool TryGetData<T>(out T value);
public bool TryGetData<TValue, TTag>(out TValue value);
```

### 类型错误日志

当 `TryGetData<T>` 发现 key 存在但类型不匹配时，输出：

- FSM 名称。
- key。
- 期望类型。
- 实际类型。

```csharp
FRLog.Warning("FSM", $"Blackboard type mismatch. Fsm:{Name}, Key:{key}, Expected:{typeof(T).FullName}, Actual:{value.GetType().FullName}");
```

### 行为规则

- `GetData<T>` 保持严格语义，可继续抛异常。
- `TryGetData` 不抛异常，返回 false。
- `GetDataOrDefault` 不抛异常，失败返回默认值。

### 验收标准

- 缺 key 时可通过 Try API 安全处理。
- 类型错误时有明确 warning。
- 现有使用 `GetData<T>` 的代码不受影响。

## 功能三：非泛型状态切换

### 目标

支持编辑器调试、配置化流程、存档恢复和工具链通过 `Type` 或状态名驱动状态切换。

### 新增 API

```csharp
public bool ChangeState(Type stateType);
public bool ChangeState(string stateName);
public bool TryGetState(Type stateType, out FsmState state);
public bool TryGetState(string stateName, out FsmState state);
```

### 泛型 API 保留

```csharp
public void ChangeState<T>() where T : FsmState;
```

泛型仍是业务主入口。

### Type 切换适用场景

- Editor 调试窗口选择状态并强制跳转。
- 自动化测试通过反射切换状态。
- 可视化状态图工具驱动运行时跳转。

### string 切换适用场景

- 配置表中保存状态名。
- 存档或调试快照记录当前状态名。
- 外部脚本或控制台命令输入状态名。

### 状态名规则

建议状态名默认使用 `state.GetType().FullName`，同时保留简短 `Name` 用于显示。

查找优先级：

1. FullName 精确匹配。
2. Name 精确匹配。
3. 简名匹配，若冲突则失败并输出 error。

### 验收标准

- `ChangeState(Type)` 可切换到已注册状态。
- `ChangeState(string)` 可按 FullName 或 Name 切换。
- 状态不存在时返回 false 或输出明确错误。
- `ChangeState<T>()` 行为不变。

## 功能四：状态切换合法性约束

### 目标

为复杂 FSM 提供可选 transition 约束，避免误跳状态。

### 新增类型

```csharp
public sealed class FsmTransitionRule
{
    public Type From;
    public Type To;
}

public enum FsmTransitionPolicy
{
    Free,
    WarningOnly,
    Strict
}
```

### FsmController 新增 API

```csharp
public FsmTransitionPolicy TransitionPolicy { get; set; } = FsmTransitionPolicy.Free;
protected void AddTransition<TFrom, TTo>() where TFrom : FsmState where TTo : FsmState;
protected void AddTransition(Type from, Type to);
protected virtual bool CanChangeState(Type from, Type to);
```

### 行为规则

- `Free`：保持现有任意切换。
- `WarningOnly`：非法切换输出 warning，但仍允许切换。
- `Strict`：非法切换输出 error 并拒绝。
- 未配置 transition 时，默认不限制。

### 验收标准

- 默认行为完全兼容现有代码。
- 配置 transition 后能检测非法跳转。
- 子类可重写 `CanChangeState` 实现动态条件。

## 功能五：暂停与恢复

### 目标

支持业务临时暂停状态机更新，例如暂停游戏、切后台、弹窗阻塞流程、剧情等待。

### 新增 API

```csharp
public bool IsPaused { get; private set; }
public void Pause();
public void Resume();
```

### FsmState 新增生命周期

```csharp
protected virtual void OnPause();
protected virtual void OnResume();
```

### 行为规则

- 暂停后 `FsmController.Update` 继续累积或不累积 `RunTime` 需要明确。
- 建议 `RunTime` 表示 FSM 存活时间，继续累积。
- 暂停时不调用当前状态 `OnUpdate`。
- 重复 Pause/Resume 不重复触发生命周期。

### 验收标准

- Pause 后状态 OnUpdate 不再执行。
- Resume 后恢复 OnUpdate。
- 当前状态能收到 OnPause/OnResume。

## 功能六：任务型 FSM 结果语义增强

### 目标

让 `FsmTaskController` 能清晰表达完成、取消、失败原因，便于业务等待流程后判断结果。

### 新增类型

```csharp
public sealed class FsmTaskResult
{
    public TaskState State;
    public bool IsSuccess => State == TaskState.Completed;
    public bool IsCanceled => State == TaskState.Canceled;
    public bool IsFailed => State == TaskState.Failed;
    public string CompleteStateName;
    public string Error;
    public Exception Exception;
    public string CancelReason;
    public FsmTaskController Controller;
}
```

### FsmTaskController 新增 API

```csharp
public string Error { get; private set; }
public Exception Exception { get; private set; }
public string CancelReason { get; private set; }
public UniTask<FsmTaskResult> WaitResultAsync();
public void Cancel(string reason = null);
public void Fail(string error = null, Exception exception = null);
```

### 兼容性

保留现有：

```csharp
UniTask<FsmTaskController> WaitAsync();
void Cancel();
void Fail();
```

旧 API 委托到新 API。

### 行为规则

- `Complete()` 设置 `TaskState.Completed`。
- `Cancel(reason)` 设置 `TaskState.Canceled` 和原因。
- `Fail(error, exception)` 设置 `TaskState.Failed`。
- `Dispose()` 如果任务未完成，可设置 canceled 或保持现有取消语义，但需文档化。

### 验收标准

- 等待者可区分成功、失败、取消。
- 失败时保留错误信息和异常。
- 旧 API 不破坏现有调用。

## 功能七：任务完成状态注册校验

### 目标

`BeginTask(Type completeStateType)` 时提前发现完成状态未注册的问题。

### 新增行为

```csharp
public bool ValidateCompleteStateOnBegin { get; set; } = true;
```

`BeginTask` 中检查：

- `completeStateType` 是否继承 `FsmState`。
- 该状态是否已注册。

失败时：

- 输出 `FRLog.Error`。
- 可直接 `Fail()`。

### 验收标准

- 完成状态未注册时能提前报错。
- 可通过配置关闭校验，兼容特殊动态注册场景。

## 功能八：无 GC 查询 API

### 目标

避免编辑器窗口或运行时频繁查询 FSM 时产生 List 分配。

### FsmManager 新增 API

```csharp
public void GetFsms<T>(List<T> results) where T : FsmController;
public void GetAllDebugInfo(List<FsmDebugInfo> results);
public int FillFsms<T>(IList<T> results) where T : FsmController;
```

### 行为规则

- 方法不主动 Clear 传入列表，还是主动 Clear 需统一约定。
- 建议主动 Clear，减少调用方误用。
- 返回填充数量。

### 兼容性

保留现有：

```csharp
List<T> GetFsms<T>();
List<FsmDebugInfo> GetAllDebugInfo();
```

### 验收标准

- 新 API 可复用外部 List。
- 旧 API 行为不变。

## 功能九：FsmDebugInfo 增强

### 目标

为 Editor 诊断和运行时排查提供更完整快照。

### 字段扩展

```csharp
public sealed class FsmDebugInfo
{
    public string Name;
    public string TypeName;
    public string OwnerName;
    public string OwnerTypeName;
    public string CurrentStateName;
    public string CurrentStateTypeName;
    public float RunTime;
    public int StateCount;
    public bool IsDisposed;
    public bool IsPaused;
    public bool IsTask;
    public TaskState TaskState;
    public IReadOnlyList<string> StateNames;
    public IReadOnlyList<string> BlackboardKeys;
}
```

### 注意事项

黑板只输出 key，不输出 value，避免泄露复杂对象或产生过多 GC。

### 验收标准

- 能区分普通 FSM 和任务 FSM。
- 能看到 Owner、当前状态、暂停状态和黑板 key。

## 功能十：状态切换历史记录

### 目标

辅助排查复杂流程跳转错误。

### 新增类型

```csharp
public sealed class FsmStateChangeRecord
{
    public string FromState;
    public string ToState;
    public float Time;
    public int Frame;
}
```

### FsmController 新增字段/API

```csharp
public int MaxStateHistoryCount { get; set; } = 32;
public IReadOnlyList<FsmStateChangeRecord> StateHistory { get; }
public void ClearStateHistory();
```

### 行为规则

- 每次成功切换状态记录一条。
- 超出最大数量移除最旧记录。
- `MaxStateHistoryCount <= 0` 表示不记录。

### 验收标准

- 可查看最近状态跳转链路。
- 不无限增长。

## 功能十一：FSM 运行统计

### 目标

从框架层观察 FSM 创建、销毁和异常情况。

### 新增类型

```csharp
public sealed class FsmManagerStatistics
{
    public int ActiveCount;
    public int TotalCreated;
    public int TotalDisposed;
    public int TotalAutoRemoved;
    public int TaskFsmCount;
}
```

### FsmManager 新增 API

```csharp
public FsmManagerStatistics GetStatistics();
```

### 验收标准

- 创建、移除、自动移除任务 FSM 时更新统计。
- 可用于 Editor 诊断窗口展示。

## 功能十二：FSM Editor 诊断窗口

### 目标

可视化查看当前运行中的 FSM，辅助调试资源流程、战斗流程、UI 流程。

### 建议路径

`Assets/FRFrame/Editor/Diagnostics/FRFrameFsmDiagnosticsWindow.cs`

菜单：`FRFrame/Diagnostics/FSM Diagnostics`

### 展示内容

- FSM 总数。
- 按类型过滤。
- 按 Owner 过滤。
- 当前状态。
- 运行时间。
- StateCount。
- IsPaused。
- TaskState。
- Blackboard keys。
- State history。

### 操作能力

- Refresh。
- Pause / Resume 指定 FSM。
- Remove 指定 FSM。
- 强制 ChangeState(Type) 到指定状态。

### 安全要求

- 强制切状态、Remove 操作只在 Play Mode 下可用。
- 危险操作需要确认。
- 不修改工程资产。

## 功能十三：配置化流程支持

### 目标

为后续剧情、新手引导、关卡脚本、工具生成流程预留配置驱动能力。

### 基础能力

依赖前述：

- `ChangeState(Type)`。
- `ChangeState(string)`。
- Transition 约束。
- State debug info。

### 可选新增类型

```csharp
public sealed class FsmGraphConfig
{
    public string InitialState;
    public List<FsmGraphTransition> Transitions;
}

public sealed class FsmGraphTransition
{
    public string From;
    public string To;
}
```

第一版不建议直接实现完整图编辑器，只提供状态名切换和 transition 规则即可。

## 功能十四：异步状态生命周期辅助

### 目标

降低状态内启动 UniTask 后，状态退出或 FSM 销毁时回调继续执行造成的风险。

### 新增基类

```csharp
public abstract class AsyncFsmState : FsmState
{
    protected CancellationToken CancellationToken { get; }
    protected override void OnExit();
    protected virtual UniTask OnEnterAsync(CancellationToken cancellationToken);
}
```

### 行为规则

- 状态进入时创建 CTS。
- 状态退出时取消 CTS。
- 异步逻辑必须观察 token。
- 异常通过 `FRLog.Exception` 记录，并可选择让 FSM Fail。

### 验收标准

- 状态退出后异步回调不再继续操作旧状态。
- 异常有日志。

## 阶段规划

### 第一阶段：稳定性与易用性

1. FSM 日志接入 `FRLog`。
2. 黑板安全读取 API。
3. `ChangeState(Type)`。
4. `ChangeState(string)`。
5. 任务型 FSM 失败/取消原因记录。
6. 任务完成状态注册校验。

### 第二阶段：诊断与性能

1. 无 GC 查询 API。
2. `FsmDebugInfo` 增强。
3. 状态切换历史记录。
4. FSM 运行统计。
5. FSM Editor 诊断窗口。

### 第三阶段：流程治理能力

1. 状态切换合法性约束。
2. 暂停/恢复能力。
3. `FsmTaskResult`。
4. 配置化流程基础支持。
5. `AsyncFsmState`。

## 推荐实现顺序

优先实现不改变行为的稳定性功能：日志接入、黑板 Try API、ChangeState(Type)。随后补齐任务结果语义和诊断快照。Transition、Pause/Resume、AsyncFsmState 涉及流程语义，应在后续阶段单独测试。

## 测试建议

- 创建 FSM 后初始状态正确。
- `ChangeState<T>` 行为保持不变。
- `ChangeState(Type)` 和 `ChangeState(string)` 能切换已注册状态。
- 非法状态切换按策略处理。
- 黑板 Try API 缺 key 和类型错误返回 false。
- Pause 后 OnUpdate 不执行。
- Task FSM 成功/失败/取消结果正确。
- AutoRemoveWhenTaskDone 仍生效。
- 无 GC 查询 API 填充结果正确。
- State history 不超过上限。
