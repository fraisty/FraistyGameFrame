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
    public enum GameplayFactionFilter { Any, Allies, Enemies, Neutrals, NonAllied }
    public enum GameplayFactionRelation { Allied, Hostile, Neutral }

    /// <summary>Directed relation evaluated from live faction values. Games own the policy and
    /// may use either actor identity or faction. Return Neutral for unknown pairs when appropriate.</summary>
    public delegate GameplayFactionRelation GameplayFactionResolver(GameplaySourceId source, int sourceFaction,
        GameplaySourceId target, int targetFaction);
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
        /// <summary>Use registered actor positions independently of the collider backend.</summary>
        public bool UseActorCenters { get; }
        private readonly Predicate<GameplaySourceId> _excluded;
        private readonly GameplayFactionResolver _relations;
        public GameplaySpatialSearchRequest(GameplayContext context, GameplaySearchGeometry geometry,
            int sourceFaction, GameplayFactionFilter faction = GameplayFactionFilter.Enemies,
            GameplayTagQuery tags = null, int layerMask = -1, int maxTargets = int.MaxValue,
            GameplayTargetOrder order = GameplayTargetOrder.Nearest, bool includeSelf = false,
            Predicate<GameplaySourceId> excluded = null, bool useActorCenters = false)
            : this(null, context, geometry, sourceFaction, faction, tags, layerMask, maxTargets, order,
                includeSelf, excluded, useActorCenters) { }

        /// <summary>Creates a request with an explicit directed faction resolver.</summary>
        public GameplaySpatialSearchRequest(GameplayFactionResolver relations, GameplayContext context, GameplaySearchGeometry geometry,
            int sourceFaction, GameplayFactionFilter faction = GameplayFactionFilter.Enemies,
            GameplayTagQuery tags = null, int layerMask = -1, int maxTargets = int.MaxValue,
            GameplayTargetOrder order = GameplayTargetOrder.Nearest, bool includeSelf = false,
            Predicate<GameplaySourceId> excluded = null, bool useActorCenters = false)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            Geometry = geometry ?? throw new ArgumentNullException(nameof(geometry));
            if (maxTargets < 1) throw new ArgumentOutOfRangeException(nameof(maxTargets));
            if (!Enum.IsDefined(typeof(GameplayFactionFilter), faction) || !Enum.IsDefined(typeof(GameplayTargetOrder), order))
                throw new ArgumentOutOfRangeException(nameof(faction));
            SourceFaction = sourceFaction; Faction = faction; Tags = tags ?? GameplayTagQuery.Empty;
            LayerMask = layerMask; MaxTargets = maxTargets; Order = order; IncludeSelf = includeSelf;
            _excluded = excluded; _relations = relations; UseActorCenters = useActorCenters;
        }
        public GameplayTargetData Select(IEnumerable<IGameplaySpatialActor> candidates)
        {
            if (candidates == null) throw new ArgumentNullException(nameof(candidates));
            var selected = new List<IGameplaySpatialActor>(); var seen = new HashSet<GameplaySourceId>();
            foreach (var actor in candidates)
            {
                if (!CanTarget(actor, Context.InstigatorId, SourceFaction, Faction, Tags, LayerMask, IncludeSelf, _relations) || !Geometry.Contains(actor.Id, actor.Position) ||
                    _excluded?.Invoke(actor.Id) == true || !seen.Add(actor.Id)) continue;
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

        internal static bool CanTarget(IGameplaySpatialActor actor, GameplaySourceId source, int sourceFaction,
            GameplayFactionFilter faction, GameplayTagQuery tags, int layerMask, bool includeSelf = false,
            GameplayFactionResolver relations = null) =>
            actor != null && actor.Id.IsValid && actor.IsTargetable && actor.Tags != null && actor.Layer >= 0 && actor.Layer <= 31 &&
            (includeSelf || actor.Id != source) && (layerMask & (1 << actor.Layer)) != 0 &&
            MatchesFaction(faction, source, sourceFaction, actor, relations) && tags.Matches(actor.Tags);

        internal static GameplayFactionRelation DefaultRelation(int sourceFaction, int targetFaction) =>
            sourceFaction == targetFaction ? GameplayFactionRelation.Allied : GameplayFactionRelation.Hostile;

        private static bool MatchesFaction(GameplayFactionFilter filter, GameplaySourceId source, int sourceFaction,
            IGameplaySpatialActor target, GameplayFactionResolver resolver)
        {
            if (filter == GameplayFactionFilter.Any) return true;
            var relation = target.Id == source ? GameplayFactionRelation.Allied :
                resolver?.Invoke(source, sourceFaction, target.Id, target.Faction) ?? DefaultRelation(sourceFaction, target.Faction);
            return filter switch
            {
                GameplayFactionFilter.Allies => relation == GameplayFactionRelation.Allied,
                GameplayFactionFilter.Enemies => relation == GameplayFactionRelation.Hostile,
                GameplayFactionFilter.Neutrals => relation == GameplayFactionRelation.Neutral,
                GameplayFactionFilter.NonAllied => relation is GameplayFactionRelation.Hostile or GameplayFactionRelation.Neutral,
                _ => false
            };
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
