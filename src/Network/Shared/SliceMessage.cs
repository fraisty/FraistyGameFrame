using System;

namespace FRFrame.Network.Shared
{
    public enum SliceError : byte
    {
        None, Invalid, Unauthenticated, AlreadyLoggedIn, RoomUnavailable, RoomFull, NotMember,
        RequestIdConflict, Overloaded, SchemaMismatch, StreamUnavailable, GameFaulted, RecoveryDeferred
    }

    /// <summary>Transport-neutral data for the POC-2 control channel. Direction and field use follow the opcode.</summary>
    public sealed class SliceMessage
    {
        public const int RoomCapacity = 3;
        public const int SchemaVersion = 1;
        public const string DefinitionId = "movement";

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

        /// <summary>Validates the fields consumed by the server. Server-owned fields cannot appear in a request.</summary>
        public bool IsRequest(ProbeOp op) =>
            RequestId != 0 && Error == 0 && ReplyTo == 0 && Player == 0 && Session == 0 &&
            Header == 0 && MemberIds.Length == 0 && Online == 0 && Capacity == 0 &&
            (op == ProbeOp.StreamReady || (Stream == 0 && Generation == 0 && Schema == 0 && Hash == 0)) &&
            (op == ProbeOp.SetMarker || Marker == 0) &&
            (op switch
            {
                ProbeOp.Login => Room == 0 && Text.Length == 1 && Text[0] >= 'A' && Text[0] <= 'H',
                ProbeOp.CreateRoom => Room == 0 && Text == DefinitionId,
                ProbeOp.JoinRoom => Room > 0 && Text.Length == 0,
                ProbeOp.StreamOpen => Room > 0 && Text.Length == 0,
                ProbeOp.RoomMessage => Room > 0 && !string.IsNullOrWhiteSpace(Text) && Text.Length <= 80,
                ProbeOp.SetMarker => Room > 0 && Text.Length == 0 && Marker >= 0 && Marker <= 9,
                ProbeOp.StreamReady => Room > 0 && Text == DefinitionId && Stream != 0 && Generation != 0,
                _ => false
            });
    }
}
