#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Network.Shared;
using FRFrame.Network.Protocol.Wire;
using Google.Protobuf;

namespace FRFrame.Network.Protocol
{
    internal static class RoomProtocol
    {
        private static readonly MessageParser<RoomConfigurationWire> ConfigurationParser = RoomConfigurationWire.Parser.WithDiscardUnknownFields(true);
        private static readonly MessageParser<RoomQueryWire> QueryParser = RoomQueryWire.Parser.WithDiscardUnknownFields(true);
        private static readonly MessageParser<RoomPageWire> PageParser = RoomPageWire.Parser.WithDiscardUnknownFields(true);
        private static readonly MessageParser<RoomInfoWire> InfoParser = RoomInfoWire.Parser.WithDiscardUnknownFields(true);
        private static readonly MessageParser<RoomInvitationWire> InvitationParser = RoomInvitationWire.Parser.WithDiscardUnknownFields(true);

        internal static IMessage? Write(OpCode op, RoomPayload? data)
        {
            switch (data)
            {
                case null:
                    return null;
                case RoomConfiguration config:
                    if ((op == OpCode.CreateRoom && !config.HasMetadata && config.Capacity == 0) ||
                        (op == OpCode.Members && !config.HasMetadata))
                        return null;
                    return WriteConfiguration(config.Name, op == OpCode.Members ? 0 : config.Capacity,
                        config.Visibility, config.JoinPolicy, config.Properties);
                case RoomDirectoryQuery query:
                    return new RoomQueryWire { AfterRoomId = query.AfterRoomId, PageSize = query.PageSize,
                        DefinitionFilter = query.DefinitionFilter, NamePrefix = query.NamePrefix };
                case RoomDirectoryPage page:
                    var pageWire = new RoomPageWire { Version = 1, HasMore = page.HasMore, NextAfterId = page.NextAfterId };
                    pageWire.Rooms.AddRange(page.Rooms.Select(WriteSummary));
                    return pageWire;
                case RoomInfo info:
                    return new RoomInfoWire { Summary = WriteSummary(info.Summary), Owner = info.Owner,
                        Configuration = WriteConfiguration(info.Summary.Name, info.Summary.Capacity, info.Visibility,
                            info.Summary.JoinPolicy, info.Properties) };
                case RoomInvitationNotice invitation:
                    return new RoomInvitationWire { RemainingMs = invitation.RemainingMilliseconds };
                default:
                    throw new ArgumentOutOfRangeException(nameof(data));
            }
        }

