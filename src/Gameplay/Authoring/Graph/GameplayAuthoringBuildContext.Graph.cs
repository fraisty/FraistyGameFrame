using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Authoring.Graph;

namespace FRFrame.Gameplay.Authoring
{
    public sealed partial class GameplayAuthoringBuildContext
    {
        internal Func<GameplayDefinitionAsset, object, string, Type, object> GraphDefinitionResolver;
        internal Func<object, string, bool> GraphBindingExists;
        private readonly Dictionary<GameplayLocalDefinitionPayload, object> _localValues =
            new Dictionary<GameplayLocalDefinitionPayload, object>();

        /// <summary>Resolves an embedded graph binding when compiling V2, or the explicitly
        /// shared asset's ordinary builder. Local graph definitions never require SO proxies.</summary>
        public T ResolveDefinition<T>(GameplayDefinitionAsset legacy, object owner, string slot, Func<T> sharedBuilder) where T : class
        {
            if (GraphDefinitionResolver != null)
            {
                object resolved = GraphDefinitionResolver(legacy, owner, slot, typeof(T));
                if (resolved != null) return (T)resolved;
            }
            if (legacy == null) throw new InvalidOperationException("Missing definition binding: " + slot);
            return sharedBuilder();
        }

        /// <summary>Optional resource bindings remain absent unless explicitly configured.</summary>
        public T ResolveOptionalDefinition<T>(GameplayDefinitionAsset legacy, object owner, string slot, Func<T> sharedBuilder) where T : class =>
            legacy == null && !(GraphBindingExists?.Invoke(owner, slot) ?? false) ? null : ResolveDefinition(legacy, owner, slot, sharedBuilder);

        /// <summary>Identity cache for plain embedded definition builders. The graph compiler
        /// validates the concrete payload and detects cycles before entering this factory.</summary>
        public T Build<T>(GameplayLocalDefinitionPayload payload, Func<T> factory) where T : class
        {
            if (_localValues.TryGetValue(payload, out var existing)) return (T)existing;
            T value = factory();
            if (value == null) throw new InvalidOperationException("A local definition builder returned null.");
            _localValues.Add(payload, value);
            return value;
        }

        /// <summary>Records the graph entity's persistent provenance for actual runtime actions.
        /// Migrated nodes retain their original DefinitionId/managed-reference identity.</summary>
        public void RegisterGraphActionSource(IGameplaySkillAction action, string sourceId, long actionId)
        {
            if (action == null || string.IsNullOrEmpty(sourceId) || actionId < 0)
                throw new ArgumentException("A graph action requires a valid persistent source.");
            _actionSources[action] = new GameplaySkillTraceSource(sourceId, actionId);
        }
    }
}
