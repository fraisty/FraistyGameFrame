using System;
using System.Numerics;
using FRFrame.Gameplay.Extensions;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Closest point on an authored attack volume, and first contact along one sampled motion interval.</summary>
    internal static class GameplayHitContact
    {
        private const float Tolerance = .00005f;

        internal static GameplayDirection ProjectForward(GameplaySpatialPlane plane, GameplayDirection forward) =>
            plane == GameplaySpatialPlane.XY ? new GameplayDirection(forward.X, forward.Y) :
            plane == GameplaySpatialPlane.XZ ? new GameplayDirection(forward.X, 0, forward.Z) : forward;

        internal static void Basis(GameplaySpatialPlane plane, GameplayDirection forward, GameplayDirection anchorUp,
            out GameplayDirection right, out GameplayDirection up)
        {
            if (plane == GameplaySpatialPlane.XY)
            {
                right = new GameplayDirection(-forward.Y, forward.X);
                up = new GameplayDirection(0, 0, 1);
            }
            else if (plane == GameplaySpatialPlane.XZ)
            {
                right = new GameplayDirection(-forward.Z, 0, forward.X);
                up = new GameplayDirection(0, 1, 0);
            }
            else
            {
                var axis = Vector(forward);
                var side = anchorUp.IsValid ? Vector3.Cross(Vector(anchorUp), axis) : Vector3.Zero;
                if (side.LengthSquared() < 1e-10f)
                    side = Vector3.Cross(Math.Abs(forward.Y) > .999f ? Vector3.UnitZ : Vector3.UnitY, axis);
                side = Vector3.Normalize(side);
                right = Direction(side);
                up = Direction(Vector3.Cross(axis, side));
            }
        }

        private static Vector3 Vector(GameplayDirection value) => new Vector3(value.X, value.Y, value.Z);
        private static GameplayDirection Direction(Vector3 value) => new GameplayDirection(value.X, value.Y, value.Z);

        /// <summary>Linear translation and shortest continuous rotation between evaluated endpoint poses.</summary>
        internal readonly struct Motion
        {
            private readonly GameplaySpatialPlane _plane;
            private readonly GameplayHitPose _from, _to;
            private readonly GameplayDirection _forward;
            private readonly float _turn;
            private readonly Quaternion _start, _end;
            internal float Radians { get; }

            internal Motion(GameplaySpatialPlane plane, GameplayHitPose from, GameplayHitPose to)
            {
                _plane = plane;
                _from = from;
                _to = to;
                _forward = ProjectForward(plane, from.Forward);
                var end = ProjectForward(plane, to.Forward);
                _start = _end = Quaternion.Identity;
                _turn = 0;
                if (plane == GameplaySpatialPlane.ThreeDimensional)
                {
                    _start = Orientation(_forward, from.Up);
                    _end = Orientation(end, to.Up);
                    var delta = Quaternion.Conjugate(_start) * _end;
                    Radians = 2 * MathF.Atan2(new Vector3(delta.X, delta.Y, delta.Z).Length(), Math.Abs(delta.W));
                }
                else
                {
                    float a = plane == GameplaySpatialPlane.XY ? _forward.Y : _forward.Z;
                    float b = plane == GameplaySpatialPlane.XY ? end.Y : end.Z;
                    _turn = MathF.Atan2(_forward.X * b - a * end.X, _forward.X * end.X + a * b);
                    Radians = Math.Abs(_turn);
                }
            }

            internal GameplayHitPose At(float t)
            {
                if (t == 0) return _from;
                if (t == 1) return _to;
                var position = GameplaySpatialMath.Lerp(_from.Position, _to.Position, t);
                if (_plane == GameplaySpatialPlane.ThreeDimensional)
                {
                    var rotation = Quaternion.Slerp(_start, _end, t);
                    return new GameplayHitPose(position, Direction(Vector3.Transform(Vector3.UnitX, rotation)),
                        Direction(Vector3.Transform(Vector3.UnitZ, rotation)), _to.Revision);
                }
                float sin = MathF.Sin(_turn * t), cos = MathF.Cos(_turn * t);
                float side = _plane == GameplaySpatialPlane.XY ? _forward.Y : _forward.Z;
                float x = _forward.X * cos - side * sin, y = _forward.X * sin + side * cos;
                return new GameplayHitPose(position, _plane == GameplaySpatialPlane.XY
                    ? new GameplayDirection(x, y) : new GameplayDirection(x, 0, y), _to.Revision);
            }

            private static Quaternion Orientation(GameplayDirection forward, GameplayDirection anchorUp)
            {
                Basis(GameplaySpatialPlane.ThreeDimensional, forward, anchorUp, out var side, out var up);
                return Quaternion.Normalize(Quaternion.CreateFromRotationMatrix(new Matrix4x4(
                    forward.X, forward.Y, forward.Z, 0, side.X, side.Y, side.Z, 0,
                    up.X, up.Y, up.Z, 0, 0, 0, 0, 1)));
            }
        }

        internal static float Separation(GameplaySearchGeometry geometry, GameplayPoint center, out GameplayPoint contact)
        {
            Basis(geometry.Plane, geometry.Direction, geometry.OrientationUp, out var right, out var up);
            float x = center.X - geometry.Origin.X, y = center.Y - geometry.Origin.Y, z = center.Z - geometry.Origin.Z;
            var forward = geometry.Direction;
            float a = x * forward.X + y * forward.Y + z * forward.Z;
            float b = x * right.X + y * right.Y + z * right.Z;
            float c = x * up.X + y * up.Y + z * up.Z;
            float along = a, side = b, deep = Math.Clamp(c, -geometry.Height / 2, geometry.Height / 2);
            bool threeD = geometry.Plane == GameplaySpatialPlane.ThreeDimensional;
            if (geometry.Shape == GameplaySearchShape.Box)
            {
                along = Math.Clamp(a, 0, geometry.Length);
                side = Math.Clamp(b, -geometry.Width / 2, geometry.Width / 2);
            }
            else
            {
                float perpendicular = threeD ? MathF.Sqrt(b * b + c * c) : Math.Abs(b);
                float axialOrigin = geometry.Shape == GameplaySearchShape.Capsule ? Math.Clamp(a, 0, geometry.Length) : 0;
                float fromAxis = a - axialOrigin;
                float distance = MathF.Sqrt(fromAxis * fromAxis + perpendicular * perpendicular);
                float radialScale = distance > geometry.Radius ? geometry.Radius / distance : 1;
                along = axialOrigin + fromAxis * radialScale;
                float radial = perpendicular * radialScale;
                if (geometry.Shape == GameplaySearchShape.Sector)
                {
                    float angle = geometry.Angle * ((float)Math.PI / 360);
                    float sin = MathF.Sin(angle), cos = MathF.Cos(angle);
                    if (a * sin < perpendicular * cos)
                    {
                        float onEdge = Math.Clamp(a * cos + perpendicular * sin, 0, geometry.Radius);
                        along = onEdge * cos;
                        radial = onEdge * sin;
                    }
                }
                float ratio = perpendicular == 0 ? 0 : radial / perpendicular;
                side = b * ratio;
                if (threeD) deep = c * ratio;
            }
            contact = GameplaySpatialMath.Offset(GameplaySpatialMath.Offset(GameplaySpatialMath.Offset(geometry.Origin, forward, along), right, side), up, deep);
            return GameplaySpatialMath.Distance(center, contact);
        }

        internal static bool TryFind(GameplayHitWindowDefinition definition, GameplayHitPose from, GameplayHitPose to,
            GameplayPoint start, GameplayPoint end, float radius, bool sweep,
            out float time, out GameplayPoint contact, out bool budgetExhausted)
        {
            time = 1;
            budgetExhausted = false;
            var last = definition.Geometry(to);
            float lastGap = Separation(last, end, out contact) - radius;
            if (!sweep) return lastGap <= Tolerance;

            var motion = new Motion(definition.Plane, from, to);
            float rotation = motion.Radians;
            var first = definition.Geometry(from);
            float Sample(float at, out GameplayPoint point)
            {
                var geometry = rotation == 0 ? first : definition.Geometry(motion.At(at));
                var center = GameplaySpatialMath.Lerp(start, end, at);
                // With a fixed orientation, move into the first volume's relative frame.
                var dx = (last.Origin.X - first.Origin.X) * at;
                var dy = (last.Origin.Y - first.Origin.Y) * at;
                var dz = (last.Origin.Z - first.Origin.Z) * at;
                if (rotation == 0) center = new GameplayPoint(center.X - dx, center.Y - dy, center.Z - dz);
                var gap = Separation(geometry, center, out point) - radius;
                if (rotation == 0) point = new GameplayPoint(point.X + dx, point.Y + dy, point.Z + dz);
                return gap;
            }

            float gapAtStart = Sample(0, out var startContact);
            if (gapAtStart <= Tolerance) { time = 0; contact = startContact; return true; }
            // A rigid motion cannot close a greater gap than relative translation plus the arc
            // travelled by the farthest point on the volume (including its anchor offset).
            var relativeEnd = new GameplayPoint(end.X - (to.Position.X - from.Position.X),
                end.Y - (to.Position.Y - from.Position.Y), end.Z - (to.Position.Z - from.Position.Z));
            float travel = GameplaySpatialMath.Distance(start, relativeEnd);
            float bound = travel +
                rotation * (first.BoundsRadius + GameplaySpatialMath.Distance(default, definition.Offset));
            if (gapAtStart > bound + Tolerance) return false;

            if (rotation == 0)
            {
                if (first.Shape == GameplaySearchShape.Circle || first.Shape == GameplaySearchShape.Capsule)
                {
                    var relativeCenter = new GameplayPoint(end.X - last.Origin.X + first.Origin.X,
                        end.Y - last.Origin.Y + first.Origin.Y, end.Z - last.Origin.Z + first.Origin.Z);
                    if (TryFixedRoundVolume(first, start, relativeCenter, radius, out time, out bool analytic))
                    { Sample(time, out contact); return true; }
                    if (analytic) return false;
                }
                // A line through a convex volume expanded by the hurtbox sphere has one
                // contact interval. Minimize distance along that line, then find its entry.
                float lo = 0, hi = 1;
                int iterations = definition.SamplingBudget / 3;
                for (int i = 0; i < iterations; i++)
                {
                    float a = lo + (hi - lo) / 3, b = hi - (hi - lo) / 3;
                    if (Sample(a, out _) < Sample(b, out _)) hi = b;
                    else lo = a;
                }
                hi = lastGap <= Tolerance ? 1 : (lo + hi) / 2;
                if (Sample(hi, out _) > Tolerance) return false;
                lo = 0;
                for (int i = 0; i < iterations; i++)
                {
                    float mid = (lo + hi) / 2;
                    if (Sample(mid, out _) <= Tolerance) hi = mid;
                    else lo = mid;
                }
                time = hi;
                Sample(time, out contact);
                return true;
            }

            // Conservative advancement over rotating geometry: each step is bounded by
            // the current verified clearance. An exhausted interval never invents a hit.
            float maxStep = Math.Min(definition.MaxSweepAngle * ((float)Math.PI / 180) / rotation,
                definition.MaxSweepDistance / travel);
            float t = 0, gap = gapAtStart;
            for (int i = 0; i < definition.SamplingBudget; i++)
            {
                t += Math.Min(gap / bound, maxStep);
                if (t >= 1) return lastGap <= Tolerance;
                gap = Sample(t, out var point);
                if (gap <= Tolerance) { time = t; contact = point; return true; }
            }
            budgetExhausted = true;
            return lastGap <= Tolerance;
        }

        private static bool TryFixedRoundVolume(GameplaySearchGeometry geometry, GameplayPoint start, GameplayPoint end,
            float radius, out float time, out bool applicable)
        {
            Basis(geometry.Plane, geometry.Direction, geometry.OrientationUp, out var side, out var up);
            var origin = geometry.Origin;
            float Project(GameplayPoint p, GameplayDirection axis) =>
                (p.X - origin.X) * axis.X + (p.Y - origin.Y) * axis.Y + (p.Z - origin.Z) * axis.Z;
            float x = Project(start, geometry.Direction), y = Project(start, side), z = Project(start, up);
            float dx = Project(end, geometry.Direction) - x, dy = Project(end, side) - y, dz = Project(end, up) - z;
            time = 1;
            applicable = geometry.Plane == GameplaySpatialPlane.ThreeDimensional ||
                Math.Max(Math.Abs(z), Math.Abs(z + dz)) <= geometry.Height / 2;
            if (!applicable) return false; // Radial expansion across the slab cap uses the convex distance path.
            if (geometry.Plane != GameplaySpatialPlane.ThreeDimensional) z = dz = 0;
            float r = geometry.Radius + radius;
            float PerpendicularEntry(float a, float b, float c, float vx, float vy, float vz)
            {
                double aa = vx * (double)vx + vy * (double)vy + vz * (double)vz;
                double bb = a * (double)vx + b * (double)vy + c * (double)vz;
                double cc = a * (double)a + b * (double)b + c * (double)c - r * (double)r;
                if (cc <= 0) return 0;
                double disc = bb * bb - aa * cc;
                return aa > 0 && bb < 0 && disc >= 0 ? (float)(cc / (-bb + Math.Sqrt(disc))) : float.PositiveInfinity;
            }
            time = PerpendicularEntry(x, y, z, dx, dy, dz);
            if (geometry.Shape == GameplaySearchShape.Circle) return time <= 1;
            time = Math.Min(time, PerpendicularEntry(x - geometry.Length, y, z, dx, dy, dz));
            if (dx != 0 || x >= 0 && x <= geometry.Length)
            {
                float lo = dx == 0 ? 0 : Math.Max(0, Math.Min(-x / dx, (geometry.Length - x) / dx));
                float hi = dx == 0 ? 1 : Math.Min(1, Math.Max(-x / dx, (geometry.Length - x) / dx));
                if (lo <= hi)
                {
                    float entry = lo + PerpendicularEntry(0, y + dy * lo, z + dz * lo, 0, dy, dz);
                    if (entry <= hi) time = Math.Min(time, entry);
                }
            }
            return time <= 1;
        }
    }
}

