# Log 模块详细说明

## 模块定位

`Assets/FRFrame/Core/Log` 是 FRFrame 的统一日志模块，提供编译期日志裁剪、运行时日志等级控制、Unity Console 输出、同步/异步文件日志、结构化上下文、异常日志、统计快照、远端日志和崩溃上报扩展模板。

Log 模块的核心模型是：业务统一使用 `FRLog` 静态入口，底层由 `FRLogger` 管理配置、handler、格式化、统计和生命周期。框架内部和游戏业务都不要直接使用 Unity `Debug.Log` 输出常规日志。

常见适用场景：

- 框架模块和业务系统输出统一格式日志。
- 开发期在 Unity Console 查看详细日志。
- 真机或线上环境写入本地日志文件。
- 高频日志使用异步文件写入降低主线程阻塞。
- 给一次请求、一次战斗、一个账号或一个流程追加结构化上下文。
- 捕获异常、记录堆栈并按需要立即 Flush。
- 在 Editor 诊断窗口查看当前日志配置、统计和日志文件路径。

## 核心类型

### FRLog

文件：`Assets/FRFrame/Core/Log/FRLog.cs`

业务和框架代码的首选日志入口。

常用 API：

```csharp
FRLog.Debug("message");
FRLog.Info("message");
FRLog.Warning("message");
FRLog.Error("message");
FRLog.Fatal("message");

FRLog.InfoT("Battle", "Battle started");
FRLog.WarningT("Resource", "Missing asset {0}", location);
FRLog.ErrorT("Network", "Request failed {0}", errorCode);

FRLog.Exception(exception);
FRLog.Exception("Battle", exception, "Skill execution failed");

bool enabled = FRLog.IsEnabled(FRLogLevel.Debug);
FRLog.SetMinimumLevel(FRLogLevel.Warning);
FRLog.SetUnityOutputEnabled(true);
FRLog.SetFileOutputEnabled(true);
FRLog.SetStackTraceLevel(FRLogLevel.Warning);

FRLogRuntimeConfigSnapshot config = FRLog.GetRuntimeConfigSnapshot();
FRLogStatistics statistics = FRLog.GetStatistics();
FRLog.ResetStatistics();
```

`FRLog` 方法带有 `[Conditional]`，在非 Editor 构建中可以通过编译符号控制日志调用是否进入最终编译结果。

### FRLogger

文件：`Assets/FRFrame/Core/Log/FRLogger.cs`

日志系统核心实现，负责初始化、handler 管理、日志格式化、运行时配置和统计。业务代码通常不直接调用，除非正在扩展日志模块。

常用 API：

```csharp
FRLogger.Initialize(FRLogConfig config = null);
FRLogger.AddHandler(IFRLogHandler handler);
FRLogger.RemoveHandler(IFRLogHandler handler);
FRLogger.ClearHandlers();
FRLogger.Flush();
FRLogger.Shutdown();
FRLogger.CreateDefaultConfig();
```

框架初始化流程会通过 `FRLogManager.InitializeAsync()` 初始化日志系统。

### FRLogManager

文件：`Assets/FRFrame/Core/Log/FRLogManager.cs`

FRFrame 管理器生命周期中的日志宿主，通常通过 `FRFM.Log` 间接访问。

```csharp
FRFM.Log.Flush();
```

`FRLogManager.Destroy()` 会调用 `FRLogger.Shutdown()`，释放 handler 和文件资源。

## 日志等级

文件：`Assets/FRFrame/Core/Log/FRLogLevel.cs`

日志等级：

```csharp
public enum FRLogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Fatal
}
```

等级过滤使用 `FRLogConfig.MinimumLevel`。低于当前最小等级的日志不会写入 handler。

推荐：

- `Debug`：开发期细节、临时流程信息。
- `Info`：关键流程节点，例如模块初始化完成、战斗开始。
- `Warning`：可恢复但需要关注的问题。
- `Error`：业务失败、资源缺失、异常流程。
- `Fatal`：严重错误，通常需要立刻定位。

## 编译期开关

文件：`Assets/FRFrame/Core/Log/FRLogDefineSymbols.cs`

`FRLog` 使用 `[Conditional]` 控制调用是否编译进入目标程序集。常用符号包括：

```csharp
FR_ENABLE_LOG
FR_LOG_DEBUG
FR_LOG_INFO
FR_LOG_WARNING
FR_LOG_ERROR
FR_LOG_FATAL
FR_LOG_DEBUG_AND_ABOVE
FR_LOG_INFO_AND_ABOVE
FR_LOG_WARNING_AND_ABOVE
FR_LOG_ERROR_AND_ABOVE
FR_LOG_FATAL_AND_ABOVE
FR_ENABLE_FILE_LOG
```

Editor 环境默认保留日志调用。非 Editor 构建中，应根据构建类型配置合适符号，避免线上包保留过多 Debug 日志。

## 配置 FRLogConfig

