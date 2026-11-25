#nullable enable
using System;
using System.Buffers.Binary;
using FRFrame.Network.Probe;
using FRFrame.Network.Shared;
using FRFrame.Network.Transport;
using LiteEntitySystem;

namespace FRFrame.Network.Replication
{
    /// <summary>A local render sample from the LES manager, with its actual ownership.</summary>
    public readonly struct PawnSample
    {
        public readonly int Player;
        public readonly float X;
        public readonly float Y;
        public readonly bool Owned;

        public PawnSample(int player, float x, float y, bool owned)
        {
            Player = player;
            X = x;
            Y = y;
            Owned = owned;
        }
    }

    /// <summary>POC-2 control and LES receive pump, owned by the caller's main thread.</summary>
    public sealed class SliceClient : IDisposable
    {
        private readonly LiteNetDatagrams _transport = new();
        private readonly LesStreamRoutes _routes = new();
        private readonly string _identity;
        private readonly Action<string> _log;
        private ClientEntityManager? _manager;
        private LesStreamRoutes.Route? _route;
        private int _connection = -1;
        private uint _nextRequest;
        private uint _opening;
        private uint _readying;

        public bool Connected => _connection >= 0 && _transport.IsConnected(_connection);
        public bool Disconnected => _transport.ConnectionsClosed != 0;
        public int Player { get; private set; }
        public int Session { get; private set; }
        public int Room { get; private set; }
        public int Owner { get; private set; }
        public int[] Members { get; private set; } = Array.Empty<int>();
        public int OnlineMask { get; private set; }
        public int Marker { get; private set; }
        public bool Ready { get; private set; }
        public int Baselines { get; private set; }
        public int Diffs { get; private set; }
        public event Action<ProbeOp, SliceMessage>? Received;

        /// <summary>Routes the provider diagnostics to the platform logger once per process.</summary>
        public static void SetReplicationLog(Action<string> info, Action<string> warning, Action<string> error) =>
            LiteEntitySystem.Logger.LoggerImpl = new SliceLesLog(info, warning, error);

        public SliceClient(string host, int port, string identity, Action<string> log)
        {
            _identity = identity;
            _log = log;
            if (!_transport.Connect(host, port))
            {
                _transport.Dispose();
                throw new InvalidOperationException($"Could not start POC-2 connection to {host}:{port}.");
            }
        }

        public uint CreateRoom() => Send(ProbeOp.CreateRoom, new SliceMessage { Text = SliceMessage.DefinitionId });
        public uint JoinRoom(int room) => Send(ProbeOp.JoinRoom, new SliceMessage { Room = room });
        public uint SetMarker(int marker) => Send(ProbeOp.SetMarker, new SliceMessage { Room = Room, Marker = marker });
        public uint SendText(string text, uint requestId = 0) =>
            Send(ProbeOp.RoomMessage, new SliceMessage { Room = Room, Text = text, RequestId = requestId });

        public void Tick(float elapsedMilliseconds, sbyte x, sbyte y, Action<PawnSample> sample)
        {
            _transport.PollIncoming();
            if (_connection < 0 && _transport.ClientConnection >= 0)
            {
                _connection = _transport.ClientConnection;
                Send(ProbeOp.Login, new SliceMessage { Text = _identity });
            }

            if (Disconnected)
                ResetStream();
            while (_transport.TryReceive(out var received) && Connected)
            {
                if (received.Bytes[0] == ProbeEnvelope.Header)
                    ReadControl(received);
                else
                    ReadStream(received);
            }

            if (Ready && Baselines != 0 && _manager != null)
            {
                _manager.GetPlayerController<PocPlayerController>()?.SetInput(x, y);
                _manager.Update();
                foreach (var pawn in _manager.GetEntities<PocPlayerPawn>())
                    sample(new PawnSample(pawn.Player, pawn.VisualX, pawn.VisualY, pawn.IsLocalControlled));
            }
            _transport.CompleteTick(elapsedMilliseconds);
        }

        public void Dispose()
        {
            ResetStream();
            _transport.Dispose();
        }

        internal uint Send(ProbeOp op, SliceMessage request)
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
            if (received.Delivery != PacketDelivery.ReliableOrdered ||
                !ProbeEnvelope.TryRead(received.Bytes, out var op, out var payload) ||
                op is < ProbeOp.Response or > ProbeOp.MarkerChanged || !SliceCodec.TryRead(payload, out var message))
                return;

