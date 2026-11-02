using System;
using System.IO;
using System.Linq;
using System.Text;

namespace FRFrame
{
    public sealed class FileFRLogHandler : IFRLogHandler, IFRLogFileHandler
    {
        private readonly FRLogConfig _config;
        private readonly object _lock = new object();
        private StreamWriter _writer;
        private string _currentPath;
        private int _fileIndex;
        private readonly string _sessionDirectory;

        public string CurrentLogFilePath => _currentPath;
        public int QueueCount => 0;
        public int DroppedCount => 0;

        public FileFRLogHandler(FRLogConfig config)
        {
            _config = config;
            _sessionDirectory = GetLogDirectory(config);
            OpenWriter();
        }

        public void Handle(in FRLogEntry entry)
        {
            lock (_lock)
            {
                WriteEntry(entry);
                if (_config.FlushImmediatelyOnError && entry.Level >= FRLogLevel.Error)
                    _writer.Flush();
            }
        }

        public void Flush()
        {
            lock (_lock)
            {
                _writer?.Flush();
            }
        }

        public void Shutdown()
        {
            lock (_lock)
            {
                _writer?.Flush();
                _writer?.Dispose();
                _writer = null;
            }
        }

        internal void WriteEntry(in FRLogEntry entry)
        {
            RotateIfNeeded();
            _writer.WriteLine(FRLogger.Format(entry));
            if (!string.IsNullOrEmpty(entry.StackTrace))
            {
                _writer.WriteLine("StackTrace:");
                _writer.WriteLine(entry.StackTrace);
            }
        }

        private void OpenWriter()
        {
            Directory.CreateDirectory(_sessionDirectory);
            string suffix = _fileIndex == 0 ? string.Empty : $"_{_fileIndex:000}";
            DateTime now = _config.UseUtcTime ? DateTime.UtcNow : DateTime.Now;
            string utc = _config.UseUtcTime ? "_UTC" : string.Empty;
            _currentPath = Path.Combine(_sessionDirectory, $"{_config.FileNamePrefix}_{now:yyyyMMdd_HHmmss_fff}{utc}{suffix}.log");
            _writer = new StreamWriter(_currentPath, true, Encoding.UTF8) { AutoFlush = false };
            TrimOldFiles();
        }

        private void RotateIfNeeded()
        {
            if (_writer == null)
            {
                OpenWriter();
                return;
            }

            if (_config.MaxLogFileSize <= 0)
                return;

            FileInfo fileInfo = new FileInfo(_currentPath);
            if (fileInfo.Exists && fileInfo.Length >= _config.MaxLogFileSize)
            {
                _writer.Flush();
                _writer.Dispose();
                _fileIndex++;
                OpenWriter();
            }
        }

        private void TrimOldFiles()
        {
            if (!_config.EnableLogRetention)
                return;

            DirectoryInfo root = new DirectoryInfo(_config.FileLogDirectory);
            if (!root.Exists)
                return;

            FileInfo[] files = root.GetFiles($"{_config.FileNamePrefix}_*.log", SearchOption.AllDirectories)
                .OrderByDescending(file => file.LastWriteTimeUtc)
                .ToArray();

            int maxFileCount = _config.MaxRetainedFileCount > 0 ? _config.MaxRetainedFileCount : _config.MaxLogFileCount;
            for (int i = maxFileCount; maxFileCount > 0 && i < files.Length; i++)
            {
                DeleteFile(files[i]);
            }

            if (_config.MaxRetainedDays > 0)
            {
                DateTime limit = DateTime.UtcNow.AddDays(-_config.MaxRetainedDays);
                for (int i = 0; i < files.Length; i++)
                {
                    if (files[i].Exists && files[i].LastWriteTimeUtc < limit)
                        DeleteFile(files[i]);
                }
            }

            if (_config.GroupLogBySession && _config.MaxRetainedSessionCount > 0)
            {
                DirectoryInfo[] sessions = root.GetDirectories("Session_*")
                    .OrderByDescending(dir => dir.LastWriteTimeUtc)
                    .ToArray();

                for (int i = _config.MaxRetainedSessionCount; i < sessions.Length; i++)
                {
                    try
                    {
                        sessions[i].Delete(true);
                    }
                    catch (Exception e)
                    {
                        UnityEngine.Debug.LogWarning($"[FRFrame][Log] Delete old log session failed: {sessions[i].FullName}, {e.Message}");
                    }
                }
            }
        }

        private static void DeleteFile(FileInfo file)
        {
            try
            {
                file.Delete();
            }
            catch (Exception e)
            {
                UnityEngine.Debug.LogWarning($"[FRFrame][Log] Delete old log failed: {file.FullName}, {e.Message}");
            }
        }

        internal static string GetLogDirectory(FRLogConfig config)
        {
            if (!config.GroupLogBySession)
                return config.FileLogDirectory;

            DateTime now = config.UseUtcTime ? DateTime.UtcNow : DateTime.Now;
            string utc = config.UseUtcTime ? "_UTC" : string.Empty;
            return Path.Combine(config.FileLogDirectory, $"Session_{now:yyyyMMdd_HHmmss_fff}{utc}");
        }
    }
}
