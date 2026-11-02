using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Extensions;

namespace FRFrame.Gameplay.Standard
{
    public enum GameplaySearchShape { Circle, Sector, Box, Line, Ray, Capsule, Single, GridCells }
    public enum GameplaySpatialPlane { XY, XZ, ThreeDimensional }

    /// <summary>Immutable, world-neutral target geometry shared by query, authoring preview and runtime.
    /// Box/Line start at Origin and extend forward by Length; Width/Height are full sizes.
    /// Circle/Sector in XY/XZ have finite Height thickness. In 3D they are a sphere/cone.
    /// Target inclusion tests actor centres, rather than backend-specific collider surfaces.</summary>
    public sealed class GameplaySearchGeometry
    {
        public GameplaySearchShape Shape { get; }
        public GameplaySpatialPlane Plane { get; }
        public GameplayPoint Origin { get; }
        public GameplayDirection Direction { get; }
        public float Radius { get; }
        public float Angle { get; }
        public float Length { get; }
        public float Width { get; }
        public float Height { get; }
        public GameplaySourceId SingleTarget { get; }
        public IReadOnlyList<GameplayPoint> Cells { get; }
        public float CellSize { get; }

        public GameplaySearchGeometry(GameplaySearchShape shape, GameplaySpatialPlane plane, GameplayPoint origin,
            GameplayDirection direction, float radius = 1, float angle = 120, float length = 1,
            float width = 1, float height = 1, GameplaySourceId singleTarget = default,
            IEnumerable<GameplayPoint> cells = null, float cellSize = 1)
        {
            if (!Enum.IsDefined(typeof(GameplaySearchShape), shape) || !Enum.IsDefined(typeof(GameplaySpatialPlane), plane))
                throw new ArgumentOutOfRangeException(nameof(shape));
            if (!direction.IsValid) throw new ArgumentException("Direction must be initialized.", nameof(direction));
            foreach (float value in new[] { radius, length, width, height, cellSize })
                if (!float.IsFinite(value) || value <= 0) throw new ArgumentOutOfRangeException(nameof(radius), "Dimensions must be finite and positive.");
            if (!float.IsFinite(angle) || angle <= 0 || angle > 360) throw new ArgumentOutOfRangeException(nameof(angle));
            if (shape == GameplaySearchShape.Single && !singleTarget.IsValid) throw new ArgumentException("Single target is required.");
            if (plane != GameplaySpatialPlane.ThreeDimensional && (plane == GameplaySpatialPlane.XY ?
                direction.X == 0 && direction.Y == 0 : direction.X == 0 && direction.Z == 0))
                throw new ArgumentException("Direction must have a component in the selected plane.");
            var copied = cells == null ? new List<GameplayPoint>() : new List<GameplayPoint>(cells);
            if (shape == GameplaySearchShape.GridCells && copied.Count == 0) throw new ArgumentException("Grid cells are required.");
            Shape = shape; Plane = plane; Origin = origin; Direction = direction;
            Radius = radius; Angle = angle; Length = length; Width = width; Height = height;
            SingleTarget = singleTarget; Cells = copied.AsReadOnly(); CellSize = cellSize;
        }

        /// <summary>Conservative 3D broadphase radius; final inclusion always uses Contains.</summary>
        public float BoundsRadius
        {
            get
            {
                double extent = Shape == GameplaySearchShape.Circle || Shape == GameplaySearchShape.Sector ? Radius :
                    Math.Sqrt((double)Length * Length + (double)Width * Width / 4) + Radius;
                if (Shape == GameplaySearchShape.GridCells)
                    foreach (var cell in Cells) extent = Math.Max(extent, Math.Sqrt(DistanceSquared(Origin, cell)) + CellSize);
                return (float)Math.Min(float.MaxValue, Math.Sqrt(extent * extent + (double)Height * Height / 4));
            }
        }
        public bool Contains(GameplaySourceId id, GameplayPoint position)
        {
            if (Shape == GameplaySearchShape.Single) return id == SingleTarget;
            if (Shape == GameplaySearchShape.GridCells)
            {
                foreach (var cell in Cells)
                    if (CellCoordinate(position.X) == CellCoordinate(cell.X) &&
                        CellCoordinate(Plane == GameplaySpatialPlane.XY ? position.Y : position.Z) ==
                        CellCoordinate(Plane == GameplaySpatialPlane.XY ? cell.Y : cell.Z) &&
                        Math.Abs(Plane == GameplaySpatialPlane.XY ? position.Z - cell.Z : position.Y - cell.Y) <= Height / 2)
                        return true;
                return false;
            }
            double x = (double)position.X - Origin.X, y = (double)position.Y - Origin.Y, z = (double)position.Z - Origin.Z;
            double fx = Direction.X, fy = Direction.Y, fz = Direction.Z;
            if (Plane != GameplaySpatialPlane.ThreeDimensional)
            {
                if (Math.Abs(Plane == GameplaySpatialPlane.XY ? z : y) > Height / 2) return false;
                if (Plane == GameplaySpatialPlane.XY) { z = 0; fz = 0; } else { y = 0; fy = 0; }
                double norm = Math.Sqrt(fx * fx + fy * fy + fz * fz); fx /= norm; fy /= norm; fz /= norm;
            }
            double distanceSquared = x * x + y * y + z * z;
            double along = x * fx + y * fy + z * fz;
            if (Shape == GameplaySearchShape.Circle || Shape == GameplaySearchShape.Sector)
            {
                if (distanceSquared > (double)Radius * Radius) return false;
                return Shape == GameplaySearchShape.Circle || distanceSquared == 0 ||
                    along / Math.Sqrt(distanceSquared) >= Math.Cos(Angle * Math.PI / 360) - 1e-7;
            }
            if (Shape == GameplaySearchShape.Capsule || Shape == GameplaySearchShape.Ray)
            {
                double nearest = Math.Max(0, Math.Min(Length, along));
                double dx = x - fx * nearest, dy = y - fy * nearest, dz = z - fz * nearest;
                double radius = Shape == GameplaySearchShape.Ray ? Width / 2 : Radius;
                return dx * dx + dy * dy + dz * dz <= radius * radius;
            }
            if (along < 0 || along > Length) return false;
            if (Plane != GameplaySpatialPlane.ThreeDimensional)
                return Math.Max(0, distanceSquared - along * along) <= (double)Width * Width / 4;
            // Stable local right/up basis; when forward is vertical, choose Z as the auxiliary up.
            double ux = 0, uy = Math.Abs(fy) > .999 ? 0 : 1, uz = Math.Abs(fy) > .999 ? 1 : 0;
            double rx = uy * fz - uz * fy, ry = uz * fx - ux * fz, rz = ux * fy - uy * fx;
            double rightNorm = Math.Sqrt(rx * rx + ry * ry + rz * rz); rx /= rightNorm; ry /= rightNorm; rz /= rightNorm;
            ux = fy * rz - fz * ry; uy = fz * rx - fx * rz; uz = fx * ry - fy * rx;
            return Math.Abs(x * rx + y * ry + z * rz) <= Width / 2 && Math.Abs(x * ux + y * uy + z * uz) <= Height / 2;
        }
        private double CellCoordinate(float coordinate) => Math.Floor((double)coordinate / CellSize);
        public static double DistanceSquared(GameplayPoint a, GameplayPoint b)
        {
            double x = (double)a.X - b.X, y = (double)a.Y - b.Y, z = (double)a.Z - b.Z;
            return x * x + y * y + z * z;
        }
    }
}
