using System;

namespace FRFrame.Gameplay
{
    /// <summary>Standard Instant/periodic scalar change to the target's BaseValue. The shared value
    /// expression is evaluated with the real application context; no mutable state lives in this execution.
    /// HP clamp/death/damage pipeline semantics are separate policies, not implied by this generic change.</summary>
    public sealed class GameplayAttributeDeltaExecution : IGameplayEffectExecution
    {
        public GameplayAttributeId Attribute { get; }
        public IGameplayFloatValue Delta { get; }
        public GameplayAttributeDeltaExecution(GameplayAttributeId attribute, IGameplayFloatValue delta)
        {
            if (!attribute.IsValid) throw new ArgumentException("Attribute must be valid.", nameof(attribute));
            Attribute = attribute;
            Delta = delta ?? throw new ArgumentNullException(nameof(delta));
        }
        public void Execute(GameplayEffectApplicationContext application)
        {
            if (application == null) throw new ArgumentNullException(nameof(application));
            var attribute = application.Target.Attributes.GetAttribute(Attribute);
            attribute.SetBaseValue(attribute.BaseValue + GameplayValueEvaluator.Evaluate(Delta, application));
        }
    }
}
