using System;
using System.Collections.Generic;

namespace FRFrame
{
    public readonly struct FRLogEntry
    {
        public readonly FRLogLevel Level;
        public readonly DateTime Time;
        public readonly int FrameCount;
        public readonly int ThreadId;
        public readonly string Tag;
        public readonly string Message;
        public readonly string StackTrace;
        public readonly UnityEngine.Object Context;
        public readonly IReadOnlyDictionary<string, string> Fields;
        public readonly Exception Exception;

        public FRLogEntry(FRLogLevel level, DateTime time, int frameCount, int threadId, string tag, string message, string stackTrace, UnityEngine.Object context, IReadOnlyDictionary<string, string> fields = null, Exception exception = null)
        {
            Level = level;
            Time = time;
            FrameCount = frameCount;
            ThreadId = threadId;
            Tag = tag;
            Message = message;
            StackTrace = stackTrace;
            Context = context;
            Fields = fields;
            Exception = exception;
        }
    }
}
