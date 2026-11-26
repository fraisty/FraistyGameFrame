using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using Cysharp.Threading.Tasks;
using Newtonsoft.Json;
using UnityEngine.Networking;

namespace FRFrame
{
    /// <summary>Unity 主线程上的轻量 HTTP 客户端。取消令牌可由任意线程触发。</summary>
    public sealed class HttpManager : IManagerBase
    {
        private const string JsonContentType = "application/json; charset=utf-8";
        private const string BytesContentType = "application/octet-stream";
        private static readonly JsonSerializerSettings JsonSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            CheckAdditionalContent = true
        };

        private readonly HashSet<ActiveRequest> _active = new HashSet<ActiveRequest>();
        private bool _shutdown;

        public override UniTask InitializeAsync() => UniTask.CompletedTask;

        public override void Destroy()
        {
            _shutdown = true;
            foreach (var active in _active)
            {
                if (!active.Operation.isDone)
                    active.Abort(HttpErrorKind.ManagerShutdown);
            }
        }

        public UniTask<HttpResult<T>> GetJsonAsync<T>(string url, HttpRequestOptions options = null,
            CancellationToken cancellationToken = default) =>
            SendJsonAsync<object, T>(HttpMethod.Get, url, null, options, cancellationToken);

        public UniTask<HttpResult<TResponse>> PostJsonAsync<TRequest, TResponse>(string url, TRequest body,
            HttpRequestOptions options = null, CancellationToken cancellationToken = default) =>
            SendJsonAsync<TRequest, TResponse>(HttpMethod.Post, url, body, options, cancellationToken);

        public UniTask<HttpResult<TResponse>> PutJsonAsync<TRequest, TResponse>(string url, TRequest body,
            HttpRequestOptions options = null, CancellationToken cancellationToken = default) =>
            SendJsonAsync<TRequest, TResponse>(HttpMethod.Put, url, body, options, cancellationToken);

        public UniTask<HttpResult<T>> DeleteJsonAsync<T>(string url, HttpRequestOptions options = null,
            CancellationToken cancellationToken = default) =>
            SendJsonAsync<object, T>(HttpMethod.Delete, url, null, options, cancellationToken);

        public UniTask<HttpResult<byte[]>> GetBytesAsync(string url, HttpRequestOptions options = null,
            CancellationToken cancellationToken = default) =>
            SendBytesAsync(HttpMethod.Get, url, options: options, cancellationToken: cancellationToken);

        public UniTask<HttpResult<byte[]>> SendBytesAsync(HttpMethod method, string url, byte[] body = null,
            string contentType = null, HttpRequestOptions options = null, CancellationToken cancellationToken = default)
        {
            var outcome = Prepare(method, url, body == null ? null : contentType ?? BytesContentType, null,
                options, cancellationToken, out var request);
            if (outcome.ErrorKind != HttpErrorKind.None)
                return UniTask.FromResult(new HttpResult<byte[]>(outcome));
            if (body != null && method != HttpMethod.Post && method != HttpMethod.Put)
                return UniTask.FromResult(new HttpResult<byte[]>(new HttpOutcome(HttpErrorKind.InvalidRequest,
                    "Only POST and PUT can send a request body.")));
            if (body != null && body.Length > request.MaxRequestBytes)
                return UniTask.FromResult(new HttpResult<byte[]>(RequestTooLarge()));

            // UploadHandlerRaw copies the bytes into native memory before SendBufferedAsync first yields.
            return SendBufferedAsync(request, body, cancellationToken);
        }

