using System;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Publishes host-authored phases to the owner's existing dispatcher. The host
    /// orders phases and may publish multiple phases at one rule time. Only StepRules advances the world.</summary>
    public sealed class GameplayPhaseBridge
    {
        public static readonly GameplayTag TurnIndex = new GameplayTag("Data.Standard.Phase.TurnIndex");
        private readonly GameplayStandardWorld _world;
        private readonly GameplaySourceId _source;

        public GameplayPhaseBridge(GameplayStandardWorld world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _source = world.Sources.Allocate();
        }

        /// <summary>Publishes one occurrence. Duplicate phases are meaningful at this layer;
        /// the host owns turn order and prevents unintended repeated input.</summary>
        public void Publish(GameplayStandardActor owner, int turn, GameplayTag phase,
            GameplayTargetData target = null, GameplayContextValues values = null)
        {
            if (owner == null || owner.World != _world || owner.IsDisposed)
                throw new ArgumentException("Phase owner must belong to this world.", nameof(owner));
            if (turn < 1) throw new ArgumentOutOfRangeException(nameof(turn));
            var context = new GameplayContext(owner.Id, target ?? new GameplayTargetData(owner.Id, owner.Position), _source, values);
            owner.Events.Publish(new GameplayEventData(phase, context, turn, GameplayContextValues.Empty.WithValue(TurnIndex, turn)));
        }

        /// <summary>Use the world's single rule clock, in the unit chosen by the host.</summary>
        public void StepRules(float amount) => _world.Advance(amount);
    }
}
