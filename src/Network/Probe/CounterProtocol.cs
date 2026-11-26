using System;
using FRFrame.Network.Protocol.Wire;
using FRFrame.Network.Shared;
using Google.Protobuf;

namespace FRFrame.Network.Protocol
{
    /// <summary>The example counter's explicit application message IDs and bounded Protobuf payloads.</summary>
    public static class CounterProtocol
    {
        public const string DefinitionId = "counter";
        public const uint Add = 1;
        public const uint Changed = 1;
        private const int MaxValue = 100;
        private const int BodyLimit = 8;
        private const uint Version = 1;
        private static readonly MessageParser<CounterAdd> AddParser = CounterAdd.Parser.WithDiscardUnknownFields(true);
        private static readonly MessageParser<CounterChanged> ChangedParser = CounterChanged.Parser.WithDiscardUnknownFields(true);

        public static byte[] EncodeAdd(int amount) => new CounterAdd { Version = Version, Amount = amount }.ToByteArray();

        public static ErrorCode DecodeAdd(ReadOnlySpan<byte> payload, out int amount)
        {
            amount = 0;
            if (!BoundedProtobuf.TryParse(AddParser, payload, BodyLimit, out var body, out var error))
                return error;
            if (body.Version != Version || body.Amount is < -9 or > 9 || !payload.SequenceEqual(body.ToByteArray()))
                return ErrorCode.Invalid;
            amount = body.Amount;
            return ErrorCode.None;
        }

        public static byte[] EncodeChanged(uint value) => new CounterChanged { Version = Version, Value = value }.ToByteArray();

        public static bool TryReadChanged(ReadOnlySpan<byte> payload, out uint value)
        {
            value = 0;
            if (!BoundedProtobuf.TryParse(ChangedParser, payload, BodyLimit, out var body, out _) ||
                body.Version != Version || body.Value > MaxValue)
                return false;
            value = body.Value;
            return true;
        }
    }
}
