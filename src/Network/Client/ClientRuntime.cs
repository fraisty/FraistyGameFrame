#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Text;
using FRFrame.Network.Protocol;
using FRFrame.Network.Shared;

namespace FRFrame.Network.Client
{
    public enum ClientConnectionState { Idle, Connecting, Connected, Authenticating, Bound, Recoverable, Replaced, Disposed }

    /// <summary>A server event for a current room. Observers only receive a read-only view of its owned payload.</summary>
    public sealed class ClientGameEvent
    {
        private readonly byte[] _payload;
        internal ClientGameEvent(int room, int player, uint id, byte[] payload)
        {
            Room = room;
            Player = player;
            Id = id;
            _payload = payload;
        }

        public int Room { get; }
        public int Player { get; }
        public uint Id { get; }
        public ReadOnlySpan<byte> Payload => _payload;
    }

    /// <summary>The server binding is separate from the local transport attempt.</summary>
    public readonly struct ClientSession
    {
        internal ClientSession(int player, int session, int epoch, int generation)
        {
            Player = player;
            Session = session;
            Epoch = epoch;
            ConnectionGeneration = generation;
        }

        public int Player { get; }
        public int Session { get; }
        public int Epoch { get; }
        public int ConnectionGeneration { get; }
    }

    /// <summary>A frozen room projection. A stale snapshot has no locally confirmed online members.</summary>
    public sealed class ClientRoomSnapshot
    {
        internal ClientRoomSnapshot(int id, int owner, IReadOnlyList<int> members, int online, int capacity, string definition, bool current)
        {
            Id = id;
            Owner = owner;
            Members = members;
            OnlineMask = online;
            Capacity = capacity;
            Definition = definition;
            IsCurrent = current;
        }

        public int Id { get; }
        public int Owner { get; }
        public IReadOnlyList<int> Members { get; }
        public int OnlineMask { get; }
        public int Capacity { get; }
        public string Definition { get; }
        public bool IsCurrent { get; }
    }

    /// <summary>Owns client state and one manual transport pump on the calling thread.</summary>
    public sealed class ClientRuntime : IDisposable
    {
        private const uint AuthRequestId = 1;
        private readonly Func<IClientTransport> _createTransport;
        private readonly Func<long> _clock;
        private readonly Action<string> _log;
        private readonly int _connectTimeout, _authTimeout, _requestTimeout;
        private readonly ClientRequests _requests = new();
        private readonly Dictionary<int, ClientRoomSnapshot> _rooms = new();
        private IClientTransport? _transport;
        private TransportConnectionHandle _handle;
        private long _deadline;
        private string _ticket = "";
        private byte[]? _authBytes;
        private bool _resume, _pumping, _starting;

        public ClientRuntime(Func<IClientTransport> createTransport, Action<string>? log = null, Func<long>? clock = null,
            int connectTimeoutMilliseconds = 10_000, int authTimeoutMilliseconds = 10_000, int requestTimeoutMilliseconds = 10_000)
        {
            if (connectTimeoutMilliseconds is < 1 or > 60_000 || authTimeoutMilliseconds is < 1 or > 60_000 ||
                requestTimeoutMilliseconds is < 1 or > 60_000)
                throw new ArgumentOutOfRangeException(nameof(connectTimeoutMilliseconds), "Client timeouts must be within 1–60000 ms.");
            _createTransport = createTransport;
            _clock = clock ?? (() => (long)(Stopwatch.GetTimestamp() * (1000.0 / Stopwatch.Frequency)));
            _log = log ?? (_ => { });
            _connectTimeout = connectTimeoutMilliseconds;
            _authTimeout = authTimeoutMilliseconds;
            _requestTimeout = requestTimeoutMilliseconds;
            Rooms = new ReadOnlyDictionary<int, ClientRoomSnapshot>(_rooms);
        }

