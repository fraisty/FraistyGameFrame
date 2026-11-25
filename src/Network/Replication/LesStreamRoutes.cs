using System;
using System.Buffers.Binary;
using FRFrame.Network.Shared;
using LiteEntitySystem.Transport;

namespace FRFrame.Network.Replication
{
    /// <summary>A POC wire-header table owned by one connection and its pump thread.</summary>
    public sealed class LesStreamRoutes
    {
        public const int WireOverhead = sizeof(uint);
        private readonly Route[] _routes = new Route[256];
        private uint _generation;

        /// <summary>Identity of one active stream on this connection.</summary>
        public sealed class Route
        {
            public byte WireHeader { get; }
            public byte ManagerHeader { get; }
            public uint Generation { get; }
            internal bool Active { get; set; } = true;

            internal Route(byte wire, byte manager, uint generation)
            {
                WireHeader = wire;
                ManagerHeader = manager;
                Generation = generation;
            }
        }

        public Route Open(byte managerHeader)
        {
            // Wire byte 0 is the FRFrame probe envelope.
            for (var index = 1; index < _routes.Length; index++)
            {
                if (_routes[index] == null)
                    return _routes[index] = new Route((byte)index, managerHeader, ++_generation);
            }

            return null!;
        }

        /// <summary>Installs the exact route negotiated by the remote server.</summary>
        internal Route Accept(byte wireHeader, uint generation)
        {
            if (wireHeader == 0 || generation == 0 || _routes[wireHeader] != null)
                return null!;
            return _routes[wireHeader] = new Route(wireHeader, wireHeader, generation);
        }

        public void Close(Route route)
        {
            if (ReferenceEquals(_routes[route.WireHeader], route))
            {
                route.Active = false;
                _routes[route.WireHeader] = null!;
            }
        }

        /// <summary>Moves the LES header in an owned datagram and returns the matching manager packet.</summary>
        public bool TryTranslate(byte[] datagram, out Route route, out ReadOnlySpan<byte> packet)
        {
            route = null!;
            packet = default;
            if (datagram.Length < WireOverhead + 2)
                return false;

            route = _routes[datagram[0]];
            if (route == null || route.Generation != BinaryPrimitives.ReadUInt32LittleEndian(datagram.AsSpan(1)))
                return false;

            datagram[WireOverhead] = route.ManagerHeader;
            packet = datagram.AsSpan(WireOverhead);
            return true;
        }
    }

    /// <summary>One LES peer per connection, logical stream and player.</summary>
    public sealed class LesProbePeer : AbstractNetPeer
    {
        private readonly IPacketTransport transport;
        private readonly int connection;
        private readonly LesStreamRoutes.Route route;

        public LesProbePeer(IPacketTransport transport, int connection, LesStreamRoutes.Route route)
        {
            this.transport = transport;
            this.connection = connection;
            this.route = route;
        }
        public override int GetMaxUnreliablePacketSize() =>
            Math.Max(0, transport.GetUnreliableLimit(connection) - LesStreamRoutes.WireOverhead);

        public override void TriggerSend() => transport.RequestFlush();

        public override void SendReliableOrdered(ReadOnlySpan<byte> data) => Send(PacketDelivery.ReliableOrdered, data);
        public override void SendUnreliable(ReadOnlySpan<byte> data) => Send(PacketDelivery.Unreliable, data);

        private void Send(PacketDelivery delivery, ReadOnlySpan<byte> data)
        {
            if (!route.Active)
                return;

            if (data.Length >= 2 && data[0] == route.ManagerHeader && data.Length <= GetMaxUnreliablePacketSize())
            {
                Span<byte> wire = stackalloc byte[data.Length + LesStreamRoutes.WireOverhead];
                wire[0] = route.WireHeader;
                BinaryPrimitives.WriteUInt32LittleEndian(wire.Slice(1), route.Generation);
                data.Slice(1).CopyTo(wire.Slice(1 + LesStreamRoutes.WireOverhead));
                if (transport.TrySend(connection, delivery, wire))
                    return;
            }

            if (delivery == PacketDelivery.ReliableOrdered)
                transport.Disconnect(connection);
        }
    }
}