        internal static bool TryRead(OpCode op, SliceWire wire, ReadOnlySpan<byte> payload, SliceMessage message)
        {
            var roomReply = op == OpCode.Response && RoomWire.IsOperation(message.ReplyTo);
            if (!wire.HasRoomPayload && !roomReply && !RoomWire.IsOperation(op))
                return true;

            // The F3 outer message and nested payloads have canonical encodings. Older opcodes keep their decoder.
            if (!payload.SequenceEqual(wire.ToByteArray()) || (wire.HasRoomPayload && wire.RoomPayload.IsEmpty))
                return false;
            var body = wire.RoomPayload.Span;

            if (roomReply)
            {
                if (message.Player != 0 || message.Text.Length != 0 || message.Stream != 0 || message.Generation != 0 ||
                    message.Header != 0 || message.Schema != 0 || message.Hash != 0 || message.Marker != 0 ||
                    message.MemberIds.Length != 0 || message.Online != 0 || message.Capacity != 0 || message.Token.Length != 0 ||
                    message.Target != 0 || message.DelayMs != 0 ||
                    ((message.ReplyTo is OpCode.ListRooms or OpCode.ListInvitations) ? message.Room != 0 : message.Room <= 0))
                    return false;
                if (message.Error != ErrorCode.None)
                    return !wire.HasRoomPayload && (HasBinding(message) ||
                        (NoBinding(message) && message.Error is ErrorCode.Invalid or ErrorCode.Unauthenticated or ErrorCode.RoomClosing or ErrorCode.ServerDraining));
                if (!HasBinding(message))
                    return false;

                switch (message.ReplyTo)
                {
                    case OpCode.ListRooms:
                    case OpCode.ListInvitations:
                        if (!TryParse(PageParser, body, out var page) || !TryPage(page, message.ReplyTo == OpCode.ListInvitations, out var pageData))
                            return false;
                        message.RoomData = pageData;
                        return true;
                    case OpCode.GetRoomInfo:
                        if (!TryParse(InfoParser, body, out var info) || !TrySummary(info.Summary, false, out var summary) ||
                            summary.RoomId != message.Room || info.Owner <= 0 ||
                            !TryConfiguration(info.Configuration, out var config) || config.Capacity != summary.Capacity ||
                            config.Name != summary.Name || config.JoinPolicy != summary.JoinPolicy)
                            return false;
                        message.RoomData = new RoomInfo(summary, info.Owner, config);
                        return true;
                    case OpCode.ConfigureRoom:
                    case OpCode.InvitePlayer:
                    case OpCode.RevokeInvitation:
                        return !wire.HasRoomPayload;
                    default:
                        return false;
                }
            }

            switch (op)
            {
                case OpCode.CreateRoom:
                case OpCode.ConfigureRoom:
                case OpCode.Members:
                    if (!TryParse(ConfigurationParser, body, out var configuration) || !TryConfiguration(configuration, out var decoded) ||
                        (!decoded.HasMetadata && decoded.Capacity == 0))
                        return false;
                    message.RoomData = decoded;
                    if (op == OpCode.Members)
                        return decoded.Capacity == 0 && ValidMembers(message);
                    break;
                case OpCode.ListRooms:
                case OpCode.ListInvitations:
                    if (!TryParse(QueryParser, body, out var query))
                        return false;
                    message.RoomData = new RoomDirectoryQuery(query.AfterRoomId, query.DefinitionFilter, query.NamePrefix, query.PageSize);
                    break;
                case OpCode.RoomInvitation:
                    if (!TryParse(InvitationParser, body, out var notice) || notice.RemainingMs is <= 0 or > RoomWire.InvitationTtlMs ||
                        message.Room <= 0 || message.Player <= 0 || !HasBinding(message) || message.Error != ErrorCode.None ||
                        message.ReplyTo != OpCode.None || message.Text.Length != 0 || message.Stream != 0 || message.Generation != 0 ||
                        message.Header != 0 || message.Schema != 0 || message.Hash != 0 || message.Marker != 0 ||
                        message.MemberIds.Length != 0 || message.Online != 0 || message.Capacity != 0 ||
                        message.Token.Length != 0 || message.Target != 0 || message.DelayMs != 0)
                        return false;
                    message.RoomData = new RoomInvitationNotice(notice.RemainingMs);
                    return true;
                case OpCode.GetRoomInfo:
                case OpCode.InvitePlayer:
                case OpCode.RevokeInvitation:
                    if (wire.HasRoomPayload)
                        return false;
                    break;
                default:
                    return false;
            }
            return message.IsRequest(op);
        }

        private static bool TryParse<T>(MessageParser<T> parser, ReadOnlySpan<byte> body, out T value) where T : class, IMessage<T>
        {
            value = null!;
            return !body.IsEmpty && BoundedProtobuf.TryParse(parser, body, RoomWire.MaxBodyBytes, out value, out _) &&
                body.SequenceEqual(value.ToByteArray());
        }

        private static bool ValidMembers(SliceMessage message) => message.Room > 0 &&
            GameWire.IsDefinitionId(message.Text) && message.MemberIds.Length is >= 1 and <= SliceMessage.RoomCapacity &&
            message.Capacity >= message.MemberIds.Length && message.Capacity <= SliceMessage.RoomCapacity &&
            message.MemberIds.All(id => id > 0) && message.MemberIds.Distinct().Count() == message.MemberIds.Length &&
            message.MemberIds.Contains(message.Player) && message.Online >= 0 && message.Online < 1 << message.MemberIds.Length &&
            message.Error == ErrorCode.None && message.ReplyTo == OpCode.None && NoBinding(message) &&
            message.Stream == 0 && message.Generation == 0 && message.Header == 0 && message.Schema == 0 && message.Hash == 0 &&
            message.Token.Length == 0 && message.Target == 0 && message.DelayMs == 0;

