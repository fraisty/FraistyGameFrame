#nullable enable
using System;
using FRFrame.Network.Protocol;
using FRFrame.Network.Shared;

namespace FRFrame.Network.Client
{
    /// <summary>One Counter room projection. Initial membership and Resume request a fresh authoritative snapshot.</summary>
    public sealed class CounterRoomState : IDisposable
    {
        private readonly ClientRuntime _client;
        private bool _member;
        private bool _disposed;
        private uint _query;
        private CounterState? _bufferedEvent;

        public CounterRoomState(ClientRuntime client, int roomId)
        {
            _client = client ?? throw new ArgumentNullException(nameof(client));
            if (roomId <= 0)
                throw new ArgumentOutOfRangeException(nameof(roomId));
            RoomId = roomId;
            _client.StateChanged += OnStateChanged;
            _client.RoomChanged += OnRoomChanged;
            _client.RequestFinished += OnResult;
            _client.GameEventReceived += OnEvent;
            OnRoomChanged(RoomId);
        }

        public int RoomId { get; }
        public CounterState? Snapshot { get; private set; }
        public bool IsCurrent { get; private set; }

        /// <summary>Requests a new read if the room is current. Call again explicitly after a failed wait or admission.</summary>
        public uint Refresh()
        {
            if (_disposed || !_member || _query != 0)
                return 0;
            _query = _client.SendGameQuery(RoomId, CounterProtocol.GetState, ReadOnlySpan<byte>.Empty);
            return _query;
        }

        private void OnStateChanged(ClientConnectionState state)
        {
            if (state != ClientConnectionState.Bound)
                MarkStale();
            OnRoomChanged(RoomId);
        }

        private void OnRoomChanged(int room)
        {
            if (room != RoomId)
                return;
            if (!_client.Rooms.TryGetValue(RoomId, out var current) || current.Definition != CounterProtocol.DefinitionId)
            {
                MarkStale();
                Snapshot = null;
            }
            else if (current.IsCurrent && _client.State == ClientConnectionState.Bound && !_member)
            {
                _member = true;
                Refresh();
            }
        }

        private void MarkStale()
        {
            _member = false;
            _query = 0;
            _bufferedEvent = null;
            IsCurrent = false;
        }

        private void OnResult(ClientRequestResult result)
        {
            if (!_member || result.RequestId != _query || result.Attempt != _client.Attempt ||
                result.Operation != OpCode.GameQuery || result.RoomId != RoomId)
                return;
            _query = 0;
            if (result.Status != ClientRequestStatus.Response || result.Error != ErrorCode.None ||
                result.GameQueryId != CounterProtocol.GetState || !CounterProtocol.TryReadState(result.GameQueryPayload, out var state) ||
                (Snapshot is { } previous && state.Revision < previous.Revision))
                return;

            Apply(state);
            if (_bufferedEvent is { } buffered)
                Apply(buffered);
            _bufferedEvent = null;
            IsCurrent = true;
        }

        private void OnEvent(ClientGameEvent change)
        {
            if (!_member || change.Room != RoomId || change.Id != CounterProtocol.Changed ||
                !CounterProtocol.TryReadChangedState(change.Payload, out var state))
                return;
            if (IsCurrent)
                Apply(state);
            // Events and responses use different channels. Anchor each membership before accepting an event.
            else if (_bufferedEvent is not { } buffered || state.Revision > buffered.Revision)
                _bufferedEvent = state;
        }

        private void Apply(CounterState state)
        {
            if (Snapshot is not { } current || state.Revision > current.Revision)
                Snapshot = state;
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            _client.StateChanged -= OnStateChanged;
            _client.RoomChanged -= OnRoomChanged;
            _client.RequestFinished -= OnResult;
            _client.GameEventReceived -= OnEvent;
            MarkStale();
        }
    }
}
