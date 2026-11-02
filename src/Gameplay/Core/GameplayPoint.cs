using System;

namespace FRFrame.Gameplay
{
    /// <summary>A captured point in the caller's coordinate space; the core does not interpret its units.</summary>
    public readonly struct GameplayPoint
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }

        public GameplayPoint(float x, float y, float z = 0f)
        {
            if (float.IsNaN(x) || float.IsInfinity(x) || float.IsNaN(y) || float.IsInfinity(y) ||
                float.IsNaN(z) || float.IsInfinity(z))
                throw new ArgumentOutOfRangeException(nameof(x), "Point coordinates must be finite.");
            X = x;
            Y = y;
            Z = z;
        }
    }
}
