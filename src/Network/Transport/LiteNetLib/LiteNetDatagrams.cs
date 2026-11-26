#nullable enable
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net;
using FRFrame.Network.Shared;
using LiteNetLib;

namespace FRFrame.Network.Transport
{
    public sealed class TrafficLimit
    {
        public int PerSecond { get; set; }
        public int Burst { get; set; }
        internal bool IsValid => PerSecond > 0 && Burst > 0;
    }

    /// <summary>Resource limits for one endpoint. Rates are expressed in packets or bytes per second.</summary>
    public sealed class TransportAdmissionOptions
    {
        public int MaxConnections { get; set; } = 8;
        public int ReliableViolationLimit { get; set; } = 8;
        public TrafficLimit ConnectionAttempts { get; set; } = new TrafficLimit { PerSecond = 4, Burst = 8 };
        public TrafficLimit RawPackets { get; set; } = new TrafficLimit { PerSecond = 240, Burst = 480 };
        public TrafficLimit RawBytes { get; set; } = new TrafficLimit { PerSecond = 128 * 1024, Burst = 256 * 1024 };
        public TrafficLimit Control { get; set; } = new TrafficLimit { PerSecond = 20, Burst = 40 };
        public TrafficLimit Room { get; set; } = new TrafficLimit { PerSecond = 10, Burst = 20 };
        public TrafficLimit Game { get; set; } = new TrafficLimit { PerSecond = 30, Burst = 60 };
        public TrafficLimit Stream { get; set; } = new TrafficLimit { PerSecond = 30, Burst = 60 };

        public bool IsValid => MaxConnections is >= 1 and <= 64 && ReliableViolationLimit > 0 &&
            ConnectionAttempts.IsValid && RawPackets.IsValid && RawBytes.IsValid &&
            Control.IsValid && Room.IsValid && Game.IsValid && Stream.IsValid;
    }

    public enum TransportCloseReason : byte { PeerDisconnected, LocalRequest, ReliableIngressOverflow, ReliableOutboundOverflow, AdmissionViolations, Stop }

    /// <summary>Single-owner LiteNetLib pump shared by the Unity client and .NET Host.</summary>
    public sealed class LiteNetDatagrams : IPacketTransport, IDisposable
    {
        public const byte ControlChannel = 0;
        public const byte DataChannel = 1;
        public const byte UnreliableChannel = 2;
        public const int QueueLimit = 32;
        public const int DataLimit = 24;
        private const int ControlWeight = 4;
        private const int SendBudget = 8;

        private readonly Dictionary<int, Connection> _byPeer = new Dictionary<int, Connection>();
        private readonly Dictionary<int, Connection> _byGeneration = new Dictionary<int, Connection>();
        private readonly PumpNetManager _manager;
        private readonly TransportAdmissionOptions _options;
        private readonly Func<double> _clock;
        private readonly int[] _descriptorRejected = new int[4];
        private readonly int[] _headerRejected = new int[(int)ErrorCode.InternalError + 1];
        private readonly int[] _closed = new int[(int)TransportCloseReason.Stop + 1];
        private Bucket _connectionAttempts;
        private int _generation, _ingressCursor;
        private bool _accepting, _flushRequested, _listening, _readyForPoll;

        private sealed class PumpNetManager : NetManager
        {
            public PumpNetManager(INetEventListener listener) : base(listener) { }

            public bool StartManual(IPAddress ipv4, IPAddress ipv6, int port)
            {
                var started = false;
                try
                {
                    started = StartInManualMode(ipv4, ipv6, port);
                    return started;
                }
                finally
                {
                    if (!started)
                    {
                        // LiteNetLib 2.1.5 allocates the IPv4 socket before Bind; Stop skips failed starts.
                        _udpSocketv4?.Dispose();
                        _udpSocketv4 = null!;
                    }
                }
            }
        }

        private struct Bucket
        {
            private readonly int _rate, _burst;
            private double _available, _last;

