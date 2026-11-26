#nullable enable
using System;
using System.Collections.Generic;
using FRFrame.Network.Shared;

namespace FRFrame.Network.Client
{
    public enum ClientRequestStatus { Response, TimedOut, Cancelled, Disconnected, SendFailed, Replaced }

    /// <summary>A single wait attempt. A lost or cancelled wait may already have executed on the server.</summary>
    public readonly struct ClientRequestResult
    {
        internal ClientRequestResult(uint id, long attempt, ClientRequestStatus status, ErrorCode error, bool uncertain)
        {
            RequestId = id;
            Attempt = attempt;
            Status = status;
            Error = error;
            MayHaveExecuted = uncertain;
        }

        public uint RequestId { get; }
        public long Attempt { get; }
        public ClientRequestStatus Status { get; }
        public ErrorCode Error { get; }
        public bool MayHaveExecuted { get; }
    }

    /// <summary>Session-bound intent storage. Only this owner can turn a pending wait into a terminal result.</summary>
    internal sealed class ClientRequests
    {
        internal const int EntryLimit = 64, PendingLimit = 16, ByteLimit = EntryLimit * Envelope.MaxPacket;

        internal sealed class Intent
        {
            public readonly uint Id;
            public readonly OpCode Op;
            public readonly byte[] Bytes;
            public readonly long RetryUntil;
            public long Attempt, Deadline;
            public bool Pending, Sent, Final;

            public Intent(uint id, OpCode op, byte[] bytes, long retryUntil)
            {
                Id = id;
                Op = op;
                Bytes = bytes;
                RetryUntil = retryUntil;
            }
        }

        private readonly Dictionary<uint, Intent> _intents = new();
        private uint _last;
        private int _pending, _bytes;
        public uint Next => _last == uint.MaxValue ? 0 : _last + 1;
        public Intent? Find(uint id) => _intents.TryGetValue(id, out var value) ? value : null;
        public bool HasWaitSlot => _pending < PendingLimit;

        public Intent? Reserve(OpCode op, byte[] bytes, long now)
        {
            if (Next == 0 || _intents.Count >= EntryLimit || _bytes + bytes.Length > ByteLimit || !HasWaitSlot)
                return null;
            var intent = new Intent(++_last, op, bytes, now + RequestReplayWindow.ClientRetryMilliseconds);
            _intents.Add(intent.Id, intent);
            _bytes += bytes.Length;
            return intent;
        }

        public void Start(Intent intent, long attempt, long now, int timeout)
        {
            intent.Attempt = attempt;
            intent.Deadline = Math.Min(now + timeout, intent.RetryUntil);
            intent.Pending = true;
            _pending++;
        }

        public ClientRequestResult Finish(Intent intent, ClientRequestStatus status, ErrorCode error = ErrorCode.None)
        {
            intent.Pending = false;
            _pending--;
            if (status == ClientRequestStatus.Response && error != ErrorCode.Overloaded)
                intent.Final = true;
            return new ClientRequestResult(intent.Id, intent.Attempt, status, error,
                intent.Sent && status != ClientRequestStatus.Response);
        }

        public void ForgetUnsent(Intent intent)
        {
            if (!intent.Sent && _intents.Remove(intent.Id))
                _bytes -= intent.Bytes.Length;
        }

        public List<ClientRequestResult>? Advance(long now, ClientRequestStatus? stop = null)
        {
            List<ClientRequestResult>? results = null;
            List<uint>? expired = null;
            foreach (var pair in _intents)
            {
                var intent = pair.Value;
                if (intent.Pending && (stop.HasValue || now >= intent.Deadline))
                {
                    results ??= new List<ClientRequestResult>();
                    results.Add(Finish(intent, stop ?? ClientRequestStatus.TimedOut));
                }
                if (now >= intent.RetryUntil)
                {
                    expired ??= new List<uint>();
                    expired.Add(intent.Id);
                    _bytes -= intent.Bytes.Length;
                }
            }
            if (expired != null)
                foreach (var id in expired)
                    _intents.Remove(id);
            return results;
        }

        public void Reset()
        {
            _intents.Clear();
            _bytes = _pending = 0;
            _last = 0;
        }
    }
}
