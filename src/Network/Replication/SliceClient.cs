#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using FRFrame.Network.Client;
using FRFrame.Network.Shared;
using FRFrame.Network.Transport;
using LiteEntitySystem;

namespace FRFrame.Network.Replication
{
    public enum SliceStreamState { Idle, Pending, Ready, Failed, Closed }

    /// <summary>Checks a manager packet before it is given to the LES decoder.</summary>
    public delegate bool LesPacketValidator(ReadOnlySpan<byte> packet, int mtu);

    /// <summary>Immutable local schema and pump callbacks for one LES room definition.</summary>
    public sealed class ClientLesRegistration
    {
        /// <summary>
        /// Registers a local type map and synchronous packet validators. An optional step borrows the ready
        /// room manager to apply input, run its native Update and read the view. Without a step the client updates it.
        /// </summary>
        public ClientLesRegistration(string definitionId, int version, Func<EntityTypesMap> types,
            LesPacketValidator baseline, LesPacketValidator diff, Action<ClientEntityManager, int>? step = null)
        {
            if (!GameWire.IsDefinitionId(definitionId) || definitionId == SliceMessage.PlainRoom)
                throw new ArgumentException("Invalid LES room definition.", nameof(definitionId));
            if (version <= 0)
                throw new ArgumentOutOfRangeException(nameof(version));
            DefinitionId = definitionId;
            Version = version;
            Types = types ?? throw new ArgumentNullException(nameof(types));
            Baseline = baseline ?? throw new ArgumentNullException(nameof(baseline));
            Diff = diff ?? throw new ArgumentNullException(nameof(diff));
            Step = step;
        }

        public string DefinitionId { get; }
        public int Version { get; }
        internal Func<EntityTypesMap> Types { get; }
        internal LesPacketValidator Baseline { get; }
        internal LesPacketValidator Diff { get; }
        internal Action<ClientEntityManager, int>? Step { get; }
    }

    public readonly struct PawnSample
    {
        public readonly int Room, Player, Entity;
        public readonly float X, Y;
        public readonly bool Owned;
        public PawnSample(int room, int player, int entity, float x, float y, bool owned)
        {
            Room = room;
            Player = player;
            Entity = entity;
            X = x;
            Y = y;
            Owned = owned;
        }
    }

    /// <summary>One independent view/manager projection within a connection.</summary>
    public sealed class SliceClientRoom
    {
        public int Id { get; }
        internal ClientRoomSnapshot Snapshot { get; set; }
        public string Definition => Snapshot.Definition;
        public int Owner => Snapshot.Owner;
        public int[] Members => Snapshot.Members.ToArray();
        public int OnlineMask => Snapshot.OnlineMask;
        public bool IsCurrent => Snapshot.IsCurrent;
        public int Marker { get; internal set; }
        public SliceStreamState State { get; internal set; }
        public ErrorCode Error { get; internal set; }
        public int Baselines { get; internal set; }
        internal byte[]? EarlyBaseline;
        public int Diffs { get; internal set; }
        public uint StreamId { get; internal set; }
        public uint Generation => Route?.Generation ?? 0;
        public int WireHeader => Route?.WireHeader ?? 0;
        internal ClientEntityManager? Manager;
        internal ClientLesRegistration? Replication { get; }
        internal LesStreamRoutes.Route? Route;
        internal uint OpenRequest, ReadyRequest;
        internal float Remaining;

        internal SliceClientRoom(ClientRoomSnapshot snapshot, ClientLesRegistration? replication)
        {
            Id = snapshot.Id;
            Snapshot = snapshot;
            Replication = replication;
        }
    }

    /// <summary>Maintains one transport connection and pumps all negotiated managers on the caller's thread.</summary>
    public sealed class SliceClient : IDisposable
    {
        private readonly ClientRuntime _runtime;
        private readonly LesStreamRoutes _routes = new();
        private readonly Dictionary<int, SliceClientRoom> _rooms = new();
        private readonly Dictionary<string, ClientLesRegistration> _replicationRegistrations = new(StringComparer.Ordinal);
        private readonly string _host, _identity;
        private readonly int _port;
        private readonly Action<string> _log;
        private bool _disposed;

