using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using FRFrame.Network.Shared;
using Google.Protobuf;
using MessagePack;

namespace FRFrame.Network.Probe
{
    /// <summary>The same business fields carried by both POC codecs.</summary>
    [MessagePackObject]
    public sealed partial class PackProbe
    {
        [Key(0)] public int Sequence { get; set; }
        [Key(1)] public string Text { get; set; } = string.Empty;
        [Key(2)] public byte[] Samples { get; set; } = Array.Empty<byte>();
    }

    /// <summary>POC-only codec comparison with bounded input and no polymorphic deserialization.</summary>
    /// <summary>POC-only codec comparison with bounded input and no polymorphic deserialization.</summary>
    public static class ProbeCodecs
    {
        private const int MaxTextBytes = 100;
        private const int MaxSamples = 350;
        private const int MaxDepth = 8;
        private static readonly MessageParser<ProtoProbe> ProtoParser =
            ProtoProbe.Parser.WithDiscardUnknownFields(true);
        private static readonly MessagePackSerializerOptions PackOptions = new MessagePackSerializerOptions(
            MessagePackFormatterFactory.DefaultAot)
        {
            MaxDepth = MaxDepth,
            MaxBufferedMessageSize = ProbeEnvelope.MaxPayload
        };

        public static PackProbe Sample() => new PackProbe
        {
            Sequence = 123,
            Text = "网络 probe",
            Samples = new byte[] { 1, 2, 3, 5, 8, 13 }
        };

        public static bool Matches(PackProbe left, PackProbe right) =>
            left.Sequence == right.Sequence && left.Text == right.Text &&
            left.Samples.AsSpan().SequenceEqual(right.Samples);

        public static byte[] Encode(ProbeOp op, PackProbe message)
        {
            if (op == ProbeOp.MessagePack || op == ProbeOp.MessagePackEcho)
                return MessagePackSerializer.Serialize(message, PackOptions);

            return new ProtoProbe
            {
                Sequence = message.Sequence,
                Text = message.Text,
                Samples = ByteString.CopyFrom(message.Samples)
            }.ToByteArray();
        }

        public static bool TryDecode(ProbeOp op, ReadOnlySpan<byte> bytes, out PackProbe message)
        {
            message = null!;
            if (bytes.Length > ProbeEnvelope.MaxPayload)
                return false;

            if (op == ProbeOp.MessagePack || op == ProbeOp.MessagePackEcho)
            {
                try
                {
                    // This POC schema accepts scalar fields and scalar extensions in one short array.
                    var reader = new MessagePackReader(bytes.ToArray());
                    var fields = reader.ReadArrayHeader();
                    if (fields > 8)
                        return false;
                    for (var index = 0; index < fields; index++)
                    {
                        if (reader.NextMessagePackType is MessagePackType.Array or MessagePackType.Map or MessagePackType.Extension)
                            return false;
                        reader.Skip();
                    }
                    if (!reader.End)
                        return false;
                    message = MessagePackSerializer.Deserialize<PackProbe>(bytes, PackOptions);
                }
                catch (MessagePackSerializationException)
                {
                    return false;
                }

                if (message == null)
                    return false;

                message.Text ??= string.Empty;
                message.Samples ??= Array.Empty<byte>();
            }
            else
            {
                try
                {
                    using var stream = new MemoryStream(bytes.ToArray(), false);
                    using var input = CodedInputStream.CreateWithLimits(stream, ProbeEnvelope.MaxPayload, MaxDepth);
                    var proto = ProtoParser.ParseFrom(input);
                    message = new PackProbe
                    {
                        Sequence = proto.Sequence,
                        Text = proto.Text,
                        Samples = proto.Samples.ToByteArray()
                    };
                }
                catch (InvalidProtocolBufferException)
                {
                    return false;
                }
            }

            return message.Samples.Length <= MaxSamples && Encoding.UTF8.GetByteCount(message.Text) <= MaxTextBytes;
        }

