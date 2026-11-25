#nullable enable
using System;
using FRFrame.Network.Protocol.Wire;
using FRFrame.Network.Shared;
using Google.Protobuf;

namespace FRFrame.Network.Protocol
{
    /// <summary>Runs identical wire fixtures in the .NET Host, Unity Editor and Player.</summary>
    public static class ProtocolChecks
    {
        public static void Run(Action<string> log)
        {
            const uint correlation = 0x12345678;
            var packet = SliceCodec.Write(OpCode.RoomMessage, new SliceMessage
            {
                RequestId = correlation, Room = 1, Text = "ok"
            });
            var golden = new byte[] { 0, 2, 9, 6, 0, 0x78, 0x56, 0x34, 0x12, 0x20, 1, 0x3A, 2, 0x6F, 0x6B };
            Require(packet.AsSpan().SequenceEqual(golden), "stable envelope and Protobuf field numbers");
            Require(SliceCodec.TryReadPacket(golden, MessageDirection.ClientToServer, PacketDelivery.ReliableOrdered,
                out var header, out var request, out _) && header.RequestId.Value == correlation &&
                header.Descriptor.Response == OpCode.Response && header.Descriptor.RequiresSession &&
                request.RequestId == correlation && request.Room == 1 && request.Text == "ok", "request fixture");

            var reply = SliceCodec.Write(OpCode.Response, new SliceMessage
            {
                RequestId = request.RequestId, ReplyTo = header.Op, Error = ErrorCode.Overloaded
            });
            Require(SliceCodec.TryReadPacket(reply, MessageDirection.ServerToClient, PacketDelivery.ReliableOrdered,
                out _, out var response, out _) && response.RequestId == correlation &&
                response.ReplyTo == OpCode.RoomMessage && response.Error == ErrorCode.Overloaded, "response correlation");

            Reject(golden.AsSpan(0, golden.Length - 1), ErrorCode.MalformedMessage);
            Reject(golden, ErrorCode.WrongDirection, MessageDirection.ServerToClient);
            Reject(golden, ErrorCode.WrongDelivery, delivery: PacketDelivery.Unreliable);
            packet[1] = 1;
            Reject(packet, ErrorCode.UnsupportedProtocolVersion);
            packet[1] = 2;
            foreach (var op in new byte[] { 2, 4, 255 })
            {
                packet[2] = op;
                Reject(packet, ErrorCode.UnknownOpCode);
            }
            packet[2] = (byte)OpCode.RoomMessage;
            packet.AsSpan(5, 4).Clear();
            Reject(packet, ErrorCode.MalformedMessage);
            packet[2] = (byte)OpCode.RoomBroadcast;
            packet[5] = 1;
            Reject(packet, ErrorCode.MalformedMessage, MessageDirection.ServerToClient);
            Reject(new byte[Envelope.MaxPacket + 1], ErrorCode.PayloadTooLarge);
            var oversizedMarker = Envelope.Write(OpCode.RoomMessage, new byte[65], new RequestId(correlation));
            oversizedMarker[2] = (byte)OpCode.SetMarker;
            Reject(oversizedMarker, ErrorCode.PayloadTooLarge);

            var unknown = new byte[] { 0x20, 1, 0x3A, 2, 0x6F, 0x6B, 0xF8, 0x01, 1 };
            var extension = Envelope.Write(OpCode.RoomMessage, unknown, new RequestId(correlation));
            Require(SliceCodec.TryReadPacket(extension, MessageDirection.ClientToServer, PacketDelivery.ReliableOrdered,
                out _, out var decoded, out _) && SliceCodec.Write(OpCode.RoomMessage, decoded).AsSpan().SequenceEqual(golden),
                "known fields survive a valid unknown field");

            foreach (var malformed in new[]
            {
                new byte[] { 0 }, new byte[] { 0x0F }, new byte[] { 0x3A, 9, 0x61 },
                new byte[] { 0x20, 0x80 }, new byte[] { 0x3A, 1, 0xFF }
            })
                Reject(Envelope.Write(OpCode.RoomMessage, malformed, new RequestId(correlation)), ErrorCode.MalformedMessage);
            var nested = new byte[(BoundedProtobuf.MaxDepth + 1) * 4];
            for (var index = 0; index < nested.Length; index += 2)
            {
                nested[index] = index < nested.Length / 2 ? (byte)0xFB : (byte)0xFC;
                nested[index + 1] = 1;
            }
            Reject(Envelope.Write(OpCode.RoomMessage, nested, new RequestId(correlation)), ErrorCode.MalformedMessage);

            var memberWire = new SliceWire();
            memberWire.MemberIds.AddRange(new[] { 1, 2, 3, 4 });
            var members = Envelope.Write(OpCode.Members, memberWire.ToByteArray());
            Require(SliceCodec.TryReadPacket(members, MessageDirection.ServerToClient, PacketDelivery.ReliableOrdered,
                out _, out var roster, out _) && roster.MemberIds.Length == 4, "max roster");
            memberWire.MemberIds.Add(5);
            Reject(Envelope.Write(OpCode.Members, memberWire.ToByteArray()), ErrorCode.PayloadTooLarge, MessageDirection.ServerToClient);
            request.Text = new string('网', 80);
            Require(SliceCodec.TryReadPacket(SliceCodec.Write(OpCode.RoomMessage, request),
                MessageDirection.ClientToServer, PacketDelivery.ReliableOrdered, out _, out _, out _), "max multibyte text");
            request.Text += "x";
            Reject(SliceCodec.Write(OpCode.RoomMessage, request), ErrorCode.PayloadTooLarge);
            var recover = new SliceMessage { RequestId = correlation, Token = new string('x', SliceCodec.MaxTicketBytes) };
            Require(SliceCodec.TryReadPacket(SliceCodec.Write(OpCode.Recover, recover),
                MessageDirection.ClientToServer, PacketDelivery.ReliableOrdered, out _, out _, out _), "max ticket bytes");
            recover.Token += "x";
            Reject(SliceCodec.Write(OpCode.Recover, recover), ErrorCode.PayloadTooLarge);
            var probe = new ProbeSample { Text = new string('x', ProbeCodec.MaxTextBytes), Samples = new byte[ProbeCodec.MaxSamples] };
            Require(ProbeCodec.TryDecode(ProbeCodec.Encode(probe), out var decodedProbe) && ProbeCodec.Matches(probe, decodedProbe),
                "max probe text and samples");
            probe.Samples = new byte[ProbeCodec.MaxSamples + 1];
            Require(!ProbeCodec.TryDecode(ProbeCodec.Encode(probe), out _), "probe sample limit");

            log("P1 protocol checks PASS: golden wire, request correlation, compatibility and decode limits.");
        }

        private static void Reject(ReadOnlySpan<byte> packet, ErrorCode expected,
            MessageDirection direction = MessageDirection.ClientToServer, PacketDelivery delivery = PacketDelivery.ReliableOrdered)
        {
            Require(!SliceCodec.TryReadPacket(packet, direction, delivery, out _, out _, out var error) && error == expected,
                $"expected {expected}, got {error}");
        }

        private static void Require(bool passed, string name)
        {
            if (!passed)
                throw new InvalidOperationException($"P1 protocol check failed: {name}.");
        }
    }
}
