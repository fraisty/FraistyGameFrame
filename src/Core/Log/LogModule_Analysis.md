# Log 模块新功能详细设计

## 设计边界

`Assets/FRFrame/Core/Log` 是 FRFrame 的统一日志模块，负责日志门面、等级过滤、编译期裁剪、Unity Console 输出、文件输出、日志格式化、堆栈捕获和 Handler 扩展。

Log 模块后续增强重点：补齐门面 API 一致性、完善异常上下文、提升文件日志性能、明确日志保留策略、修正 UTC 时间一致性、降低格式化 GC、提供运行时动态配置、补充诊断辅助能力，并为远程日志和崩溃上报预留 Handler 模板。

编辑器日志配置入口已经集成，本设计不重复实现配置入口。后续 Editor 侧只补充调试辅助工具，例如打开日志目录、复制日志路径、查看运行时配置和 Handler 状态。

## 当前基础

当前 Log 模块已具备：

- `FRLog` 静态日志门面。
- `FRLogger` 核心日志调度器。
- `FRLogManager` 接入框架生命周期。
- `FRLogConfig` 日志配置。
- `FRLogLevel` 日志等级。
- `FRLogEntry` 单条日志数据。
- `IFRLogHandler` 输出处理器扩展接口。
- `UnityFRLogHandler` 输出到 Unity Console。
- `FileFRLogHandler` 输出到本地文件。
- `FRLogDefineSymbols` 编译宏常量。
- `FRLogStackTraceUtility` 堆栈捕获工具。
- Debug / Info / Warning / Error / Fatal 五级日志。
- Tag、Unity Object Context、Exception 日志。
- 编译期日志裁剪和运行期最低等级过滤。
- 可选文件输出、文件大小轮转、旧日志清理、错误日志立即 Flush。

## 功能一：补齐格式化日志重载

### 目标

让 Debug、Warning、Error、Fatal 与 Info 具备一致的格式化 API，减少业务层字符串拼接和手动 `string.Format`，统一日志写法。

### 调整范围

- `Assets/FRFrame/Core/Log/FRLog.cs`
- `Assets/FRFrame/Core/Log/FRLogger.cs`

### 新增 API

```csharp
public static void Debug<T>(string format, T arg);
public static void Debug<T1, T2>(string format, T1 arg1, T2 arg2);
public static void Debug<T1, T2, T3>(string format, T1 arg1, T2 arg2, T3 arg3);

public static void Warning<T>(string format, T arg);
public static void Warning<T1, T2>(string format, T1 arg1, T2 arg2);
public static void Warning<T1, T2, T3>(string format, T1 arg1, T2 arg2, T3 arg3);

public static void Error<T>(string format, T arg);
public static void Error<T1, T2>(string format, T1 arg1, T2 arg2);
public static void Error<T1, T2, T3>(string format, T1 arg1, T2 arg2, T3 arg3);

public static void Fatal<T>(string format, T arg);
public static void Fatal<T1, T2>(string format, T1 arg1, T2 arg2);
public static void Fatal<T1, T2, T3>(string format, T1 arg1, T2 arg2, T3 arg3);
```

带 tag 版本：

```csharp
public static void Debug<T>(string tag, string format, T arg);
public static void Warning<T>(string tag, string format, T arg);
public static void Error<T>(string tag, string format, T arg);
public static void Fatal<T>(string tag, string format, T arg);
```

### 行为规则

- 保持现有 `Info<T...>` 行为不变。
- 新增重载继续受 `[Conditional]` 编译宏控制。
- 运行期仍经过 `MinimumLevel` 过滤。
- 不建议第一版直接提供 `params object[]` 作为主入口，避免常规调用产生数组分配。

### 验收标准

- Debug / Warning / Error / Fatal 均支持 1-3 参数格式化。
- 带 tag 和不带 tag 写法一致。
- 被宏裁剪的日志调用不会进入运行时代码。

## 功能二：补齐 Exception Context 重载

### 目标

让异常日志能完整携带 Unity `Object context`，在 Unity Console 中支持点击定位相关对象，同时保持当前异常日志用法。

