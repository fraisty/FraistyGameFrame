using System;
using System.Collections.Generic;

namespace FRFrame
{
    public sealed class RemoteFRLogHandler : IFRLogHandler
    {
        private readonly RemoteFRLogOptions _options;
        private readonly Queue<FRLogEntry> _queue = new Queue<FRLogEntry>();
        private int _sequence;

        public int QueueCount => _queue.Count;
        public int DroppedCount { get; private set; }

        public RemoteFRLogHandler(RemoteFRLogOptions options)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
        }

        public void Handle(in FRLogEntry entry)
        {
            if (entry.Level < _options.MinimumLevel)
                return;

            if (_options.SampleRate < 1f)
            {
                _sequence++;
                int interval = Math.Max(1, (int)(1f / Math.Max(0.0001f, _options.SampleRate)));
                if (_sequence % interval != 0)
                    return;
            }

            if (_options.MaxQueueCount > 0 && _queue.Count >= _options.MaxQueueCount)
            {
                DroppedCount++;
                return;
            }

            _queue.Enqueue(entry);
            if (_options.BatchSize > 0 && _queue.Count >= _options.BatchSize)
                Flush();
        }

        public void Flush()
        {
            if (string.IsNullOrEmpty(_options.Endpoint))
            {
                _queue.Clear();
                return;
            }

            _queue.Clear();
        }

        public void Shutdown()
        {
            Flush();
        }
    }
}