            public Bucket(TrafficLimit limit, double now)
            {
                _rate = limit.PerSecond;
                _burst = limit.Burst;
                _available = _burst;
                _last = now;
            }

            public bool Take(double now, int amount)
            {
                _available = Math.Min(_burst, _available + (now - _last) * _rate);
                _last = now;
                if (_available < amount)
                    return false;
                _available -= amount;
                return true;
            }
        }

        private sealed class Lanes<T>
        {
            public readonly Queue<T> Control = new Queue<T>();
            public readonly Queue<T> Data = new Queue<T>();
            public int Count => Control.Count + Data.Count;
            private int _controlBurst;

            public bool HasRoom(bool control) => Count < QueueLimit && (control || Data.Count < DataLimit);

            public Queue<T>? Select(bool controlReady, bool dataReady)
            {
                if (controlReady && (!dataReady || _controlBurst < ControlWeight))
                {
                    _controlBurst = dataReady ? _controlBurst + 1 : 0;
                    return Control;
                }
                if (!dataReady)
                    return null;
                _controlBurst = 0;
                return Data;
            }
        }

        private sealed class Connection
        {
            public readonly NetPeer Peer;
            public readonly TransportConnectionHandle Handle;
            public readonly Lanes<ReceivedPacket> Ingress = new Lanes<ReceivedPacket>();
            public readonly Lanes<(PacketDelivery Delivery, byte Channel, byte[] Bytes)> Outbound =
                new Lanes<(PacketDelivery, byte, byte[])>();
            public Bucket RawPackets, RawBytes;
            public readonly Bucket[] Descriptor;
            public int ReliableViolations;

            public Connection(NetPeer peer, int generation, TransportAdmissionOptions options, double now)
            {
                Peer = peer;
                Handle = new TransportConnectionHandle(peer.Id, generation);
                RawPackets = new Bucket(options.RawPackets, now);
                RawBytes = new Bucket(options.RawBytes, now);
                Descriptor = new[]
                {
                    new Bucket(options.Control, now), new Bucket(options.Room, now),
                    new Bucket(options.Game, now), new Bucket(options.Stream, now)
                };
            }
        }

        public LiteNetDatagrams(TransportAdmissionOptions? options = null)
            : this(options ?? new TransportAdmissionOptions(), () => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency) { }

        internal LiteNetDatagrams(TransportAdmissionOptions options, Func<double> clock)
        {
            if (!options.IsValid)
                throw new ArgumentOutOfRangeException(nameof(options));
            _options = options;
            _clock = clock;
            var events = new EventBasedNetListener();
            _manager = new PumpNetManager(events)
            {
                AutoRecycle = true,
                ChannelsCount = 3,
                MtuDiscovery = false,
                MaxFragmentsCount = 0,
                MaxPacketPerManualReceive = 64,
                EnableStatistics = true
            };
            events.ConnectionRequestEvent += OnConnectionRequest;
            events.PeerConnectedEvent += peer =>
            {
                var connection = new Connection(peer, checked(++_generation), _options, _clock());
                _byPeer.Add(peer.Id, connection);
                _byGeneration.Add(connection.Handle.Generation, connection);
                if (!_listening)
                    ClientConnection = connection.Handle;
                ConnectionsOpened++;
            };
            events.PeerDisconnectedEvent += (peer, _) => OnPeerDisconnected(peer);
            events.NetworkReceiveEvent += OnReceive;
        }

        public TransportConnectionHandle ClientConnection { get; private set; }
        public int ConnectionsOpened { get; private set; }
        public int ConnectionsClosed { get; private set; }
        public int RejectedNotReady { get; private set; }
        public int RejectedConnectionLimit { get; private set; }
        public int RejectedConnectionRate { get; private set; }
        public int RejectedIngress { get; private set; }
        public int RejectedOutbound { get; private set; }
        public int RejectedSize { get; private set; }
        public int RejectedChannel { get; private set; }
        public int RejectedDelivery { get; private set; }
        public int RejectedRawPackets { get; private set; }
        public int RejectedRawBytes { get; private set; }
        public int IngressHighWater { get; private set; }
        public int OutboundHighWater { get; private set; }
        public int ReliableHighWater { get; private set; }
        public int ReliableOverflows { get; private set; }
        public int ActiveConnections => _byGeneration.Count;
        public NetStatistics Statistics => _manager.Statistics;
        public int LocalPort => _manager.LocalPort;