        public ClientConnectionState State => _runtime.State;
        public bool Connected => _runtime.Connected;
        public bool Disconnected => State is ClientConnectionState.Idle or ClientConnectionState.Recoverable or
            ClientConnectionState.Replaced or ClientConnectionState.Disposed;
        public bool Authenticated => State == ClientConnectionState.Bound && Connected;
        public bool CanRecover => _runtime.CanResume;
        public ErrorCode AuthenticationError => _runtime.AuthenticationError;
        public ErrorCode ConnectionError => _runtime.ConnectionError;
        public ErrorCode LastSendError => _runtime.LastSendError;
        public int Player => _runtime.Binding.Player;
        public int Session => _runtime.Binding.Session;
        public int Epoch => _runtime.Binding.Epoch;
        public int ConnectionGeneration => _runtime.Binding.ConnectionGeneration;
        public int Room { get; private set; }
        public bool Replaced => State == ClientConnectionState.Replaced;
        public IReadOnlyDictionary<int, SliceClientRoom> Rooms { get; }
        private SliceClientRoom? Selected => _rooms.TryGetValue(Room, out var room) ? room : null;
        public int Owner => Selected?.Owner ?? 0;
        public int[] Members => Selected?.Members ?? Array.Empty<int>();
        public int OnlineMask => Selected?.OnlineMask ?? 0;
        public int Marker => Selected?.Marker ?? 0;
        public SliceStreamState StreamState => Selected?.State ?? SliceStreamState.Idle;
        public ErrorCode StreamError => Selected?.Error ?? ErrorCode.None;
        public bool Ready => StreamState == SliceStreamState.Ready;
        public int Baselines => Selected?.Baselines ?? 0;
        public int Diffs => Selected?.Diffs ?? 0;
        public event Action<OpCode, SliceMessage>? Received;
        public event Action<ClientGameEvent>? GameEventReceived
        {
            add => _runtime.GameEventReceived += value;
            remove => _runtime.GameEventReceived -= value;
        }
        public event Action<ClientRequestResult>? RequestFinished
        {
            add => _runtime.RequestFinished += value;
            remove => _runtime.RequestFinished -= value;
        }

        public static void SetReplicationLog(Action<string> info, Action<string> warning, Action<string> error) =>
            LiteEntitySystem.Logger.LoggerImpl = new SliceLesLog(info, warning, error);

        public SliceClient(string host, int port, string identity, Action<string> log)
            : this(host, port, identity, log, () => new LiteNetDatagrams()) { }

        /// <summary>Copies the explicit LES registrations before starting the connection. Unregistered rooms stay control-only.</summary>
        public SliceClient(string host, int port, string identity, Action<string> log, IEnumerable<ClientLesRegistration> registrations)
            : this(host, port, identity, log, () => new LiteNetDatagrams(),
                registrations ?? throw new ArgumentNullException(nameof(registrations))) { }

        internal SliceClient(string host, int port, string identity, Action<string> log, Func<IClientTransport> createTransport)
            : this(host, port, identity, log, createTransport, MovementLesClient.Registrations) { }

        private SliceClient(string host, int port, string identity, Action<string> log, Func<IClientTransport> createTransport,
            IEnumerable<ClientLesRegistration> registrations)
        {
            foreach (var registration in registrations)
                if (registration == null || !_replicationRegistrations.TryAdd(registration.DefinitionId, registration))
                    throw new ArgumentException("Invalid or duplicate LES room definition.", nameof(registrations));
            _host = host;
            _port = port;
            _identity = identity;
            _log = log;
            Rooms = new ReadOnlyDictionary<int, SliceClientRoom>(_rooms);
            _runtime = new ClientRuntime(createTransport, log);
            _runtime.StateChanged += OnStateChanged;
            _runtime.RoomChanged += OnRoomChanged;
            _runtime.WireReceived += ReadControl;
            _runtime.RawReceived += ReadStream;
            if (!_runtime.Login(host, port, identity))
            {
                Dispose();
                throw new InvalidOperationException($"Could not start POC connection to {host}:{port}.");
            }
        }

