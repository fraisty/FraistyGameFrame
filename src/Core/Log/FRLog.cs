using System;
using System.Diagnostics;
using Object = UnityEngine.Object;

namespace FRFrame
{
    public static class FRLog
    {
        public static FRLogContext Context => FRLogger.Context;

        public static bool IsEnabled(FRLogLevel level)
        {
            return FRLogger.IsLevelEnabled(level);
        }

        public static IDisposable BeginScope(string key, string value)
        {
            return FRLogger.Context.BeginScope(key, value);
        }

        public static void SetGlobalContext(string key, string value)
        {
            FRLogger.Context.SetGlobal(key, value);
        }

        public static bool RemoveGlobalContext(string key)
        {
            return FRLogger.Context.RemoveGlobal(key);
        }

        public static void SetMinimumLevel(FRLogLevel level)
        {
            FRLogger.SetMinimumLevel(level);
        }

        public static void SetUnityOutputEnabled(bool enabled)
        {
            FRLogger.SetUnityOutputEnabled(enabled);
        }

        public static void SetFileOutputEnabled(bool enabled)
        {
            FRLogger.SetFileOutputEnabled(enabled);
        }

        public static void SetStackTraceLevel(FRLogLevel level)
        {
            FRLogger.SetStackTraceLevel(level);
        }

        public static FRLogRuntimeConfigSnapshot GetRuntimeConfigSnapshot()
        {
            return FRLogger.GetRuntimeConfigSnapshot();
        }

        public static FRLogStatistics GetStatistics()
        {
            return FRLogger.CreateStatisticsSnapshot();
        }

