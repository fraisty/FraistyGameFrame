# FRFrame HTTP 使用指南

引用 `FRFrame.Core`，在 Unity 主线程等待 `FRFM.WaitReady<HttpManager>()` 后，通过 `FRFM.Http` 发起请求。以下示例使用 F2 本机 fixture。

```csharp
using System.Threading;
using Cysharp.Threading.Tasks;
using FRFrame;
using UnityEngine;

public sealed class ExamplePayload
{
    public int value;
}

public static class HttpExample
{
    public static async UniTask RunAsync(string newFilePath, CancellationToken cancellationToken)
    {
        const string url = "http://127.0.0.1:18952";
        await FRFM.WaitReady<HttpManager>();
        var http = FRFM.Http;
        var response = await http.GetJsonAsync<ExamplePayload>(url + "/json", cancellationToken: cancellationToken);
        if (response.IsSuccess)
            Debug.Log(response.Data.value);
        else
            Debug.LogWarning($"HTTP {response.StatusCode} ({response.ErrorKind})");

        var missing = await http.GetBytesAsync(url + "/missing", cancellationToken: cancellationToken);
        if (missing.ErrorKind == HttpErrorKind.HttpStatus)
            Debug.Log($"HTTP {missing.StatusCode}, {missing.ErrorBody.Length} error bytes");

        var file = await http.DownloadFileAsync(url + "/file", newFilePath,
            onProgress: value => Debug.Log($"Download {value:P0}"), cancellationToken: cancellationToken);
        if (file.IsSuccess)
            Debug.Log($"Downloaded {file.BytesDownloaded} bytes");
    }
}
```

- JSON 提供 GET／POST／PUT／DELETE。`204` 和空响应成功时 `Data` 为 `default`。原始 bytes 使用 `GetBytesAsync` 或 `SendBytesAsync`。只有 POST／PUT 可发送请求体。
- `HttpRequestOptions.Headers` 只作用于当前请求。JSON 默认 `Accept: application/json`，有请求体时默认 `Content-Type: application/json; charset=utf-8`。bytes 请求体默认 `application/octet-stream`。调用者可按请求覆盖这些 Header。Unity 管理 `Content-Length`、`Transfer-Encoding`、`Host`、`Accept-Encoding` 和 `X-Unity-Version`，传入这些 Header 会返回 `InvalidRequest`。
- `TimeoutSeconds` 默认 15 秒，允许 0–300，0 表示禁用。`MaxRequestBytes` 与 `MaxResponseBytes` 默认 4 MiB，可降低到 0。响应在接收完成后检查。超限的 2xx 返回 `ResponseTooLarge`，非 2xx 保留 `HttpStatus` 与上限以内的错误体前缀。`ResponseHeaders` 按大小写不敏感的名称读取。JSON 解析失败时 `ErrorBody` 包含有界的原始响应，业务按需要处理其中的数据。
- `CancellationTokenSource.Cancel()` 可以从任意线程调用。`Cancelled`、`Timeout` 和 `ManagerShutdown` 是独立结果。框架所有者在 Unity 主线程调用 `FRFM.Shutdown()`，停止旧实例接收新请求并终止在途请求。
- `DownloadFileAsync` 将内容写入目标同目录的临时文件，成功后移动到新路径。可用 `Application.persistentDataPath` 构建目标路径。进度在主线程报告，达到 `1` 时最终文件已落盘。磁盘写入与提交失败时检查 `ErrorKind`，已有目标文件会返回 `DestinationExists`。
- HTTP(S) 使用 Unity 的 TLS、重定向及平台 HTTP 策略。用于实际服务时使用适合目标平台的 HTTPS 地址。Unity Player 的设置和平台验收见 [F2 设计](../../../../Docs/FRFrameNetwork/Design/Network-Http-Client-Design.md)。

## 在现有 Unity Editor 复核

1. 从仓库根目录运行 `python Docs/FRFrameNetwork/Validation/F2HttpLoopback.py`，看到 `127.0.0.1:18952` 后保持它运行。
2. 将 [F2 Editor 检查脚本](../../../../Docs/FRFrameNetwork/Validation/F2HttpEditorChecks.cs) 临时复制到 `Assets/FRFrame/Core/Http/F2HttpEditorChecks.cs`。编译完成后打开 `Assets/FRFrame/Network/Unity/NetworkMultiplayerDemo.unity`，进入 Play Mode。选择 `FRFrame > Validation > Run F2 HTTP Checks`，每次 Play 运行一次。最后一组检查会关闭 FRFrame，停在 Play Mode 内查看结果。
3. Console 过滤 `[F2 HTTP]`，确认 `PASS all loopback checks`。检查 JSON 值、404／500 结果、取消和超时的区分、文件进度与覆盖保护，以及 Shutdown 后活动请求数为 0、临时文件已清理。进度回调抛错是有意触发的一次 Warning。
4. 退出 Play Mode，删除步骤 2 复制的脚本和它的 `.meta`，用 `Ctrl+C` 关闭 fixture。结果范围与证据见 [Master Task](../../../../Docs/FRFrameNetwork/Tasks/Network-POC-Master-Task.md#836-f2-lightweight-http-client-实施交付2026-11-26)。
