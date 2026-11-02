using System;
using System.Collections.Generic;

namespace FRFrame
{
    public enum FRLogFileWriteMode
    {
        Sync,
        Async
    }

    public enum FRLogAsyncQueueOverflowPolicy
    {
        DropNewest,
        DropOldest,
        Block,
        WriteSync
    }

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

    public sealed class RemoteFRLogOptions
    {
        public string Endpoint;
        public FRLogLevel MinimumLevel = FRLogLevel.Warning;
        public int BatchSize = 20;
        public float FlushInterval = 5f;
        public int MaxQueueCount = 1000;
        public float SampleRate = 1f;
    }

    public interface ICrashReportAdapter
    {
        void SetUserId(string userId);
        void SetCustomKey(string key, string value);
        void Log(string message);
        void RecordException(Exception exception, string message = null);
    }

    internal interface IFRLogFileHandler
    {
        string CurrentLogFilePath { get; }
        int QueueCount { get; }
        int DroppedCount { get; }
    }

    internal static class FRLogFormatUtility
    {
        public static string Format<T>(string format, T arg)
        {
            return string.Format(format, arg);
        }

        public static string Format<T1, T2>(string format, T1 arg1, T2 arg2)
        {
            return string.Format(format, arg1, arg2);
        }

        public static string Format<T1, T2, T3>(string format, T1 arg1, T2 arg2, T3 arg3)
        {
            return string.Format(format, arg1, arg2, arg3);
        }

        public static Dictionary<string, string> CopyFields(IReadOnlyDictionary<string, string> fields)
        {
            if (fields == null || fields.Count == 0)
                return null;

            return new Dictionary<string, string>(fields);
        }
    }
}
