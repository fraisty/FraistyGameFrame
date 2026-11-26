using System;
using FRFrame.Network.Protocol.Wire;
using FRFrame.Network.Shared;
using Google.Protobuf;

namespace FRFrame.Network.Protocol
{
    /// <summary>Typed codec for the room-independent local Profile example.</summary>
    public static class ProfileProtocol
    {
        public const uint RequestId = 1;
        public const uint PushId = 1;
        private const uint Version = 1;
        private const int ExampleBodyBytes = 16;
        private static readonly MessageParser<ProfileRequest> RequestParser = ProfileRequest.Parser.WithDiscardUnknownFields(true);
        private static readonly MessageParser<ProfileSnapshot> SnapshotParser = ProfileSnapshot.Parser.WithDiscardUnknownFields(true);

        public static byte[] EncodeRequest(uint increment = 0) =>
            new ProfileRequest { Version = Version, Increment = increment }.ToByteArray();

        public static ErrorCode DecodeRequest(ReadOnlySpan<byte> bytes, out uint increment)
        {
            increment = 0;
            if (!BoundedProtobuf.TryParse(RequestParser, bytes, ExampleBodyBytes, out var request, out var error))
                return error;
            if (request.Version != Version || request.Increment > 9 || !bytes.SequenceEqual(request.ToByteArray()))
                return ErrorCode.Invalid;
            increment = request.Increment;
            return ErrorCode.None;
        }

        public static byte[] EncodeSnapshot((int Player, uint Count) profile) =>
            new ProfileSnapshot { Version = Version, PlayerId = profile.Player, Count = profile.Count }.ToByteArray();

        public static bool TryReadSnapshot(ReadOnlySpan<byte> bytes, out (int Player, uint Count) profile)
        {
            profile = default;
            if (!BoundedProtobuf.TryParse(SnapshotParser, bytes, ExampleBodyBytes, out var snapshot, out _) ||
                snapshot.Version != Version || snapshot.PlayerId <= 0 || !bytes.SequenceEqual(snapshot.ToByteArray()))
                return false;
            profile = (snapshot.PlayerId, snapshot.Count);
            return true;
        }
    }
}