        public int HeaderRejections(ErrorCode error) => _headerRejected[(int)error];
        public int DescriptorRejections(MessageRateClass rateClass) => _descriptorRejected[(int)rateClass];
        public int Disconnects(TransportCloseReason reason) => _closed[(int)reason];

        public bool Listen(IPAddress ipv4, bool ipv6Loopback, int port)
        {
            if (_manager.IsRunning)
                return false;
            _manager.IPv6Enabled = ipv6Loopback;
            _listening = _manager.StartManual(ipv4, IPAddress.IPv6Loopback, port);
            _accepting = _listening;
            _connectionAttempts = new Bucket(_options.ConnectionAttempts, _clock());
            return _listening;
        }

        public bool Connect(string host, int port)
        {
            if (_manager.IsRunning || !_manager.StartManual(IPAddress.Any, IPAddress.IPv6Any, 0))
                return false;
            var connecting = false;
            try
            {
                connecting = _manager.Connect(host, port, new LiteNetLib.Utils.NetDataWriter()) != null;
                return connecting;
            }
            finally
            {
                if (!connecting)
                    Dispose();
            }
        }

        private void OnConnectionRequest(ConnectionRequest request)
        {
            if (!_accepting || !_readyForPoll)
                RejectedNotReady++;
            else if (!_connectionAttempts.Take(_clock(), 1))
                RejectedConnectionRate++;
            else if (_byPeer.Count >= _options.MaxConnections)
                RejectedConnectionLimit++;
            else
            {
                request.Accept();
                return;
            }

            // Forced rejection removes the SDK request immediately and does not allocate a temporary peer.
            request.Reject(ReadOnlySpan<byte>.Empty, force: true);
        }

        public bool IsConnected(TransportConnectionHandle connection) =>
            _byPeer.TryGetValue(connection.PeerId, out var target) && target.Handle == connection;

        /// <summary>Maps the existing POC Authority integer connection key to the active physical handle.</summary>
        public TransportConnectionHandle HandleForGeneration(int generation) =>
            _byGeneration.TryGetValue(generation, out var connection) ? connection.Handle : default;

        public void PollIncoming(bool ready = true)
        {
            _readyForPoll = ready;
            _manager.PollEvents();
        }
        public void StopAccepting() => _accepting = false;

        public bool TryReceive(out ReceivedPacket packet)
        {
            Connection? next = null, first = null;
            foreach (var connection in _byGeneration.Values)
            {
                if (connection.Ingress.Count == 0)
                    continue;
                var generation = connection.Handle.Generation;
                if (first == null || generation < first.Handle.Generation)
                    first = connection;
                if (generation > _ingressCursor && (next == null || generation < next.Handle.Generation))
                    next = connection;
            }
            next ??= first;
            if (next == null)
            {
                packet = default;
                return false;
            }
            _ingressCursor = next.Handle.Generation;
            packet = next.Ingress.Select(next.Ingress.Control.Count != 0, next.Ingress.Data.Count != 0)!.Dequeue();
            return true;
        }