        public static void ResetStatistics()
        {
            FRLogger.ResetStatistics();
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Debug)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        public static void Debug(string message, Object context = null)
        {
            FRLogger.Log(FRLogLevel.Debug, message, context);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Info)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        public static void Info(string message, Object context = null)
        {
            FRLogger.Log(FRLogLevel.Info, message, context);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Warning)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        public static void Warning(string message, Object context = null)
        {
            FRLogger.Log(FRLogLevel.Warning, message, context);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Error)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        public static void Error(string message, Object context = null)
        {
            FRLogger.Log(FRLogLevel.Error, message, context);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Fatal)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        [Conditional(FRLogDefineSymbols.FatalAndAbove)]
        public static void Fatal(string message, Object context = null)
        {
            FRLogger.Log(FRLogLevel.Fatal, message, context);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Debug)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        public static void DebugT(string tag, string message, Object context = null)
        {
            FRLogger.Log(FRLogLevel.Debug, tag, message, context);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Info)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        public static void InfoT(string tag, string message, Object context = null)
        {
            FRLogger.Log(FRLogLevel.Info, tag, message, context);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Warning)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        public static void WarningT(string tag, string message, Object context = null)
        {
            FRLogger.Log(FRLogLevel.Warning, tag, message, context);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Error)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        public static void ErrorT(string tag, string message, Object context = null)
        {
            FRLogger.Log(FRLogLevel.Error, tag, message, context);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Fatal)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        [Conditional(FRLogDefineSymbols.FatalAndAbove)]
        public static void FatalT(string tag, string message, Object context = null)
        {
            FRLogger.Log(FRLogLevel.Fatal, tag, message, context);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Debug)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        public static void Debug<T>(string format, T arg)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Debug)) Debug(FRLogFormatUtility.Format(format, arg));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Debug)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        public static void Debug<T1, T2>(string format, T1 arg1, T2 arg2)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Debug)) Debug(FRLogFormatUtility.Format(format, arg1, arg2));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Debug)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        public static void Debug<T1, T2, T3>(string format, T1 arg1, T2 arg2, T3 arg3)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Debug)) Debug(FRLogFormatUtility.Format(format, arg1, arg2, arg3));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Info)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        public static void Info<T>(string format, T arg)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Info)) Info(FRLogFormatUtility.Format(format, arg));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Info)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        public static void Info<T1, T2>(string format, T1 arg1, T2 arg2)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Info)) Info(FRLogFormatUtility.Format(format, arg1, arg2));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Info)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        public static void Info<T1, T2, T3>(string format, T1 arg1, T2 arg2, T3 arg3)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Info)) Info(FRLogFormatUtility.Format(format, arg1, arg2, arg3));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Warning)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        public static void Warning<T>(string format, T arg)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Warning)) Warning(FRLogFormatUtility.Format(format, arg));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Warning)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        public static void Warning<T1, T2>(string format, T1 arg1, T2 arg2)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Warning)) Warning(FRLogFormatUtility.Format(format, arg1, arg2));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Warning)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        public static void Warning<T1, T2, T3>(string format, T1 arg1, T2 arg2, T3 arg3)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Warning)) Warning(FRLogFormatUtility.Format(format, arg1, arg2, arg3));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Error)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        public static void Error<T>(string format, T arg)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Error)) Error(FRLogFormatUtility.Format(format, arg));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Error)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        public static void Error<T1, T2>(string format, T1 arg1, T2 arg2)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Error)) Error(FRLogFormatUtility.Format(format, arg1, arg2));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Error)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        public static void Error<T1, T2, T3>(string format, T1 arg1, T2 arg2, T3 arg3)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Error)) Error(FRLogFormatUtility.Format(format, arg1, arg2, arg3));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Fatal)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        [Conditional(FRLogDefineSymbols.FatalAndAbove)]
        public static void Fatal<T>(string format, T arg)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Fatal)) Fatal(FRLogFormatUtility.Format(format, arg));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Fatal)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        [Conditional(FRLogDefineSymbols.FatalAndAbove)]
        public static void Fatal<T1, T2>(string format, T1 arg1, T2 arg2)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Fatal)) Fatal(FRLogFormatUtility.Format(format, arg1, arg2));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Fatal)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        [Conditional(FRLogDefineSymbols.FatalAndAbove)]
        public static void Fatal<T1, T2, T3>(string format, T1 arg1, T2 arg2, T3 arg3)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Fatal)) Fatal(FRLogFormatUtility.Format(format, arg1, arg2, arg3));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Debug)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        public static void DebugT<T>(string tag, string format, T arg)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Debug)) FRLogger.Log(FRLogLevel.Debug, tag, FRLogFormatUtility.Format(format, arg));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Debug)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        public static void DebugT<T1, T2>(string tag, string format, T1 arg1, T2 arg2)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Debug)) FRLogger.Log(FRLogLevel.Debug, tag, FRLogFormatUtility.Format(format, arg1, arg2));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Debug)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        public static void DebugT<T1, T2, T3>(string tag, string format, T1 arg1, T2 arg2, T3 arg3)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Debug)) FRLogger.Log(FRLogLevel.Debug, tag, FRLogFormatUtility.Format(format, arg1, arg2, arg3));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Info)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        public static void InfoT<T>(string tag, string format, T arg)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Info)) FRLogger.Log(FRLogLevel.Info, tag, FRLogFormatUtility.Format(format, arg));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Info)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        public static void InfoT<T1, T2>(string tag, string format, T1 arg1, T2 arg2)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Info)) FRLogger.Log(FRLogLevel.Info, tag, FRLogFormatUtility.Format(format, arg1, arg2));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Info)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        public static void InfoT<T1, T2, T3>(string tag, string format, T1 arg1, T2 arg2, T3 arg3)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Info)) FRLogger.Log(FRLogLevel.Info, tag, FRLogFormatUtility.Format(format, arg1, arg2, arg3));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Warning)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        public static void WarningT<T>(string tag, string format, T arg)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Warning)) FRLogger.Log(FRLogLevel.Warning, tag, FRLogFormatUtility.Format(format, arg));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Warning)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        public static void WarningT<T1, T2>(string tag, string format, T1 arg1, T2 arg2)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Warning)) FRLogger.Log(FRLogLevel.Warning, tag, FRLogFormatUtility.Format(format, arg1, arg2));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Warning)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        public static void WarningT<T1, T2, T3>(string tag, string format, T1 arg1, T2 arg2, T3 arg3)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Warning)) FRLogger.Log(FRLogLevel.Warning, tag, FRLogFormatUtility.Format(format, arg1, arg2, arg3));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Error)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        public static void ErrorT<T>(string tag, string format, T arg)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Error)) FRLogger.Log(FRLogLevel.Error, tag, FRLogFormatUtility.Format(format, arg));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Error)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        public static void ErrorT<T1, T2>(string tag, string format, T1 arg1, T2 arg2)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Error)) FRLogger.Log(FRLogLevel.Error, tag, FRLogFormatUtility.Format(format, arg1, arg2));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Error)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        public static void ErrorT<T1, T2, T3>(string tag, string format, T1 arg1, T2 arg2, T3 arg3)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Error)) FRLogger.Log(FRLogLevel.Error, tag, FRLogFormatUtility.Format(format, arg1, arg2, arg3));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Fatal)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        [Conditional(FRLogDefineSymbols.FatalAndAbove)]
        public static void FatalT<T>(string tag, string format, T arg)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Fatal)) FRLogger.Log(FRLogLevel.Fatal, tag, FRLogFormatUtility.Format(format, arg));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Fatal)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        [Conditional(FRLogDefineSymbols.FatalAndAbove)]
        public static void FatalT<T1, T2>(string tag, string format, T1 arg1, T2 arg2)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Fatal)) FRLogger.Log(FRLogLevel.Fatal, tag, FRLogFormatUtility.Format(format, arg1, arg2));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Fatal)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        [Conditional(FRLogDefineSymbols.FatalAndAbove)]
        public static void FatalT<T1, T2, T3>(string tag, string format, T1 arg1, T2 arg2, T3 arg3)
        {
            if (FRLogger.IsLevelEnabled(FRLogLevel.Fatal)) FRLogger.Log(FRLogLevel.Fatal, tag, FRLogFormatUtility.Format(format, arg1, arg2, arg3));
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Error)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        public static void Exception(Exception exception)
        {
            FRLogger.Exception(exception);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Error)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        public static void Exception(Exception exception, Object context)
        {
            FRLogger.Exception(exception, null, context);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Error)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        public static void Exception(Exception exception, string message)
        {
            FRLogger.Exception(exception, message);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Error)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        public static void Exception(Exception exception, string message, Object context)
        {
            FRLogger.Exception(exception, message, context);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Error)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        public static void Exception(string tag, Exception exception, string message = null)
        {
            FRLogger.Exception(tag, exception, message);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Error)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        public static void Exception(string tag, Exception exception, Object context)
        {
            FRLogger.Exception(tag, exception, null, context);
        }

        [Conditional("UNITY_EDITOR")]
        [Conditional(FRLogDefineSymbols.EnableLog)]
        [Conditional(FRLogDefineSymbols.Error)]
        [Conditional(FRLogDefineSymbols.DebugAndAbove)]
        [Conditional(FRLogDefineSymbols.InfoAndAbove)]
        [Conditional(FRLogDefineSymbols.WarningAndAbove)]
        [Conditional(FRLogDefineSymbols.ErrorAndAbove)]
        public static void Exception(string tag, Exception exception, string message, Object context)
        {
            FRLogger.Exception(tag, exception, message, context);
        }
    }
}
