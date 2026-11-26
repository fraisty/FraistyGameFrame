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
            (op == OpCode.Kick || op == OpCode.TransferOwner || op == OpCode.LeaveRoom || Target == 0) &&
            (op == OpCode.SetMarker || DelayMs == 0) &&
            (op == OpCode.SetMarker || Marker == 0) &&
            (op == OpCode.GameCommand || (GameMessageId == 0 && GamePayload.Length == 0)) &&
            (op switch
            {
                OpCode.Login => Room == 0 && Text.Length == 1 && Text[0] >= 'A' && Text[0] <= 'H',
                OpCode.Recover => Room == 0 && Text.Length == 0 && Token.Length == 44,
                OpCode.CreateRoom => Room == 0 && GameWire.IsDefinitionId(Text),
                OpCode.JoinRoom => Room > 0 && Text.Length == 0,
                OpCode.StreamOpen => Room > 0 && Text.Length == 0,
                OpCode.RoomMessage => Room > 0 && !string.IsNullOrWhiteSpace(Text) && Text.Length <= 80,
                OpCode.SetMarker => Room > 0 && Text.Length == 0 && Marker >= 0 && Marker <= 9 &&
                    DelayMs >= 0 && DelayMs <= 12_000,
                OpCode.StreamReady => Room > 0 && GameWire.IsDefinitionId(Text) && Stream != 0 && Generation != 0,
                OpCode.GameCommand => Room > 0 && Text.Length == 0 && GameMessageId != 0 && GamePayload.Length <= GameWire.MaxBodyBytes,
                OpCode.StreamCancel => Room > 0 && Text.Length == 0 && Stream != 0 && Generation != 0,
                OpCode.StreamConfirm => Room > 0 && Text.Length == 0 && Stream != 0 && Generation != 0,
                OpCode.LeaveRoom => Room > 0 && Text.Length == 0 && Target >= 0 && Target <= 8,
                OpCode.Kick => Room > 0 && Text.Length == 0 && Target >= 1 && Target <= 8,
                OpCode.TransferOwner => Room > 0 && Text.Length == 0 && Target >= 1 && Target <= 8,
                OpCode.CloseRoom => Room > 0 && Text.Length == 0,
                _ => false
            });

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
