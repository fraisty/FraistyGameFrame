using FRFrame.Gameplay.Standard;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Maps the actual Standard Actor combat resources without writing to gameplay.</summary>
    public static class GameplayWorldHealth
    {
        public static WorldHealthSnapshot Read(GameplayStandardActor actor)
        {
            var map = actor?.CombatAttributes;
            if (map == null || !actor.IsTargetable || !actor.Resources.TryGetBalance(map.Health,out float health))
                return default;
            float shield = 0;
            if (map.Shield.IsValid) actor.Resources.TryGetBalance(map.Shield,out shield);
            return new WorldHealthSnapshot(health,actor.Attributes.GetCurrentValue(map.MaxHealth),shield);
        }
    }
}