文件：`Assets/FRFrame/Core/Log/FRLogConfig.cs`

配置字段：

```csharp
var config = new FRLogConfig
{
    EnableUnityLog = true,
    EnableFileLog = true,
    MinimumLevel = FRLogLevel.Info,
    ShowTimestamp = true,
    ShowFrameCount = true,
    ShowThreadId = false,
    CaptureStackTrace = true,
    StackTraceMinLevel = FRLogLevel.Warning,
    FileLogDirectory = logDirectory,
    MaxLogFileSize = 10 * 1024 * 1024,
    MaxLogFileCount = 10,
    EnableLogRetention = true,
    MaxRetainedFileCount = 10,
    MaxRetainedDays = 7,
    GroupLogBySession = true,
    MaxRetainedSessionCount = 5,
    FlushImmediatelyOnError = true,
    FileNamePrefix = "FRLog",
    UseUtcTime = false,
    FileWriteMode = FRLogFileWriteMode.Async,
    AsyncFileQueueCapacity = 4096,
    AsyncFileBatchSize = 64,
    AsyncFileFlushIntervalMs = 1000,
    AsyncQueueOverflowPolicy = FRLogAsyncQueueOverflowPolicy.DropNewest
};

FRLogger.Initialize(config);
```

业务通常不需要重新初始化日志系统。需要临时调整时优先使用运行时配置 API。

## 输出 Handler

### UnityFRLogHandler

文件：`Assets/FRFrame/Core/Log/UnityFRLogHandler.cs`

输出到 Unity Console。开发期默认启用。

```csharp
FRLog.SetUnityOutputEnabled(true);
```

### FileFRLogHandler

文件：`Assets/FRFrame/Core/Log/FileFRLogHandler.cs`

同步写入本地文件，支持：

- 文件大小轮转。
- 保留文件数量。
- 保留天数。
- 按 session 分目录。
- 当前日志文件路径统计。

适合低频日志或工具场景。

### AsyncFileFRLogHandler

文件：`Assets/FRFrame/Core/Log/AsyncFileFRLogHandler.cs`

异步写入本地文件，主线程入队，后台线程批量写入。

```csharp
var config = FRLogger.CreateDefaultConfig();
config.EnableFileLog = true;
config.FileWriteMode = FRLogFileWriteMode.Async;
config.AsyncFileQueueCapacity = 4096;
config.AsyncFileBatchSize = 64;
config.AsyncFileFlushIntervalMs = 1000;
config.AsyncQueueOverflowPolicy = FRLogAsyncQueueOverflowPolicy.DropNewest;
FRLogger.Initialize(config);
```

队列溢出策略：

```csharp
public enum FRLogAsyncQueueOverflowPolicy
{
    DropNewest,
    DropOldest,
    Block,
    WriteSync
}
```

建议线上环境避免使用 `Block`，否则高频日志可能阻塞主线程。

### RemoteFRLogHandler

文件：`Assets/FRFrame/Core/Log/RemoteFRLogHandler.cs`

远端日志扩展模板，当前负责按等级、采样率和队列上限缓存日志，`Flush` 时清理队列。实际网络上传需要业务接入具体 SDK 或服务。

```csharp
var options = new RemoteFRLogOptions
{
    Endpoint = "https://example.invalid/log",
    MinimumLevel = FRLogLevel.Warning,
    BatchSize = 20,
    FlushInterval = 5f,
    MaxQueueCount = 1000,
    SampleRate = 1f
};

FRLogger.AddHandler(new RemoteFRLogHandler(options));
```

### CrashReportFRLogHandler

文件：`Assets/FRFrame/Core/Log/CrashReportFRLogHandler.cs`

崩溃上报扩展模板，通过 `ICrashReportAdapter` 适配第三方崩溃平台。

```csharp
public sealed class CrashAdapter : ICrashReportAdapter
{
    public void SetUserId(string userId) {}
    public void SetCustomKey(string key, string value) {}
    public void Log(string message) {}
    public void RecordException(Exception exception, string message = null) {}
}

FRLogger.AddHandler(new CrashReportFRLogHandler(new CrashAdapter()));
```

## 结构化上下文

文件：`Assets/FRFrame/Core/Log/FRLogContext.cs`

结构化上下文会输出到日志格式中的字段区域，适合为一段流程添加账号、战斗、请求等上下文。

### 全局上下文

```csharp
FRLog.SetGlobalContext("userId", userId);
FRLog.SetGlobalContext("server", serverId);

FRLog.InfoT("Login", "Login success");

FRLog.RemoveGlobalContext("userId");
```

全局字段适合整个运行期都有效的信息。

### Scope 上下文

```csharp
using (FRLog.BeginScope("battleId", battleId.ToString()))
{
    FRLog.InfoT("Battle", "Battle start");
    await RunBattleAsync();
    FRLog.InfoT("Battle", "Battle end");
}
```

Scope 字段使用 `AsyncLocal` 保存，适合异步流程中传递上下文。

## 异常日志

