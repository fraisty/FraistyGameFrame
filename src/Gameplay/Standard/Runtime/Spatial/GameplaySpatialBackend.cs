using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay.Standard
{
    public interface IGameplaySpatialActor
    {
        GameplaySourceId Id { get; }
        GameplayPoint Position { get; }
        GameplayTagContainer Tags { get; }
        int Faction { get; }
        int Layer { get; }
        bool IsTargetable { get; }
    }
    public enum GameplayFactionFilter { Any, Allies, Enemies }
    public enum GameplayTargetOrder { Nearest, Farthest, SourceId }

    /// <summary>Shared deterministic target policy; Physics and logical backends only supply candidates.
    /// Tag queries are hierarchical, layer 0 is valid, and deduplication precedes the target limit.</summary>
    public sealed class GameplaySpatialSearchRequest
    {
        public GameplayContext Context { get; }
        public GameplaySearchGeometry Geometry { get; }
        public GameplayTagQuery Tags { get; }
        public GameplayFactionFilter Faction { get; }
        public int SourceFaction { get; }
        public int LayerMask { get; }
        public int MaxTargets { get; }
        public GameplayTargetOrder Order { get; }
        public bool IncludeSelf { get; }
        public GameplaySpatialSearchRequest(GameplayContext context, GameplaySearchGeometry geometry,
            int sourceFaction, GameplayFactionFilter faction = GameplayFactionFilter.Enemies,
            GameplayTagQuery tags = null, int layerMask = -1, int maxTargets = int.MaxValue,
            GameplayTargetOrder order = GameplayTargetOrder.Nearest, bool includeSelf = false)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            if (maxTargets < 1) throw new ArgumentOutOfRangeException(nameof(maxTargets));
            if (!Enum.IsDefined(typeof(GameplayFactionFilter), faction) || !Enum.IsDefined(typeof(GameplayTargetOrder), order))
                throw new ArgumentOutOfRangeException(nameof(faction));
            SourceFaction = sourceFaction; Faction = faction; Tags = tags ?? GameplayTagQuery.Empty;
            LayerMask = layerMask; MaxTargets = maxTargets; Order = order; IncludeSelf = includeSelf;
        }
        public GameplayTargetData Select(IEnumerable<IGameplaySpatialActor> candidates)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            var selected = new List<IGameplaySpatialActor>(); var seen = new HashSet<GameplaySourceId>();
            foreach (var actor in candidates)
            {
                if (actor == null || !actor.Id.IsValid || !actor.IsTargetable || actor.Tags == null || actor.Layer < 0 || actor.Layer > 31 ||
                    (!IncludeSelf && actor.Id == Context.InstigatorId) || (LayerMask & (1 << actor.Layer)) == 0 ||
                    (Faction == GameplayFactionFilter.Allies && actor.Faction != SourceFaction) ||
                    (Faction == GameplayFactionFilter.Enemies && actor.Faction == SourceFaction) ||
                    !Tags.Matches(actor.Tags) || !Geometry.Contains(actor.Id, actor.Position) || !seen.Add(actor.Id)) continue;
                selected.Add(actor);
            }
            selected.Sort((a, b) =>
            {
                int distance = Order == GameplayTargetOrder.SourceId ? 0 :
                    GameplaySearchGeometry.DistanceSquared(Geometry.Origin, a.Position).CompareTo(GameplaySearchGeometry.DistanceSquared(Geometry.Origin, b.Position));
                if (Order == GameplayTargetOrder.Farthest) distance = -distance;
                return distance != 0 ? distance : a.Id.Value.CompareTo(b.Id.Value);
            });
            var ids = new List<GameplaySourceId>();
            for (int i = 0; i < selected.Count && i < MaxTargets; i++) ids.Add(selected[i].Id);
            GameplayArea? area = Geometry.Shape == GameplaySearchShape.Circle || Geometry.Shape == GameplaySearchShape.Sector ?
                new GameplayArea(Geometry.Origin, Geometry.Radius) : (GameplayArea?)null;
            return new GameplayTargetData(ids.Count > 0 ? ids[0] : default, Geometry.Origin, area, ids);
        }
    }
    public interface IGameplaySpatialQueryBackend { GameplayTargetData Query(GameplaySpatialSearchRequest request); }

    /// <summary>Out-of-box live logical/grid query; no Unity object, collider or Update dependency.</summary>
    public sealed class GameplayLogicalSpatialBackend : IGameplaySpatialQueryBackend
    {
        private readonly Func<IEnumerable<IGameplaySpatialActor>> _actors;
        public GameplayLogicalSpatialBackend(Func<IEnumerable<IGameplaySpatialActor>> actors) => _actors = actors ?? throw new ArgumentNullException(nameof(actors));
        public GameplayTargetData Query(GameplaySpatialSearchRequest request) =>
            (request ?? throw new ArgumentNullException(nameof(request))).Select(_actors());
    }
}
