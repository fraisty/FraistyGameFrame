using System;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Optional host behaviour port. State names are explicitly registered by the integration;
    /// rule actions never construct a state machine or discover state types by reflection.</summary>
    public interface IGameplayBehaviourDriver : IDisposable
    {
        string CurrentState { get; }
        bool TryChangeState(string state);
        void Advance(float amount);
    }

    /// <summary>Requests an existing actor behaviour transition. Rejection leaves the host state intact.</summary>
    public sealed class GameplayChangeStateAction : IGameplaySkillAction
    {
        private readonly string _state;
        public GameplayChangeStateAction(string state)
        { if (string.IsNullOrWhiteSpace(state)) throw new ArgumentException("State is required.", nameof(state)); _state=state; }
        public GameplaySkillActionResult Execute(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host)
            => host is GameplayStandardActor actor && actor.Behaviour != null && actor.Behaviour.TryChangeState(_state)
                ? GameplaySkillActionResult.Succeeded : GameplaySkillActionResult.Rejected;
    }
}
