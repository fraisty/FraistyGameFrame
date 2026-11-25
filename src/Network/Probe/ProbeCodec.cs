#nullable enable
using System;
using System.Text;
using FRFrame.Network.Shared;
using FRFrame.Network.Protocol.Wire;
using Google.Protobuf;

namespace FRFrame.Network.Protocol
{
    public sealed class ProbeSample
    {
        public int Sequence { get; set; }
        public string Text { get; set; } = string.Empty;
        public byte[] Samples { get; set; } = Array.Empty<byte>();
    }

    /// <summary>Protobuf echo and acknowledgement used by the existing loopback validation entry.</summary>
    public static class ProbeCodec
    {
        internal const int MaxTextBytes = 100;
        internal const int MaxSamples = 350;
        private static readonly MessageParser<ProtoProbe> ProbeParser = ProtoProbe.Parser.WithDiscardUnknownFields(true);
        private static readonly MessageParser<StreamAcknowledgement> AckParser =
            StreamAcknowledgement.Parser.WithDiscardUnknownFields(true);

        public static ProbeSample Sample() => new ProbeSample
        {
            Sequence = 123,
            Text = "网络 probe",
            Samples = new byte[] { 1, 2, 3, 5, 8, 13 }
        };

        public static bool Matches(ProbeSample left, ProbeSample right) =>
            left.Sequence == right.Sequence && left.Text == right.Text &&
            left.Samples.AsSpan().SequenceEqual(right.Samples);

        public static byte[] Encode(ProbeSample message) => new ProtoProbe
        {
            Sequence = message.Sequence,
            Text = message.Text,
            Samples = ByteString.CopyFrom(message.Samples)
        }.ToByteArray();

        public static bool TryDecode(ReadOnlySpan<byte> bytes, out ProbeSample message)
        {
            message = null!;
            if (!BoundedProtobuf.TryParse(ProbeParser, bytes, Envelope.MaxPayload, out var proto, out _) ||
                proto.Samples.Length > MaxSamples || Encoding.UTF8.GetByteCount(proto.Text) > MaxTextBytes)
                return false;
            message = new ProbeSample
            {
                Sequence = proto.Sequence, Text = proto.Text, Samples = proto.Samples.ToByteArray()
            };
            return true;
        }

        public static byte[] EncodeAcknowledgement(byte stream) => new StreamAcknowledgement { Stream = stream }.ToByteArray();

        public static bool TryDecodeAcknowledgement(ReadOnlySpan<byte> bytes, out byte stream)
        {
            stream = 0;
            if (!BoundedProtobuf.TryParse(AckParser, bytes, 8, out var acknowledgement, out _) ||
                acknowledgement.Stream is < 1 or > 2)
                return false;
            stream = (byte)acknowledgement.Stream;
            return true;
        }
    }
}
