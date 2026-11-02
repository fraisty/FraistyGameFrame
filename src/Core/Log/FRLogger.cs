using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FRFrame
{
    public static class FRLogger
    {
        private static readonly object LockObject = new object();
        private static readonly List<IFRLogHandler> Handlers = new List<IFRLogHandler>();
        private static readonly FRLogContext LogContext = new FRLogContext();
        private static FRLogConfig _config;
        private static bool _initialized;
        private static int _debugCount;
        private static int _infoCount;
        private static int _warningCount;
        private static int _errorCount;
        private static int _fatalCount;
        private static int _exceptionCount;

        public static FRLogContext Context => LogContext;

        public static void Initialize(FRLogConfig config = null)
        {
            lock (LockObject)
            {
                ShutdownInternal();
                _config = config ?? CreateDefaultConfig();
                AddConfiguredHandlers();
                _initialized = true;
                Log(FRLogLevel.Info, "Log", $"Log initialized. Directory:{_config.FileLogDirectory}, Retention:{_config.EnableLogRetention}, MaxFiles:{GetMaxRetainedFileCount()}, MaxDays:{_config.MaxRetainedDays}");
            }
        }

        public static void AddHandler(IFRLogHandler handler)
        {
            if (handler == null)
                return;

            EnsureInitialized();
            lock (LockObject)
            {
                if (!Handlers.Contains(handler))
                    Handlers.Add(handler);
            }
        }

        public static void RemoveHandler(IFRLogHandler handler)
        {
            if (handler == null)
                return;

            lock (LockObject)
            {
                if (Handlers.Remove(handler))
                    handler.Shutdown();
            }
        }

        public static void ClearHandlers()
        {
            lock (LockObject)
            {
                ShutdownInternal();
                _initialized = true;
            }
        }

        public static bool IsLevelEnabled(FRLogLevel level)
        {
            EnsureInitialized();
            return level >= _config.MinimumLevel;
        }

        public static void SetMinimumLevel(FRLogLevel level)
        {
            EnsureInitialized();
            _config.MinimumLevel = level;
        }

        public static void SetUnityOutputEnabled(bool enabled)
        {
            EnsureInitialized();
            lock (LockObject)
            {
                _config.EnableUnityLog = enabled;
                RemoveHandlers<UnityFRLogHandler>();
                if (enabled)
                    Handlers.Add(new UnityFRLogHandler());
            }
        }

        public static void SetFileOutputEnabled(bool enabled)
        {
            EnsureInitialized();
            lock (LockObject)
            {
                _config.EnableFileLog = enabled;
                RemoveFileHandlers();
                if (enabled)
                    Handlers.Add(CreateFileHandler(_config));
            }
        }

        public static void SetStackTraceLevel(FRLogLevel level)
        {
            EnsureInitialized();
            _config.StackTraceMinLevel = level;
            _config.CaptureStackTrace = true;
        }

        public static FRLogRuntimeConfigSnapshot GetRuntimeConfigSnapshot()
        {
            EnsureInitialized();
            lock (LockObject)
            {
                return new FRLogRuntimeConfigSnapshot
                {
                    MinimumLevel = _config.MinimumLevel,
                    UnityOutputEnabled = _config.EnableUnityLog,
                    FileOutputEnabled = _config.EnableFileLog,
                    CaptureStackTrace = _config.CaptureStackTrace,
                    StackTraceLevel = _config.StackTraceMinLevel,
                    HandlerCount = Handlers.Count,
                    FileDirectory = _config.FileLogDirectory
                };
            }
        }

        public static FRLogStatistics CreateStatisticsSnapshot()
        {
            EnsureInitialized();
            lock (LockObject)
            {
                var statistics = new FRLogStatistics
                {
                    DebugCount = _debugCount,
                    InfoCount = _infoCount,
                    WarningCount = _warningCount,
                    ErrorCount = _errorCount,
                    FatalCount = _fatalCount,
                    ExceptionCount = _exceptionCount,
                    HandlerCount = Handlers.Count
                };

                for (int i = 0; i < Handlers.Count; i++)
                {
                    if (Handlers[i] is IFRLogFileHandler fileHandler)
                    {
                        statistics.FileQueueCount += fileHandler.QueueCount;
                        statistics.DroppedCount += fileHandler.DroppedCount;
                        if (string.IsNullOrEmpty(statistics.CurrentLogFilePath))
                            statistics.CurrentLogFilePath = fileHandler.CurrentLogFilePath;
                    }
                }

                return statistics;
            }
        }

        public static void ResetStatistics()
        {
            lock (LockObject)
            {
                _debugCount = 0;
                _infoCount = 0;
                _warningCount = 0;
                _errorCount = 0;
                _fatalCount = 0;
                _exceptionCount = 0;
            }
        }

        public static void Log(FRLogLevel level, string message, Object context = null)
        {
            Log(level, null, message, context);
        }

        public static void Log(FRLogLevel level, string tag, string message, Object context = null)
        {
            EnsureInitialized();
            if (level < _config.MinimumLevel)
                return;

            FRLogEntry entry = new FRLogEntry(
                level,
                GetTime(),
                Time.frameCount,
                Thread.CurrentThread.ManagedThreadId,
                tag,
                message,
                ShouldCaptureStackTrace(level) ? FRLogStackTraceUtility.Capture(4) : string.Empty,
                context,
                LogContext.CreateFieldsSnapshot());

            IncrementLevelCount(level);
            Write(entry);
            if (_config.FlushImmediatelyOnError && level >= FRLogLevel.Error)
                Flush();
        }

        public static void Exception(Exception exception, string message = null, Object context = null)
        {
            Exception(null, exception, message, context);
        }

        public static void Exception(string tag, Exception exception, string message = null, Object context = null)
        {
            EnsureInitialized();
            if (FRLogLevel.Error < _config.MinimumLevel)
                return;

            string exceptionMessage = exception == null ? string.Empty : $"{exception.GetType().Name}: {exception.Message}";
            string finalMessage = string.IsNullOrEmpty(message) ? exceptionMessage : $"{message} {exceptionMessage}";
            FRLogEntry entry = new FRLogEntry(FRLogLevel.Error, GetTime(), Time.frameCount, Thread.CurrentThread.ManagedThreadId, tag, finalMessage, exception?.ToString() ?? string.Empty, context, LogContext.CreateFieldsSnapshot(), exception);
            _errorCount++;
            _exceptionCount++;
            Write(entry);
            if (_config.FlushImmediatelyOnError)
                Flush();
        }

        public static void Flush()
        {
            lock (LockObject)
            {
                for (int i = 0; i < Handlers.Count; i++)
                    Handlers[i].Flush();
            }
        }

        public static void Shutdown()
        {
            lock (LockObject)
            {
                ShutdownInternal();
            }
        }

        public static string Format(in FRLogEntry entry)
        {
            FRLogConfig config = _config ?? CreateDefaultConfig();
            var builder = new StringBuilder(128);
            builder.Append("[FRFrame]");
            if (config.ShowTimestamp)
                builder.Append('[').Append(entry.Time.ToString("yyyy-MM-dd HH:mm:ss.fff")).Append(']');

            builder.Append('[').Append(entry.Level).Append(']');
            if (config.ShowFrameCount)
                builder.Append("[F:").Append(entry.FrameCount).Append(']');

            if (config.ShowThreadId)
                builder.Append("[T:").Append(entry.ThreadId).Append(']');

            if (!string.IsNullOrEmpty(entry.Tag))
                builder.Append('[').Append(entry.Tag).Append(']');

            if (entry.Fields != null && entry.Fields.Count > 0)
            {
                builder.Append('[');
                bool first = true;
                foreach (var pair in entry.Fields)
                {
                    if (!first)
                        builder.Append(',');
                    builder.Append(pair.Key).Append('=').Append(pair.Value);
                    first = false;
                }
                builder.Append(']');
            }

            builder.Append(' ').Append(entry.Message);
            return builder.ToString();
        }

        public static FRLogConfig CreateDefaultConfig()
        {
            return new FRLogConfig
            {
                MinimumLevel = GetDefaultMinimumLevel(),
                EnableUnityLog = true,
#if FR_ENABLE_FILE_LOG
                EnableFileLog = true
#else
                EnableFileLog = false
#endif
            };
        }

        private static void EnsureInitialized()
        {
            if (_initialized)
                return;

            Initialize(CreateDefaultConfig());
        }

        private static void Write(in FRLogEntry entry)
        {
            lock (LockObject)
            {
                for (int i = 0; i < Handlers.Count; i++)
                    Handlers[i].Handle(entry);
            }
        }

        private static void ShutdownInternal()
        {
            for (int i = 0; i < Handlers.Count; i++)
                Handlers[i].Shutdown();

            Handlers.Clear();
            _initialized = false;
        }

        private static DateTime GetTime()
        {
            return _config != null && _config.UseUtcTime ? DateTime.UtcNow : DateTime.Now;
        }

        private static bool ShouldCaptureStackTrace(FRLogLevel level)
        {
            return _config != null && _config.CaptureStackTrace && level >= _config.StackTraceMinLevel;
        }

        private static void AddConfiguredHandlers()
        {
            if (_config.EnableUnityLog)
                Handlers.Add(new UnityFRLogHandler());

            if (_config.EnableFileLog)
                Handlers.Add(CreateFileHandler(_config));
        }

        private static IFRLogHandler CreateFileHandler(FRLogConfig config)
        {
            return config.FileWriteMode == FRLogFileWriteMode.Async ? new AsyncFileFRLogHandler(config) : new FileFRLogHandler(config);
        }

        private static void RemoveFileHandlers()
        {
            for (int i = Handlers.Count - 1; i >= 0; i--)
            {
                if (Handlers[i] is IFRLogFileHandler)
                {
                    Handlers[i].Shutdown();
                    Handlers.RemoveAt(i);
                }
            }
        }

        private static void RemoveHandlers<T>() where T : IFRLogHandler
        {
            for (int i = Handlers.Count - 1; i >= 0; i--)
            {
                if (Handlers[i] is T)
                {
                    Handlers[i].Shutdown();
                    Handlers.RemoveAt(i);
                }
            }
        }

        private static int GetMaxRetainedFileCount()
        {
            return _config.MaxRetainedFileCount > 0 ? _config.MaxRetainedFileCount : _config.MaxLogFileCount;
        }

        private static void IncrementLevelCount(FRLogLevel level)
        {
            switch (level)
            {
                case FRLogLevel.Debug:
                    _debugCount++;
                    break;
                case FRLogLevel.Info:
                    _infoCount++;
                    break;
                case FRLogLevel.Warning:
                    _warningCount++;
                    break;
                case FRLogLevel.Error:
                    _errorCount++;
                    break;
                case FRLogLevel.Fatal:
                    _fatalCount++;
                    break;
            }
        }

        private static FRLogLevel GetDefaultMinimumLevel()
        {
#if FR_ENABLE_DEBUG_LOG || FR_ENABLE_DEBUG_AND_ABOVE_LOG
            return FRLogLevel.Debug;
#elif FR_ENABLE_INFO_LOG || FR_ENABLE_INFO_AND_ABOVE_LOG
            return FRLogLevel.Info;
#elif FR_ENABLE_WARNING_LOG || FR_ENABLE_WARNING_AND_ABOVE_LOG
            return FRLogLevel.Warning;
#elif FR_ENABLE_ERROR_LOG || FR_ENABLE_ERROR_AND_ABOVE_LOG
            return FRLogLevel.Error;
#elif FR_ENABLE_FATAL_LOG || FR_ENABLE_FATAL_AND_ABOVE_LOG
            return FRLogLevel.Fatal;
#else
            return UnityEngine.Debug.isDebugBuild ? FRLogLevel.Debug : FRLogLevel.Warning;
#endif
        }
    }
}