### 调整范围

- `Assets/FRFrame/Core/Log/FRLog.cs`
- `Assets/FRFrame/Core/Log/FRLogger.cs`

### 新增 API

```csharp
public static void Exception(Exception exception, Object context);
public static void Exception(Exception exception, string message, Object context);
public static void Exception(string tag, Exception exception, Object context);
public static void Exception(string tag, Exception exception, string message, Object context);
```

### 行为规则

- 旧 API 保留并委托到新重载。
- `context` 传入 `FRLogEntry.Context`。
- `Exception` 日志等级继续按当前实现映射到 Error 或专用异常输出。
- `message` 为空时只输出异常信息。

### 验收标准

- 异常日志可携带 Unity 对象上下文。
- Unity Console 中 context 点击定位行为正常。
- 现有异常日志调用不需要修改。

## 功能三：异步文件日志 Handler

### 目标

降低大量文件日志写入对主线程的影响，避免移动端或慢 I/O 场景中同步写文件造成卡顿。

### 新增类型

建议新增文件：

- `Assets/FRFrame/Core/Log/AsyncFileFRLogHandler.cs`
- `Assets/FRFrame/Core/Log/FRLogFileWriteMode.cs`

```csharp
public enum FRLogFileWriteMode
{
    Sync,
    Async
}
```

`FRLogConfig` 扩展：

```csharp
public FRLogFileWriteMode FileWriteMode = FRLogFileWriteMode.Sync;
public int AsyncFileQueueCapacity = 4096;
public int AsyncFileBatchSize = 64;
public int AsyncFileFlushIntervalMs = 1000;
public FRLogAsyncQueueOverflowPolicy AsyncQueueOverflowPolicy = FRLogAsyncQueueOverflowPolicy.DropNewest;
```

新增溢出策略：

```csharp
public enum FRLogAsyncQueueOverflowPolicy
{
    DropNewest,
    DropOldest,
    Block,
    WriteSync
}
```

### 内部设计

- 主线程 `Handle` 只创建待写入记录并入队。
- 后台线程按批次写入文件。
- Error / Fatal 可触发立即 flush 信号。
- `Shutdown()` 必须等待队列内日志写完或达到超时。
- 记录丢弃数量，供诊断 API 查询。

### 行为规则

- 默认仍使用同步文件日志，保持兼容。
- 开启异步后，日志顺序在单 Handler 内保持入队顺序。
- 当队列满时按策略处理，并至少记录丢弃计数。
- Unity API 不在后台线程调用。

### 验收标准

- Async 模式下主线程不直接执行文件写入。
- `Shutdown()` 后队列内日志尽量落盘。
- 队列溢出策略生效。
- Error / Fatal 可及时 flush。

## 功能四：文件日志保留策略明确化

### 目标

让文件日志删除旧文件的副作用更明确，支持按数量、天数和 session 维度控制保留策略。

### 调整范围

- `Assets/FRFrame/Core/Log/FRLogConfig.cs`
- `Assets/FRFrame/Core/Log/FileFRLogHandler.cs`
- `Assets/FRFrame/Core/Log/AsyncFileFRLogHandler.cs`

### 新增配置

```csharp
public bool EnableLogRetention = true;
public int MaxRetainedFileCount = 10;
public int MaxRetainedDays = 0;
public bool GroupLogBySession = false;
public int MaxRetainedSessionCount = 0;
```

### 行为规则

- `EnableLogRetention = false` 时不主动删除旧日志。
- `MaxRetainedFileCount > 0` 时按文件数量保留最新日志。
- `MaxRetainedDays > 0` 时删除超过天数的日志。
- `GroupLogBySession = true` 时每次运行使用独立 session 目录。
- 初始化时输出当前日志目录和保留策略。

### 验收标准

- 日志删除行为可通过配置关闭。
- 文件数量和天数保留策略可独立生效。
- 初始化日志能说明当前保留策略。

## 功能五：UTC 时间一致性

### 目标

修正日志条目时间与日志文件名时间可能不一致的问题，避免跨时区或 UTC 配置下排查困难。

### 调整范围