        public void Disconnect() => _runtime.Disconnect();

        public bool Reconnect(bool recover) => recover ? _runtime.Resume(_host, _port) : _runtime.Login(_host, _port, _identity);
        public uint Retry(uint requestId) => _runtime.Retry(requestId);
        public uint SendGameCommand(int room, uint id, ReadOnlySpan<byte> payload) => _runtime.SendGameCommand(room, id, payload);

        internal SliceClient StartOnlineReplacement(int port) =>
            new(_host, port == 0 ? _port : port, _identity, _log, _replicationRegistrations.Values);

        public void SelectRoom(int room)
        {
            if (_rooms.ContainsKey(room))
                Room = room;
        }

        public uint CreateRoom(string definition = SliceMessage.DefinitionId) =>
            Send(OpCode.CreateRoom, new SliceMessage { Text = definition });
        public uint JoinRoom(int room) => _runtime.JoinRoom(room);
        public uint LeaveRoom(int room, int successor = 0) =>
            _runtime.LeaveRoom(room, successor);
        public uint Kick(int room, int player) => _runtime.Kick(room, player);
        public uint TransferOwner(int room, int player) =>
            _runtime.TransferOwner(room, player);
        public uint CloseRoom(int room, uint requestId = 0) =>
            Send(OpCode.CloseRoom, new SliceMessage { Room = room, RequestId = requestId });
        public uint SetMarker(int marker) => SetMarker(Room, marker);
        public uint SetMarker(int room, int marker, int delayMs = 0, uint requestId = 0) =>
            Send(OpCode.SetMarker, new SliceMessage { Room = room, Marker = marker, DelayMs = delayMs, RequestId = requestId });
        public uint SendText(string text, uint requestId = 0) => SendText(Room, text, requestId);
        public uint SendText(int room, string text, uint requestId = 0) =>
            Send(OpCode.RoomMessage, new SliceMessage { Room = room, Text = text, RequestId = requestId });

        /// <summary>Pumps input, room-local LES managers and configured views, then flushes the same connection.</summary>
        public void Tick(float elapsedMilliseconds) => Tick(elapsedMilliseconds, null);

        public void Tick(float elapsedMilliseconds, sbyte x, sbyte y, Action<PawnSample> sample) =>
            Tick(elapsedMilliseconds, (room, attempt) => MovementLesClient.Step(room.Manager!, room.Id,
                room.Id == Room ? x : (sbyte)0, room.Id == Room ? y : (sbyte)0, pawn =>
                {
                    sample(pawn);
                    return _runtime.Attempt == attempt;
                }));

        private void Tick(float elapsedMilliseconds, Action<SliceClientRoom, long>? movement)
        {
            _runtime.Tick(elapsedMilliseconds, () =>
            {
                var attempt = _runtime.Attempt;
                foreach (var room in _rooms.Values)
                {
                    if (room.State == SliceStreamState.Pending && (room.Remaining -= elapsedMilliseconds) <= 0)
                        EndStream(room, SliceStreamState.Failed, ErrorCode.StreamUnavailable, cancel: true);
                    if (_runtime.Attempt != attempt)
                        return;
                    if (room.State != SliceStreamState.Ready || room.Baselines == 0 || room.Manager == null)
                        continue;
                    if (movement != null && MovementLesClient.IsBuiltIn(room.Replication!))
                        movement(room, attempt);
                    else if (room.Replication!.Step is { } step)
                        step(room.Manager, room.Id);
                    else
                        room.Manager.Update();
                    if (_runtime.Attempt != attempt)
                        return;
                }
            });
        }

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            Received = null;
            foreach (var room in _rooms.Values)
                EndStream(room, SliceStreamState.Closed, ErrorCode.None, cancel: false);
            _rooms.Clear();
            _runtime.Dispose();
        }

