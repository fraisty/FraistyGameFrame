using System;

namespace FRFrame.Gameplay
{
    /// <summary>A circular area captured in the caller's coordinate space.</summary>
    public readonly struct GameplayArea
    {
        public GameplayPoint Center { get; }
        public float Radius { get; }

        public GameplayArea(GameplayPoint center, float radius)
        {
            if (!float.IsFinite(radius) || radius <= 0f)
                throw new ArgumentOutOfRangeException(nameof(radius));
            Center = center;
            Radius = radius;
        }
    }
}
