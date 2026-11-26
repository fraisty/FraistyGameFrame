#nullable enable
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Network.Protocol;
using FRFrame.Network.Shared;
using FRFrame.Network.Transport;
using LiteEntitySystem;

namespace FRFrame.Network.Replication
{
    public enum SliceStreamState { Idle, Pending, Ready, Failed, Closed }

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
        public string Definition { get; internal set; } = "";
        public int Owner { get; internal set; }
        public int[] Members { get; internal set; } = Array.Empty<int>();
        public int OnlineMask { get; internal set; }
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
        internal LesStreamRoutes.Route? Route;
        internal uint OpenRequest, ReadyRequest;
        internal float Remaining;

        internal SliceClientRoom(int id) => Id = id;
    }

    /// <summary>Maintains one transport connection and pumps all negotiated managers on the caller's thread.</summary>
    public sealed class SliceClient : IDisposable
    {
        private LiteNetDatagrams _transport = new();
        private readonly LesStreamRoutes _routes = new();
        private readonly Dictionary<int, SliceClientRoom> _rooms = new();
        private readonly string _host, _identity;
        private readonly int _port;
        private readonly Action<string> _log;
        private string _token = "";
        private bool _recover;
        private TransportConnectionHandle _connection;
        private uint _nextRequest;
        private uint _authRequest;

        public bool Connected => _transport.IsConnected(_connection);
        public bool Disconnected => _transport.ConnectionsClosed != 0 || (_connection.IsValid && !Connected);
        public bool Authenticated => Connected && _authenticated;
        public bool CanRecover => _token.Length != 0;
        public ErrorCode AuthenticationError { get; private set; }
        private bool _authenticated;
        public int Player { get; private set; }
        public int Session { get; private set; }
        public int Epoch { get; private set; }
        public int ConnectionGeneration { get; private set; }
        public int Room { get; private set; }
        public bool Replaced { get; private set; }
        public IReadOnlyDictionary<int, SliceClientRoom> Rooms => _rooms;
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

        public static void SetReplicationLog(Action<string> info, Action<string> warning, Action<string> error) =>
            LiteEntitySystem.Logger.LoggerImpl = new SliceLesLog(info, warning, error);

        public SliceClient(string host, int port, string identity, Action<string> log)
        {
            _host = host;
            _port = port;
            _identity = identity;
            _log = log;
            if (!_transport.Connect(host, port))
            {
                _transport.Dispose();
                throw new InvalidOperationException($"Could not start POC connection to {host}:{port}.");
            }
        }

        public void Disconnect()
        {
            _authenticated = false;
            if (Connected)
                _transport.Disconnect(_connection);
            foreach (var room in _rooms.Values)
                EndStream(room, SliceStreamState.Closed, ErrorCode.StreamUnavailable, cancel: false);
        }

        public bool Reconnect(bool recover)
        {
            if (recover && _token.Length == 0)
                return false;
            Disconnect();
            _transport.Dispose();
            _transport = new LiteNetDatagrams();
            _connection = default;
            _recover = recover;
            Replaced = false;
            AuthenticationError = ErrorCode.None;
            return _transport.Connect(_host, _port);
        }

        internal SliceClient StartOnlineReplacement(int port) => new(_host, port == 0 ? _port : port, _identity, _log);

        public void SelectRoom(int room)
        {
            if (_rooms.ContainsKey(room))
                Room = room;
        }

        public uint CreateRoom(string definition = SliceMessage.DefinitionId) =>
            Send(OpCode.CreateRoom, new SliceMessage { Text = definition });
        public uint JoinRoom(int room) => Send(OpCode.JoinRoom, new SliceMessage { Room = room });
        public uint LeaveRoom(int room, int successor = 0) =>
            Send(OpCode.LeaveRoom, new SliceMessage { Room = room, Target = successor });
        public uint Kick(int room, int player) => Send(OpCode.Kick, new SliceMessage { Room = room, Target = player });
        public uint TransferOwner(int room, int player) =>
            Send(OpCode.TransferOwner, new SliceMessage { Room = room, Target = player });
        public uint CloseRoom(int room, uint requestId = 0) =>
            Send(OpCode.CloseRoom, new SliceMessage { Room = room, RequestId = requestId });
        public uint SetMarker(int marker) => SetMarker(Room, marker);
        public uint SetMarker(int room, int marker, int delayMs = 0, uint requestId = 0) =>
            Send(OpCode.SetMarker, new SliceMessage { Room = room, Marker = marker, DelayMs = delayMs, RequestId = requestId });
        public uint SendText(string text, uint requestId = 0) => SendText(Room, text, requestId);
        public uint SendText(int room, string text, uint requestId = 0) =>
            Send(OpCode.RoomMessage, new SliceMessage { Room = room, Text = text, RequestId = requestId });

        public void Tick(float elapsedMilliseconds, sbyte x, sbyte y, Action<PawnSample> sample)
        {
            _transport.PollIncoming();
            if (!_connection.IsValid && _transport.ClientConnection.IsValid)
            {
                _connection = _transport.ClientConnection;
                _authRequest = _recover
                    ? Send(OpCode.Recover, new SliceMessage { Token = _token })
                    : Send(OpCode.Login, new SliceMessage { Text = _identity });
            }

            while (_transport.TryReceive(out var packet) && Connected)
            {
                if (packet.IsEnvelope)
                    ReadControl(packet);
                else
                    ReadStream(packet);
            }

            foreach (var room in _rooms.Values)
            {
                if (Disconnected && room.State is SliceStreamState.Pending or SliceStreamState.Ready)
                    EndStream(room, SliceStreamState.Closed, ErrorCode.StreamUnavailable, cancel: false);
                if (room.State == SliceStreamState.Pending && (room.Remaining -= elapsedMilliseconds) <= 0)
                    EndStream(room, SliceStreamState.Failed, ErrorCode.StreamUnavailable, cancel: true);
                if (room.State != SliceStreamState.Ready || room.Baselines == 0 || room.Manager == null)
                    continue;
                room.Manager.GetPlayerController<PocPlayerController>()?.SetInput(room.Id == Room ? x : (sbyte)0, room.Id == Room ? y : (sbyte)0);
                room.Manager.Update();
                foreach (var pawn in room.Manager.GetEntities<PocPlayerPawn>())
                    sample(new PawnSample(room.Id, pawn.Player, pawn.Id, pawn.VisualX, pawn.VisualY, pawn.IsLocalControlled));
            }
            _transport.CompleteTick(elapsedMilliseconds);
        }

        public void Dispose()
        {
            try
            {
                foreach (var room in _rooms.Values)
                    EndStream(room, SliceStreamState.Closed, ErrorCode.None, cancel: false);
                _rooms.Clear();
                _token = "";
            }
            finally
            {
                _transport.Dispose();
            }
        }

        internal uint Send(OpCode op, SliceMessage request)
        {
            if (!Connected)
                return 0;
            if (request.RequestId == 0)
                request.RequestId = ++_nextRequest;
            if (_transport.TrySend(_connection, PacketDelivery.ReliableOrdered, SliceCodec.Write(op, request)))
                return request.RequestId;

            _log($"POC-2 send queue rejected {op}. Disconnecting.");
            _transport.Disconnect(_connection);
            return 0;
        }

        private void ReadControl(ReceivedPacket received)
        {
            if (!SliceCodec.TryRead(received.Header, received.Payload, out var message, out _))
                return;
            var op = received.Header.Op;

            if (op == OpCode.Response)
                ApplyReply(message);
            else if (op == OpCode.Members && Authenticated && message.MemberIds.Contains(Player))
            {
                var room = GetRoom(message.Room, message.Text);
                room.Owner = message.Player;
                room.Members = message.MemberIds;
                room.OnlineMask = message.Online;
                room.Marker = message.Marker;
                if (room.State == SliceStreamState.Idle)
                    Open(room);
            }
            else if (op is OpCode.RoomClosed or OpCode.RoomLeft)
                RemoveRoom(message.Room);
            else if (op == OpCode.SessionReplaced && message.Session == Session)
            {
                Replaced = true;
                _token = "";
                _log($"POC-3 session replaced player={Player} session={Session}");
                Disconnect();
            }
            else if (_rooms.TryGetValue(message.Room, out var room))
            {
                if (op == OpCode.MarkerChanged)
                    room.Marker = message.Marker;
                if (op == OpCode.StreamClosed && message.Error != ErrorCode.None && MatchesStream(room, message))
                    EndStream(room, SliceStreamState.Closed, message.Error, cancel: false);
            }
            Received?.Invoke(op, message);
        }

        private void ApplyReply(SliceMessage message)
        {
            if (message.ReplyTo is OpCode.Login or OpCode.Recover)
            {
                if (_authRequest == 0 || message.RequestId != _authRequest)
                    return;
                _authRequest = 0;
                AuthenticationError = message.Error;
                if (AuthenticationError != ErrorCode.None)
                {
                    _authenticated = false;
                    if (AuthenticationError != ErrorCode.Overloaded)
                    {
                        _token = "";
                        Session = Player = Epoch = ConnectionGeneration = 0;
                        foreach (var id in _rooms.Keys.ToArray())
                            RemoveRoom(id);
                    }
                    _log($"POC-3 authentication failed: {AuthenticationError}");
                    return;
                }
                _authenticated = true;
                if (message.Session != Session)
                    _nextRequest = 0;
                Session = message.Session;
                Player = message.Player;
                Epoch = message.Epoch;
                ConnectionGeneration = message.ConnectionGeneration;
                _token = message.Token;
                _log($"POC-3 {message.ReplyTo} player={Player} session={Session} epoch={Epoch} connectionGeneration={ConnectionGeneration}");
                foreach (var id in _rooms.Keys.Where(id => !message.MemberIds.Contains(id)).ToArray())
                    RemoveRoom(id);
                foreach (var room in _rooms.Values)
                    room.State = SliceStreamState.Idle;
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
                         message.Schema == SliceMessage.SchemaVersion && message.Header is > 0 and <= byte.MaxValue)
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

        private SliceClientRoom GetRoom(int id, string definition)
        {
            if (!_rooms.TryGetValue(id, out var room))
                _rooms.Add(id, room = new SliceClientRoom(id));
            room.Definition = definition.Length == 0 ? SliceMessage.DefinitionId : definition;
            if (Room == 0)
                Room = id;
            return room;
        }

        public void OpenRoomStream(int id)
        {
            if (_rooms.TryGetValue(id, out var room) && room.State is SliceStreamState.Failed or SliceStreamState.Closed)
                Open(room);
        }

        private void Open(SliceClientRoom room)
        {
            if (room.Definition == SliceMessage.PlainRoom)
                return;
            room.State = SliceStreamState.Pending;
            room.Error = ErrorCode.None;
            room.Remaining = 10_000;
            room.OpenRequest = Send(OpCode.StreamOpen, new SliceMessage { Room = room.Id });
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
            var peer = new LesProbePeer(_transport, _connection, room.Route);
            room.Manager = new ClientEntityManager(PocPlayerPawn.CreateTypes(), peer, room.Route.ManagerHeader);
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

            if (received.Delivery == PacketDelivery.ReliableOrdered && packet.Length >= 17 && packet[1] == 3)
            {
                var originalLength = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(8));
                var events = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(12));
                if (packet[2] == 0 || packet[3] != 1 || packet[6] != 20 || originalLength is < 1 or > 4096 ||
                    events < 0 || events > originalLength)
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
                packet.Length < 18 || packet[1] != 4 || packet[2] != 0)
                return;
            var bodyLength = packet.Length - 18;
            var mtu = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(packet.Length - 12));
            var eventBytes = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(packet.Length - 4));
            if (mtu < 6 + bodyLength || mtu > _transport.GetUnreliableLimit(_connection) || eventBytes < 0 || eventBytes > bodyLength)
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
            if (room.Route != null)
            {
                if (cancel)
                    Send(OpCode.StreamCancel, new SliceMessage { Room = room.Id, Stream = room.StreamId, Generation = room.Generation });
                _routes.Close(room.Route);
            }
            room.Route = null;
            room.EarlyBaseline = null;
            var manager = room.Manager;
            room.Manager = null;
            room.StreamId = room.OpenRequest = room.ReadyRequest = 0;
            room.Baselines = room.Diffs = 0;
            room.State = state;
            room.Error = reason;
            manager?.Reset();
            if (reason != ErrorCode.None)
                _log($"POC-2 stream {state} reason={reason} room={room.Id}");
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
