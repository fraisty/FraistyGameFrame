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
        private static readonly MessageParser<GameOptionsWire> OptionsParser = GameOptionsWire.Parser.WithDiscardUnknownFields(true);

        /// <summary>Freezes canonical room-result bytes before a successful query enters the replay ledger.</summary>
        public static EncodedRoomResult FreezeRoomResult(RoomPayload data)
        {
            if (data is not (RoomDirectoryPage or RoomInfo))
                throw new ArgumentException("Expected a Room query result.", nameof(data));
            return new EncodedRoomResult(RoomProtocol.Write(OpCode.Response, data)!.ToByteString().Span);
        }

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
            if (message.BusinessMessageId != 0)
                wire.BusinessMessageId = message.BusinessMessageId;
            if (message.BusinessPayload.Length != 0)
                wire.BusinessPayload = ByteString.CopyFrom(message.BusinessPayload);
            if (message.RoomData is EncodedRoomResult frozen)
            {
                if (op != OpCode.Response || message.Error != ErrorCode.None || !RoomWire.IsQuery(message.ReplyTo))
                    throw new ArgumentException("Frozen Room results are limited to query responses.", nameof(message));
                wire.RoomPayload = ByteString.CopyFrom(frozen.Bytes);
            }
            else if (RoomProtocol.Write(op, message.RoomData) is { } room)
            {
                if (room.CalculateSize() > RoomWire.MaxBodyBytes)
                    throw new ArgumentOutOfRangeException(nameof(message));
                wire.RoomPayload = room.ToByteString();
            }
            if (message.GameOptions is { } options)
                wire.GameOptions = new GameOptionsWire { SchemaVersion = (uint)options.SchemaVersion,
                    Payload = ByteString.CopyFrom(options.Payload) }.ToByteString();
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
                Encoding.UTF8.GetByteCount(wire.Token) > MaxTicketBytes || wire.GamePayload.Length > GameWire.MaxBodyBytes ||
                wire.BusinessPayload.Length > BusinessWire.MaxBodyBytes || wire.RoomPayload.Length > RoomWire.MaxBodyBytes ||
                (header.Op == OpCode.CreateRoom && !wire.HasGameOptions && payload.Length > (wire.HasRoomPayload ? 256 : 64)) ||
                (header.Op == OpCode.Members && !wire.HasRoomPayload && payload.Length > 128))
            {
                error = ErrorCode.PayloadTooLarge;
                return false;
            }
            var businessDelivery = header.Op == OpCode.BusinessPush ||
                (header.Op == OpCode.Response && wire.ReplyTo == (uint)OpCode.BusinessRequest);
            var queryReply = header.Op == OpCode.Response && wire.ReplyTo == (uint)OpCode.GameQuery;
            var gameQuery = header.Op == OpCode.GameQuery || queryReply;
            if (wire.Error > (uint)ErrorCode.InternalError ||
                (wire.ReplyTo != 0 && (wire.ReplyTo > byte.MaxValue || !SliceMessage.IsClientRequest((OpCode)wire.ReplyTo))) ||
                (header.Descriptor.Kind == MessageKind.Response && wire.ReplyTo == 0) ||
                (!gameQuery && header.Op is not (OpCode.GameCommand or OpCode.GameEvent) && (wire.HasGameMessageId || wire.HasGamePayload)) ||
                (queryReply && wire.Error != 0 && (wire.HasGameMessageId || wire.HasGamePayload)) ||
                (gameQuery && wire.HasGamePayload && wire.GamePayload.IsEmpty) ||
                (wire.HasGameOptions && header.Op != OpCode.CreateRoom) ||
                (!businessDelivery && header.Op != OpCode.BusinessRequest && (wire.HasBusinessMessageId || wire.HasBusinessPayload)) ||
                (businessDelivery && wire.Error != 0 && (wire.HasBusinessMessageId || wire.HasBusinessPayload)) ||
                (header.Op == OpCode.GameEvent && (wire.Room <= 0 || wire.Player <= 0 || wire.GameMessageId == 0 ||
                    wire.Error != 0 || wire.ReplyTo != 0 || wire.Session != 0 || wire.Text.Length != 0 || wire.Stream != 0 ||
                    wire.Generation != 0 || wire.Header != 0 || wire.Schema != 0 || wire.Hash != 0 || wire.Marker != 0 ||
                    wire.MemberIds.Count != 0 || wire.Online != 0 || wire.Capacity != 0 || wire.Epoch != 0 ||
                    wire.ConnectionGeneration != 0 || wire.Token.Length != 0 || wire.Target != 0 || wire.DelayMs != 0)))
            {
                error = ErrorCode.MalformedMessage;
                return false;
            }

            var decoded = new SliceMessage
            {
                RequestId = header.RequestId.Value, Error = (ErrorCode)wire.Error, ReplyTo = (OpCode)wire.ReplyTo,
                Room = wire.Room, Player = wire.Player, Session = wire.Session, Text = wire.Text,
                Stream = wire.Stream, Generation = wire.Generation, Header = wire.Header, Schema = wire.Schema,
                Hash = wire.Hash, Marker = wire.Marker, MemberIds = wire.MemberIds.ToArray(), Online = wire.Online,
                Capacity = wire.Capacity, Epoch = wire.Epoch, ConnectionGeneration = wire.ConnectionGeneration,
                Token = wire.Token, Target = wire.Target, DelayMs = wire.DelayMs,
                GameMessageId = wire.GameMessageId, GamePayload = wire.GamePayload.IsEmpty ? Array.Empty<byte>() : wire.GamePayload.ToByteArray(),
                BusinessMessageId = wire.BusinessMessageId,
                BusinessPayload = wire.BusinessPayload.IsEmpty ? Array.Empty<byte>() : wire.BusinessPayload.ToByteArray()
            };
            if (wire.HasGameOptions)
            {
                var body = wire.GameOptions.Span;
                if (body.IsEmpty || !BoundedProtobuf.TryParse(OptionsParser, body, GameInitialization.MaxBodyBytes + 9,
                    out var options, out _) || options.SchemaVersion is 0 or > int.MaxValue ||
                    options.Payload.Length > GameInitialization.MaxBodyBytes || !body.SequenceEqual(options.ToByteArray()))
                {
                    error = ErrorCode.MalformedMessage;
                    return false;
                }
                decoded.GameOptions = new GameInitialization((int)options.SchemaVersion, options.Payload.Span);
            }
            if (((gameQuery || (wire.HasGameOptions && !wire.HasRoomPayload)) && !payload.SequenceEqual(wire.ToByteArray())) ||
                (businessDelivery && !decoded.IsBusinessDelivery(header.Op)) || !RoomProtocol.TryRead(header.Op, wire, payload, decoded) ||
                (queryReply && !decoded.IsGameQueryReply()) ||
                ((header.Op == OpCode.GameQuery || wire.HasGameOptions) && !decoded.IsRequest(header.Op)))
            {
                error = ErrorCode.MalformedMessage;
                return false;
            }
            message = decoded;
            return true;
        }
    }
}