        internal uint Send(OpCode op, SliceMessage request) => _runtime.SendCompatible(op, request);

        private void OnStateChanged(ClientConnectionState state)
        {
            if (_disposed || state is ClientConnectionState.Connecting or ClientConnectionState.Authenticating or ClientConnectionState.Connected)
                return;
            if (state == ClientConnectionState.Replaced)
                _log("POC-3 session replaced");
            foreach (var id in _rooms.Keys.ToArray())
            {
                OnRoomChanged(id);
                if (_rooms.TryGetValue(id, out var room))
                {
                    if (state == ClientConnectionState.Bound)
                        room.State = SliceStreamState.Idle;
                    else
                        EndStream(room, SliceStreamState.Closed, ErrorCode.StreamUnavailable, cancel: false);
                }
            }
        }

        private void OnRoomChanged(int id)
        {
            if (!_runtime.Rooms.TryGetValue(id, out var snapshot))
            {
                RemoveRoom(id);
                return;
            }
            if (_rooms.TryGetValue(id, out var room))
                room.Snapshot = snapshot;
            else
                _rooms.Add(id, new SliceClientRoom(snapshot,
                    _replicationRegistrations.TryGetValue(snapshot.Definition, out var schema) ? schema : null));
            if (Room == 0)
                Room = id;
        }

        private void ReadControl((OpCode Op, SliceMessage Message) received)
        {
            var (op, message) = received;
            var attempt = _runtime.Attempt;
            if (op == OpCode.Response)
                ApplyReply(message);
            else if (_rooms.TryGetValue(message.Room, out var room))
            {
                if (op is OpCode.MarkerChanged or OpCode.Members)
                    room.Marker = message.Marker;
                if (op == OpCode.Members && room.State == SliceStreamState.Idle)
                    Open(room);
                if (op == OpCode.StreamClosed && message.Error != ErrorCode.None && MatchesStream(room, message))
                    EndStream(room, SliceStreamState.Closed, message.Error, cancel: false);
            }
            var observers = Received;
            if (observers != null)
                foreach (Action<OpCode, SliceMessage> observer in observers.GetInvocationList())
                {
                    if (_disposed || attempt != _runtime.Attempt)
                        break;
                    try { observer(op, message); }
                    catch (Exception error) { _log($"POC client observer failed: {error.GetType().Name}"); }
                }
        }

        private void ApplyReply(SliceMessage message)
        {
            if (message.ReplyTo is OpCode.Login or OpCode.Recover)
            {
                if (message.Error == ErrorCode.None)
                    _log($"POC-3 {message.ReplyTo} player={Player} session={Session} epoch={Epoch} connectionGeneration={ConnectionGeneration}");
                else
                    _log($"POC-3 authentication failed: {message.Error}");
                return;
            }
            // Create/Join responses may be historical replays. Members owns the room projection.
            if (!_rooms.TryGetValue(message.Room, out var current) || current.State != SliceStreamState.Pending)
                return;
            if (message.ReplyTo == OpCode.StreamOpen && message.RequestId == current.OpenRequest && current.Route == null)
            {
                if (message.Error != ErrorCode.None)
                    EndStream(current, SliceStreamState.Failed, message.Error, cancel: false);
                else if (message.Text == current.Definition && message.Stream != 0 &&
                         message.Schema == current.Replication!.Version && message.Header is > 0 and <= byte.MaxValue)
                    AcceptOffer(current, message);
                else
                    EndStream(current, SliceStreamState.Failed, ErrorCode.SchemaMismatch, cancel: false);
            }
            else if (message.ReplyTo == OpCode.StreamReady && message.RequestId == current.ReadyRequest && MatchesStream(current, message))
            {
                if (message.Error == ErrorCode.None)
                {
                    current.State = SliceStreamState.Ready;
                    Send(OpCode.StreamConfirm, new SliceMessage { Room = current.Id, Stream = current.StreamId, Generation = current.Generation });
                    _log($"POC-2 stream Ready room={current.Id} id={current.StreamId} generation={current.Generation} header={current.WireHeader}");
                    if (current.EarlyBaseline is { } baseline)
                    {
                        current.EarlyBaseline = null;
                        ReadBaseline(current, current.Manager!, baseline.AsSpan(LesStreamRoutes.WireOverhead));
                    }
                }
                else
                    EndStream(current, SliceStreamState.Failed, message.Error, cancel: false);
            }
        }