        /// <summary>流式下载到新文件。进度在主线程通知，1 表示最终文件已可用。</summary>
        public async UniTask<HttpDownloadResult> DownloadFileAsync(string url, string destinationPath,
            HttpRequestOptions options = null, Action<float> onProgress = null,
            CancellationToken cancellationToken = default)
        {
            var outcome = Prepare(HttpMethod.Get, url, null, null, options, cancellationToken, out var settings);
            if (outcome.ErrorKind != HttpErrorKind.None)
                return new HttpDownloadResult(outcome);
            if (string.IsNullOrWhiteSpace(destinationPath))
                return new HttpDownloadResult(new HttpOutcome(HttpErrorKind.InvalidRequest, "A destination file is required."));

            string tempPath = null;
            float lastProgress = -1f;
            void ReportProgress(float value)
            {
                if (onProgress == null || value <= lastProgress)
                    return;
                lastProgress = value;
                try { onProgress(value); }
                catch (Exception)
                {
                    onProgress = null;
                    FRLog.Warning("HTTP file download progress callback failed and was disabled.");
                }
            }

            try
            {
                destinationPath = Path.GetFullPath(destinationPath);
                if (string.IsNullOrEmpty(Path.GetFileName(destinationPath)))
                    return new HttpDownloadResult(new HttpOutcome(HttpErrorKind.InvalidRequest, "A destination file is required."));
                if (File.Exists(destinationPath) || Directory.Exists(destinationPath))
                    return new HttpDownloadResult(new HttpOutcome(HttpErrorKind.DestinationExists, "The download destination already exists."));

                string directory = Path.GetDirectoryName(destinationPath);
                Directory.CreateDirectory(directory);
                tempPath = Path.Combine(directory, $".frframe-{Guid.NewGuid():N}.part");

                ulong bytesDownloaded;
                using (var request = new UnityWebRequest(settings.Url, settings.Verb))
                {
                    request.downloadHandler = new DownloadHandlerFile(tempPath) { removeFileOnAbort = true };
                    outcome = await SendAsync(request, settings, cancellationToken, onProgress == null ? null : ReportProgress);
                    if (outcome.ErrorKind != HttpErrorKind.None)
                        return new HttpDownloadResult(outcome);
                    bytesDownloaded = request.downloadedBytes;
                }

                // The native handler must be closed before moving the file on Windows.
                try { File.Move(tempPath, destinationPath); }
                catch (IOException) when (File.Exists(destinationPath) || Directory.Exists(destinationPath))
                {
                    return new HttpDownloadResult(outcome.WithError(HttpErrorKind.DestinationExists, "The download destination already exists."));
                }

                tempPath = null;
                ReportProgress(1f);
                return new HttpDownloadResult(outcome, destinationPath, bytesDownloaded);
            }
            catch (Exception error) when (error is ArgumentException || error is NotSupportedException)
            {
                return new HttpDownloadResult(outcome.WithError(HttpErrorKind.InvalidRequest, "The download path is invalid."));
            }
            catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
            {
                return new HttpDownloadResult(outcome.WithError(HttpErrorKind.FileIO, "Unable to write or move the download file."));
            }
            finally
            {
                if (tempPath != null)
                {
                    try { File.Delete(tempPath); }
                    catch (Exception error) when (error is IOException || error is UnauthorizedAccessException)
                    {
                        FRLog.Warning("Unable to remove an HTTP download temporary file.");
                    }
                }
            }
        }

        private async UniTask<HttpResult<TResponse>> SendJsonAsync<TRequest, TResponse>(HttpMethod method,
            string url, TRequest body, HttpRequestOptions options, CancellationToken cancellationToken)
        {
            bool hasBody = method == HttpMethod.Post || method == HttpMethod.Put;
            var outcome = Prepare(method, url, hasBody ? JsonContentType : null, "application/json", options,
                cancellationToken, out var request);
            if (outcome.ErrorKind != HttpErrorKind.None)
                return new HttpResult<TResponse>(outcome);

            byte[] payload = null;
            if (hasBody)
            {
                try
                {
                    using var writer = new StringWriter(System.Globalization.CultureInfo.InvariantCulture);
                    using var json = new JsonTextWriter(writer);
                    JsonSerializer.Create(JsonSettings).Serialize(json, body);
                    var serialized = writer.ToString();
                    if (Encoding.UTF8.GetByteCount(serialized) > request.MaxRequestBytes)
                        return new HttpResult<TResponse>(RequestTooLarge());
                    payload = Encoding.UTF8.GetBytes(serialized);
                }
                catch (Exception)
                {
                    return new HttpResult<TResponse>(new HttpOutcome(HttpErrorKind.Serialization, "Unable to encode the JSON request."));
                }
            }

            var bytes = await SendBufferedAsync(request, payload, cancellationToken);
            outcome = new HttpOutcome(bytes.ErrorKind, bytes.ErrorMessage, bytes.StatusCode, bytes.ResponseHeaders);
            if (!bytes.IsSuccess || bytes.Data.Length == 0)
                return new HttpResult<TResponse>(outcome, errorBody: bytes.ErrorBody);

            try
            {
                using var reader = new StreamReader(new MemoryStream(bytes.Data, writable: false), Encoding.UTF8);
                using var json = new JsonTextReader(reader);
                return new HttpResult<TResponse>(outcome, JsonSerializer.Create(JsonSettings).Deserialize<TResponse>(json));
            }
            catch (Exception)
            {
                return new HttpResult<TResponse>(outcome.WithError(HttpErrorKind.Serialization, "Unable to decode the JSON response."),
                    errorBody: bytes.Data);
            }
        }

