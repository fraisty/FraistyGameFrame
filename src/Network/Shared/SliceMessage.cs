#nullable enable
using System;

namespace FRFrame.Network.Shared
{
    public enum ErrorCode : byte
    {
        None = 0,
        Invalid = 1,
        Unauthenticated = 2,
        AlreadyLoggedIn = 3,
        RoomUnavailable = 4,
        RoomFull = 5,
        NotMember = 6,
        RequestIdConflict = 7,
        Overloaded = 8,
        SchemaMismatch = 9,
        StreamUnavailable = 10,
        GameFaulted = 11,
        RecoveryDeferred = 12,
        NotOwner = 13,
        RoomClosing = 14,
        Cancelled = 15,
        CleanupFaulted = 16,
        NoGame = 17,
        InvalidRecovery = 18,
        CallbackTimeout = 19,
        MalformedMessage = 20,
        UnsupportedProtocolVersion = 21,
        UnknownOpCode = 22,
        WrongDirection = 23,
        WrongDelivery = 24,
        PayloadTooLarge = 25,
        RateLimited = 26,
        SessionExpired = 27,
        Forbidden = 28,
        InvalidState = 29,
        ServerDraining = 30,
        InternalError = 31
    }

    /// <summary>Transport-neutral data for the POC-2 control channel. Direction and field use follow the opcode.</summary>
    public sealed class SliceMessage
    {
        public const int RoomCapacity = 3;
        public const int MembershipCapacity = 4;
        public const int SchemaVersion = 1;
        public const string DefinitionId = "movement";
        public const string SharedDefinitionId = "movement.shared";
        public const string PlainRoom = "plain";

        public uint RequestId;
        public ErrorCode Error;
        public OpCode ReplyTo;
        public int Room;
        public int Player;
        public int Session;
        public string Text = string.Empty;
        public uint Stream;
        public uint Generation;
        public int Header;
        public int Schema;
        public ulong Hash;
        public int Marker;
        public int[] MemberIds = Array.Empty<int>();
        public int Online;
        public int Capacity;
        public int Epoch;
        public int ConnectionGeneration;
        public string Token = string.Empty;
        public int Target;
        public int DelayMs;
        public uint GameMessageId;
        public byte[] GamePayload = Array.Empty<byte>();
        public uint BusinessMessageId;
        public byte[] BusinessPayload = Array.Empty<byte>();
        public RoomPayload? RoomData;
        public GameInitialization? GameOptions;

        public static bool IsClientRequest(OpCode op) =>
            ProtocolMessages.TryGet(op, out var descriptor) && descriptor.Kind == MessageKind.Request;

        /// <summary>Validates the fields consumed by the server. Server-owned fields cannot appear in a request.</summary>
        public bool IsRequest(OpCode op) =>
            RequestId != 0 && Error == 0 && ReplyTo == 0 && Player == 0 && Session == 0 &&
            Header == 0 && MemberIds.Length == 0 && Online == 0 && Capacity == 0 &&
            Epoch == 0 && ConnectionGeneration == 0 &&
            (op == OpCode.Recover || Token.Length == 0) &&
            (op == OpCode.StreamReady || (Schema == 0 && Hash == 0)) &&
            (op == OpCode.StreamReady || op == OpCode.StreamCancel || op == OpCode.StreamConfirm ||
                (Stream == 0 && Generation == 0)) &&
            (op is OpCode.Kick or OpCode.TransferOwner or OpCode.LeaveRoom or OpCode.InvitePlayer or OpCode.RevokeInvitation || Target == 0) &&
            (op == OpCode.SetMarker || DelayMs == 0) &&
            (op == OpCode.SetMarker || Marker == 0) &&
            (op is OpCode.GameCommand or OpCode.GameQuery || (GameMessageId == 0 && GamePayload.Length == 0)) &&
            (op == OpCode.CreateRoom || GameOptions == null) &&
            (op == OpCode.BusinessRequest || (BusinessMessageId == 0 && BusinessPayload.Length == 0)) &&
            (op is OpCode.CreateRoom or OpCode.ConfigureRoom or OpCode.ListRooms or OpCode.ListInvitations || RoomData == null) &&
            (op switch
            {
                OpCode.Login => Room == 0 && BusinessWire.IsIdentity(Text),
                OpCode.Recover => Room == 0 && Text.Length == 0 && Token.Length == 44,
                OpCode.CreateRoom => Room == 0 && GameWire.IsDefinitionId(Text) && (RoomData == null || RoomData is RoomConfiguration),
                OpCode.ConfigureRoom => Room > 0 && Text.Length == 0 && RoomData is RoomConfiguration { Capacity: > 0 },
                OpCode.GetRoomInfo => Room > 0 && Text.Length == 0,
                OpCode.InvitePlayer or OpCode.RevokeInvitation => Room > 0 && Text.Length == 0 && Target > 0,
                OpCode.ListRooms or OpCode.ListInvitations => Room == 0 && Text.Length == 0 &&
                    RoomData is RoomDirectoryQuery query && query.IsValid(op == OpCode.ListInvitations),
                OpCode.JoinRoom => Room > 0 && Text.Length == 0,
                OpCode.StreamOpen => Room > 0 && Text.Length == 0,
                OpCode.RoomMessage => Room > 0 && !string.IsNullOrWhiteSpace(Text) && Text.Length <= 80,
                OpCode.SetMarker => Room > 0 && Text.Length == 0 && Marker >= 0 && Marker <= 9 &&
                    DelayMs >= 0 && DelayMs <= 12_000,
                OpCode.StreamReady => Room > 0 && GameWire.IsDefinitionId(Text) && Stream != 0 && Generation != 0,
                OpCode.GameCommand or OpCode.GameQuery => Room > 0 && Text.Length == 0 && GameMessageId != 0 &&
                    GamePayload.Length <= GameWire.MaxBodyBytes,
                OpCode.BusinessRequest => Room == 0 && Text.Length == 0 && BusinessMessageId != 0 &&
                    BusinessPayload.Length <= BusinessWire.MaxBodyBytes,
                OpCode.StreamCancel => Room > 0 && Text.Length == 0 && Stream != 0 && Generation != 0,
                OpCode.StreamConfirm => Room > 0 && Text.Length == 0 && Stream != 0 && Generation != 0,
                OpCode.LeaveRoom => Room > 0 && Text.Length == 0 && Target >= 0,
                OpCode.Kick or OpCode.TransferOwner => Room > 0 && Text.Length == 0 && Target > 0,
                OpCode.CloseRoom => Room > 0 && Text.Length == 0,
                _ => false
            });

