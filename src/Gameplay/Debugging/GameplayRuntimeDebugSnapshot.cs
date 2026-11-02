using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FRFrame.Gameplay.Debugging
{
    /// <summary>Scene-lifetime read-only capture source; Capture must not advance gameplay time.</summary>
    public interface IGameplayRuntimeDebugSource
    {
        string DisplayName { get; }
        GameplayRuntimeDebugSnapshot CaptureSnapshot();
    }

    /// <summary>Detached lines for one named unit section, including game-specific Build data.</summary>
    public sealed class GameplayDebugSection
    {
        public string Name { get; }
        public IReadOnlyList<string> Lines { get; }

        public GameplayDebugSection(string name, IEnumerable<string> lines)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            Lines = new ReadOnlyCollection<string>(new List<string>(lines ?? throw new ArgumentNullException(nameof(lines))));
        }
    }

    /// <summary>Detached unit identity and section text; no mutable runtime container escapes.</summary>
    public sealed class GameplayDebugUnitSnapshot
    {
        public GameplaySourceId SourceId { get; }
        public string DisplayName { get; }
        public string Group { get; }
        public IReadOnlyList<GameplayDebugSection> Sections { get; }

        public GameplayDebugUnitSnapshot(GameplaySourceId sourceId, string displayName, string group,
            IEnumerable<GameplayDebugSection> sections)
        {
            SourceId = sourceId;
            DisplayName = displayName ?? throw new ArgumentNullException(nameof(displayName));
            Group = group ?? throw new ArgumentNullException(nameof(group));
            Sections = new ReadOnlyCollection<GameplayDebugSection>(
                new List<GameplayDebugSection>(sections ?? throw new ArgumentNullException(nameof(sections))));
        }
    }

    /// <summary>One immutable capture; Freeze in the Editor retains this value without pausing the battle.</summary>
    public sealed class GameplayRuntimeDebugSnapshot
    {
        public long TickVersion { get; }
        public bool IsPaused { get; }
        public IReadOnlyList<GameplayDebugUnitSnapshot> Units { get; }
        public IReadOnlyList<GameplayEventHistoryEntry> RecentEvents { get; }

        public GameplayRuntimeDebugSnapshot(long tickVersion, bool isPaused,
            IEnumerable<GameplayDebugUnitSnapshot> units, IEnumerable<GameplayEventHistoryEntry> recentEvents)
        {
            TickVersion = tickVersion;
            IsPaused = isPaused;
            Units = new ReadOnlyCollection<GameplayDebugUnitSnapshot>(
                new List<GameplayDebugUnitSnapshot>(units ?? throw new ArgumentNullException(nameof(units))));
            RecentEvents = new ReadOnlyCollection<GameplayEventHistoryEntry>(
                new List<GameplayEventHistoryEntry>(recentEvents ?? throw new ArgumentNullException(nameof(recentEvents))));
        }
    }
}