- `Assets/FRFrame/Core/Log/FRLogger.cs`
- `Assets/FRFrame/Core/Log/FileFRLogHandler.cs`
- `Assets/FRFrame/Core/Log/AsyncFileFRLogHandler.cs`

### 设计方案

文件名时间生成统一使用 `FRLogConfig.UseUtcTime`：

```csharp
DateTime now = config.UseUtcTime ? DateTime.UtcNow : DateTime.Now;
```

### 行为规则

- `UseUtcTime = true` 时，日志条目和文件名均使用 UTC。
- `UseUtcTime = false` 时，日志条目和文件名均使用本地时间。
- 文件名可追加 `UTC` 标记，降低人工识别成本。

### 验收标准

- 文件名时间与日志内容时间来源一致。
- 切换 `UseUtcTime` 后行为明确。

## 功能六：格式化 GC 优化

### 目标

降低高频日志格式化带来的 GC，尤其是运行期保留的 Info / Warning 日志。

### 调整范围

- `Assets/FRFrame/Core/Log/FRLogger.cs`
- `Assets/FRFrame/Core/Log/FRLog.cs`

### 设计方案

新增等级预检查 API：

```csharp
public static bool IsEnabled(FRLogLevel level);
public bool IsLevelEnabled(FRLogLevel level);
```

格式化调用先判断等级：

```csharp
if (!FRLogger.Instance.IsLevelEnabled(level))
    return;
```

可选新增内部格式化工具：

```csharp
internal static class FRLogFormatUtility
{
    public static string Format<T>(string format, T arg);
    public static string Format<T1, T2>(string format, T1 arg1, T2 arg2);
    public static string Format<T1, T2, T3>(string format, T1 arg1, T2 arg2, T3 arg3);
}
```

### 行为规则

- 被运行期等级过滤的日志不执行格式化。
- 常用 1-3 参数格式化避免 `object[]` 分配。
- 不改变最终输出文本格式。
- 高频复杂对象日志仍建议业务先判断 `FRLog.IsEnabled(level)`。

### 验收标准

- 关闭等级的格式化日志不产生格式化字符串。
- 常用格式化重载不产生 `params object[]` 分配。
- 输出内容与旧实现一致。

## 功能七：结构化日志上下文

### 目标

支持模块名、场景名、玩家 ID、会话 ID、资源 key 等结构化字段，便于文件日志、远程日志和崩溃上报分析。

### 新增类型

建议新增文件：

- `Assets/FRFrame/Core/Log/FRLogContext.cs`
- `Assets/FRFrame/Core/Log/FRLogScope.cs`

```csharp
public sealed class FRLogContext
{
    public IReadOnlyDictionary<string, string> GlobalFields { get; }
    public void SetGlobal(string key, string value);
    public bool RemoveGlobal(string key);
    public void ClearGlobal();
    public IDisposable BeginScope(string key, string value);
}
```

`FRLogEntry` 扩展：

```csharp
public IReadOnlyDictionary<string, string> Fields;
```

`FRLog` 新增入口：

```csharp
public static FRLogContext Context { get; }
public static IDisposable BeginScope(string key, string value);
public static void SetGlobalContext(string key, string value);
public static bool RemoveGlobalContext(string key);
```

### 行为规则

- 全局字段应用于所有后续日志。
- Scope 字段只在当前作用域内生效。
- 同名字段优先级：Scope 高于 Global。
- Handler 可选择是否输出结构化字段。
- Unity Console 可继续输出简化文本，文件和远程 Handler 可输出完整字段。

### 验收标准

- 可设置全局日志上下文。
- `using` scope 内日志带临时字段，退出后自动移除。
- 文件日志能输出结构化字段。

## 功能八：运行时动态配置 API

### 目标

支持开发期或线上调试时动态调整日志等级、输出目标和堆栈捕获策略，不需要重新初始化日志系统。

### 调整范围

- `Assets/FRFrame/Core/Log/FRLogger.cs`
- `Assets/FRFrame/Core/Log/FRLog.cs`
- `Assets/FRFrame/Core/Log/FRLogConfig.cs`

### 新增 API

