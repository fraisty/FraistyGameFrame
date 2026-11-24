using System;
using System.Buffers.Binary;
using FRFrame.Network.Probe;
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
        private readonly bool[] _ackedStreams = new bool[2];
        private readonly int[] _reliableReplies = new int[2];
        private readonly bool[] _unreliableReplies = new bool[2];
        private readonly Action<string> _log;
        private int _connection = -1;
        private int _baselineCount;

        public NetworkProbeClient(string host, int port, Action<string> log)
        {
            _log = log;
            if (!_transport.Connect(host, port))
            {
                _transport.Dispose();
                throw new InvalidOperationException($"Could not start probe connection to {host}:{port}.");
            }
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

                foreach (var op in new[] { ProbeOp.Protobuf, ProbeOp.MessagePack })
                {
                    for (var sequence = 1; sequence <= 3; sequence++)
                    {
                        var message = ProbeCodecs.Sample();
                        message.Sequence = sequence;
                        var delivery = sequence == 3 ? PacketDelivery.Unreliable : PacketDelivery.ReliableOrdered;
                        if (!_transport.TrySend(_connection, delivery, ProbeEnvelope.Write(op, ProbeCodecs.Encode(op, message))))
                            throw new InvalidOperationException($"Probe {op} enqueue failed.");
                    }
                }
            }

            while (_transport.TryReceive(out var received))
            {
                if (received.Bytes[0] == ProbeEnvelope.Header)
                    ReadControl(received);
                else
                    ReadBaseline(received.Bytes);
            }

            _transport.CompleteTick(elapsedMilliseconds);
            if (!Passed && _baselineCount == 2 && _ackedStreams[0] && _ackedStreams[1] &&
                _reliableReplies[0] == 2 && _reliableReplies[1] == 2 &&
                _unreliableReplies[0] && _unreliableReplies[1])
            {
                Passed = true;
                _log("POC-1 PASS: Protobuf and MessagePack echoed in both delivery classes; two LES streams on one connection.");
            }
        }

        public void Dispose() => _transport.Dispose();

        private void ReadControl(ReceivedPacket received)
        {
            if (!ProbeEnvelope.TryRead(received.Bytes, out var op, out var payload))
                throw new InvalidOperationException("Invalid server envelope.");

            if (op == ProbeOp.LesAcknowledged && payload.Length == 1 && payload[0] is 1 or 2)
            {
                _ackedStreams[payload[0] - 1] = true;
                _log($"LES stream {payload[0]} reverse translation acknowledged.");
                return;
            }

            var index = op == ProbeOp.ProtobufEcho ? 0 : op == ProbeOp.MessagePackEcho ? 1 : -1;
            if (index < 0 || !ProbeCodecs.TryDecode(op, payload, out var message))
                throw new InvalidOperationException($"Invalid {op} echo.");

            var expected = ProbeCodecs.Sample();
            expected.Sequence = received.Delivery == PacketDelivery.ReliableOrdered
                ? ++_reliableReplies[index]
                : 3;
            if (!ProbeCodecs.Matches(expected, message))
                throw new InvalidOperationException($"Out-of-order or changed {op} echo.");

            if (received.Delivery == PacketDelivery.Unreliable)
                _unreliableReplies[index] = true;
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
            if (_managers[index].Deserialize(packet) != DeserializeResult.Done)
                throw new InvalidOperationException($"LES stream {route.WireHeader} rejected its baseline.");

            _baselineCount++;
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
