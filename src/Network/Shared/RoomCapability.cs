#nullable enable
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;

namespace FRFrame.Network.Shared
{
    public enum RoomVisibility { Discoverable, Hidden }
    public enum RoomJoinPolicy { Open, InviteOnly }

    /// <summary>Typed framework Room data. Each opcode admits only its own payload shape.</summary>
    public abstract class RoomPayload
    {
        internal RoomPayload() { }
    }

    /// <summary>Immutable room metadata. Capacity zero selects the server default on Create only.</summary>
    public sealed class RoomConfiguration : RoomPayload
    {
        public static RoomConfiguration Default { get; } = new();

        public RoomConfiguration(string name = "", int capacity = 0,
            RoomVisibility visibility = RoomVisibility.Discoverable, RoomJoinPolicy joinPolicy = RoomJoinPolicy.Open,
            IReadOnlyDictionary<string, string>? properties = null)
        {
            if (!RoomWire.IsText(name, 32) || capacity < 0 || capacity > SliceMessage.RoomCapacity ||
                visibility is not (RoomVisibility.Discoverable or RoomVisibility.Hidden) ||
                joinPolicy is not (RoomJoinPolicy.Open or RoomJoinPolicy.InviteOnly) || properties?.Count > 3)
                throw new ArgumentException("Invalid room configuration.");

            var frozen = new SortedDictionary<string, string>(StringComparer.Ordinal);
            if (properties != null)
                foreach (var entry in properties)
                {
                    if (entry.Key is not { Length: >= 1 and <= 12 } || !GameWire.IsDefinitionId(entry.Key) ||
                        !RoomWire.IsText(entry.Value, 24) || frozen.ContainsKey(entry.Key) || frozen.Count == 3)
                        throw new ArgumentException("Invalid room property.");
                    frozen.Add(entry.Key, entry.Value);
                }

            Name = name;
            Capacity = capacity;
            Visibility = visibility;
            JoinPolicy = joinPolicy;
            Properties = new ReadOnlyDictionary<string, string>(frozen);
        }

        public string Name { get; }
        public int Capacity { get; }
        public RoomVisibility Visibility { get; }
        public RoomJoinPolicy JoinPolicy { get; }
        public IReadOnlyDictionary<string, string> Properties { get; }
        public bool HasMetadata => Name.Length != 0 || Properties.Count != 0 ||
            Visibility != RoomVisibility.Discoverable || JoinPolicy != RoomJoinPolicy.Open;

        public bool SameMetadata(RoomConfiguration other) => Name == other.Name && Visibility == other.Visibility &&
            JoinPolicy == other.JoinPolicy && Properties.SequenceEqual(other.Properties);
    }

    public sealed class RoomDirectoryQuery : RoomPayload
    {
        public RoomDirectoryQuery(int afterRoomId = 0, string? definitionFilter = null, string? namePrefix = null, int pageSize = 2)
        {
            AfterRoomId = afterRoomId;
            DefinitionFilter = definitionFilter ?? "";
            NamePrefix = namePrefix ?? "";
            PageSize = pageSize;
        }

        public int AfterRoomId { get; }
        public string DefinitionFilter { get; }
        public string NamePrefix { get; }
        public int PageSize { get; }
        public bool IsValid(bool invitations) => AfterRoomId >= 0 && PageSize is >= 1 and <= 2 &&
            (invitations ? DefinitionFilter.Length == 0 && NamePrefix.Length == 0 :
                (DefinitionFilter.Length == 0 || GameWire.IsDefinitionId(DefinitionFilter)) && RoomWire.IsText(NamePrefix, 32));
    }

    /// <summary>Directory projection. Invitation fields are present only on the target's invitation page.</summary>
    public sealed class RoomSummary
    {
        public RoomSummary(int roomId, string definition, string name, int capacity, int memberCount, int onlineCount,
            RoomJoinPolicy joinPolicy, int invitationOwner = 0, int invitationRemainingMs = 0)
        {
            RoomId = roomId;
            Definition = definition;
            Name = name;
            Capacity = capacity;
            MemberCount = memberCount;
            OnlineCount = onlineCount;
            JoinPolicy = joinPolicy;
            InvitationOwner = invitationOwner;
            InvitationRemainingMs = invitationRemainingMs;
        }

        public int RoomId { get; }
        public string Definition { get; }
        public string Name { get; }
        public int Capacity { get; }
        public int MemberCount { get; }
        public int OnlineCount { get; }
        public RoomJoinPolicy JoinPolicy { get; }
        public int InvitationOwner { get; }
        public int InvitationRemainingMs { get; }
    }

    /// <summary>A historical page. Its cursor is the last returned RoomId, or zero for an empty page.</summary>
    public sealed class RoomDirectoryPage : RoomPayload
    {
        public RoomDirectoryPage(IEnumerable<RoomSummary> rooms, bool hasMore)
        {
            Rooms = Array.AsReadOnly(rooms.ToArray());
            HasMore = hasMore;
        }
        public IReadOnlyList<RoomSummary> Rooms { get; }
        public bool HasMore { get; }
        public int NextAfterId => Rooms.Count == 0 ? 0 : Rooms[Rooms.Count - 1].RoomId;
    }

    public sealed class RoomInfo : RoomPayload
    {
        public RoomInfo(RoomSummary summary, int owner, RoomConfiguration configuration)
        {
            Summary = summary;
            Owner = owner;
            Visibility = configuration.Visibility;
            Properties = configuration.Properties;
        }
        public RoomSummary Summary { get; }
        public int Owner { get; }
        public RoomVisibility Visibility { get; }
        public IReadOnlyDictionary<string, string> Properties { get; }
    }

    /// <summary>Owned, encoded result retained by the server replay ledger. Client decoding exposes typed data.</summary>
    public sealed class EncodedRoomResult : RoomPayload
    {
        private readonly byte[] _bytes;
        public EncodedRoomResult(ReadOnlySpan<byte> bytes)
        {
            if (bytes.IsEmpty || bytes.Length > RoomWire.MaxBodyBytes)
                throw new ArgumentOutOfRangeException(nameof(bytes));
            _bytes = bytes.ToArray();
        }
        public ReadOnlySpan<byte> Bytes => _bytes;
    }

    /// <summary>Advisory lifetime reported when the invitation was sent, not a join credential.</summary>
    public sealed class RoomInvitationNotice : RoomPayload
    {
        public RoomInvitationNotice(int remainingMilliseconds) => RemainingMilliseconds = remainingMilliseconds;
        public int RemainingMilliseconds { get; }
    }

    public static class RoomWire
    {
        public const int MaxBodyBytes = 320;
        public const int InvitationTtlMs = 60_000;
        public const int InvitationLimit = 8;
        private static readonly UTF8Encoding StrictUtf8 = new(false, true);

        public static bool IsOperation(OpCode op) => op is >= OpCode.ListRooms and <= OpCode.RoomInvitation;
        public static bool IsQuery(OpCode op) => op is OpCode.ListRooms or OpCode.GetRoomInfo or OpCode.ListInvitations;

        public static bool IsText(string? value, int maxBytes)
        {
            if (value == null || value.Length > maxBytes || value.Any(char.IsControl))
                return false;
            try { return StrictUtf8.GetByteCount(value) <= maxBytes; }
            catch (EncoderFallbackException) { return false; }
        }
    }
}