        public ClientConnectionState State { get; private set; }
        public ClientSession Binding { get; private set; }
        public long Attempt { get; private set; }
        public bool Connected => _transport?.IsConnected(_handle) == true;
        public bool CanResume => _ticket.Length != 0;
        public ErrorCode ConnectionError { get; private set; }
        public ErrorCode AuthenticationError { get; private set; }
        public ErrorCode LastSendError { get; private set; }
        public IReadOnlyDictionary<int, ClientRoomSnapshot> Rooms { get; }
        public event Action<ClientConnectionState>? StateChanged;
        public event Action<ClientRequestResult>? RequestFinished;
        public event Action<int>? RoomChanged;
        public event Action<(int Room, int Player, string Text)>? RoomMessageReceived;
        public event Action<ClientGameEvent>? GameEventReceived;

        internal IClientTransport Transport => _transport!;
        internal TransportConnectionHandle Handle => _handle;
        internal event Action<(OpCode Op, SliceMessage Message)>? WireReceived;
        internal event Action<ReceivedPacket>? RawReceived;

        public bool Login(string host, int port, string declaration) => Begin(host, port, false, declaration);
        public bool Resume(string host, int port) => Begin(host, port, true, "");

        public uint CreateRoom(string definition) => SendCompatible(OpCode.CreateRoom, new SliceMessage { Text = definition });
        public uint JoinRoom(int room) => SendCompatible(OpCode.JoinRoom, new SliceMessage { Room = room });
        public uint LeaveRoom(int room, int successor = 0) =>
            SendCompatible(OpCode.LeaveRoom, new SliceMessage { Room = room, Target = successor });
        public uint Kick(int room, int player) => SendCompatible(OpCode.Kick, new SliceMessage { Room = room, Target = player });
        public uint TransferOwner(int room, int player) =>
            SendCompatible(OpCode.TransferOwner, new SliceMessage { Room = room, Target = player });
        public uint CloseRoom(int room) => SendCompatible(OpCode.CloseRoom, new SliceMessage { Room = room });
        public uint SendRoomMessage(int room, string text) =>
            SendCompatible(OpCode.RoomMessage, new SliceMessage { Room = room, Text = text });

        public uint SendGameCommand(int room, uint id, ReadOnlySpan<byte> payload)
        {
            if (room <= 0 || id == 0 || payload.Length > GameWire.MaxBodyBytes)
                return RejectSend(ErrorCode.Invalid);
            return SendCompatible(OpCode.GameCommand, new SliceMessage { Room = room, GameMessageId = id, GamePayload = payload.ToArray() });
        }

        /// <summary>Restarts a local wait for an uncertain or transient result, using the original bytes.</summary>
        public uint Retry(uint requestId) => SendIntent(_requests.Find(requestId), _clock(), false);

        /// <summary>Ends a local wait. The server may still execute the original request.</summary>
        public bool Cancel(uint requestId)
        {
            if (_requests.Find(requestId) is not { Pending: true } intent)
                return false;
            Notify(RequestFinished, _requests.Finish(intent, ClientRequestStatus.Cancelled));
            return true;
        }

        private bool Begin(string host, int port, bool resume, string declaration)
        {
            if (_starting || State == ClientConnectionState.Disposed || (resume && !CanResume))
                return false;
            var auth = resume ? new SliceMessage { Token = _ticket } : new SliceMessage { Text = declaration };
            if (string.IsNullOrWhiteSpace(host) || port is < 1 or > 65535 ||
                !TryEncode(resume ? OpCode.Recover : OpCode.Login, auth, AuthRequestId, out var authBytes))
            {
                ConnectionError = ErrorCode.Invalid;
                return false;
            }

            _starting = true;
            try
            {
                if (_transport != null || !resume)
                    EndConnection(ClientRequestStatus.Disconnected, resume, resume ? ClientConnectionState.Recoverable : ClientConnectionState.Idle);
                if (State == ClientConnectionState.Disposed)
                    return false;
                _resume = resume;
                _authBytes = authBytes;
                ConnectionError = AuthenticationError = ErrorCode.None;
                var transport = _createTransport();
                _transport = transport;
                Attempt++;
                if (!transport.Connect(host, port))
                {
                    ConnectionError = ErrorCode.StreamUnavailable;
                    LostConnection();
                    return false;
                }
                _deadline = _clock() + _connectTimeout;
                ChangeState(ClientConnectionState.Connecting);
                return ReferenceEquals(_transport, transport);
            }
            finally { _starting = false; }
        }