        public void OpenRoomStream(int id)
        {
            if (_rooms.TryGetValue(id, out var room) && room.State is SliceStreamState.Failed or SliceStreamState.Closed)
                Open(room);
        }

        private void Open(SliceClientRoom room)
        {
            if (room.Replication == null)
                return;
            room.State = SliceStreamState.Pending;
            room.Error = ErrorCode.None;
            room.Remaining = 10_000;
            room.OpenRequest = Send(OpCode.StreamOpen, new SliceMessage { Room = room.Id });
            if (room.OpenRequest == 0)
                EndStream(room, SliceStreamState.Failed, LastSendError, cancel: false);
        }

        private void AcceptOffer(SliceClientRoom room, SliceMessage offer)
        {
            room.Route = _routes.Accept((byte)offer.Header, offer.Generation, 1);
            if (room.Route == null)
            {
                EndStream(room, SliceStreamState.Failed, ErrorCode.StreamUnavailable, cancel: false);
                return;
            }
            room.StreamId = offer.Stream;
            var peer = new LesProbePeer(_runtime.Transport, _runtime.Handle, room.Route);
            room.Manager = new ClientEntityManager(room.Replication!.Types(), peer, room.Route.ManagerHeader);
            if (room.Manager.ClassDataHash != offer.Hash)
            {
                _log($"POC-2 rejected stream hash. server={offer.Hash} local={room.Manager.ClassDataHash}; {DescribeLayout()}");
                EndStream(room, SliceStreamState.Failed, ErrorCode.SchemaMismatch, cancel: true);
                return;
            }
            room.Remaining = 10_000;
            room.ReadyRequest = Send(OpCode.StreamReady, new SliceMessage
            {
                Room = room.Id, Text = room.Definition, Stream = offer.Stream, Generation = offer.Generation,
                Schema = offer.Schema, Hash = room.Manager.ClassDataHash
            });
            if (room.ReadyRequest == 0)
                EndStream(room, SliceStreamState.Failed, LastSendError, cancel: true);
        }

        internal static string DescribeLayout() =>
            $"LES SyncVar<float> Pack={typeof(SyncVar<float>).StructLayoutAttribute?.Pack}, " +
            $"EntitySharedReference Pack={typeof(EntitySharedReference).StructLayoutAttribute?.Pack}";

        private void ReadStream(ReceivedPacket received)
        {
            if (!_routes.TryTranslate(received.Bytes, out var route, out var packet))
                return;
            var room = _rooms.Values.FirstOrDefault(r => r.Route == route);
            if (room?.Manager == null)
                return;

            var schema = room.Replication!;
            if (received.Delivery == PacketDelivery.ReliableOrdered)
            {
                if (!schema.Baseline(packet, 0))
                    return;
                if (room.State == SliceStreamState.Pending)
                {
                    // The reliable StreamReady response and the LES baseline use different channels.
                    room.EarlyBaseline = received.Bytes;
                    return;
                }
                ReadBaseline(room, room.Manager, packet);
                return;
            }
            if (room.Baselines == 0 || received.Delivery != PacketDelivery.Unreliable ||
                !schema.Diff(packet, _runtime.Transport.GetUnreliableLimit(_runtime.Handle)))
                return;
            if (room.Manager.Deserialize(packet) == DeserializeResult.Done && ++room.Diffs == 1)
                _log($"POC-2 diff Unreliable accepted room={room.Id}");
        }

        private void ReadBaseline(SliceClientRoom room, ClientEntityManager manager, ReadOnlySpan<byte> packet)
        {
            if (manager.Deserialize(packet) == DeserializeResult.Done)
                _log($"POC-2 baseline ReliableOrdered #{++room.Baselines} room={room.Id}");
        }