        private async UniTask<HttpResult<byte[]>> SendBufferedAsync(RequestSettings settings, byte[] body,
            CancellationToken cancellationToken)
        {
            using var request = new UnityWebRequest(settings.Url, settings.Verb);
            var download = new DownloadHandlerBuffer();
            request.downloadHandler = download;
            if (body != null)
                request.uploadHandler = new UploadHandlerRaw(body);

            var outcome = await SendAsync(request, settings, cancellationToken);
            if (outcome.ErrorKind != HttpErrorKind.None && outcome.ErrorKind != HttpErrorKind.HttpStatus)
                return new HttpResult<byte[]>(outcome);

            var data = download.nativeData;
            if (outcome.ErrorKind == HttpErrorKind.HttpStatus)
            {
                int size = Math.Min(data.Length, settings.MaxResponseBytes);
                var errorBody = size == 0 ? Array.Empty<byte>() : new byte[size];
                for (int i = 0; i < size; i++)
                    errorBody[i] = data[i];
                return new HttpResult<byte[]>(outcome, errorBody: errorBody);
            }
            if (data.Length > settings.MaxResponseBytes)
                return new HttpResult<byte[]>(outcome.WithError(HttpErrorKind.ResponseTooLarge, "The response exceeds the configured byte limit."));
            return new HttpResult<byte[]>(outcome, data.Length == 0 ? Array.Empty<byte>() : data.ToArray());
        }

        private async UniTask<HttpOutcome> SendAsync(UnityWebRequest request, RequestSettings settings,
            CancellationToken cancellationToken, Action<float> progress = null)
        {
            if (_shutdown)
                return Stopped(HttpErrorKind.ManagerShutdown);
            if (cancellationToken.IsCancellationRequested)
                return Stopped(HttpErrorKind.Cancelled);

            UnityWebRequestAsyncOperation operation;
            try
            {
                foreach (var header in settings.Headers)
                    request.SetRequestHeader(header.Key, header.Value);
                operation = request.SendWebRequest();
            }
            catch (Exception error) when (error is ArgumentException || error is InvalidOperationException)
            {
                return new HttpOutcome(HttpErrorKind.InvalidRequest, "Unity rejected the request URL, headers, or platform HTTP settings.");
            }

            var active = new ActiveRequest(request, operation);
            _active.Add(active);
            long deadline = Stopwatch.GetTimestamp() + settings.TimeoutSeconds * Stopwatch.Frequency;
            try
            {
                while (!operation.isDone)
                {
                    if (active.StopReason == HttpErrorKind.None)
                    {
                        if (cancellationToken.IsCancellationRequested)
                            active.Abort(HttpErrorKind.Cancelled);
                        else if (settings.TimeoutSeconds != 0 && Stopwatch.GetTimestamp() >= deadline)
                            active.Abort(HttpErrorKind.Timeout);
                        else if (progress != null)
                            progress(Math.Clamp(request.downloadProgress, 0f, 0.99f));
                    }
                    await UniTask.Yield(PlayerLoopTiming.Update);
                }

                if (active.StopReason != HttpErrorKind.None)
                    return Stopped(active.StopReason);

                long status = Math.Max(0, request.responseCode);
                var responseHeaders = request.GetResponseHeaders();
                IReadOnlyDictionary<string, string> headers = null;
                if (responseHeaders != null)
                {
                    var snapshot = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                    foreach (var header in responseHeaders)
                        snapshot[header.Key] = header.Value;
                    headers = new ReadOnlyDictionary<string, string>(snapshot);
                }

                if (status > 0 && (status < 200 || status >= 300))
                    return new HttpOutcome(HttpErrorKind.HttpStatus, "The HTTP server returned a non-success status.", status, headers);
                if (request.result == UnityWebRequest.Result.ConnectionError || status <= 0)
                    return new HttpOutcome(HttpErrorKind.Connection, "The HTTP connection failed.", status, headers);
                if (request.result == UnityWebRequest.Result.DataProcessingError)
                    return request.downloadHandler is DownloadHandlerFile
                        ? new HttpOutcome(HttpErrorKind.FileIO, "Unable to write the response to the download file.", status, headers)
                        : new HttpOutcome(HttpErrorKind.Connection, "Unable to receive the HTTP response.", status, headers);
                return new HttpOutcome(HttpErrorKind.None, statusCode: status, headers: headers);
            }
            finally
            {
                _active.Remove(active);
            }
        }

