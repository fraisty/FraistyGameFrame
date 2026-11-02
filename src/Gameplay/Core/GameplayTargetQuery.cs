using System;

namespace FRFrame.Gameplay
{
    /// <summary>Area search request only; a game-layer resolver reads the world and returns target IDs.</summary>
    public readonly struct GameplayTargetQuery
    {
        public GameplayArea Area { get; }
        /// <summary>Maximum resolved targets; a positive value is always required.</summary>
        public int MaxTargets { get; }

        public GameplayTargetQuery(GameplayArea area, int maxTargets)
        {
            if (area.Radius <= 0f) throw new ArgumentException("Area must be initialized.", nameof(area));
            if (maxTargets <= 0) throw new ArgumentOutOfRangeException(nameof(maxTargets));
            Area = area;
            MaxTargets = maxTargets;
        }
    }
}
