using System;
using System.Buffers.Binary;

namespace FRFrame.Network.Shared
{
    /// <summary>Stable wire identifiers. Values 2 and 4 belonged to the retired POC codec.</summary>
    public enum OpCode : byte
    {
        None = 0,
        Protobuf = 1,
        ProtobufEcho = 3,
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
        StreamClosed = 17,
        Recover = 18,
        LeaveRoom = 19,
        Kick = 20,
        TransferOwner = 21,
        CloseRoom = 22,
        StreamCancel = 23,
        StreamConfirm = 24,
        RoomClosed = 25,
        RoomLeft = 26,
        SessionReplaced = 27,
        GameCommand = 28,
        GameEvent = 29,
        BusinessRequest = 30,
        BusinessPush = 31,
        ListRooms = 32,
        GetRoomInfo = 33,
        ConfigureRoom = 34,
        InvitePlayer = 35,
        RevokeInvitation = 36,
        ListInvitations = 37,
        RoomInvitation = 38,
        GameQuery = 39
    }

    /// <summary>Correlates a request and its response within the server-selected Session.</summary>
    public readonly struct RequestId
    {
        public RequestId(uint value) => Value = value;
        public uint Value { get; }
        public bool IsPresent => Value != 0;
    }

    public enum MessageDirection : byte { ClientToServer, ServerToClient }
    public enum MessageKind : byte { Control, Request, Response, Push }
    public enum PayloadKind : byte { Slice, Probe, StreamAcknowledgement }
    public enum MessageRateClass : byte { Control, Room, Game, Stream }

    /// <summary>The explicit protocol contract. Session and rate policy are consumed by subsequent runtime stages.</summary>
    public readonly struct MessageDescriptor
    {
        internal MessageDescriptor(OpCode op, MessageDirection direction, MessageKind kind, PayloadKind payload,
            int maxPayload, MessageRateClass rateClass, bool requiresSession = false, bool replayResult = false,
            PacketDelivery? delivery = PacketDelivery.ReliableOrdered)
        {
            Op = op;
            Direction = direction;
            Kind = kind;
            Payload = payload;
            MaxPayload = maxPayload;
            RateClass = rateClass;
            RequiresSession = requiresSession;
            ReplayResult = replayResult;
            Delivery = delivery;
        }

        public OpCode Op { get; }
        public MessageDirection Direction { get; }
        public MessageKind Kind { get; }
        public PayloadKind Payload { get; }
        public int MaxPayload { get; }
        public MessageRateClass RateClass { get; }
        public bool RequiresSession { get; }
        public bool ReplayResult { get; }
        public OpCode Response => Kind == MessageKind.Request ? OpCode.Response : OpCode.None;
        public bool HasRequestId => Kind == MessageKind.Request || Kind == MessageKind.Response;
        /// <summary>Null permits both delivery classes for the diagnostic echo.</summary>
        public PacketDelivery? Delivery { get; }
    }

    public static class ProtocolMessages
    {
        public static bool TryGet(OpCode op, out MessageDescriptor descriptor)
        {
            descriptor = op switch
            {
                OpCode.Protobuf => new MessageDescriptor(op, MessageDirection.ClientToServer, MessageKind.Control,
                    PayloadKind.Probe, Envelope.MaxPayload, MessageRateClass.Control, delivery: null),
                OpCode.ProtobufEcho => new MessageDescriptor(op, MessageDirection.ServerToClient, MessageKind.Control,
                    PayloadKind.Probe, Envelope.MaxPayload, MessageRateClass.Control, delivery: null),
                OpCode.LesAcknowledged => new MessageDescriptor(op, MessageDirection.ServerToClient, MessageKind.Control,
                    PayloadKind.StreamAcknowledgement, 8, MessageRateClass.Stream),
                OpCode.Login or OpCode.Recover => Request(op, 64, MessageRateClass.Control, false),
                OpCode.CreateRoom => Request(op, 384, MessageRateClass.Room),
                OpCode.ConfigureRoom => Request(op, 256, MessageRateClass.Room),
                OpCode.ListRooms or OpCode.ListInvitations => Request(op, 96, MessageRateClass.Room),
                OpCode.JoinRoom or OpCode.LeaveRoom or OpCode.TransferOwner or OpCode.CloseRoom or OpCode.Kick or
                    OpCode.GetRoomInfo or OpCode.InvitePlayer or OpCode.RevokeInvitation =>
                    Request(op, 64, MessageRateClass.Room),
                OpCode.RoomMessage => Request(op, 256, MessageRateClass.Room),
                OpCode.SetMarker => Request(op, 64, MessageRateClass.Game),
                OpCode.GameCommand or OpCode.GameQuery => Request(op, 256, MessageRateClass.Game),
                OpCode.BusinessRequest => Request(op, Envelope.MaxPayload, MessageRateClass.Game),
                OpCode.StreamOpen or OpCode.StreamReady or OpCode.StreamCancel or OpCode.StreamConfirm =>
                    Request(op, 96, MessageRateClass.Stream),
                OpCode.Response => new MessageDescriptor(op, MessageDirection.ServerToClient, MessageKind.Response,
                    PayloadKind.Slice, Envelope.MaxPayload, MessageRateClass.Control),
                OpCode.Members => Push(op, 256, MessageRateClass.Control),
                OpCode.RoomInvitation => Push(op, 64, MessageRateClass.Control),
                OpCode.RoomBroadcast => Push(op, 256, MessageRateClass.Room),
                OpCode.MarkerChanged => Push(op, 64, MessageRateClass.Game),
                OpCode.GameEvent => Push(op, 256, MessageRateClass.Game),
                OpCode.BusinessPush => Push(op, Envelope.MaxPayload, MessageRateClass.Game),
                OpCode.StreamClosed => Push(op, 64, MessageRateClass.Stream),
                OpCode.RoomClosed or OpCode.RoomLeft => Push(op, 64, MessageRateClass.Control),
                OpCode.SessionReplaced => Push(op, 64, MessageRateClass.Control),
                _ => default
            };
            return descriptor.Op != OpCode.None;
        }