        private static bool HasBinding(SliceMessage message) => message.Session > 0 && message.Epoch > 0 && message.ConnectionGeneration > 0;
        private static bool NoBinding(SliceMessage message) => message.Session == 0 && message.Epoch == 0 && message.ConnectionGeneration == 0;

        private static RoomConfigurationWire WriteConfiguration(string name, int capacity, RoomVisibility visibility,
            RoomJoinPolicy joinPolicy, IReadOnlyDictionary<string, string> properties)
        {
            var wire = new RoomConfigurationWire { Version = 1, Name = name, Capacity = capacity,
                Visibility = (uint)visibility, JoinPolicy = (uint)joinPolicy };
            foreach (var pair in properties)
                wire.Properties.Add(new RoomPropertyWire { Key = pair.Key, Value = pair.Value });
            return wire;
        }

        private static bool TryConfiguration(RoomConfigurationWire? wire, out RoomConfiguration data)
        {
            data = null!;
            if (wire is null || wire.Version != 1 || wire.Visibility > (uint)RoomVisibility.Hidden ||
                wire.JoinPolicy > (uint)RoomJoinPolicy.InviteOnly || wire.Properties.Count > 3)
                return false;
            var properties = new Dictionary<string, string>(StringComparer.Ordinal);
            var previous = "";
            foreach (var pair in wire.Properties)
            {
                if (string.CompareOrdinal(pair.Key, previous) <= 0)
                    return false;
                properties.Add(pair.Key, pair.Value);
                previous = pair.Key;
            }
            try
            {
                data = new RoomConfiguration(wire.Name, wire.Capacity, (RoomVisibility)wire.Visibility,
                    (RoomJoinPolicy)wire.JoinPolicy, properties);
                return true;
            }
            catch (ArgumentException) { return false; }
        }

        private static RoomSummaryWire WriteSummary(RoomSummary summary) => new RoomSummaryWire
        {
            RoomId = summary.RoomId, Definition = summary.Definition, Name = summary.Name, Capacity = summary.Capacity,
            MemberCount = summary.MemberCount, OnlineCount = summary.OnlineCount, JoinPolicy = (uint)summary.JoinPolicy,
            InvitationOwner = summary.InvitationOwner, InvitationRemainingMs = summary.InvitationRemainingMs
        };

        private static bool TrySummary(RoomSummaryWire? wire, bool invitations, out RoomSummary data)
        {
            data = null!;
            if (wire == null || wire.RoomId <= 0 || !GameWire.IsDefinitionId(wire.Definition) || !RoomWire.IsText(wire.Name, 32) ||
                wire.Capacity is < 1 or > SliceMessage.RoomCapacity || wire.MemberCount < 1 || wire.MemberCount > wire.Capacity ||
                wire.OnlineCount < 0 || wire.OnlineCount > wire.MemberCount || wire.JoinPolicy > (uint)RoomJoinPolicy.InviteOnly ||
                (invitations ? wire.JoinPolicy != (uint)RoomJoinPolicy.InviteOnly || wire.InvitationOwner <= 0 ||
                    wire.InvitationRemainingMs is <= 0 or > RoomWire.InvitationTtlMs : wire.InvitationOwner != 0 || wire.InvitationRemainingMs != 0))
                return false;
            data = new RoomSummary(wire.RoomId, wire.Definition, wire.Name, wire.Capacity, wire.MemberCount,
                wire.OnlineCount, (RoomJoinPolicy)wire.JoinPolicy, wire.InvitationOwner, wire.InvitationRemainingMs);
            return true;
        }

        private static bool TryPage(RoomPageWire wire, bool invitations, out RoomDirectoryPage data)
        {
            data = null!;
            if (wire.Version != 1 || wire.Rooms.Count > 2 || (wire.Rooms.Count == 0 && wire.HasMore))
                return false;
            var items = new List<RoomSummary>(wire.Rooms.Count);
            int previous = 0;
            foreach (var room in wire.Rooms)
            {
                if (!TrySummary(room, invitations, out var summary) || summary.RoomId <= previous)
                    return false;
                items.Add(summary);
                previous = summary.RoomId;
            }
            if (wire.NextAfterId != previous)
                return false;
            data = new RoomDirectoryPage(items, wire.HasMore);
            return true;
        }
    }
}
