#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

namespace FRFrame.Editor
{
    public enum FRFrameBuildLogLevel
    {
        Info,
        Warning,
        Error
    }

    public readonly struct FRFrameBuildLogEntry
    {
        public readonly DateTime Time;
        public readonly FRFrameBuildLogLevel Level;
        public readonly string Message;

        public FRFrameBuildLogEntry(FRFrameBuildLogLevel level, string message)
        {
            Time = DateTime.Now;
            Level = level;
            Message = message ?? string.Empty;
        }
    }

    public sealed class FRFrameBuildLogger
    {
        private readonly List<FRFrameBuildLogEntry> _entries = new List<FRFrameBuildLogEntry>();

        public IReadOnlyList<FRFrameBuildLogEntry> Entries => _entries;

        public void Info(string message)
        {
            Add(FRFrameBuildLogLevel.Info, message);
        }

        public void Warning(string message)
        {
            Add(FRFrameBuildLogLevel.Warning, message);
        }

        public void Error(string message)
        {
            Add(FRFrameBuildLogLevel.Error, message);
        }

        public void Clear()
        {
            _entries.Clear();
        }

        public string ToText()
        {
            StringBuilder builder = new StringBuilder();
            foreach (FRFrameBuildLogEntry entry in _entries)
            {
                builder.Append('[').Append(entry.Time.ToString("HH:mm:ss")).Append("] ");
                builder.Append('[').Append(entry.Level).Append("] ");
                builder.AppendLine(entry.Message);
            }

            return builder.ToString();
        }

        private void Add(FRFrameBuildLogLevel level, string message)
        {
            FRFrameBuildLogEntry entry = new FRFrameBuildLogEntry(level, message);
            _entries.Add(entry);

            switch (level)
            {
                case FRFrameBuildLogLevel.Warning:
                    Debug.LogWarning(message);
                    break;
                case FRFrameBuildLogLevel.Error:
                    Debug.LogError(message);
                    break;
                default:
                    Debug.Log(message);
                    break;
            }
        }
    }
}
#endif
