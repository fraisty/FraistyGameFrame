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
        private readonly Dictionary<int, Connection> _connections = new Dictionary<int, Connection>();
        private readonly Dictionary<int, Connection> _receiving = new Dictionary<int, Connection>();
        private readonly NetManager _manager;
        private int _connectionSerial;
        private bool _accepting;
        private bool _flushRequested;

        private sealed class Connection
        {
            public readonly NetPeer Peer;
            public readonly int Id;
            public readonly Queue<ReceivedPacket> Ingress = new Queue<ReceivedPacket>();
            public readonly Queue<(PacketDelivery Delivery, byte[] Bytes)> Outbound =
                new Queue<(PacketDelivery, byte[])>();

            public Connection(NetPeer peer, int id)
            {
                Peer = peer;
                Id = id;
            }
        }

        public LiteNetDatagrams()
        {
            var events = new EventBasedNetListener();
            _manager = new NetManager(events)
            {
                AutoRecycle = true,
                ChannelsCount = 1,
                MtuDiscovery = false,
                MaxPacketPerManualReceive = 64
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
                if (_receiving.Remove(peer.Id, out var connection))
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
        public int LocalPort => _manager.LocalPort;

        public bool Listen(IPAddress ipv4, bool ipv6Loopback, int port)
        {
            _manager.IPv6Enabled = ipv6Loopback;
            _accepting = _manager.StartInManualMode(ipv4, IPAddress.IPv6Loopback, port);
            return _accepting;
        }

        public bool Connect(string host, int port)
        {
            if (!_manager.StartInManualMode(0))
                return false;

            return _manager.Connect(host, port, ProbeKey) != null;
        }

        public bool IsConnected(int connection) => _connections.ContainsKey(connection);

        public void PollIncoming() => _manager.PollEvents();

        public bool TryReceive(out ReceivedPacket packet)
        {
            foreach (var connection in _connections.Values)
            {
                if (connection.Ingress.Count != 0)
                {
                    packet = connection.Ingress.Dequeue();
                    return true;
                }
            }

            packet = default;
            return false;
        }

        public bool TrySend(int connection, PacketDelivery delivery, ReadOnlySpan<byte> bytes)
        {
            var method = delivery == PacketDelivery.ReliableOrdered ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable;
            if (!_connections.TryGetValue(connection, out var target) ||
                target.Outbound.Count >= QueueLimit || bytes.Length == 0 ||
                bytes.Length > Math.Min(ProbeEnvelope.MaxPacket, target.Peer.GetMaxSinglePacketSize(method)))
            {
                RejectedOutbound++;
                return false;
            }

            target.Outbound.Enqueue((delivery, bytes.ToArray()));
            RequestFlush();
            return true;
        }

        public int GetUnreliableLimit(int connection) =>
            _connections.TryGetValue(connection, out var target)
                ? Math.Min(ProbeEnvelope.MaxPacket, target.Peer.GetMaxSinglePacketSize(DeliveryMethod.Unreliable))
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
                        if (reliable && connection.Peer.GetPacketsCountInReliableQueue(0, true) >= QueueLimit)
                            break;

                        connection.Outbound.Dequeue();
                        connection.Peer.Send(packet.Bytes, 0,
                            reliable ? DeliveryMethod.ReliableOrdered : DeliveryMethod.Unreliable);
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
                reader.AvailableBytes > ProbeEnvelope.MaxPacket || target.Ingress.Count >= QueueLimit)
            {
                RejectedIngress++;
                if (reliable)
                    peer.Disconnect();
                return;
            }

            var delivery = reliable ? PacketDelivery.ReliableOrdered : PacketDelivery.Unreliable;
            target.Ingress.Enqueue(new ReceivedPacket(target.Id, delivery, reader.GetRemainingBytesSpan().ToArray()));
        }
    }
}