        private static bool MatchesStream(SliceClientRoom room, SliceMessage message) =>
            room.Route != null && message.Stream == room.StreamId && message.Generation == room.Generation;

        private void RemoveRoom(int id)
        {
            if (_rooms.Remove(id, out var room))
                EndStream(room, SliceStreamState.Closed, ErrorCode.None, cancel: false);
            if (id == Room)
                Room = _rooms.Keys.FirstOrDefault();
        }

        private void EndStream(SliceClientRoom room, SliceStreamState state, ErrorCode reason, bool cancel)
        {
            var route = room.Route;
            var manager = room.Manager;
            var attempt = _runtime.Attempt;
            var binding = _runtime.Binding;
            var request = cancel && route != null
                ? new SliceMessage { Room = room.Id, Stream = room.StreamId, Generation = route.Generation }
                : null;

            // Sending can synchronously disconnect and reenter cleanup. Revoke ownership first.
            room.Route = null;
            room.EarlyBaseline = null;
            room.Manager = null;
            room.StreamId = room.OpenRequest = room.ReadyRequest = 0;
            room.Baselines = room.Diffs = 0;
            room.Remaining = 0;
            room.State = state;
            room.Error = reason;
            if (route != null)
                _routes.Close(route);
            manager?.Reset();
            if (reason != ErrorCode.None)
                _log($"POC-2 stream {state} reason={reason} room={room.Id}");

            if (request != null && Authenticated && _runtime.Attempt == attempt && _runtime.Binding.Equals(binding))
                Send(OpCode.StreamCancel, request);
        }
    }

    internal static class MovementLesClient
    {
        internal static readonly ClientLesRegistration[] Registrations =
        {
            new(SliceMessage.DefinitionId, SliceMessage.SchemaVersion, PocPlayerPawn.CreateTypes, Baseline, Diff),
            new(SliceMessage.SharedDefinitionId, SliceMessage.SchemaVersion, PocPlayerPawn.CreateTypes, Baseline, Diff)
        };

        internal static bool IsBuiltIn(ClientLesRegistration registration) =>
            ReferenceEquals(registration, Registrations[0]) || ReferenceEquals(registration, Registrations[1]);

        internal static void Step(ClientEntityManager manager, int room, sbyte x, sbyte y, Func<PawnSample, bool> sample)
        {
            manager.GetPlayerController<PocPlayerController>()?.SetInput(x, y);
            manager.Update();
            foreach (var pawn in manager.GetEntities<PocPlayerPawn>())
                if (!sample(new PawnSample(room, pawn.Player, pawn.Id, pawn.VisualX, pawn.VisualY, pawn.IsLocalControlled)))
                    return;
        }

        internal static bool Baseline(ReadOnlySpan<byte> packet, int _)
        {
            if (packet.Length < 17 || packet[1] != 3 || packet[2] == 0 || packet[3] != 1 || packet[6] != 20)
                return false;
            var originalLength = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(8));
            var events = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(12));
            return originalLength is >= 1 and <= 4096 && events >= 0 && events <= originalLength;
        }

        internal static bool Diff(ReadOnlySpan<byte> packet, int limit)
        {
            if (packet.Length < 18 || packet[1] != 4 || packet[2] != 0)
                return false;
            var bodyLength = packet.Length - 18;
            var mtu = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(packet.Length - 12));
            var eventBytes = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(packet.Length - 4));
            return mtu >= 6 + bodyLength && mtu <= limit && eventBytes >= 0 && eventBytes <= bodyLength;
        }
    }

    internal sealed class SliceLesLog : LiteEntitySystem.ILogger
    {
        private readonly Action<string> _info, _warning, _error;
        public SliceLesLog(Action<string> info, Action<string> warning, Action<string> error)
        {
            _info = info;
            _warning = warning;
            _error = error;
        }
        public void Log(string message) => _info(message);
        public void LogWarning(string message) => _warning(message);
        public void LogError(string message) => _error(message);
    }
}