```csharp
public static void SetMinimumLevel(FRLogLevel level);
public static void SetUnityOutputEnabled(bool enabled);
public static void SetFileOutputEnabled(bool enabled);
public static void SetStackTraceLevel(FRLogLevel level);
public static FRLogRuntimeConfigSnapshot GetRuntimeConfigSnapshot();
```

新增快照：

```csharp
public sealed class FRLogRuntimeConfigSnapshot
{
    public FRLogLevel MinimumLevel;
    public bool UnityOutputEnabled;
    public bool FileOutputEnabled;
    public bool CaptureStackTrace;
    public FRLogLevel StackTraceLevel;
    public int HandlerCount;
    public string FileDirectory;
}
```

### 行为规则

- 调整最低等级立即生效。
- 开关 Unity 输出时动态增删或启停 `UnityFRLogHandler`。
- 开关文件输出时动态增删或启停文件 Handler。
- 关闭文件输出时必须 flush 并 shutdown 文件 Handler。
- 不绕过编译宏裁剪，已被裁剪的调用不会恢复。

### 验收标准

- 运行时可调整最低日志等级。
- 运行时可启停 Unity/File 输出。
- 配置快照能反映当前状态。

## 功能九：日志统计与诊断快照

### 目标

从框架层观察日志系统运行状态，定位日志过量、文件队列堆积、Handler 异常和丢弃日志等问题。

### 新增类型

```csharp
public sealed class FRLogStatistics
{
    public int DebugCount;
    public int InfoCount;
    public int WarningCount;
    public int ErrorCount;
    public int FatalCount;
    public int ExceptionCount;
    public int DroppedCount;
    public int HandlerCount;
    public int FileQueueCount;
    public string CurrentLogFilePath;
}
```

新增 API：

```csharp
public static FRLogStatistics GetStatistics();
public static void ResetStatistics();
public FRLogStatistics CreateStatisticsSnapshot();
```

### 行为规则

- 每次成功进入日志调度时更新等级统计。
- 被运行期等级过滤的日志可不计入成功输出统计。
- 异步队列丢弃日志计入 `DroppedCount`。
- 统计 API 不暴露可修改内部状态。

### 验收标准

- 可查询各等级日志数量。
- 可查询 Handler 数和当前文件路径。
- Async 文件日志可查询队列和丢弃数量。

## 功能十：Editor 调试辅助工具

### 目标

在已有编辑器配置入口基础上补充调试辅助能力，不重复设计日志配置面板。

### 建议路径

`Assets/FRFrame/Editor/Diagnostics/FRFrameLogDiagnosticsWindow.cs`

菜单：`FRFrame/Diagnostics/Log Diagnostics`

### 展示内容

- 当前最低日志等级。
- Unity/File 输出开关状态。
- Handler 列表。
- 当前日志目录。
- 当前日志文件路径。
- 各等级日志计数。
- 异步文件队列长度和丢弃数量。

### 操作能力

- Refresh。
- Open Log Directory。
- Copy Log Directory。
- Copy Current Log File Path。
- Flush。
- Reset Statistics。

### 行为规则

- 不修改工程资产。
- 不替代已有日志配置入口。
- Play Mode 下显示运行时状态；非 Play Mode 下可显示默认配置或提示未运行。

### 验收标准

- 能打开和复制日志目录。
- 能查看运行时配置和统计。
- 能手动 Flush 文件日志。

## 功能十一：远程日志 Handler 模板

### 目标

为后续线上日志上传提供标准扩展模板，验证 `IFRLogHandler` 对远程输出场景的适配方式。

### 新增类型

建议新增示例或模板文件：

- `Assets/FRFrame/Core/Log/RemoteFRLogHandler.cs`
- `Assets/FRFrame/Core/Log/RemoteFRLogOptions.cs`

```csharp
public sealed class RemoteFRLogOptions
{
    public string Endpoint;
    public FRLogLevel MinimumLevel = FRLogLevel.Warning;
    public int BatchSize = 20;
    public float FlushInterval = 5f;
    public int MaxQueueCount = 1000;
    public float SampleRate = 1f;
}
```

