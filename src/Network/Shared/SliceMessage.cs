using System;

namespace FRFrame.Network.Shared
{
    public enum SliceError : byte
    {
        None, Invalid, Unauthenticated, AlreadyLoggedIn, RoomUnavailable, RoomFull, NotMember,
        RequestIdConflict, Overloaded, SchemaMismatch, StreamUnavailable, GameFaulted, RecoveryDeferred,
        NotOwner, RoomClosing, Cancelled, CleanupFaulted, NoGame, InvalidRecovery, CallbackTimeout
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
        public SliceError Error;
        public ProbeOp ReplyTo;
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

        public static bool IsClientRequest(ProbeOp op) =>
            (op >= ProbeOp.Login && op <= ProbeOp.StreamReady) ||
            (op >= ProbeOp.Recover && op <= ProbeOp.StreamConfirm);

        /// <summary>Validates the fields consumed by the server. Server-owned fields cannot appear in a request.</summary>
        public bool IsRequest(ProbeOp op) =>
            RequestId != 0 && Error == 0 && ReplyTo == 0 && Player == 0 && Session == 0 &&
            Header == 0 && MemberIds.Length == 0 && Online == 0 && Capacity == 0 &&
            Epoch == 0 && ConnectionGeneration == 0 &&
            (op == ProbeOp.Recover || Token.Length == 0) &&
            (op == ProbeOp.StreamReady || (Schema == 0 && Hash == 0)) &&
            (op == ProbeOp.StreamReady || op == ProbeOp.StreamCancel || op == ProbeOp.StreamConfirm ||
                (Stream == 0 && Generation == 0)) &&
            (op == ProbeOp.Kick || op == ProbeOp.TransferOwner || op == ProbeOp.LeaveRoom || Target == 0) &&
            (op == ProbeOp.SetMarker || DelayMs == 0) &&
            (op == ProbeOp.SetMarker || Marker == 0) &&
            (op switch
            {
                ProbeOp.Login => Room == 0 && Text.Length == 1 && Text[0] >= 'A' && Text[0] <= 'H',
                ProbeOp.Recover => Room == 0 && Text.Length == 0 && Token.Length == 44,
                ProbeOp.CreateRoom => Room == 0 && IsDefinition(Text),
                ProbeOp.JoinRoom => Room > 0 && Text.Length == 0,
                ProbeOp.StreamOpen => Room > 0 && Text.Length == 0,
                ProbeOp.RoomMessage => Room > 0 && !string.IsNullOrWhiteSpace(Text) && Text.Length <= 80,
                ProbeOp.SetMarker => Room > 0 && Text.Length == 0 && Marker >= 0 && Marker <= 9 &&
                    DelayMs >= 0 && DelayMs <= 12_000,
                ProbeOp.StreamReady => Room > 0 && IsDefinition(Text) && Stream != 0 && Generation != 0,
                ProbeOp.StreamCancel => Room > 0 && Text.Length == 0 && Stream != 0 && Generation != 0,
                ProbeOp.StreamConfirm => Room > 0 && Text.Length == 0 && Stream != 0 && Generation != 0,
                ProbeOp.LeaveRoom => Room > 0 && Text.Length == 0 && Target >= 0 && Target <= 8,
                ProbeOp.Kick => Room > 0 && Text.Length == 0 && Target >= 1 && Target <= 8,
                ProbeOp.TransferOwner => Room > 0 && Text.Length == 0 && Target >= 1 && Target <= 8,
                ProbeOp.CloseRoom => Room > 0 && Text.Length == 0,
                _ => false
            });

        private static bool IsDefinition(string text) =>
            text == DefinitionId || text == SharedDefinitionId || text == PlainRoom;
    }
}
