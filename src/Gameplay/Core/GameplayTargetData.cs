using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay
{
    /// <summary>The optional primary target and point captured for one execution.</summary>
    public sealed class GameplayTargetData
    {
        public GameplaySourceId PrimaryTargetId { get; }
        public bool HasPrimaryTarget => PrimaryTargetId.IsValid;
        public GameplayPoint Point { get; }
        public bool HasPoint { get; }
        public GameplayArea Area { get; }
        public bool HasArea { get; }
        /// <summary>Resolved target IDs in deterministic execution order; empty means no multi-target result.</summary>
        public IReadOnlyList<GameplaySourceId> Targets { get; }

        public GameplayTargetData(GameplaySourceId primaryTargetId, GameplayPoint? point = null)
            : this(primaryTargetId, point, null, null) { }

        public GameplayTargetData(GameplaySourceId primaryTargetId, GameplayPoint? point,
            GameplayArea? area, IEnumerable<GameplaySourceId> targets)
        {
            PrimaryTargetId = primaryTargetId;
            HasPoint = point.HasValue;
            Point = point.GetValueOrDefault();
            HasArea = area.HasValue;
            Area = area.GetValueOrDefault();
            var copy = targets == null ? new List<GameplaySourceId>() : new List<GameplaySourceId>(targets);
            if (targets != null && primaryTargetId.IsValid && !copy.Contains(primaryTargetId))
                throw new ArgumentException("Primary target must belong to resolved targets.", nameof(targets));
            foreach (GameplaySourceId id in copy)
                if (!id.IsValid) throw new ArgumentException("Target IDs must be valid.", nameof(targets));
            if (new HashSet<GameplaySourceId>(copy).Count != copy.Count)
                throw new ArgumentException("Resolved target IDs must be unique.", nameof(targets));
            Targets = copy.AsReadOnly();
        }
    }
}
