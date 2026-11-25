using System;
using System.Buffers.Binary;

namespace FRFrame.Network.Shared
{
    /// <summary>Temporary POC opcodes carried in one reliable or unreliable UDP packet.</summary>
    public enum ProbeOp : byte
    {
        Protobuf = 1,
        MessagePack = 2,
        ProtobufEcho = 3,
        MessagePackEcho = 4,
        LesAcknowledged = 5,
        Login = 6,
        CreateRoom = 7,
        JoinRoom = 8,
        RoomMessage = 9,
        SetMarker = 10,
        StreamOpen = 11,
        StreamReady = 12,
        Response = 13,
        Members = 14,
        RoomBroadcast = 15,
        MarkerChanged = 16,
        StreamClosed = 17
    }

    /// <summary>POC-only wire envelope: reserved header, version, opcode, LE length and payload.</summary>
    public static class ProbeEnvelope
    {
        public const byte Header = 0;
        public const int HeaderSize = 5;
        public const int MaxPacket = 480;
        public const int MaxPayload = MaxPacket - HeaderSize;

        public static byte[] Write(ProbeOp op, ReadOnlySpan<byte> payload)
        {
            if (payload.Length > MaxPayload)
                throw new ArgumentOutOfRangeException(nameof(payload));

            var packet = new byte[HeaderSize + payload.Length];
            packet[0] = Header;
            packet[1] = (byte)ProtocolVersion.Current.Value;
            packet[2] = (byte)op;
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(3), (ushort)payload.Length);
            payload.CopyTo(packet.AsSpan(HeaderSize));
            return packet;
        }

        public static bool TryRead(ReadOnlySpan<byte> packet, out ProbeOp op, out ReadOnlySpan<byte> payload)
        {
            op = default;
            payload = default;
            if (packet.Length < HeaderSize || packet.Length > MaxPacket || packet[0] != Header ||
                packet[1] != ProtocolVersion.Current.Value || packet[2] < (byte)ProbeOp.Protobuf ||
                packet[2] > (byte)ProbeOp.StreamClosed ||
                BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(3)) != packet.Length - HeaderSize)
                return false;

            op = (ProbeOp)packet[2];
            payload = packet.Slice(HeaderSize);
            return true;
        }
    }
}
