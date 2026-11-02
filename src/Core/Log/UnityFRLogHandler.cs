using UnityEngine;

namespace FRFrame
{
    public sealed class UnityFRLogHandler : IFRLogHandler
    {
        public void Handle(in FRLogEntry entry)
        {
            string message = FRLogger.Format(entry);
            switch (entry.Level)
            {
                case FRLogLevel.Warning:
                    LogWarning(message, entry.Context);
                    break;
                case FRLogLevel.Error:
                case FRLogLevel.Fatal:
                    LogError(message, entry.Context);
                    break;
                default:
                    Log(message, entry.Context);
                    break;
            }
        }

        public void Flush()
        {
        }

        public void Shutdown()
        {
        }

        private static void Log(string message, Object context)
        {
            if (context == null)
            {
                Debug.Log(message);
                return;
            }

            Debug.Log(message, context);
        }

        private static void LogWarning(string message, Object context)
        {
            if (context == null)
            {
                Debug.LogWarning(message);
                return;
            }

            Debug.LogWarning(message, context);
        }

        private static void LogError(string message, Object context)
        {
            if (context == null)
            {
                Debug.LogError(message);
                return;
            }

            Debug.LogError(message, context);
        }
    }
}
