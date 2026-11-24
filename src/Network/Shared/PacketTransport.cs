using System;

namespace FRFrame.Network.Shared
{
    /// <summary>Delivery semantics supported by the local POC transport.</summary>
    public enum PacketDelivery : byte
    {
        ReliableOrdered,
        Unreliable
    }

    /// <summary>A receive queue entry that owns its payload, independent of the provider reader.</summary>
    public readonly struct ReceivedPacket
    {
        public readonly int Connection;
        public readonly PacketDelivery Delivery;
        public readonly byte[] Bytes;

        public ReceivedPacket(int connection, PacketDelivery delivery, byte[] bytes)
        {
            Connection = connection;
            Delivery = delivery;
            Bytes = bytes;
        }
    }

    /// <summary>Packet send operations available to POC callers on the owning pump thread.</summary>
    public interface IPacketTransport
    {
        /// <summary>Copies the payload before returning true. False means the packet was rejected.</summary>
        bool TrySend(int connection, PacketDelivery delivery, ReadOnlySpan<byte> bytes);

        int GetUnreliableLimit(int connection);
        void Disconnect(int connection);
        void RequestFlush();
    }
}
