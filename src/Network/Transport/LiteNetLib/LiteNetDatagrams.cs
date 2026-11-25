#nullable enable
using System;
using System.Collections.Generic;
using System.Net;
using FRFrame.Network.Shared;
using LiteNetLib;

namespace FRFrame.Network.Transport
{
    /// <summary>Single-owner LiteNetLib pump shared by the Unity client and the .NET Host.</summary>
    public sealed class LiteNetDatagrams : IPacketTransport, IDisposable
    {
        public const int QueueLimit = 32;

        // A fixed key scopes this test protocol. It is not an authentication credential.
        private const string ProbeKey = "frframe-poc-1";
        private const int ConnectionLimit = 8;
        private const int SendBudget = 8;
        private const int ControlReserve = 8;
        private readonly Dictionary<int, Connection> _connections = new Dictionary<int, Connection>();
        private readonly Dictionary<int, Connection> _receiving = new Dictionary<int, Connection>();
        private readonly ProbeNetManager _manager;
        private int _connectionSerial;
        private bool _accepting;
        private bool _flushRequested;
        private int _controlCursor, _inputCursor, _controlBurst;

        private sealed class ProbeNetManager : NetManager
        {
            public ProbeNetManager(INetEventListener listener) : base(listener) { }

            public bool StartManual(IPAddress ipv4, IPAddress ipv6, int port)
            {
                if (IsRunning)
                    return false;

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
                        if (IsRunning)
                            Stop();
                        else
                        {
                            // LiteNetLib 2.1.5 allocates the IPv4 socket before Bind; Stop skips failed starts.
                            _udpSocketv4?.Dispose();
                            _udpSocketv4 = null!;
                        }
                    }
                }
            }
        }

        private sealed class Connection
        {
            public readonly NetPeer Peer;
            public readonly int Id;
            public readonly Queue<ReceivedPacket> Ingress = new Queue<ReceivedPacket>();
            public readonly Queue<ReceivedPacket> Control = new Queue<ReceivedPacket>();
            public readonly Queue<(PacketDelivery Delivery, byte[] Bytes)> Outbound =
                new Queue<(PacketDelivery, byte[])>();
            public int UnreliableOutbound;

            public Connection(NetPeer peer, int id)
            {
                Peer = peer;
                Id = id;
            }
        }

        public LiteNetDatagrams()
        {
            var events = new EventBasedNetListener();
            _manager = new ProbeNetManager(events)
            {
                AutoRecycle = true,
                ChannelsCount = 1,
                MtuDiscovery = false,
                MaxFragmentsCount = 0,
                MaxPacketPerManualReceive = 64,
                EnableStatistics = true
            };
            events.ConnectionRequestEvent += request =>
            {
                if (_accepting && _connections.Count < ConnectionLimit)
                    request.AcceptIfKey(ProbeKey);
                else
                    request.Reject();
            };
            events.PeerConnectedEvent += peer =>
            {
                var connection = new Connection(peer, ++_connectionSerial);
                _connections.Add(connection.Id, connection);
                _receiving.Add(peer.Id, connection);
                ClientConnection = connection.Id;
                ConnectionsOpened++;
            };
            events.PeerDisconnectedEvent += (peer, _) =>
            {
                if (peer != null && _receiving.Remove(peer.Id, out var connection))
                {
                    _connections.Remove(connection.Id);
                    ConnectionsClosed++;
                    if (ClientConnection == connection.Id)
                        ClientConnection = -1;
                }
            };
            events.NetworkReceiveEvent += OnReceive;
        }

        public int ClientConnection { get; private set; } = -1;
        public int ConnectionsOpened { get; private set; }
        public int ConnectionsClosed { get; private set; }
        public int RejectedIngress { get; private set; }
        public int RejectedOutbound { get; private set; }
        public int IngressHighWater { get; private set; }
        public int OutboundHighWater { get; private set; }
        public int ReliableHighWater { get; private set; }
        public int ReliableOverflows { get; private set; }
        public int ActiveConnections => _connections.Count;
        public NetStatistics Statistics => _manager.Statistics;
        public int LocalPort => _manager.LocalPort;

        public bool Listen(IPAddress ipv4, bool ipv6Loopback, int port)
        {
            _manager.IPv6Enabled = ipv6Loopback;
            _accepting = _manager.StartManual(ipv4, IPAddress.IPv6Loopback, port);
            return _accepting;
        }

        public bool Connect(string host, int port)
        {
            if (!_manager.StartManual(IPAddress.Any, IPAddress.IPv6Any, 0))
                return false;

            var connecting = false;
            try
            {
                connecting = _manager.Connect(host, port, ProbeKey) != null;
                return connecting;
            }
            finally
            {
                if (!connecting)
                    Dispose();
            }
        }

        public bool IsConnected(int connection) => _connections.ContainsKey(connection);

        public void PollIncoming() => _manager.PollEvents();
        public void StopAccepting() => _accepting = false;

        public bool TryReceive(out ReceivedPacket packet)
        {
            if (_controlBurst < 4 && TryReceiveLane(true, ref _controlCursor, out packet))
            {
                _controlBurst++;
                return true;
            }
            if (TryReceiveLane(false, ref _inputCursor, out packet))
            {
                _controlBurst = 0;
                return true;
            }
            return TryReceiveLane(true, ref _controlCursor, out packet);
        }

        private bool TryReceiveLane(bool control, ref int cursor, out ReceivedPacket packet)
        {
            Connection? next = null, first = null;
            foreach (var connection in _connections.Values)
            {
                if ((control ? connection.Control : connection.Ingress).Count == 0)
                    continue;
                if (first == null || connection.Id < first.Id)
                    first = connection;
                if (connection.Id > cursor && (next == null || connection.Id < next.Id))
                    next = connection;
            }
            next ??= first;
            if (next != null)
            {
                cursor = next.Id;
                packet = (control ? next.Control : next.Ingress).Dequeue();
                return true;
            }
            packet = default;
            return false;
        }

        public bool TrySend(int connection, PacketDelivery delivery, ReadOnlySpan<byte> bytes)
        {
            var method = delivery == PacketDelivery.ReliableOrdered ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable;
            if (!_connections.TryGetValue(connection, out var target) ||
                target.Outbound.Count >= QueueLimit || bytes.Length == 0 ||
                (delivery == PacketDelivery.Unreliable && target.UnreliableOutbound >= QueueLimit - ControlReserve) ||
                bytes.Length > Math.Min(Envelope.MaxPacket, target.Peer.GetMaxSinglePacketSize(method)))
            {
                RejectedOutbound++;
                if (target != null && target.Outbound.Count == QueueLimit && delivery == PacketDelivery.ReliableOrdered)
                    ReliableOverflows++;
                return false;
            }

            target.Outbound.Enqueue((delivery, bytes.ToArray()));
            if (delivery == PacketDelivery.Unreliable)
                target.UnreliableOutbound++;
            OutboundHighWater = Math.Max(OutboundHighWater, target.Outbound.Count);
            RequestFlush();
            return true;
        }

        public int GetUnreliableLimit(int connection) =>
            _connections.TryGetValue(connection, out var target)
                ? Math.Min(Envelope.MaxPacket, target.Peer.GetMaxSinglePacketSize(DeliveryMethod.Unreliable))
                : 0;

        /// <summary>Coalesces requests into the next shared pump pass over all connections.</summary>
        public void RequestFlush() => _flushRequested = true;

        public void CompleteTick(float elapsedMilliseconds)
        {
            if (_flushRequested)
            {
                _flushRequested = false;
                foreach (var connection in _connections.Values)
                {
                    for (var sent = 0; sent < SendBudget && connection.Outbound.Count != 0; sent++)
                    {
                        var packet = connection.Outbound.Peek();
                        var reliable = packet.Delivery == PacketDelivery.ReliableOrdered;
                        var pending = connection.Peer.GetPacketsCountInReliableQueue(0, true);
                        ReliableHighWater = Math.Max(ReliableHighWater, pending);
                        if (reliable && pending >= QueueLimit)
                            break;

                        connection.Outbound.Dequeue();
                        if (!reliable)
                            connection.UnreliableOutbound--;
                        connection.Peer.Send(packet.Bytes, 0,
                            reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
                        if (reliable)
                            ReliableHighWater = Math.Max(ReliableHighWater, pending + 1);
                    }

                    _flushRequested |= connection.Outbound.Count != 0;
                }
            }

            _manager.ManualUpdate(elapsedMilliseconds);
        }

        public void Disconnect(int connection)
        {
            if (_connections.TryGetValue(connection, out var target))
                target.Peer.Disconnect();
        }

        public void Dispose()
        {
            _accepting = false;
            _manager.Stop();
            _connections.Clear();
            _receiving.Clear();
            ClientConnection = -1;
            _flushRequested = false;
        }

        private void OnReceive(NetPeer peer, NetPacketReader reader, byte channel, DeliveryMethod method)
        {
            var reliable = method == DeliveryMethod.ReliableOrdered;
            if (!_receiving.TryGetValue(peer.Id, out var target) || channel != 0 ||
                (!reliable && method != DeliveryMethod.Unreliable) || reader.AvailableBytes == 0 ||
                reader.AvailableBytes > Envelope.MaxPacket ||
                target.Control.Count + target.Ingress.Count >= QueueLimit ||
                (!reliable && target.Ingress.Count >= QueueLimit - ControlReserve))
            {
                RejectedIngress++;
                if (reliable)
                    peer.Disconnect();
                return;
            }

            var delivery = reliable ? PacketDelivery.ReliableOrdered : PacketDelivery.Unreliable;
            (reliable ? target.Control : target.Ingress)
                .Enqueue(new ReceivedPacket(target.Id, delivery, reader.GetRemainingBytesSpan().ToArray()));
            IngressHighWater = Math.Max(IngressHighWater, target.Control.Count + target.Ingress.Count);
        }
    }
}