        public bool TrySend(TransportConnectionHandle connection, PacketDelivery delivery, ReadOnlySpan<byte> bytes)
        {
            if (!_byPeer.TryGetValue(connection.PeerId, out var target) || target.Handle != connection ||
                delivery is not (PacketDelivery.ReliableOrdered or PacketDelivery.Unreliable) ||
                bytes.Length == 0 || bytes.Length > PacketLimit(target.Peer, delivery))
            {
                RejectedOutbound++;
                return false;
            }

            EnvelopeHeader header = default;
            if (bytes[0] == Envelope.Header && !Envelope.TryRead(bytes, out header, out _, out _,
                    _listening ? MessageDirection.ServerToClient : MessageDirection.ClientToServer, delivery))
            {
                RejectedOutbound++;
                return false;
            }

            var channel = GetChannel(delivery, header);
            var control = channel == ControlChannel;
            if (!target.Outbound.HasRoom(control))
            {
                RejectedOutbound++;
                if (delivery == PacketDelivery.ReliableOrdered)
                {
                    ReliableOverflows++;
                    Close(target, TransportCloseReason.ReliableOutboundOverflow);
                }
                return false;
            }
            (control ? target.Outbound.Control : target.Outbound.Data).Enqueue((delivery, channel, bytes.ToArray()));
            OutboundHighWater = Math.Max(OutboundHighWater, target.Outbound.Count);
            RequestFlush();
            return true;
        }

        public int GetUnreliableLimit(TransportConnectionHandle connection) =>
            _byPeer.TryGetValue(connection.PeerId, out var target) && target.Handle == connection
                ? PacketLimit(target.Peer, PacketDelivery.Unreliable)
                : 0;

        /// <summary>Coalesces requests into the next shared pump pass over all connections.</summary>
        public void RequestFlush() => _flushRequested = true;

        public void CompleteTick(float elapsedMilliseconds)
        {
            if (_flushRequested)
            {
                _flushRequested = false;
                foreach (var connection in _byGeneration.Values)
                {
                    var lanes = connection.Outbound;
                    var peer = connection.Peer;
                    var controlPending = peer.GetPacketsCountInReliableQueue(ControlChannel, true);
                    var dataPending = peer.GetPacketsCountInReliableQueue(DataChannel, true);
                    ReliableHighWater = Math.Max(ReliableHighWater, controlPending + dataPending);
                    for (var sent = 0; sent < SendBudget && lanes.Count != 0; sent++)
                    {
                        var room = controlPending + dataPending < QueueLimit;
                        var lane = lanes.Select(room && lanes.Control.Count != 0,
                            lanes.Data.Count != 0 && (lanes.Data.Peek().Delivery == PacketDelivery.Unreliable ||
                                                      (room && dataPending < DataLimit)));
                        if (lane == null)
                            break;
                        var packet = lane.Dequeue();
                        peer.Send(packet.Bytes, packet.Channel, Method(packet.Delivery));
                        if (packet.Delivery == PacketDelivery.ReliableOrdered)
                        {
                            if (packet.Channel == ControlChannel)
                                controlPending++;
                            else
                                dataPending++;
                            ReliableHighWater = Math.Max(ReliableHighWater, controlPending + dataPending);
                        }
                    }
                    _flushRequested |= lanes.Count != 0;
                }
            }

            _manager.ManualUpdate(elapsedMilliseconds);
        }

        public void Disconnect(TransportConnectionHandle connection)
        {
            if (_byPeer.TryGetValue(connection.PeerId, out var target) && target.Handle == connection)
                Close(target, TransportCloseReason.LocalRequest);
        }

        private void Close(Connection connection, TransportCloseReason reason)
        {
            Remove(connection, reason);
            connection.Peer.Disconnect();
        }

        private void Remove(Connection connection, TransportCloseReason reason)
        {
            _byPeer.Remove(connection.Handle.PeerId);
            _byGeneration.Remove(connection.Handle.Generation);
            if (ClientConnection == connection.Handle)
                ClientConnection = default;
            _closed[(int)reason]++;
            ConnectionsClosed++;
        }

        internal void OnPeerDisconnected(NetPeer? peer)
        {
            if (peer != null && _byPeer.TryGetValue(peer.Id, out var target) &&
                ReferenceEquals(target.Peer, peer) && IsConnected(target.Handle))
                Remove(target, TransportCloseReason.PeerDisconnected);
        }

