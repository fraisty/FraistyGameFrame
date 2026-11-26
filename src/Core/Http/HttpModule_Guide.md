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
- `CancellationTokenSource.Cancel()` 可以从任意线程调用。`Cancelled`、`Timeout` 和 `ManagerShutdown` 是独立结果。`FRFM.Shutdown()` 在 Unity 主线程同步关闭尚未交付的请求及其 Handler，清理临时文件，旧实例停止接收新请求。若 PlayerLoop 继续运行，被撤销的任务返回 `ManagerShutdown`。已复制的内存响应或已提交的文件保留原结果。
- `DownloadFileAsync` 将完整内容写入目标同目录的临时文件，成功后移动到新路径。可用 `Application.persistentDataPath` 构建目标路径。进度在主线程报告，达到 `1` 时最终文件已落盘。已有目标文件会返回 `DestinationExists`。此 API 对 `Range` 或 `If-Range` 返回 `InvalidRequest`。接收结束后发现 `206` 或其他带 `Content-Range` 的成功状态时，文件下载返回 `HttpStatus` 并保留状态码和 Header。部分响应及截断响应均不会生成最终文件。普通 bytes 请求仍支持按范围读取。
- HTTP(S) 使用 Unity 的 TLS、重定向及平台 HTTP 策略。用于实际服务时使用适合目标平台的 HTTPS 地址。Unity Player 的设置和平台验收见 [F2 设计](../../../../Docs/FRFrameNetwork/Design/Network-Http-Client-Design.md)。

## 在现有 Unity Editor 复核

1. 从仓库根目录运行 `python Docs/FRFrameNetwork/Validation/F2HttpLoopback.py`，看到 `127.0.0.1:18952` 后保持它运行。
2. 将 [F2 Editor 检查脚本](../../../../Docs/FRFrameNetwork/Validation/F2HttpEditorChecks.cs) 临时复制到 `Assets/FRFrame/Core/Http/F2HttpEditorChecks.cs`。编译完成后打开 `Assets/FRFrame/Network/Unity/NetworkMultiplayerDemo.unity`，进入 Play Mode。选择 `FRFrame > Validation > Run F2 HTTP Checks`，每次 Play 运行一次。最后一组检查会关闭 FRFrame，停在 Play Mode 内查看结果。
3. 在 Console 过滤 `[F2 HTTP]`，确认 `PASS all loopback checks`，包括 bytes 范围读取、部分响应与截断保护、完整文件下载和正常 Shutdown。进度回调抛错是有意触发的一次 Warning。
4. 分别从新的 Play Mode 会话运行 `FRFrame > Validation > Run F2 HTTP Root Teardown Check` 与 `Run F2 HTTP Stop Play Check`。前者检查销毁根节点的同一帧已释放句柄并删除 `.part`，后者退出 Play Mode 并检查目标目录为空。核对 `PASS CR-F2-2` 日志。
5. 回到 Edit Mode 后删除步骤 2 临时复制的脚本和它的 `.meta`，用 `Ctrl+C` 关闭 fixture。结果范围与证据见 [Master Task](../../../../Docs/FRFrameNetwork/Tasks/Network-POC-Master-Task.md#838-f2-审查修复交付2026-11-26)。