        /// <summary>A query response identifies its room and carries data only on success.</summary>
        public bool IsGameQueryReply() => Room > 0 && Player == 0 && Text.Length == 0 && Stream == 0 && Generation == 0 &&
            Header == 0 && Schema == 0 && Hash == 0 && Marker == 0 && MemberIds.Length == 0 && Online == 0 && Capacity == 0 &&
            Token.Length == 0 && Target == 0 && DelayMs == 0 &&
            (Error == ErrorCode.None
                ? GameMessageId != 0 && Session > 0 && Epoch > 0 && ConnectionGeneration > 0
                : GameMessageId == 0 && GamePayload.Length == 0 &&
                    ((Session > 0 && Epoch > 0 && ConnectionGeneration > 0) ||
                     (Session == 0 && Epoch == 0 && ConnectionGeneration == 0 &&
                      Error is ErrorCode.Invalid or ErrorCode.Unauthenticated or ErrorCode.RoomClosing or ErrorCode.ServerDraining)));

        /// <summary>Business replies and notifications carry only binding and application data.</summary>
        public bool IsBusinessDelivery(OpCode op) =>
            Room == 0 && Player == 0 && Text.Length == 0 && Stream == 0 && Generation == 0 && Header == 0 &&
            Schema == 0 && Hash == 0 && Marker == 0 && MemberIds.Length == 0 && Online == 0 && Capacity == 0 &&
            Token.Length == 0 && Target == 0 && DelayMs == 0 && GameMessageId == 0 && GamePayload.Length == 0 &&
            BusinessPayload.Length <= BusinessWire.MaxBodyBytes &&
            (op == OpCode.BusinessPush
                ? ReplyTo == 0 && Error == ErrorCode.None && BusinessMessageId != 0 &&
                    Session > 0 && Epoch > 0 && ConnectionGeneration > 0
                : (Error == ErrorCode.None
                    ? BusinessMessageId != 0 && Session > 0 && Epoch > 0 && ConnectionGeneration > 0
                    : BusinessMessageId == 0 && BusinessPayload.Length == 0 &&
                        ((Session > 0 && Epoch > 0 && ConnectionGeneration > 0) ||
                         (Session == 0 && Epoch == 0 && ConnectionGeneration == 0 &&
                          Error is ErrorCode.Invalid or ErrorCode.Unauthenticated or ErrorCode.RoomClosing or ErrorCode.ServerDraining))));
    }

    public static class BusinessWire
    {
        public const int MaxBodyBytes = 384;
        public static bool IsIdentity(string declaration)
        {
            if (string.IsNullOrEmpty(declaration) || declaration.Length > 32)
                return false;
            foreach (var c in declaration)
                if (c is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-'))
                    return false;
            return true;
        }
    }

    /// <summary>Owned initialization bytes for a single Context and the schema that decodes them.</summary>
    public sealed class GameInitialization
    {
        public const int MaxBodyBytes = 128;
        private readonly byte[] _payload;

        public GameInitialization(int schemaVersion, ReadOnlySpan<byte> payload)
        {
            if (schemaVersion <= 0)
                throw new ArgumentOutOfRangeException(nameof(schemaVersion));
            if (payload.Length > MaxBodyBytes)
                throw new ArgumentOutOfRangeException(nameof(payload));
            SchemaVersion = schemaVersion;
            _payload = payload.ToArray();
        }

        public int SchemaVersion { get; }
        public ReadOnlySpan<byte> Payload => _payload;
    }

    /// <summary>Bounds and identifiers shared by game registrations and the compatible protocol.</summary>
    public static class GameWire
    {
        public const int MaxBodyBytes = 192;

        public static bool IsDefinitionId(string text)
        {
            if (string.IsNullOrEmpty(text) || text.Length > 48)
                return false;
            foreach (var c in text)
                if (c is not (>= 'a' and <= 'z' or >= 'A' and <= 'Z' or >= '0' and <= '9' or '.' or '_' or '-'))
                    return false;
            return true;
        }
    }
}
