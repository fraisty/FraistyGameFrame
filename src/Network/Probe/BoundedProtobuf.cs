#nullable enable
using System;
using System.IO;
using System.Text;
using Google.Protobuf;
using FRFrame.Network.Shared;

namespace FRFrame.Network.Protocol
{
    internal static class BoundedProtobuf
    {
        internal const int MaxDepth = 4;

        internal static bool TryParse<T>(MessageParser<T> parser, ReadOnlySpan<byte> payload,
            int byteBudget, out T message, out ErrorCode error) where T : class, IMessage<T>
        {
            message = null!;
            if (payload.Length > byteBudget)
            {
                error = ErrorCode.PayloadTooLarge;
                return false;
            }

            // The protocol uses proto3 fields. Check their framing before the generated parser,
            // which can interpret a literal zero tag as end of message when discarding unknown fields.
            if (!HasValidFieldFraming(payload))
            {
                error = ErrorCode.MalformedMessage;
                return false;
            }

            try
            {
                using var stream = new MemoryStream(payload.ToArray(), writable: false);
                using var input = CodedInputStream.CreateWithLimits(stream, byteBudget, MaxDepth);
                message = parser.ParseFrom(input);
                error = ErrorCode.None;
                return true;
            }
            catch (Exception exception) when (exception is InvalidProtocolBufferException or DecoderFallbackException)
            {
                error = ErrorCode.MalformedMessage;
                return false;
            }
        }

        private static bool HasValidFieldFraming(ReadOnlySpan<byte> payload)
        {
            while (!payload.IsEmpty)
            {
                if (!TryReadVarint(ref payload, out var tag) || tag < 8 || tag > uint.MaxValue)
                    return false;
                switch (tag & 7)
                {
                    case 0:
                        if (!TryReadVarint(ref payload, out _))
                            return false;
                        continue;
                    case 1:
                        if (payload.Length < 8)
                            return false;
                        payload = payload.Slice(8);
                        break;
                    case 2:
                        if (!TryReadVarint(ref payload, out var length) || length > (ulong)payload.Length)
                            return false;
                        payload = payload.Slice((int)length);
                        break;
                    case 5:
                        if (payload.Length < 4)
                            return false;
                        payload = payload.Slice(4);
                        break;
                    default:
                        return false; // Groups and the remaining wire types are outside this schema.
                }
            }
            return true;
        }

        private static bool TryReadVarint(ref ReadOnlySpan<byte> bytes, out ulong value)
        {
            value = 0;
            for (var index = 0; index < bytes.Length && index < 10; index++)
            {
                var current = bytes[index];
                if (index == 9 && current > 1)
                    return false;
                value |= (ulong)(current & 0x7f) << (index * 7);
                if (current < 0x80)
                {
                    bytes = bytes.Slice(index + 1);
                    return true;
                }
            }
            return false;
        }
    }
}