        public void Tick(float elapsedMilliseconds) => Tick(elapsedMilliseconds, null);

        internal void Tick(float elapsedMilliseconds, Action? updateCompatibility)
        {
            var transport = _transport;
            if (_pumping || transport == null)
                return;

            _pumping = true;
            try
            {
                var attempt = Attempt;
                var now = _clock();
                PublishResults(_requests.Advance(now));
                if (Attempt != attempt)
                    return;
                if (State is ClientConnectionState.Connecting or ClientConnectionState.Authenticating && now >= _deadline)
                {
                    if (State == ClientConnectionState.Authenticating)
                        AuthenticationError = ErrorCode.CallbackTimeout;
                    else
                        ConnectionError = ErrorCode.CallbackTimeout;
                    LostConnection();
                    return;
                }
                transport.PollIncoming();
                if (State == ClientConnectionState.Connecting && transport.ClientConnection.IsValid)
                {
                    _handle = transport.ClientConnection;
                    ChangeState(ClientConnectionState.Connected);
                    if (Attempt == attempt)
                        SendAuth(now);
                }
                while (Attempt == attempt && Connected && transport.TryReceive(out var packet))
                    if (packet.Connection == _handle)
                        Receive(packet, now);

                if (Attempt == attempt)
                {
                    if (_handle.IsValid && !Connected)
                        LostConnection();
                    else
                    {
                        updateCompatibility?.Invoke();
                        if (Attempt == attempt)
                            transport.CompleteTick(elapsedMilliseconds);
                    }
                }
            }
            finally { _pumping = false; }
        }

        private void SendAuth(long now)
        {
            var bytes = _authBytes!;
            _authBytes = null;
            if (!_transport!.TrySend(_handle, PacketDelivery.ReliableOrdered, bytes))
            {
                AuthenticationError = ErrorCode.Overloaded;
                LostConnection();
                return;
            }
            _deadline = now + _authTimeout;
            ChangeState(ClientConnectionState.Authenticating);
        }

        internal uint SendCompatible(OpCode op, SliceMessage request)
        {
            if (State != ClientConnectionState.Bound)
                return RejectSend(ErrorCode.Unauthenticated);
            var id = request.RequestId == 0 ? _requests.Next : request.RequestId;
            if (id == 0)
                return RejectSend(ErrorCode.Overloaded);
            if (!ProtocolMessages.TryGet(op, out var descriptor) || !descriptor.RequiresSession ||
                !TryEncode(op, request, id, out var bytes))
                return RejectSend(ErrorCode.Invalid);
            var now = _clock();
            ClientRequests.Intent? intent;
            if (request.RequestId == 0)
                intent = _requests.Reserve(op, bytes, now);
            else
            {
                intent = _requests.Find(id);
                if (intent == null || !bytes.AsSpan().SequenceEqual(intent.Bytes))
                    return RejectSend(ErrorCode.RequestIdConflict);
            }
            var sent = SendIntent(intent, now, true);
            if (sent != 0)
                request.RequestId = sent;
            return sent;
        }

        private uint SendIntent(ClientRequests.Intent? intent, long now, bool compatibilityReplay)
        {
            if (State != ClientConnectionState.Bound || !Connected)
            {
                if (intent != null)
                    _requests.ForgetUnsent(intent);
                return RejectSend(ErrorCode.Unauthenticated);
            }
            if (intent == null)
                return RejectSend(ErrorCode.Overloaded);
            if (now >= intent.RetryUntil || (intent.Final && !compatibilityReplay))
                return RejectSend(ErrorCode.InvalidState);
            if (intent.Pending)
            {
                LastSendError = ErrorCode.None;
                return intent.Id;
            }
            if (!_requests.HasWaitSlot)
                return RejectSend(ErrorCode.Overloaded);

            _requests.Start(intent, Attempt, now, _requestTimeout);
            if (_transport!.TrySend(_handle, PacketDelivery.ReliableOrdered, intent.Bytes))
            {
                intent.Sent = true;
                LastSendError = ErrorCode.None;
                return intent.Id;
            }
            var failed = _requests.Finish(intent, ClientRequestStatus.SendFailed, ErrorCode.Overloaded);
            _requests.ForgetUnsent(intent);
            LastSendError = ErrorCode.Overloaded;
            var attempt = Attempt;
            Notify(RequestFinished, failed);
            if (Attempt == attempt && !Connected)
                LostConnection();
            return 0;
        }

