using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FRFrame
{
    public enum HttpErrorKind
    {
        None,
        InvalidRequest,
        HttpStatus,
        Connection,
        Timeout,
        Cancelled,
        ResponseTooLarge,
        Serialization,
        FileIO,
        DestinationExists,
        ManagerShutdown
    }

    /// <summary>JSON 或原始字节响应。ErrorBody 仅保留有界的 HTTP 错误或 JSON 解析失败响应。</summary>
    public sealed class HttpResult<T>
    {
        private readonly HttpOutcome _outcome;

        internal HttpResult(HttpOutcome outcome, T data = default, byte[] errorBody = null)
        {
            _outcome = outcome;
            Data = data;
            ErrorBody = errorBody ?? Array.Empty<byte>();
        }

        public bool IsSuccess => ErrorKind == HttpErrorKind.None;
        public long StatusCode => _outcome.StatusCode;
        public T Data { get; }
        public HttpErrorKind ErrorKind => _outcome.ErrorKind;
        public string ErrorMessage => _outcome.ErrorMessage;
        public IReadOnlyDictionary<string, string> ResponseHeaders => _outcome.Headers;
        public byte[] ErrorBody { get; }
    }

    /// <summary>文件下载结果。仅在最终文件落盘后提供 FilePath 和 BytesDownloaded。</summary>
    public sealed class HttpDownloadResult
    {
        private readonly HttpOutcome _outcome;

        internal HttpDownloadResult(HttpOutcome outcome, string filePath = null, ulong bytesDownloaded = 0)
        {
            _outcome = outcome;
            FilePath = filePath;
            BytesDownloaded = bytesDownloaded;
        }

        public bool IsSuccess => ErrorKind == HttpErrorKind.None;
        public long StatusCode => _outcome.StatusCode;
        public string FilePath { get; }
        public ulong BytesDownloaded { get; }
        public HttpErrorKind ErrorKind => _outcome.ErrorKind;
        public string ErrorMessage => _outcome.ErrorMessage;
        public IReadOnlyDictionary<string, string> ResponseHeaders => _outcome.Headers;
    }

    internal readonly struct HttpOutcome
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyHeaders =
            new ReadOnlyDictionary<string, string>(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));

        internal HttpOutcome(HttpErrorKind errorKind, string errorMessage = null, long statusCode = 0,
            IReadOnlyDictionary<string, string> headers = null)
        {
            ErrorKind = errorKind;
            ErrorMessage = errorMessage;
            StatusCode = statusCode;
            Headers = headers ?? EmptyHeaders;
        }

        internal HttpErrorKind ErrorKind { get; }
        internal string ErrorMessage { get; }
        internal long StatusCode { get; }
        internal IReadOnlyDictionary<string, string> Headers { get; }

        internal HttpOutcome WithError(HttpErrorKind errorKind, string message) =>
            new HttpOutcome(errorKind, message, StatusCode, Headers);
    }
}