            if (op == ProbeOp.Response && message.Error == SliceError.None)
            {
                if (message.ReplyTo == ProbeOp.Login)
                {
                    Player = message.Player;
                    Session = message.Session;
                }
                else if (Room == 0 && message.Room > 0 &&
                         (message.ReplyTo == ProbeOp.CreateRoom || message.ReplyTo == ProbeOp.JoinRoom))
                {
                    Room = message.Room;
                    _opening = Send(ProbeOp.StreamOpen, new SliceMessage { Room = Room });
                }
                else if (message.ReplyTo == ProbeOp.StreamOpen && message.RequestId == _opening && _manager == null)
                {
                    if (message.Room == Room && message.Text == SliceMessage.DefinitionId && message.Stream != 0 &&
                        message.Schema == SliceMessage.SchemaVersion && message.Header is > 0 and <= byte.MaxValue)
                        AcceptOffer(message);
                    else
                        _log("POC-2 rejected incompatible stream offer.");
                }
                else if (message.ReplyTo == ProbeOp.StreamReady && message.RequestId == _readying && _manager != null)
                    Ready = true;
            }
            else if (message.Room == Room && Room != 0)
            {
                if (op == ProbeOp.Members)
                {
                    Owner = message.Player;
                    Members = message.MemberIds;
                    OnlineMask = message.Online;
                }
                else if (op == ProbeOp.MarkerChanged)
                    Marker = message.Marker;
            }
            Received?.Invoke(op, message);
        }

        private void AcceptOffer(SliceMessage offer)
        {
            _route = _routes.Accept((byte)offer.Header, offer.Generation);
            if (_route == null)
                return;
            var peer = new LesProbePeer(_transport, _connection, _route);
            _manager = new ClientEntityManager(PocPlayerPawn.CreateTypes(), peer, _route.ManagerHeader);
            if (_manager.ClassDataHash != offer.Hash)
            {
                _log($"POC-2 rejected stream hash. server={offer.Hash} local={_manager.ClassDataHash}; {DescribeLayout()}");
                ResetStream();
                return;
            }
            _log($"POC-2 offer stream={offer.Stream} generation={offer.Generation} schema={offer.Schema} hash={offer.Hash}");
            _readying = Send(ProbeOp.StreamReady, new SliceMessage
            {
                Room = Room, Text = SliceMessage.DefinitionId, Stream = offer.Stream,
                Generation = offer.Generation, Schema = offer.Schema, Hash = _manager.ClassDataHash
            });
        }

        internal static string DescribeLayout() =>
            $"LES SyncVar<float> Pack={typeof(SyncVar<float>).StructLayoutAttribute?.Pack}, " +
            $"EntitySharedReference Pack={typeof(EntitySharedReference).StructLayoutAttribute?.Pack}";

        private void ReadStream(ReceivedPacket received)
        {
            if (_manager == null || !_routes.TryTranslate(received.Bytes, out _, out var packet))
                return;

            // The three-player world fits one diff part at the negotiated payload limit.
            // Validate the LES 1.3.0 native layouts before passing them to the unsafe parser.
            if (received.Delivery == PacketDelivery.ReliableOrdered && packet.Length >= 17 && packet[1] == 3)
            {
                var originalLength = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(8));
                var events = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(12));
                if (packet[2] == 0 || packet[3] != 1 || packet[6] != 20 || originalLength is < 1 or > 4096 ||
                    events < 0 || events > originalLength)
                    return;
                if (_manager.Deserialize(packet) == DeserializeResult.Done)
                    _log($"POC-2 baseline ReliableOrdered #{++Baselines}");
                return;
            }
            if (Baselines == 0 || received.Delivery != PacketDelivery.Unreliable ||
                packet.Length < 18 || packet[1] != 4 || packet[2] != 0)
                return;
            var bodyLength = packet.Length - 18; // DiffPartHeader (6) and LastPartData (12)
            var mtu = BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(packet.Length - 12));
            var eventBytes = BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(packet.Length - 4));
            if (mtu < 6 + bodyLength || mtu > _transport.GetUnreliableLimit(_connection) || eventBytes < 0 || eventBytes > bodyLength)
                return;
            if (_manager.Deserialize(packet) == DeserializeResult.Done && ++Diffs == 1)
                _log("POC-2 diff Unreliable accepted");
        }

        private void ResetStream()
        {
            if (_route != null)
                _routes.Close(_route);
            _route = null;
            _manager?.Reset();
            _manager = null;
            Ready = false;
        }
    }

    internal sealed class SliceLesLog : LiteEntitySystem.ILogger
    {
        private readonly Action<string> _info;
        private readonly Action<string> _warning;
        private readonly Action<string> _error;

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