        private HttpOutcome Prepare(HttpMethod method, string url, string contentType, string accept,
            HttpRequestOptions options, CancellationToken cancellationToken, out RequestSettings request)
        {
            request = default;
            if (!PlayerLoopHelper.IsMainThread)
                return new HttpOutcome(HttpErrorKind.InvalidRequest, "HTTP requests must start on the Unity main thread.");
            if (_shutdown)
                return Stopped(HttpErrorKind.ManagerShutdown);
            if (cancellationToken.IsCancellationRequested)
                return Stopped(HttpErrorKind.Cancelled);

            string verb = method switch
            {
                HttpMethod.Get => UnityWebRequest.kHttpVerbGET,
                HttpMethod.Post => UnityWebRequest.kHttpVerbPOST,
                HttpMethod.Put => UnityWebRequest.kHttpVerbPUT,
                HttpMethod.Delete => UnityWebRequest.kHttpVerbDELETE,
                _ => null
            };
            if (verb == null || !Uri.TryCreate(url, UriKind.Absolute, out var uri) || string.IsNullOrEmpty(uri.Host) ||
                (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return new HttpOutcome(HttpErrorKind.InvalidRequest, "An absolute HTTP(S) URL and a supported method are required.");

            options ??= new HttpRequestOptions();
            int timeout = options.TimeoutSeconds;
            int requestLimit = options.MaxRequestBytes;
            int responseLimit = options.MaxResponseBytes;
            if (timeout < 0 || timeout > 300 || requestLimit < 0 || requestLimit > HttpRequestOptions.InMemoryByteLimit ||
                responseLimit < 0 || responseLimit > HttpRequestOptions.InMemoryByteLimit)
                return new HttpOutcome(HttpErrorKind.InvalidRequest, "Timeout or byte limits are outside the supported range.");

            var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (options.Headers != null)
            {
                foreach (var header in options.Headers)
                {
                    if (!IsValidHeader(header.Key, header.Value))
                        return new HttpOutcome(HttpErrorKind.InvalidRequest, "A request header is invalid or controlled by Unity.");
                    headers[header.Key] = header.Value;
                }
            }
            if (contentType != null && !headers.ContainsKey("Content-Type"))
            {
                if (!IsValidHeader("Content-Type", contentType))
                    return new HttpOutcome(HttpErrorKind.InvalidRequest, "The request content type is invalid.");
                headers.Add("Content-Type", contentType);
            }
            if (accept != null)
                headers.TryAdd("Accept", accept);

            request = new RequestSettings(uri, verb, headers, timeout, requestLimit, responseLimit);
            return new HttpOutcome(HttpErrorKind.None);
        }

        private static bool IsValidHeader(string name, string value)
        {
            if (string.IsNullOrEmpty(name) || string.IsNullOrEmpty(value))
                return false;

            const string punctuation = "!#$%&'*+-.^_`|~";
            foreach (char c in name)
            {
                if (!(c >= 'a' && c <= 'z') && !(c >= 'A' && c <= 'Z') && !(c >= '0' && c <= '9') &&
                    punctuation.IndexOf(c) < 0)
                    return false;
            }
            foreach (char c in value)
            {
                if ((c < ' ' && c != '\t') || c == '\u007f')
                    return false;
            }

            return !name.Equals("Content-Length", StringComparison.OrdinalIgnoreCase) &&
                   !name.Equals("Transfer-Encoding", StringComparison.OrdinalIgnoreCase) &&
                   !name.Equals("Host", StringComparison.OrdinalIgnoreCase) &&
                   !name.Equals("Accept-Encoding", StringComparison.OrdinalIgnoreCase) &&
                   !name.Equals("X-Unity-Version", StringComparison.OrdinalIgnoreCase);
        }

        private static HttpOutcome RequestTooLarge() =>
            new HttpOutcome(HttpErrorKind.InvalidRequest, "The request body exceeds the configured byte limit.");

        private static HttpOutcome Stopped(HttpErrorKind reason) => new HttpOutcome(reason, reason switch
        {
            HttpErrorKind.ManagerShutdown => "The HTTP manager has shut down.",
            HttpErrorKind.Timeout => "The HTTP request timed out.",
            _ => "The HTTP request was cancelled."
        });

        private readonly struct RequestSettings
        {
            internal RequestSettings(Uri url, string verb, Dictionary<string, string> headers, int timeoutSeconds,
                int maxRequestBytes, int maxResponseBytes)
            {
                Url = url;
                Verb = verb;
                Headers = headers;
                TimeoutSeconds = timeoutSeconds;
                MaxRequestBytes = maxRequestBytes;
                MaxResponseBytes = maxResponseBytes;
            }

            internal Uri Url { get; }
            internal string Verb { get; }
            internal Dictionary<string, string> Headers { get; }
            internal int TimeoutSeconds { get; }
            internal int MaxRequestBytes { get; }
            internal int MaxResponseBytes { get; }
        }

        private sealed class ActiveRequest
        {
            private readonly UnityWebRequest _request;

            internal ActiveRequest(UnityWebRequest request, UnityWebRequestAsyncOperation operation)
            {
                _request = request;
                Operation = operation;
            }

            internal UnityWebRequestAsyncOperation Operation { get; }
            internal HttpErrorKind StopReason { get; private set; }

            internal void Abort(HttpErrorKind reason)
            {
                if (StopReason != HttpErrorKind.None)
                    return;
                StopReason = reason;
                _request.Abort();
            }
        }
    }
}
