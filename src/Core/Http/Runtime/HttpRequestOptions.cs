using System.Collections.Generic;

namespace FRFrame
{
    /// <summary>HTTP 请求支持的方法。GET 和 DELETE 不发送请求体。</summary>
    public enum HttpMethod
    {
        Get,
        Post,
        Put,
        Delete
    }

    /// <summary>单次请求的设置。调用时读取快照，之后可为下一次请求修改。</summary>
    public sealed class HttpRequestOptions
    {
        public const int InMemoryByteLimit = 4 * 1024 * 1024;

        /// <summary>允许 0–300 秒。0 禁用超时。</summary>
        public int TimeoutSeconds { get; set; } = 15;

        public int MaxRequestBytes { get; set; } = InMemoryByteLimit;

        /// <summary>接收后校验的内存响应上限，不适用于流式文件下载。</summary>
        public int MaxResponseBytes { get; set; } = InMemoryByteLimit;

        public IReadOnlyDictionary<string, string> Headers { get; set; }
    }
}
