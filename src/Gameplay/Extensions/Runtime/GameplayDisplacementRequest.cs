using System;

namespace FRFrame.Gameplay.Extensions
{
    /// <summary>Source-relative or explicit-direction movement; physical trajectory and collision belong to the driver.</summary>
    public enum GameplayDisplacementMode { PushFromSource, PullToSource, Directional }
    /// <summary>Movement request: distance and speed are the only rate model. Game driver resolves source/target positions at Start.</summary>
    public sealed class GameplayDisplacementRequest
    {
        public GameplayContext Context { get; }
        public GameplaySourceId EffectiveSourceId => Context.EffectiveSourceId;
        public GameplaySourceId TargetId { get; }
        public GameplayDisplacementMode Mode { get; }
        public GameplayDirection? Direction { get; }
        public float Distance { get; }
        public float Speed { get; }
        public GameplayDisplacementRequest(GameplayContext context, GameplaySourceId targetId, GameplayDisplacementMode mode,
            float distance, float speed, GameplayDirection? direction = null)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            if (!targetId.IsValid) throw new ArgumentException("Target must be valid.", nameof(targetId));
            if (!Enum.IsDefined(typeof(GameplayDisplacementMode), mode)) throw new ArgumentOutOfRangeException(nameof(mode));
            if (!float.IsFinite(distance) || distance <= 0 || !float.IsFinite(speed) || speed <= 0) throw new ArgumentOutOfRangeException(nameof(distance));
            if (mode == GameplayDisplacementMode.Directional ? !direction.HasValue || !direction.Value.IsValid : direction.HasValue)
                throw new ArgumentException("Only Directional requires an explicit initialized direction.", nameof(direction));
            TargetId = targetId; Mode = mode; Distance = distance; Speed = speed; Direction = direction;
        }
    }
    /// <summary>Game-owned physical simulation. Returned lifetime supports cancellation; no Effect/Ability/Sequence clock is advanced here.</summary>
    public interface IGameplayDisplacementDriver
    {
        IDisposable Start(GameplayDisplacementRequest request);
    }
}