        private static MessageDescriptor Request(OpCode op, int max, MessageRateClass rate, bool session = true) =>
            new MessageDescriptor(op, MessageDirection.ClientToServer, MessageKind.Request, PayloadKind.Slice,
                max, rate, requiresSession: session, replayResult: session);

        private static MessageDescriptor Push(OpCode op, int max, MessageRateClass rate) =>
            new MessageDescriptor(op, MessageDirection.ServerToClient, MessageKind.Push, PayloadKind.Slice, max, rate);
    }

    public readonly struct EnvelopeHeader
    {
        internal EnvelopeHeader(MessageDescriptor descriptor, RequestId requestId)
        {
            Descriptor = descriptor;
            RequestId = requestId;
        }

        public MessageDescriptor Descriptor { get; }
        public OpCode Op => Descriptor.Op;
        public RequestId RequestId { get; }
    }

    /// <summary>One datagram contains one header and one Protobuf payload, with no transport or identity fields.</summary>
    public static class Envelope
    {
        public const byte Header = 0;
        public const int HeaderSize = 9;
        public const int MaxPacket = 480;
        public const int MaxPayload = MaxPacket - HeaderSize;

        public static byte[] Write(OpCode op, ReadOnlySpan<byte> payload, RequestId requestId = default)
        {
            if (!ProtocolMessages.TryGet(op, out var descriptor))
                throw new ArgumentOutOfRangeException(nameof(op));
            if (descriptor.HasRequestId != requestId.IsPresent)
                throw new ArgumentException("RequestId must be nonzero for a request or response and zero for other messages.", nameof(requestId));
            if (payload.Length > descriptor.MaxPayload)
                throw new ArgumentOutOfRangeException(nameof(payload));

            var packet = new byte[HeaderSize + payload.Length];
            packet[0] = Header;
            packet[1] = (byte)ProtocolVersion.Current.Value;
            packet[2] = (byte)op;
            BinaryPrimitives.WriteUInt16LittleEndian(packet.AsSpan(3), (ushort)payload.Length);
            BinaryPrimitives.WriteUInt32LittleEndian(packet.AsSpan(5), requestId.Value);
            payload.CopyTo(packet.AsSpan(HeaderSize));
            return packet;
        }

        public static bool TryRead(ReadOnlySpan<byte> packet, out EnvelopeHeader header,
            out ReadOnlySpan<byte> payload, out ErrorCode error, MessageDirection? direction = null,
            PacketDelivery? delivery = null)
        {
            header = default;
            payload = default;
            error = ErrorCode.MalformedMessage;
            if (packet.Length > MaxPacket)
            {
                error = ErrorCode.PayloadTooLarge;
                return false;
            }
            if (packet.Length < 2 || packet[0] != Header)
                return false;
            if (packet[1] != ProtocolVersion.Current.Value)
            {
                error = ErrorCode.UnsupportedProtocolVersion;
                return false;
            }
            if (packet.Length < HeaderSize ||
                BinaryPrimitives.ReadUInt16LittleEndian(packet.Slice(3)) != packet.Length - HeaderSize)
                return false;
            if (!ProtocolMessages.TryGet((OpCode)packet[2], out var descriptor))
            {
                error = ErrorCode.UnknownOpCode;
                return false;
            }
            if (direction.HasValue && descriptor.Direction != direction.Value)
            {
                error = ErrorCode.WrongDirection;
                return false;
            }
            if (delivery.HasValue && descriptor.Delivery.HasValue && delivery.Value != descriptor.Delivery.Value)
            {
                error = ErrorCode.WrongDelivery;
                return false;
            }

            var requestId = new RequestId(BinaryPrimitives.ReadUInt32LittleEndian(packet.Slice(5)));
            if (descriptor.HasRequestId != requestId.IsPresent)
                return false;
            if (packet.Length - HeaderSize > descriptor.MaxPayload)
            {
                error = ErrorCode.PayloadTooLarge;
                return false;
            }

            header = new EnvelopeHeader(descriptor, requestId);
            payload = packet.Slice(HeaderSize);
            error = ErrorCode.None;
            return true;
        }
    }
}

