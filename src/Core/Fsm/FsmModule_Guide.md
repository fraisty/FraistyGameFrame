# Fsm 模块详细说明

## 模块定位

`Assets/FRFrame/Core/Fsm` 是 FRFrame 的有限状态机模块，用于承载框架流程、业务流程、可等待任务流程和工具调试流程。模块保持代码驱动、类型安全、轻量运行时的定位：业务常规切状态优先使用 `ChangeState<T>()`，运行时诊断、配置化流程、存档恢复和编辑器工具可以使用 `ChangeState(Type)` 或 `ChangeState(string)`。

常见适用场景：

- 资源初始化、版本检查、资源下载等框架流程。
- 战斗角色 AI、怪物行为、技能阶段。
- UI 引导流程、新手教程、剧情步骤。
- 可 await 的任务流程，例如加载、下载、准备战斗。
- Editor 诊断窗口中查看、暂停、恢复和调试状态机。

## 核心类型

### FsmManager

文件：`Assets/FRFrame/Core/Fsm/FsmManager.cs`

统一管理 FSM 的创建、更新、查询、移除和统计，通常通过 `FRFM.Fsm` 访问。

常用 API：

```csharp
T CreateFsm<T>(FsmCreateOption option = null) where T : FsmController, new();
FsmController CreateFsm(Type type, FsmCreateOption option = null);
bool RemoveFsm(FsmController fsm);
int RemoveByOwner(object owner);
int RemoveByType<T>() where T : FsmController;
T GetFirstFsm<T>() where T : FsmController;
List<T> GetFsms<T>() where T : FsmController;
void GetFsms<T>(List<T> results) where T : FsmController;
int FillFsms<T>(IList<T> results) where T : FsmController;
List<FsmDebugInfo> GetAllDebugInfo();
void GetAllDebugInfo(List<FsmDebugInfo> results);
FsmManagerStatistics GetStatistics();
```

`GetFsms<T>(List<T>)`、`FillFsms<T>`、`GetAllDebugInfo(List<FsmDebugInfo>)` 会复用调用方传入列表，适合诊断窗口或高频查询。

### FsmCreateOption

创建 FSM 时的配置：

```csharp
var fsm = FRFM.Fsm.CreateFsm<BattleFsm>(new FsmCreateOption
{
    Owner = this,
    EnableLog = true,
    AutoRemoveWhenTaskDone = true
});
```

字段含义：

- `Owner`：FSM 归属对象，可用于 `RemoveByOwner` 批量移除。
- `EnableLog`：是否输出 FSM 流程日志，日志走 `FRLog`，tag 为 `FSM`。
- `AutoRemoveWhenTaskDone`：任务型 FSM 完成、失败或取消后由 `FsmManager` 自动移除。

### FsmController

状态机控制器基类，负责状态注册、切换、黑板、暂停恢复、流转约束和调试快照。

重要属性：

```csharp
string Name;
string CurrentStateName;
FsmState CurState;
bool IsDisposed;
bool IsPaused;
object Owner;
float RunTime;
int StateCount;
IReadOnlyList<string> StateNames;
IReadOnlyList<FsmStateChangeRecord> StateHistory;
FsmTransitionPolicy TransitionPolicy;
int MaxStateHistoryCount;
```

常用 API：

```csharp
void Initialize();
void Update(float elapseSeconds, float realElapseSeconds);
void Pause();
void Resume();
T GetState<T>() where T : FsmState;
bool TryGetState(Type stateType, out FsmState state);
bool TryGetState(string stateName, out FsmState state);
bool ChangeState<T>() where T : FsmState;
bool ChangeState(Type stateType);
bool ChangeState(string stateName);
FsmDebugInfo GetDebugInfo();
void Dispose();
```

状态注册在子类 `OnInitialize` 中完成：

```csharp
public sealed class BattleFsm : FsmController
{
    protected override void OnInitialize()
    {
        AddState<BattlePrepareState>();
        AddState<BattleRunState>();
        AddState<BattleEndState>();

        ChangeState<BattlePrepareState>();
    }
}
```

## 状态基类 FsmState

文件：`Assets/FRFrame/Core/Fsm/FsmState.cs`

状态生命周期：

```csharp
public virtual void OnInit(FsmController fsmController);
public virtual void OnEnter();
public virtual void OnExit();
public virtual void OnUpdate(float elapseSeconds, float realElapseSeconds);
public virtual void OnPause();
public virtual void OnResume();
```