        public void Dispose()
        {
            StopAccepting();
            _closed[(int)TransportCloseReason.Stop] += _byGeneration.Count;
            ConnectionsClosed += _byGeneration.Count;
            _byPeer.Clear();
            _byGeneration.Clear();
            _manager.Stop();
            ClientConnection = default;
            _listening = _flushRequested = false;
        }

        private void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
        {
            if (!_byPeer.TryGetValue(peer.Id, out var target) || !ReferenceEquals(target.Peer, peer))
            {
                RejectedIngress++;
                return;
            }
            var reliable = method == DeliveryMethod.ReliableOrdered;
            if (!reliable && method != DeliveryMethod.Unreliable)
            {
                RejectedDelivery++;
                RejectIngress(target, false);
                return;
            }

            var delivery = reliable ? PacketDelivery.ReliableOrdered : PacketDelivery.Unreliable;
            var size = reader.AvailableBytes;
            if (size <= 0 || size > PacketLimit(peer, delivery))
            {
                RejectedSize++;
                RejectIngress(target, reliable);
                return;
            }
            var now = _clock();
            if (!target.RawPackets.Take(now, 1))
            {
                RejectedRawPackets++;
                RejectIngress(target, reliable);
                return;
            }
            if (!target.RawBytes.Take(now, size))
            {
                RejectedRawBytes++;
                RejectIngress(target, reliable);
                return;
            }

            var bytes = reader.GetRemainingBytesSpan();
            EnvelopeHeader header = default;
            if (bytes[0] == Envelope.Header && !Envelope.TryRead(bytes, out header, out _, out var error,
                    _listening ? MessageDirection.ClientToServer : MessageDirection.ServerToClient, delivery))
            {
                _headerRejected[(int)error]++;
                RejectIngress(target, reliable);
                return;
            }
            var expectedChannel = GetChannel(delivery, header);
            // LiteNetLib has no channel byte for native Unreliable; the callback reports zero.
            if (channel != (reliable ? expectedChannel : 0))
            {
                RejectedChannel++;
                RejectIngress(target, reliable);
                return;
            }
            if (header.Op != OpCode.None)
            {
                var rateClass = (int)header.Descriptor.RateClass;
                if (!target.Descriptor[rateClass].Take(now, 1))
                {
                    _descriptorRejected[rateClass]++;
                    RejectIngress(target, reliable);
                    return;
                }
            }

            var isControl = expectedChannel == ControlChannel;
            if (!target.Ingress.HasRoom(isControl))
            {
                RejectedIngress++;
                if (reliable)
                {
                    ReliableOverflows++;
                    Close(target, TransportCloseReason.ReliableIngressOverflow);
                }
                return;
            }

            if (reliable)
                target.ReliableViolations = 0;
            (isControl ? target.Ingress.Control : target.Ingress.Data)
                .Enqueue(new ReceivedPacket(target.Handle, delivery, expectedChannel, bytes.ToArray(), header));
            IngressHighWater = Math.Max(IngressHighWater, target.Ingress.Count);
        }

        private void RejectIngress(Connection target, bool reliable)
        {
            RejectedIngress++;
            if (reliable && ++target.ReliableViolations >= _options.ReliableViolationLimit)
                Close(target, TransportCloseReason.AdmissionViolations);
        }

        private static byte GetChannel(PacketDelivery delivery, EnvelopeHeader header) =>
            delivery == PacketDelivery.Unreliable ? UnreliableChannel :
            header.Op != OpCode.None &&
            (header.Descriptor.Kind == MessageKind.Response || header.Descriptor.RateClass is MessageRateClass.Control or MessageRateClass.Stream)
                ? ControlChannel : DataChannel;

        private static DeliveryMethod Method(PacketDelivery delivery) =>
            delivery == PacketDelivery.ReliableOrdered ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable;

        private static int PacketLimit(NetPeer peer, PacketDelivery delivery) =>
            Math.Min(Envelope.MaxPacket, peer.GetMaxSinglePacketSize(Method(delivery)));
    }
}
