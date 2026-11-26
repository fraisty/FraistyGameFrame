#nullable enable
using System;
using System.Linq;
using System.Text;
using FRFrame.Network.Shared;
using FRFrame.Network.Protocol.Wire;
using Google.Protobuf;

namespace FRFrame.Network.Protocol
{
    /// <summary>Serializes the shared control DTO on both the client and server.</summary>
    public static class SliceCodec
    {
        public const int MaxTextBytes = 240;
        public const int MaxTicketBytes = 44;
        private static readonly MessageParser<SliceWire> Parser = SliceWire.Parser.WithDiscardUnknownFields(true);

        public static byte[] Write(OpCode op, SliceMessage message)
        {
            var wire = new SliceWire
            {
                Error = (uint)message.Error, ReplyTo = (uint)message.ReplyTo,
                Room = message.Room, Player = message.Player, Session = message.Session, Text = message.Text,
                Stream = message.Stream, Generation = message.Generation, Header = message.Header,
                Schema = message.Schema, Hash = message.Hash, Marker = message.Marker, Online = message.Online,
                Capacity = message.Capacity, Epoch = message.Epoch, ConnectionGeneration = message.ConnectionGeneration,
                Token = message.Token, Target = message.Target, DelayMs = message.DelayMs
            };
            if (message.GameMessageId != 0)
                wire.GameMessageId = message.GameMessageId;
            if (message.GamePayload.Length != 0)
                wire.GamePayload = ByteString.CopyFrom(message.GamePayload);
            wire.MemberIds.AddRange(message.MemberIds);
            return Envelope.Write(op, wire.ToByteArray(), new RequestId(message.RequestId));
        }

        public static bool TryReadPacket(ReadOnlySpan<byte> packet, MessageDirection direction, PacketDelivery delivery,
            out EnvelopeHeader header, out SliceMessage message, out ErrorCode error)
        {
            message = null!;
            return Envelope.TryRead(packet, out header, out var payload, out error, direction, delivery) &&
                TryRead(header, payload, out message, out error);
        }

        public static bool TryRead(EnvelopeHeader header, ReadOnlySpan<byte> payload,
            out SliceMessage message, out ErrorCode error)
        {
            message = null!;
            if (header.Op == OpCode.None || header.Descriptor.Payload != PayloadKind.Slice)
            {
                error = ErrorCode.UnknownOpCode;
                return false;
            }
            if (!BoundedProtobuf.TryParse(Parser, payload, header.Descriptor.MaxPayload, out var wire, out error))
                return false;
            if (wire.MemberIds.Count > SliceMessage.MembershipCapacity || wire.Text.Length > 80 ||
                Encoding.UTF8.GetByteCount(wire.Text) > MaxTextBytes ||
                Encoding.UTF8.GetByteCount(wire.Token) > MaxTicketBytes || wire.GamePayload.Length > GameWire.MaxBodyBytes)
            {
                error = ErrorCode.PayloadTooLarge;
                return false;
            }
            if (wire.Error > (uint)ErrorCode.InternalError ||
                (wire.ReplyTo != 0 && (wire.ReplyTo > byte.MaxValue || !SliceMessage.IsClientRequest((OpCode)wire.ReplyTo))) ||
                (header.Descriptor.Kind == MessageKind.Response && wire.ReplyTo == 0) ||
                (header.Op is not (OpCode.GameCommand or OpCode.GameEvent) && (wire.HasGameMessageId || wire.HasGamePayload)) ||
                (header.Op == OpCode.GameEvent && (wire.Room <= 0 || wire.Player <= 0 || wire.GameMessageId == 0 ||
                    wire.Error != 0 || wire.ReplyTo != 0 || wire.Session != 0 || wire.Text.Length != 0 || wire.Stream != 0 ||
                    wire.Generation != 0 || wire.Header != 0 || wire.Schema != 0 || wire.Hash != 0 || wire.Marker != 0 ||
                    wire.MemberIds.Count != 0 || wire.Online != 0 || wire.Capacity != 0 || wire.Epoch != 0 ||
                    wire.ConnectionGeneration != 0 || wire.Token.Length != 0 || wire.Target != 0 || wire.DelayMs != 0)))
            {
                error = ErrorCode.MalformedMessage;
                return false;
            }

            message = new SliceMessage
            {
                RequestId = header.RequestId.Value, Error = (ErrorCode)wire.Error, ReplyTo = (OpCode)wire.ReplyTo,
                Room = wire.Room, Player = wire.Player, Session = wire.Session, Text = wire.Text,
                Stream = wire.Stream, Generation = wire.Generation, Header = wire.Header, Schema = wire.Schema,
                Hash = wire.Hash, Marker = wire.Marker, MemberIds = wire.MemberIds.ToArray(), Online = wire.Online,
                Capacity = wire.Capacity, Epoch = wire.Epoch, ConnectionGeneration = wire.ConnectionGeneration,
                Token = wire.Token, Target = wire.Target, DelayMs = wire.DelayMs,
                GameMessageId = wire.GameMessageId, GamePayload = wire.GamePayload.IsEmpty ? Array.Empty<byte>() : wire.GamePayload.ToByteArray()
            };
            return true;
        }
    }
}