状态内部常用保护方法：

```csharp
ChangeState<T>();
ChangeState(Type stateType);
ChangeState(string stateName);
SetData(string key, object data);
GetData<T>(string key);
TryGetData<T>(string key, out T value);
TryGetData<T>(out T value);
GetDataOrDefault<T>(string key, T defaultValue = default);
RemoveData(string key);
```

示例：

```csharp
public sealed class BattlePrepareState : FsmState
{
    public override void OnEnter()
    {
        SetData("StartTime", UnityEngine.Time.time);
    }

    public override void OnUpdate(float elapseSeconds, float realElapseSeconds)
    {
        if (TryGetData("Ready", out bool ready) && ready)
            ChangeState<BattleRunState>();
    }
}
```

## 黑板数据

黑板支持三种 key 模式：

### 字符串 key

```csharp
fsm.SetData("speed", 5f);
float speed = fsm.GetData<float>("speed");
```

### Tag 类型 key

```csharp
public struct SpeedTag {}

fsm.SetData<SpeedTag>(5f);
float speed = fsm.GetData<float, SpeedTag>();
```

### 值类型自身作为 key

```csharp
fsm.SetData(100);
int hp = fsm.GetData<int>();
```

安全读取 API：

```csharp
bool TryGetData(string key, out object value);
bool TryGetData(Type type, out object value);
bool TryGetData<T>(string key, out T value);
bool TryGetData<T>(out T value);
bool TryGetData<TValue, TTag>(out TValue value);
T GetDataOrDefault<T>(string key, T defaultValue = default);
T GetDataOrDefault<T>(T defaultValue = default);
```

`GetData<T>` 保持严格语义，缺 key 或类型错误时可能抛异常。业务流程中可恢复的读取优先使用 `TryGetData` 或 `GetDataOrDefault`。

## 状态切换

### 泛型切换

业务代码优先使用：

```csharp
ChangeState<BattleRunState>();
```

未注册状态会抛异常，便于开发期尽早暴露错误。

### Type 切换

适合编辑器、自动化测试、反射工具：

```csharp
fsm.ChangeState(typeof(BattleRunState));
```

### string 切换

适合配置、存档、控制台命令：

```csharp
fsm.ChangeState("FRFrame.BattleRunState");
fsm.ChangeState("BattleRunState");
```

查找逻辑支持 FullName、StateName、类型简名。简名冲突时返回失败并输出错误日志。

## 状态切换约束

`FsmTransitionPolicy`：

```csharp
public enum FsmTransitionPolicy
{
    Free,
    WarningOnly,
    Strict
}
```

默认 `Free`，不限制跳转。配置 transition 后可限制复杂流程误跳：

```csharp
public sealed class GuideFsm : FsmController
{
    protected override void OnInitialize()
    {
        AddState<GuideStartState>();
        AddState<GuideStepState>();
        AddState<GuideEndState>();

        TransitionPolicy = FsmTransitionPolicy.Strict;
        AddTransition<GuideStartState, GuideStepState>();
        AddTransition<GuideStepState, GuideEndState>();

        ChangeState<GuideStartState>();
    }
}
```

规则：

- `Free`：任意切换。
- `WarningOnly`：非法切换输出 warning，但仍允许。
- `Strict`：非法切换输出 error 并拒绝。
- 未配置任何 transition 时不限制。

## 暂停与恢复

```csharp
fsm.Pause();
fsm.Resume();
```

行为：

- `RunTime` 继续累积，表示 FSM 存活时间。
- 暂停期间不调用当前状态 `OnUpdate`。
- 当前状态会收到 `OnPause` / `OnResume`。
- 重复 Pause/Resume 不重复触发生命周期。

适合暂停游戏、弹窗阻塞、切后台、剧情等待。

## 任务型 FSM

`FsmTaskController` 用于可等待流程，支持完成、取消、失败结果。

常用 API：

```csharp
void BeginTask(Type completeStateType, CancellationToken token = default);
void BeginTask<TCompleteState>(CancellationToken token = default) where TCompleteState : FsmState;
UniTask<FsmTaskController> WaitAsync();
UniTask<FsmTaskResult> WaitResultAsync();
void Complete();
void Cancel(string reason = null);
void Fail(string error = null, Exception exception = null);
void ResetTaskState();
```

结果字段：