        private uint RejectSend(ErrorCode error)
        {
            LastSendError = error;
            return 0;
        }

        private static bool TryEncode(OpCode op, SliceMessage request, uint id, out byte[] bytes)
        {
            bytes = Array.Empty<byte>();
            if (request.Text == null || request.Token == null || request.MemberIds == null ||
                request.Text.Length > 80 || Encoding.UTF8.GetByteCount(request.Text) > SliceCodec.MaxTextBytes ||
                request.Token.Length > SliceCodec.MaxTicketBytes)
                return false;
            var previous = request.RequestId;
            request.RequestId = id;
            try
            {
                if (!request.IsRequest(op))
                    return false;
                bytes = SliceCodec.Write(op, request);
                return true;
            }
            catch (ArgumentOutOfRangeException) { return false; }
            finally { request.RequestId = previous; }
        }

        private void Receive(ReceivedPacket packet, long now)
        {
            if (!packet.IsEnvelope)
            {
                if (State == ClientConnectionState.Bound)
                    Notify(RawReceived, packet);
                return;
            }
            if (!SliceCodec.TryRead(packet.Header, packet.Payload, out var message, out _))
                return;
            var op = packet.Header.Op;
            var attempt = Attempt;
            if (State == ClientConnectionState.Authenticating)
            {
                if (op == OpCode.Response && message.RequestId == AuthRequestId && message.ReplyTo == (_resume ? OpCode.Recover : OpCode.Login))
                    ApplyAuth(op, message);
                return;
            }
            if (State != ClientConnectionState.Bound)
                return;

            switch (op)
            {
                case OpCode.Response:
                    if (_requests.Find(message.RequestId) is not { Pending: true } intent || intent.Attempt != attempt ||
                        intent.Op != message.ReplyTo || now >= intent.Deadline || !MatchesBinding(message))
                        return;
                    Notify(RequestFinished, _requests.Finish(intent, ClientRequestStatus.Response, message.Error));
                    break;
                case OpCode.Members:
                    if (message.Room <= 0 || !message.MemberIds.Contains(Binding.Player))
                        return;
                    _rooms[message.Room] = new ClientRoomSnapshot(message.Room, message.Player,
                        Array.AsReadOnly((int[])message.MemberIds.Clone()), message.Online, message.Capacity, message.Text, true);
                    Notify(RoomChanged, message.Room);
                    break;
                case OpCode.RoomClosed:
                case OpCode.RoomLeft:
                    if (!_rooms.Remove(message.Room))
                        return;
                    Notify(RoomChanged, message.Room);
                    break;
                case OpCode.SessionReplaced:
                    if (message.Session == Binding.Session && message.Player == Binding.Player)
                        EndConnection(ClientRequestStatus.Replaced, false, ClientConnectionState.Replaced);
                    return;
                case OpCode.RoomBroadcast:
                    if (!_rooms.TryGetValue(message.Room, out var room) || !room.IsCurrent)
                        return;
                    Notify(RoomMessageReceived, (message.Room, message.Player, message.Text));
                    break;
                case OpCode.GameEvent:
                    if (_rooms.TryGetValue(message.Room, out var gameRoom) && gameRoom.IsCurrent)
                        Notify(GameEventReceived, new ClientGameEvent(message.Room, message.Player, message.GameMessageId, message.GamePayload));
                    return;
                default:
                    // The POC compatibility bridge consumes its own pushes for current rooms.
                    if (!_rooms.TryGetValue(message.Room, out var current) || !current.IsCurrent)
                        return;
                    break;
            }
            if (Attempt == attempt)
                Notify(WireReceived, (op, message));
        }

