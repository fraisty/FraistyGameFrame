using System;

namespace FRFrame
{
    public sealed class CrashReportFRLogHandler : IFRLogHandler
    {
        private readonly ICrashReportAdapter _adapter;
        private readonly FRLogLevel _minimumLevel;

        public CrashReportFRLogHandler(ICrashReportAdapter adapter, FRLogLevel minimumLevel = FRLogLevel.Error)
        {
            _adapter = adapter ?? throw new ArgumentNullException(nameof(adapter));
            _minimumLevel = minimumLevel;
        }

        public void Handle(in FRLogEntry entry)
        {
            if (entry.Level < _minimumLevel)
                return;

            try
            {
                if (entry.Fields != null)
                {
                    foreach (var pair in entry.Fields)
                        _adapter.SetCustomKey(pair.Key, pair.Value);
                }

                _adapter.Log(FRLogger.Format(entry));
                if (entry.Exception != null)
                    _adapter.RecordException(entry.Exception, entry.Message);
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[FRFrame][Log] Crash report handler failed: {e.Message}");
            }
        }

        public void Flush()
        {
        }

        public void Shutdown()
        {
        }
    }
}
