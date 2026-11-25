using System;
using System.IO;
using System.Linq;
using FRFrame.Network.Shared;
using Google.Protobuf;

namespace FRFrame.Network.Probe
{
    /// <summary>Single Protobuf codec for POC-2 application opcodes.</summary>
    public static class SliceCodec
    {
        private static readonly MessageParser<SliceWire> Parser = SliceWire.Parser.WithDiscardUnknownFields(true);

        public static byte[] Write(ProbeOp op, SliceMessage message)
        {
            var wire = new SliceWire
            {
                RequestId = message.RequestId, Error = (uint)message.Error, ReplyTo = (uint)message.ReplyTo,
                Room = message.Room, Player = message.Player, Session = message.Session, Text = message.Text,
                Stream = message.Stream, Generation = message.Generation, Header = message.Header,
                Schema = message.Schema, Hash = message.Hash, Marker = message.Marker, Online = message.Online,
                Capacity = message.Capacity, Epoch = message.Epoch, ConnectionGeneration = message.ConnectionGeneration,
                Token = message.Token, Target = message.Target, DelayMs = message.DelayMs
            };
            wire.MemberIds.AddRange(message.MemberIds);
            return ProbeEnvelope.Write(op, wire.ToByteArray());
        }

        public static bool TryRead(ReadOnlySpan<byte> payload, out SliceMessage message)
        {
            message = null!;
            try
            {
                using var stream = new MemoryStream(payload.ToArray(), false);
                using var input = CodedInputStream.CreateWithLimits(stream, ProbeEnvelope.MaxPayload, 4);
                var wire = Parser.ParseFrom(input);
                if (wire.Error > (uint)SliceError.CallbackTimeout ||
                    (wire.ReplyTo != 0 && (wire.ReplyTo > byte.MaxValue || !SliceMessage.IsClientRequest((ProbeOp)wire.ReplyTo))) ||
                    wire.MemberIds.Count > SliceMessage.MembershipCapacity || wire.Text.Length > 80 || wire.Token.Length > 44)
                    return false;

                message = new SliceMessage
                {
                    RequestId = wire.RequestId, Error = (SliceError)wire.Error, ReplyTo = (ProbeOp)wire.ReplyTo,
                    Room = wire.Room, Player = wire.Player, Session = wire.Session, Text = wire.Text,
                    Stream = wire.Stream, Generation = wire.Generation, Header = wire.Header, Schema = wire.Schema,
                    Hash = wire.Hash, Marker = wire.Marker, MemberIds = wire.MemberIds.ToArray(), Online = wire.Online,
                    Capacity = wire.Capacity, Epoch = wire.Epoch, ConnectionGeneration = wire.ConnectionGeneration,
                    Token = wire.Token, Target = wire.Target, DelayMs = wire.DelayMs
                };
                return true;
            }
            catch (InvalidProtocolBufferException)
            {
                return false;
            }
        }
    }
}