        private bool MatchesBinding(SliceMessage message) =>
            (message.Session == Binding.Session && message.Epoch == Binding.Epoch) ||
            (message.Session == 0 && message.Epoch == 0 && message.ConnectionGeneration == 0 &&
             message.Error is ErrorCode.Invalid or ErrorCode.Unauthenticated or ErrorCode.RoomClosing or ErrorCode.ServerDraining);

        private void ApplyAuth(OpCode op, SliceMessage message)
        {
            if (message.Error != ErrorCode.None)
            {
                AuthenticationError = message.Error;
                var retain = _resume && message.Error == ErrorCode.Overloaded;
                var failedAttempt = Attempt + 1;
                EndConnection(ClientRequestStatus.Disconnected, retain,
                    retain ? ClientConnectionState.Recoverable : ClientConnectionState.Idle);
                if (Attempt == failedAttempt)
                    Notify(WireReceived, (op, message));
                return;
            }
            Span<byte> ticket = stackalloc byte[32];
            if (message.Player <= 0 || message.Session <= 0 || message.Epoch <= 0 || message.ConnectionGeneration <= 0 ||
                !Convert.TryFromBase64String(message.Token, ticket, out var length) || length != ticket.Length ||
                (_resume && (message.Player != Binding.Player || message.Session != Binding.Session ||
                 message.Epoch != Binding.Epoch || message.ConnectionGeneration <= Binding.ConnectionGeneration || message.Token == _ticket)))
                return;

            Binding = new ClientSession(message.Player, message.Session, message.Epoch, message.ConnectionGeneration);
            _ticket = message.Token;
            foreach (var room in _rooms.Keys.Where(id => !message.MemberIds.Contains(id)).ToArray())
                _rooms.Remove(room);
            var attempt = Attempt;
            ChangeState(ClientConnectionState.Bound);
            if (Attempt == attempt)
                Notify(WireReceived, (op, message));
        }

        public void Disconnect()
        {
            if (_transport != null)
                LostConnection();
        }

        private void LostConnection() => EndConnection(ClientRequestStatus.Disconnected, CanResume,
            CanResume ? ClientConnectionState.Recoverable : ClientConnectionState.Idle);

        private void EndConnection(ClientRequestStatus status, bool keepSession, ClientConnectionState state)
        {
            var transport = _transport;
            var handle = _handle;
            _transport = null;
            _handle = default;
            _authBytes = null;
            Attempt++;
            var results = _requests.Advance(_clock(), status);
            if (keepSession)
            {
                foreach (var id in _rooms.Keys.ToArray())
                {
                    var room = _rooms[id];
                    _rooms[id] = new ClientRoomSnapshot(id, room.Owner, room.Members, 0, room.Capacity, room.Definition, false);
                }
            }
            else
            {
                Binding = default;
                _ticket = "";
                _requests.Reset();
                _rooms.Clear();
            }
            if (transport != null)
            {
                if (handle.IsValid)
                    transport.Disconnect(handle);
                transport.Dispose();
            }
            ChangeState(state);
            PublishResults(results, state == ClientConnectionState.Disposed);
        }

        public void Dispose()
        {
            if (State == ClientConnectionState.Disposed)
                return;
            EndConnection(ClientRequestStatus.Cancelled, false, ClientConnectionState.Disposed);
            RequestFinished = null;
            StateChanged = null;
            RoomChanged = null;
            RoomMessageReceived = null;
            GameEventReceived = null;
            WireReceived = null;
            RawReceived = null;
        }

        private void ChangeState(ClientConnectionState state)
        {
            if (State == state)
                return;
            State = state;
            Notify(StateChanged, state, state == ClientConnectionState.Disposed);
        }

        private void PublishResults(List<ClientRequestResult>? results, bool disposing = false)
        {
            if (results != null)
                foreach (var result in results)
                    Notify(RequestFinished, result, disposing);
        }

        private void Notify<T>(Action<T>? observers, T value, bool disposing = false)
        {
            if (observers == null)
                return;
            var attempt = Attempt;
            foreach (Action<T> observer in observers.GetInvocationList())
            {
                if (attempt != Attempt || (!disposing && State == ClientConnectionState.Disposed))
                    break;
                try { observer(value); }
                catch (Exception error) { _log($"Client observer failed: {error.GetType().Name}"); }
            }
        }
    }
}
