using System;

namespace FRFrame.Network.Shared
{
    /// <summary>Delivery semantics supported by the local POC transport.</summary>
    public enum PacketDelivery : byte
    {
        ReliableOrdered,
        Unreliable
    }

    /// <summary>Physical connection identity within one transport instance.</summary>
    public readonly struct TransportConnectionHandle : IEquatable<TransportConnectionHandle>
    {
        public TransportConnectionHandle(int peerId, int generation)
        {
            PeerId = peerId;
            Generation = generation;
        }

        public int PeerId { get; }
        public int Generation { get; }
        public bool IsValid => Generation > 0;

        public bool Equals(TransportConnectionHandle other) => PeerId == other.PeerId && Generation == other.Generation;
        public override bool Equals(object obj) => obj is TransportConnectionHandle other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(PeerId, Generation);
        public static bool operator ==(TransportConnectionHandle left, TransportConnectionHandle right) => left.Equals(right);
        public static bool operator !=(TransportConnectionHandle left, TransportConnectionHandle right) => !left.Equals(right);
        public override string ToString() => $"{PeerId}/{Generation}";
    }

    /// <summary>A receive queue entry that owns its payload, independent of the provider reader.</summary>
    public readonly struct ReceivedPacket
    {
        public readonly TransportConnectionHandle Connection;
        public readonly PacketDelivery Delivery;
        public readonly byte Channel;
        public readonly byte[] Bytes;
        public readonly EnvelopeHeader Header;
        public bool IsEnvelope => Header.Op != OpCode.None;
        public ReadOnlySpan<byte> Payload => Bytes.AsSpan(Envelope.HeaderSize);

        public ReceivedPacket(TransportConnectionHandle connection, PacketDelivery delivery, byte channel,
            byte[] bytes, EnvelopeHeader header = default)
        {
            Connection = connection;
            Delivery = delivery;
            Channel = channel;
            Bytes = bytes;
            Header = header;
        }
    }

    /// <summary>Packet send operations available to POC callers on the owning pump thread.</summary>
    public interface IPacketTransport
    {
        /// <summary>Copies the payload before returning true. False means the packet was rejected.</summary>
        bool TrySend(TransportConnectionHandle connection, PacketDelivery delivery, ReadOnlySpan<byte> bytes);

        int GetUnreliableLimit(TransportConnectionHandle connection);
        void Disconnect(TransportConnectionHandle connection);
        void RequestFlush();
    }

    /// <summary>One client connection attempt. The caller owns polling, sending and disposal.</summary>
    public interface IClientTransport : IPacketTransport, IDisposable
    {
        TransportConnectionHandle ClientConnection { get; }
        bool Connect(string host, int port);
        bool IsConnected(TransportConnectionHandle connection);
        void PollIncoming(bool ready = true);
        bool TryReceive(out ReceivedPacket packet);
        void CompleteTick(float elapsedMilliseconds);
    }
}