        /// <summary>Exercises schema evolution, bounded decoding and the same measurement loop on both runtimes.</summary>
        public static void VerifyAndMeasure(Action<string> report)
        {
            var nominal = Sample();
            var large = new PackProbe { Sequence = int.MaxValue, Text = new string('x', MaxTextBytes), Samples = new byte[MaxSamples] };
            foreach (var op in new[] { ProbeOp.Protobuf, ProbeOp.MessagePack })
            {
                var defaults = new PackProbe();
                var empty = Encode(op, defaults);
                var encoded = Encode(op, nominal);
                var maximal = Encode(op, large);
                var evolved = new byte[encoded.Length + (op == ProbeOp.Protobuf ? 2 : 1)];
                encoded.CopyTo(evolved, 0);
                if (op == ProbeOp.Protobuf)
                    evolved[evolved.Length - 2] = 0x20; // New scalar field 4.
                else
                    evolved[0] = 0x94; // Same three fields plus a fourth scalar.
                evolved[evolved.Length - 1] = 1;

                var oversizedCollection = Encode(op, new PackProbe { Samples = new byte[MaxSamples + 1] });
                var nested = op == ProbeOp.Protobuf
                    ? new byte[] { 0x23, 0x23, 0x23, 0x23, 0x23, 0x23, 0x23, 0x23, 0x23, 0x24, 0x24, 0x24, 0x24, 0x24, 0x24, 0x24, 0x24, 0x24 }
                    : new byte[] { 0x94, 1, 0xa0, 0xc4, 0, 0x91, 0x91, 0x91, 0x91, 0x91, 0x91, 0x91, 0x91, 0x91, 0 };
                if (!TryDecode(op, empty, out var defaultRoundtrip) || !Matches(defaults, defaultRoundtrip) ||
                    !TryDecode(op, evolved, out var evolvedRoundtrip) || !Matches(nominal, evolvedRoundtrip) ||
                    !TryDecode(op, maximal, out var maxRoundtrip) || !Matches(large, maxRoundtrip) ||
                    TryDecode(op, encoded.AsSpan(0, encoded.Length - 1), out _) ||
                    TryDecode(op, oversizedCollection, out _) ||
                    TryDecode(op, new byte[ProbeEnvelope.MaxPayload + 1], out _) ||
                    TryDecode(op, nested, out _))
                    throw new InvalidOperationException($"POC codec boundary failed for {op}.");

                report($"{op} size bytes default/nominal/max={empty.Length}/{encoded.Length}/{maximal.Length}");
                Measure($"{op} encode nominal", () => Encode(op, nominal), report);
                Measure($"{op} decode nominal", () => TryDecode(op, encoded, out var decoded)
                    ? decoded : throw new InvalidOperationException($"Could not decode {op}."), report);
            }
        }

        private static void Measure(string label, Func<object> work, Action<string> report)
        {
            const int iterations = 2048;
            var ticks = new long[iterations];
            object last = work();
            for (var i = 0; i < 64; i++)
                last = work();

            var allocated = GC.GetAllocatedBytesForCurrentThread();
            for (var i = 0; i < ticks.Length; i++)
            {
                var start = Stopwatch.GetTimestamp();
                last = work();
                ticks[i] = Stopwatch.GetTimestamp() - start;
            }

            allocated = GC.GetAllocatedBytesForCurrentThread() - allocated;
            GC.KeepAlive(last);
            Array.Sort(ticks);
            var nanos = 1e9 / Stopwatch.Frequency;
            var allocation = allocated == 0 ? "unreported" : $"{(double)allocated / iterations:F1} B/op";
            report($"{label}: p50={ticks[1023] * nanos:F0} ns p95={ticks[1945] * nanos:F0} ns " +
                $"p99={ticks[2027] * nanos:F0} ns, {allocation}, n={iterations}");
        }
    }
}