```csharp
try
{
    await LoadConfigAsync();
}
catch (Exception e)
{
    FRLog.Exception("Config", e, "Load config failed");
}
```

异常日志等级为 `Error`，会记录异常类型、消息和堆栈。`FlushImmediatelyOnError` 开启时，Error/Fatal/Exception 会立即 Flush 文件 handler。

## 格式化日志

优先使用带参数的格式化重载，避免在日志等级被过滤时提前拼接字符串。

```csharp
FRLog.InfoT("Battle", "Wave {0} started", waveIndex);
FRLog.WarningT("Resource", "Asset missing: {0}", location);
FRLog.ErrorT("Network", "Request {0} failed", requestId);
FRLog.DebugT("AzurLane", "Loaded {0} ShipSkinDataConfigs", count);
```

带 tag 的格式化重载统一使用 `FRLog.<Level>T(tag, format, arg...)`，直接写入 `FRLogger.Log(level, tag, formattedMessage)`，不会再走 `FRLog.<Level>(format, arg)` 的无 tag 格式化入口。

如果只是输出一条带 tag 的完整文本，使用：

```csharp
FRLog.InfoT("Battle", "Battle started");
```

无 tag 格式化 API 保持原方法名：

```csharp
FRLog.Info("Loaded {0} configs", count);
```

带 tag 的日志必须使用 `T` 后缀，避免 `FRLog.Info("Tag", "Message")` 被编译器当成无 tag 的 `Info(format, arg)`。

```csharp
FRLog.InfoT("Battle", "Battle {0} started", battleId);
```

如果需要构造复杂字符串，先判断等级：

```csharp
if (FRLog.IsEnabled(FRLogLevel.Debug))
{
    FRLog.DebugT("AI", BuildDebugMessage(agent));
}
```

## 运行时配置和统计

```csharp
FRLog.SetMinimumLevel(FRLogLevel.Warning);
FRLog.SetUnityOutputEnabled(false);
FRLog.SetFileOutputEnabled(true);
FRLog.SetStackTraceLevel(FRLogLevel.Error);

FRLogRuntimeConfigSnapshot config = FRLog.GetRuntimeConfigSnapshot();
FRLogStatistics statistics = FRLog.GetStatistics();
FRLog.ResetStatistics();
```

`FRLogStatistics` 包含各等级数量、异常数量、handler 数量、异步文件队列数量、丢弃数量和当前日志文件路径。

## Editor 诊断窗口

菜单：`FRFrame/Diagnostics/Log Diagnostics`

可查看：

- 当前最小日志等级。
- Unity/File 输出开关。
- handler 数量。
- 文件目录和当前日志文件路径。
- 各等级日志数量。
- 异步队列数量和丢弃数量。

常用操作：

- Refresh。
- Open Log Directory。
- Copy Log Directory。
- Copy Current File Path。
- Flush。
- Reset Statistics。

## 推荐工作流

### 普通业务日志

```csharp
FRLog.InfoT("Inventory", "Inventory opened");
FRLog.WarningT("Inventory", "Item not found: {0}", itemId);
```

### 模块初始化日志

```csharp
FRLog.InfoT("Resource", "Package initialized: {0}", packageName);
```

### 关键异步流程

```csharp
using (FRLog.BeginScope("requestId", requestId))
{
    FRLog.InfoT("Network", "Request start");
    try
    {
        await SendAsync();
        FRLog.InfoT("Network", "Request success");
    }
    catch (Exception e)
    {
        FRLog.Exception("Network", e, "Request failed");
    }
}
```

### 真机文件日志

```csharp
var config = FRLogger.CreateDefaultConfig();
config.EnableUnityLog = true;
config.EnableFileLog = true;
config.MinimumLevel = FRLogLevel.Info;
config.FileWriteMode = FRLogFileWriteMode.Async;
config.GroupLogBySession = true;
config.MaxRetainedSessionCount = 5;
config.MaxRetainedDays = 7;
FRLogger.Initialize(config);
```

## 使用规范

- 业务和框架模块统一使用 `FRLog`，不要直接使用 Unity `Debug.Log`。
- 日志 tag 使用模块名或业务域名，例如 `Resource`、`Pool`、`FSM`、`Event`、`Battle`。
- 高频路径避免字符串插值和复杂拼接，使用格式化重载或先 `IsEnabled`。
- 异常使用 `FRLog.Exception`，不要只输出 `e.Message`。
- 线上或真机文件日志优先使用异步写入。
- `Block` 溢出策略只适合工具或必须保日志的低频场景。
- 远端日志和崩溃上报 handler 是扩展模板，接入第三方 SDK 时应封装 adapter，不要把 SDK 调用散落在业务代码里。
- 修改日志等级、文件输出和堆栈等级时，优先使用运行时配置 API，而不是重复初始化日志系统。
- 退出或关键错误后需要确保持久化时调用 `FRFM.Log.Flush()` 或 `FRLogger.Flush()`。