```csharp
TaskState State;
bool IsSuccess;
bool IsCanceled;
bool IsFailed;
string CompleteStateName;
string Error;
Exception Exception;
string CancelReason;
FsmTaskController Controller;
```

示例：

```csharp
var fsm = FRFM.Fsm.CreateFsm<ResourceDownloadFsm>(new FsmCreateOption
{
    Owner = this,
    EnableLog = true,
    AutoRemoveWhenTaskDone = true
});

fsm.BeginTask<DownloadCompleteState>();
FsmTaskResult result = await fsm.WaitResultAsync();

if (result.IsSuccess)
    FRLog.Info("Download", "Download complete");
else if (result.IsCanceled)
    FRLog.Warning("Download", result.CancelReason);
else
    FRLog.Error("Download", result.Error);
```

`ValidateCompleteStateOnBegin` 默认为 true，`BeginTask` 时会校验完成状态是否已注册。特殊动态注册场景可关闭。

## AsyncFsmState

`AsyncFsmState` 用于状态进入后启动异步逻辑，并在退出状态时自动取消。

```csharp
public sealed class LoadConfigState : AsyncFsmState
{
    protected override async UniTask OnEnterAsync(CancellationToken cancellationToken)
    {
        await LoadConfigAsync(cancellationToken);
        ChangeState<NextState>();
    }
}
```

规则：

- 进入状态时创建 CTS。
- 退出状态时取消并释放 CTS。
- 异步异常会通过 `FRLog.Exception("FSM", ...)` 输出。
- 如果所属 FSM 是 `FsmTaskController`，异常会调用 `Fail`。

异步逻辑应观察 `cancellationToken`，避免状态退出后继续操作旧状态。

## 调试与诊断

### FsmDebugInfo

```csharp
public class FsmDebugInfo
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
    public IReadOnlyList<FsmStateChangeRecord> StateHistory;
}
```

黑板快照只输出 key，不输出 value。

### 状态切换历史

```csharp
fsm.MaxStateHistoryCount = 32;
IReadOnlyList<FsmStateChangeRecord> history = fsm.StateHistory;
fsm.ClearStateHistory();
```

`MaxStateHistoryCount <= 0` 表示不记录。

### 管理器统计

```csharp
FsmManagerStatistics statistics = FRFM.Fsm.GetStatistics();
```

包含：活跃数量、总创建数、总销毁数、自动移除数、任务 FSM 数。

### Editor 窗口

菜单：`FRFrame/Diagnostics/FSM Diagnostics`

可查看 FSM 列表、Owner、当前状态、RunTime、暂停状态、任务状态、黑板 key 和状态历史。

## 日志规范

Fsm 模块内部统一使用 `FRLog`，tag 为 `FSM`。

```csharp
FRLog.Info("FSM", "...");
FRLog.Warning("FSM", "...");
FRLog.Error("FSM", "...");
```

单个 FSM 的流程日志由 `FsmCreateOption.EnableLog` 控制。

## 推荐工作流

1. 为业务流程创建一个 `FsmController` 或 `FsmTaskController` 子类。
2. 在 `OnInitialize` 注册所有状态，并切入初始状态。
3. 在状态类中只处理本状态职责，通过 `ChangeState<T>()` 跳转。
4. 用黑板传递少量运行时上下文，复杂数据优先封装为明确类型。
5. 可等待流程使用 `FsmTaskController.WaitResultAsync()`。
6. 需要防误跳时配置 transition，并选择 `WarningOnly` 或 `Strict`。
7. 异步状态优先继承 `AsyncFsmState`，并观察 token。
8. 调试时使用 `GetDebugInfo()`、`GetStatistics()` 或 Editor 诊断窗口。

## 使用规范

- 常规业务切状态优先使用 `ChangeState<T>()`。
- `ChangeState(Type/string)` 主要用于工具、配置、存档和测试。
- 状态不要持有长期外部资源；若持有，应在 `OnExit` 释放。
- `OnUpdate` 中避免高频分配。
- 黑板严格读取用于必需数据，非必需数据使用 Try API。
- 任务 FSM 应明确成功、取消、失败语义，不要用同一个 Fail 表达取消。
- 开启 `AutoRemoveWhenTaskDone` 前确认外部不会继续持有并使用该 FSM。
- 不要直接调用 Unity `Debug.Log` 输出 FSM 框架日志，统一使用 `FRLog`。
