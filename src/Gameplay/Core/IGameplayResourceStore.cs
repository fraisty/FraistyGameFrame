using System.Collections.Generic;

namespace FRFrame.Gameplay
{
    /// <summary>Battle-only resource balances keyed by the same canonical IDs used in authoring.
    /// AttributeSet is the default implementation. Games with existing HP/Mana fields can adapt those
    /// exact fields instead of maintaining a second attribute/resource balance.</summary>
    public interface IGameplayResourceStore
    {
        bool TryGetBalance(GameplayAttributeId id, out float balance);
        /// <summary>Preflight every finite balance and bound before committing; exceptions must leave
        /// all balances unchanged. Used by multi-resource Cost and HP/Shield hit settlement.</summary>
        void SetBalancesAtomic(IReadOnlyDictionary<GameplayAttributeId, float> balances);
    }
    /// <summary>Optional actual resource capability; no persistent inventory or game type crosses Core.</summary>
    public interface IGameplayResourceTarget : IGameplayEffectExecutionTarget
    {
        IGameplayResourceStore Resources { get; }
    }
}