```csharp
public sealed class RemoteFRLogHandler : IFRLogHandler
{
    public void Handle(in FRLogEntry entry);
    public void Flush();
    public void Shutdown();
}
```

### 行为规则

- 默认不启用远程日志。
- 只提供模板和扩展点，不绑定具体服务商。
- 支持等级过滤、批量上报、采样和队列上限。
- 上传失败不能影响主流程。
- 敏感字段脱敏策略由后续功能或业务配置提供。

### 验收标准

- 可通过 Handler 机制接入远程日志。
- 默认工程行为不发生变化。
- 上传失败不会抛到业务主流程。

## 功能十二：崩溃上报 Handler 模板

### 目标

为接入 Bugly、Firebase Crashlytics、Sentry 等崩溃上报 SDK 预留统一模板。

### 新增类型

建议新增：

- `Assets/FRFrame/Core/Log/CrashReportFRLogHandler.cs`
- `Assets/FRFrame/Core/Log/ICrashReportAdapter.cs`

```csharp
public interface ICrashReportAdapter
{
    void SetUserId(string userId);
    void SetCustomKey(string key, string value);
    void Log(string message);
    void RecordException(Exception exception, string message = null);
}
```

```csharp
public sealed class CrashReportFRLogHandler : IFRLogHandler
{
    public CrashReportFRLogHandler(ICrashReportAdapter adapter, FRLogLevel minimumLevel = FRLogLevel.Error);
    public void Handle(in FRLogEntry entry);
    public void Flush();
    public void Shutdown();
}
```

### 行为规则

- 默认不绑定任何第三方 SDK。
- Error / Fatal / Exception 可转发到崩溃上报。
- 结构化字段可同步为 custom key。
- Handler 内部捕获 SDK 异常，避免日志系统反向影响主流程。

### 验收标准

- 可通过 Adapter 接入具体崩溃 SDK。
- 默认不增加第三方依赖。
- Error / Exception 能转发给 Adapter。

## 阶段规划

### 第一阶段：API 补齐与一致性

1. 补齐 Debug / Warning / Error / Fatal 格式化重载。
2. 补齐 Exception context 重载。
3. 增加 `IsEnabled` / `IsLevelEnabled`。
4. 修正文件名时间与 `UseUtcTime` 一致性。
5. 明确文件保留策略配置。

### 第二阶段：性能与诊断

1. 增加异步文件日志 Handler。
2. 优化格式化 GC。
3. 增加运行时动态配置 API。
4. 增加日志统计与诊断快照。
5. 增加 Editor 调试辅助窗口。

### 第三阶段：线上能力

1. 增加结构化日志上下文。
2. 增加远程日志 Handler 模板。
3. 增加崩溃上报 Handler 模板。
4. 增加采样、限流、脱敏策略。
5. 增加日志上传流程。

## 推荐实现顺序

优先实现不改变语义的 API 补齐：格式化重载、Exception context、UTC 文件名一致性和保留策略配置。随后实现 `IsEnabled`、格式化 GC 优化和运行时动态配置。异步文件日志、结构化上下文、远程日志和崩溃上报涉及线程、数据格式和线上策略，应分阶段单独测试。

## 测试建议

- Debug / Info / Warning / Error / Fatal 基础输出正确。
- 带 tag 和不带 tag 输出格式正确。
- 1-3 参数格式化重载输出正确。
- 被运行期等级过滤的格式化日志不执行格式化。
- Exception 日志可携带 message、tag 和 context。
- Unity Console 中 Warning / Error 等级正确。
- 文件日志开启时可写入并轮转。
- `UseUtcTime` 同时影响日志条目和文件名。
- 文件保留策略可按数量、天数关闭或生效。
- 异步文件日志能批量写入并在 Shutdown 时 flush。
- 异步队列溢出策略和丢弃计数正确。
- 运行时调整最低日志等级立即生效。
- 运行时启停 Unity/File 输出不丢异常。
- 结构化全局字段和 scope 字段输出正确。
- 远程日志 Handler 上传失败不影响主流程。
- 崩溃上报 Handler 不引入强制第三方依赖。
