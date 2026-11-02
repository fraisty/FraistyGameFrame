using System;
using System.Collections.Generic;
using System.Threading;

namespace FRFrame
{
    public sealed class AsyncFileFRLogHandler : IFRLogHandler, IFRLogFileHandler
    {
        private readonly FRLogConfig _config;
        private readonly FileFRLogHandler _writer;
        private readonly Queue<FRLogEntry> _queue = new Queue<FRLogEntry>();
        private readonly object _lock = new object();
        private readonly AutoResetEvent _signal = new AutoResetEvent(false);
        private readonly Thread _thread;
        private bool _running = true;
        private int _droppedCount;

        public string CurrentLogFilePath => _writer.CurrentLogFilePath;

        public int QueueCount
        {
            get
            {
                lock (_lock)
                    return _queue.Count;
            }
        }

        public int DroppedCount => _droppedCount + _writer.DroppedCount;

        public AsyncFileFRLogHandler(FRLogConfig config)
        {
            _config = config;
            _writer = new FileFRLogHandler(config);
            _thread = new Thread(WriteLoop)
            {
                IsBackground = true,
                Name = "FRLogFileWriter"
            };
            _thread.Start();
        }

        public void Handle(in FRLogEntry entry)
        {
            bool writeSync = false;
            lock (_lock)
            {
                int capacity = _config.AsyncFileQueueCapacity;
                if (capacity > 0 && _queue.Count >= capacity)
                {
                    switch (_config.AsyncQueueOverflowPolicy)
                    {
                        case FRLogAsyncQueueOverflowPolicy.DropOldest:
                            _queue.Dequeue();
                            _droppedCount++;
                            break;
                        case FRLogAsyncQueueOverflowPolicy.Block:
                            while (_running && _queue.Count >= capacity)
                                Monitor.Wait(_lock, 10);
                            break;
                        case FRLogAsyncQueueOverflowPolicy.WriteSync:
                            writeSync = true;
                            break;
                        default:
                            _droppedCount++;
                            return;
                    }
                }

                if (!writeSync)
                    _queue.Enqueue(entry);
            }

            if (writeSync)
            {
                _writer.Handle(entry);
                return;
            }

            _signal.Set();
            if (_config.FlushImmediatelyOnError && entry.Level >= FRLogLevel.Error)
                Flush();
        }

        public void Flush()
        {
            while (true)
            {
                FRLogEntry[] batch = DequeueBatch(int.MaxValue);
                if (batch.Length == 0)
                    break;

                WriteBatch(batch);
            }

            _writer.Flush();
        }

        public void Shutdown()
        {
            _running = false;
            _signal.Set();
            _thread.Join(2000);
            Flush();
            _writer.Shutdown();
            _signal.Dispose();
        }

        private void WriteLoop()
        {
            while (_running)
            {
                _signal.WaitOne(Math.Max(1, _config.AsyncFileFlushIntervalMs));
                FRLogEntry[] batch = DequeueBatch(Math.Max(1, _config.AsyncFileBatchSize));
                if (batch.Length > 0)
                {
                    WriteBatch(batch);
                    _writer.Flush();
                }
            }
        }

        private FRLogEntry[] DequeueBatch(int maxCount)
        {
            lock (_lock)
            {
                int count = Math.Min(maxCount, _queue.Count);
                if (count <= 0)
                    return Array.Empty<FRLogEntry>();

                FRLogEntry[] batch = new FRLogEntry[count];
                for (int i = 0; i < count; i++)
                {
                    batch[i] = _queue.Dequeue();
                }

                Monitor.PulseAll(_lock);
                return batch;
            }
        }

        private void WriteBatch(FRLogEntry[] batch)
        {
            for (int i = 0; i < batch.Length; i++)
            {
                _writer.Handle(batch[i]);
            }
        }
    }
}
