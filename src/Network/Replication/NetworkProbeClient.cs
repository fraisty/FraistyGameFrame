using System;
using System.Buffers.Binary;
using FRFrame.Network.Protocol;
using FRFrame.Network.Shared;
using FRFrame.Network.Transport;
using LiteEntitySystem;

namespace FRFrame.Network.Replication
{
    internal enum ProbeEntityType : byte { Marker }

    // LES requires a registered entity hierarchy even when this probe spawns no entities.
    internal sealed class ProbeMarker : EntityLogic
    {
        public ProbeMarker(EntityParams parameters) : base(parameters) { }

        public static EntityTypesMap CreateTypes() =>
            new EntityTypesMap<ProbeEntityType>().Register(ProbeEntityType.Marker, parameters => new ProbeMarker(parameters));
    }

    /// <summary>Local Windows, Android and command-line client for the POC transport probe.</summary>
    public sealed class NetworkProbeClient : IDisposable
    {
        private readonly LiteNetDatagrams _transport = new LiteNetDatagrams();
        private readonly LesStreamRoutes _routes = new LesStreamRoutes();
        private readonly ClientEntityManager[] _managers = new ClientEntityManager[2];
        private readonly LesProbePeer[] _peers = new LesProbePeer[2];
        private readonly bool[] _baselines = new bool[2];
        private readonly bool[] _ackedStreams = new bool[2];
        private int _reliableReplies;
        private bool _unreliableReply;
        private readonly Action<string> _log;
        private int _connection = -1;

        public NetworkProbeClient(string host, int port, Action<string> log)
        {
            _log = log;
            if (!_transport.Connect(host, port))
                throw new InvalidOperationException($"Could not start probe connection to {host}:{port}.");
        }

        public bool Passed { get; private set; }

        public void Tick(float elapsedMilliseconds)
        {
            _transport.PollIncoming();
            if (_connection < 0 && _transport.ClientConnection >= 0)
            {
                _connection = _transport.ClientConnection;
                _log($"Connected, usable unreliable payload {_transport.GetUnreliableLimit(_connection)} bytes.");
                for (var i = 0; i < _managers.Length; i++)
                {
                    // POC only: both sides open the two test routes in the same order.
                    var route = _routes.Open((byte)(i + 1));
                    _peers[i] = new LesProbePeer(_transport, _connection, route);
                    _managers[i] = new ClientEntityManager(ProbeMarker.CreateTypes(), _peers[i], route.ManagerHeader);
                }

                for (var sequence = 1; sequence <= 3; sequence++)
                {
                    var message = ProbeCodec.Sample();
                    message.Sequence = sequence;
                    var delivery = sequence == 3 ? PacketDelivery.Unreliable : PacketDelivery.ReliableOrdered;
                    if (!_transport.TrySend(_connection, delivery, Envelope.Write(OpCode.Protobuf, ProbeCodec.Encode(message))))
                        throw new InvalidOperationException("Protobuf probe enqueue failed.");
                }
            }

            while (_transport.TryReceive(out var received))
            {
                if (received.Bytes[0] == Envelope.Header)
                    ReadControl(received);
                else
                    ReadBaseline(received.Bytes);
            }

            _transport.CompleteTick(elapsedMilliseconds);
            if (!Passed && _baselines[0] && _baselines[1] && _ackedStreams[0] && _ackedStreams[1] &&
                _reliableReplies == 2 && _unreliableReply)
            {
                Passed = true;
                _log("P1 PASS: Protobuf echoed in both delivery classes; two LES streams on one connection.");
            }
        }

        public void Dispose() => _transport.Dispose();

        private void ReadControl(ReceivedPacket received)
        {
            if (!Envelope.TryRead(received.Bytes, out var header, out var payload, out var error,
                    MessageDirection.ServerToClient, received.Delivery))
                throw new InvalidOperationException($"Invalid server envelope: {error}.");
            var op = header.Op;

            if (op == OpCode.LesAcknowledged && ProbeCodec.TryDecodeAcknowledgement(payload, out var stream))
            {
                _ackedStreams[stream - 1] = true;
                _log($"LES stream {stream} reverse translation acknowledged.");
                return;
            }

            if (op != OpCode.ProtobufEcho || !ProbeCodec.TryDecode(payload, out var message))
                throw new InvalidOperationException($"Invalid {op} echo.");

            var expected = ProbeCodec.Sample();
            expected.Sequence = received.Delivery == PacketDelivery.ReliableOrdered
                ? ++_reliableReplies
                : 3;
            if (!ProbeCodec.Matches(expected, message))
                throw new InvalidOperationException($"Out-of-order or changed {op} echo.");

            if (received.Delivery == PacketDelivery.Unreliable)
                _unreliableReply = true;
            _log($"{op} {received.Delivery} #{message.Sequence}");
        }

        private void ReadBaseline(byte[] bytes)
        {
            // LES 1.3.0 empty-manager baseline only. Bound both lengths before the native-layout parser.
            if (!_routes.TryTranslate(bytes, out var route, out var packet) ||
                packet.Length < 16 || packet.Length > 17 || packet[1] != 3 ||
                packet[3] != 1 || packet[6] != 20 ||
                BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(8)) != 0 ||
                BinaryPrimitives.ReadInt32LittleEndian(packet.Slice(12)) != 0)
                throw new InvalidOperationException("Invalid LES empty baseline frame.");

            var index = route.WireHeader - 1;
            if (_baselines[index])
                return;

            if (_managers[index].Deserialize(packet) != DeserializeResult.Done)
                throw new InvalidOperationException($"LES stream {route.WireHeader} rejected its baseline.");

            _baselines[index] = true;
            _log($"LES stream {route.WireHeader} accepted baseline on manager header {route.ManagerHeader}.");
            // Empty LES ClientInput header: packet type plus two UInt16 fields. No entities or user input.
            Span<byte> reply = stackalloc byte[6];
            reply.Clear();
            reply[0] = route.ManagerHeader;
            reply[1] = 2;
            _peers[index].SendUnreliable(reply);
            _peers[index].TriggerSend();
        }
    }
}
