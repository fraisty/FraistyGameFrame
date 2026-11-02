using System;

namespace FRFrame.Gameplay.Extensions
{
    /// <summary>World-neutral geometry; Line means a forward length with a full perpendicular width.</summary>
    public enum GameplaySpatialShape { Circle, Sector, Line }
    /// <summary>Finite, normalized world-space direction. Default is invalid; adapters choose their coordinate convention.</summary>
    public readonly struct GameplayDirection
    {
        public float X { get; }
        public float Y { get; }
        public float Z { get; }
        public bool IsValid => X != 0 || Y != 0 || Z != 0;
        public GameplayDirection(float x, float y, float z = 0)
        {
            if (!float.IsFinite(x) || !float.IsFinite(y) || !float.IsFinite(z)) throw new ArgumentOutOfRangeException(nameof(x));
            double length = Math.Sqrt((double)x*x + (double)y*y + (double)z*z);
            if (length == 0) throw new ArgumentException("Direction must be non-zero.");
            X = (float)(x/length); Y = (float)(y/length); Z = (float)(z/length);
        }
    }
    /// <summary>Immutable geometry and context snapshot. Resolver owns current-world filtering and deterministic ordering.</summary>
    public sealed class GameplaySpatialQuery
    {
        public GameplayContext Context { get; }
        public GameplayPoint Origin { get; }
        public GameplayDirection Direction { get; }
        public GameplaySpatialShape Shape { get; }
        public float Radius { get; }
        public float Angle { get; }
        public float Length { get; }
        public float Width { get; }
        public int MaxTargets { get; }
        public GameplaySpatialQuery(GameplayContext context, GameplayPoint origin, GameplayDirection direction,
            GameplaySpatialShape shape, int maxTargets, float radius = 0, float angle = 0, float length = 0, float width = 0)
        {
            Context = context ?? throw new ArgumentNullException(nameof(context));
            if (!direction.IsValid) throw new ArgumentException("Direction must be initialized.", nameof(direction));
            if (!Enum.IsDefined(typeof(GameplaySpatialShape), shape)) throw new ArgumentOutOfRangeException(nameof(shape));
            if (maxTargets <= 0) throw new ArgumentOutOfRangeException(nameof(maxTargets));
            foreach (float value in new[] { radius, angle, length, width })
                if (!float.IsFinite(value) || value < 0) throw new ArgumentOutOfRangeException(nameof(radius));
            if (shape == GameplaySpatialShape.Line ? length <= 0 || width <= 0 || radius != 0 || angle != 0 :
                radius <= 0 || length != 0 || width != 0 || (shape == GameplaySpatialShape.Circle ? angle != 0 : angle <= 0 || angle > 360))
                throw new ArgumentException("Provide exactly the dimensions of the selected shape.");
            Origin = origin; Direction = direction; Shape = shape; MaxTargets = maxTargets;
            Radius = radius; Angle = angle; Length = length; Width = width;
        }
    }
    /// <summary>Returns unique valid IDs in deterministic order, at most MaxTargets. No Unity types cross this boundary.</summary>
    public interface IGameplaySpatialQueryResolver
    {
        GameplayTargetData Resolve(GameplaySpatialQuery query);
    }
}
