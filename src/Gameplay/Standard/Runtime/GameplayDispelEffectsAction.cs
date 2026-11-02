using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Dispels classified effects on explicitly selected targets using their existing
    /// containers. Classification uses EffectTags, not granted state tags. Removal is a Dispel,
    /// so it never runs natural-expiry continuations. Empty selection never falls back to self.</summary>
    public sealed class GameplayDispelEffectsAction : IGameplaySkillAction
    {
        public GameplayEffectQuery Query { get; }

        public GameplayDispelEffectsAction(GameplayEffectQuery query)
        {
            Query = query ?? throw new ArgumentNullException(nameof(query));
            if (query.EffectTags.All.Count == 0 && query.EffectTags.Any.Count == 0)
                throw new ArgumentException("Select at least one effect classification to dispel.", nameof(query));
        }

        public GameplaySkillActionResult Execute(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host)
        {
            if (host is GameplayStandardActor disposed && disposed.IsDisposed) return GameplaySkillActionResult.Rejected;
            var ids = context.TargetData.Targets.Count > 0 ? context.TargetData.Targets :
                context.TargetData.HasPrimaryTarget ? new[] { context.TargetData.PrimaryTargetId } : Array.Empty<GameplaySourceId>();
            if (ids.Count == 0) return GameplaySkillActionResult.Rejected;
            var containers = new List<GameplayEffectContainer>();
            var visited = new HashSet<GameplayEffectContainer>();
            foreach (var id in ids)
            {
                GameplayEffectContainer effects;
                if (id == host.OwnerId) effects = host.Effects;
                else if (host is GameplayStandardActor actor && !actor.IsDisposed && actor.World.TryGetActor(id, out var target))
                    effects = target.Effects;
                else return GameplaySkillActionResult.Rejected;
                if (visited.Add(effects)) containers.Add(effects);
            }
            // Resolve all targets before changing any container. RemoveMatching snapshots its
            // own handles; effects created by removal observers are not recursively dispelled.
            foreach (var container in containers) container.RemoveMatching(Query, GameplayEffectRemovalCause.Dispel);
            return GameplaySkillActionResult.Succeeded;
        }
    }
}
