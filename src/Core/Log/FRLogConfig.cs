using System.IO;
using UnityEngine;

namespace FRFrame
{
    public sealed class FRLogConfig
    {
        public bool EnableUnityLog = true;
        public bool EnableFileLog = false;
        public FRLogLevel MinimumLevel = FRLogLevel.Debug;
        public bool ShowTimestamp = true;
        public bool ShowFrameCount = true;
        public bool ShowThreadId = false;
        public bool CaptureStackTrace = true;
        public FRLogLevel StackTraceMinLevel = FRLogLevel.Warning;
        public string FileLogDirectory = Path.Combine(Application.persistentDataPath, "FRFrameOut", "Logs");
        public long MaxLogFileSize = 10 * 1024 * 1024;
        public int MaxLogFileCount = 10;
        public bool EnableLogRetention = true;
        public int MaxRetainedFileCount = 10;
        public int MaxRetainedDays = 0;
        public bool GroupLogBySession = false;
        public int MaxRetainedSessionCount = 0;
        public bool FlushImmediatelyOnError = true;
        public string FileNamePrefix = "FRLog";
        public bool UseUtcTime = false;
        public FRLogFileWriteMode FileWriteMode = FRLogFileWriteMode.Sync;
        public int AsyncFileQueueCapacity = 4096;
        public int AsyncFileBatchSize = 64;
        public int AsyncFileFlushIntervalMs = 1000;
        public FRLogAsyncQueueOverflowPolicy AsyncQueueOverflowPolicy = FRLogAsyncQueueOverflowPolicy.DropNewest;
    }
}
