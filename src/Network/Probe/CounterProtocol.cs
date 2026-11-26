using System;
using FRFrame.Network.Protocol.Wire;
using FRFrame.Network.Shared;
using Google.Protobuf;

namespace FRFrame.Network.Protocol
{
    /// <summary>Counter's versioned full state. Revision is independent from the Protobuf schema version.</summary>
    public readonly struct CounterState
    {
        public CounterState(uint value, uint limit, ulong revision)
        {
            Value = value;
            Limit = limit;
            Revision = revision;
        }
        public uint Value { get; }
        public uint Limit { get; }
        public ulong Revision { get; }
    }

    /// <summary>The example counter's explicit application message IDs and bounded Protobuf payloads.</summary>
    public static class CounterProtocol
    {
        public const string DefinitionId = "counter";
        public const uint Add = 1;
        public const uint Changed = 1;
        public const uint GetState = 1;
        public const int OptionsSchemaVersion = 1;
        private const int MaxValue = 100;
        private const int BodyLimit = 8;
        private const int StateBodyLimit = 24;
        private const uint Version = 1;
        private static readonly MessageParser<CounterAdd> AddParser = CounterAdd.Parser.WithDiscardUnknownFields(true);
        private static readonly MessageParser<CounterChanged> ChangedParser = CounterChanged.Parser.WithDiscardUnknownFields(true);
        private static readonly MessageParser<CounterOptionsWire> OptionsParser = CounterOptionsWire.Parser.WithDiscardUnknownFields(true);
        private static readonly MessageParser<CounterStateWire> StateParser = CounterStateWire.Parser.WithDiscardUnknownFields(true);

        public static GameInitialization CreateOptions(uint initial, uint limit) =>
            new GameInitialization(OptionsSchemaVersion, new CounterOptionsWire { Initial = initial, Limit = limit }.ToByteArray());

        public static ErrorCode DecodeOptions(ReadOnlySpan<byte> payload, out (uint Initial, uint Limit) options)
        {
            options = default;
            if (!BoundedProtobuf.TryParse(OptionsParser, payload, GameInitialization.MaxBodyBytes, out var body, out var error))
                return error;
            if (!ValidState(body.Initial, body.Limit, 1) || !payload.SequenceEqual(body.ToByteArray()))
                return ErrorCode.Invalid;
            options = (body.Initial, body.Limit);
            return ErrorCode.None;
        }

        public static ErrorCode DecodeGetState(ReadOnlySpan<byte> payload, out bool request)
        {
            request = payload.IsEmpty;
            return request ? ErrorCode.None : ErrorCode.Invalid;
        }

        public static byte[] EncodeState(CounterState state) => new CounterStateWire
        {
            Version = Version, Value = state.Value, Limit = state.Limit, StateRevision = state.Revision
        }.ToByteArray();

        public static bool TryReadState(ReadOnlySpan<byte> payload, out CounterState state)
        {
            state = default;
            if (!BoundedProtobuf.TryParse(StateParser, payload, StateBodyLimit, out var body, out _) || body.Version != Version ||
                !ValidState(body.Value, body.Limit, body.StateRevision) || !payload.SequenceEqual(body.ToByteArray()))
                return false;
            state = new CounterState(body.Value, body.Limit, body.StateRevision);
            return true;
        }

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

        public static byte[] EncodeChanged(CounterState state) => new CounterChanged
        {
            Version = Version, Value = state.Value, Limit = state.Limit, StateRevision = state.Revision
        }.ToByteArray();

        public static bool TryReadChanged(ReadOnlySpan<byte> payload, out uint value)
        {
            value = 0;
            if (!BoundedProtobuf.TryParse(ChangedParser, payload, StateBodyLimit, out var body, out _) ||
                body.Version != Version || body.Value > MaxValue)
                return false;
            value = body.Value;
            return true;
        }

        public static bool TryReadChangedState(ReadOnlySpan<byte> payload, out CounterState state)
        {
            state = default;
            if (!BoundedProtobuf.TryParse(ChangedParser, payload, StateBodyLimit, out var body, out _) || body.Version != Version ||
                !body.HasStateRevision || !body.HasLimit || !ValidState(body.Value, body.Limit, body.StateRevision) ||
                !payload.SequenceEqual(body.ToByteArray()))
                return false;
            state = new CounterState(body.Value, body.Limit, body.StateRevision);
            return true;
        }

        private static bool ValidState(uint value, uint limit, ulong revision) =>
            limit is > 0 and <= MaxValue && value <= limit && revision > 0;
    }
}
