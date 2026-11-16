using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Extensions;

namespace FRFrame.Gameplay.Standard
{
    public enum GameplayHitAnchorKind { OwnerRoot, TransformPath, SpineBone }
    public enum GameplayHitSweepPolicy { SampledPose, Swept }

    /// <summary>A model-relative socket name or a skeleton path and bone name. Empty path selects the subject.</summary>
    public readonly struct GameplayHitAnchor
    {
        public GameplayHitAnchorKind Kind { get; }
        public string Path { get; }
        public string Bone { get; }

        public GameplayHitAnchor(GameplayHitAnchorKind kind, string path = null, string bone = null)
        {
            if (!Enum.IsDefined(typeof(GameplayHitAnchorKind), kind) ||
                kind == GameplayHitAnchorKind.TransformPath && string.IsNullOrWhiteSpace(path) ||
                kind == GameplayHitAnchorKind.SpineBone && string.IsNullOrWhiteSpace(bone))
                throw new ArgumentException("A Transform path or Spine bone name is required for the selected anchor.", nameof(kind));
            Kind = kind;
            Path = path;
            Bone = bone;
        }
    }

    /// <summary>One evaluated world pose. Change revision on a model, track or discrete transform cut.</summary>
    public readonly struct GameplayHitPose
    {
        public GameplayPoint Position { get; }
        public GameplayDirection Forward { get; }
        public long Revision { get; }
        public GameplayHitPose(GameplayPoint position, GameplayDirection forward, long revision = 0)
        { Position = position; Forward = forward; Revision = revision; }
    }

    /// <summary>Reads evaluated pose at the supplied World sample time without advancing animation.</summary>
    public interface IGameplayHitAnchorProvider
    {
        bool TrySample(GameplayHitAnchor anchor, double worldTime, out GameplayHitPose pose);
    }

    /// <summary>Explicit spherical hurtbox. The default is one logical point at the actor root.</summary>
    public readonly struct GameplayHurtbox
    {
        public float Radius { get; }
        public GameplayPoint Offset { get; }
        public GameplayHurtbox(float radius, GameplayPoint offset = default)
        {
            if (!float.IsFinite(radius) || radius < 0) throw new ArgumentOutOfRangeException(nameof(radius));
            Radius = radius;
            Offset = offset;
        }
        internal GameplayPoint Center(GameplayPoint root) => new GameplayPoint(root.X + Offset.X, root.Y + Offset.Y, root.Z + Offset.Z);
    }

    /// <summary>Immutable hit geometry, tracking and callbacks shared by each independently opened window.</summary>
    public sealed class GameplayHitWindowDefinition
    {
        public GameplaySearchShape Shape { get; }
        public GameplaySpatialPlane Plane { get; }
        public GameplayHitAnchor Anchor { get; }
        public GameplayPoint Offset { get; }
        public float DirectionAngle { get; }
        public float Radius { get; }
        public float Angle { get; }
        public float Length { get; }
        public float Width { get; }
        public float Height { get; }
        public float Duration { get; }
        public bool FollowAnchor { get; }
        public GameplayFactionFilter Faction { get; }
        public GameplayTagQuery Tags { get; }
        public int LayerMask { get; }
        public GameplayTargetOrder Order { get; }
        public int MaxDistinctTargets { get; }
        public int MaxHitsPerTarget { get; }
        public float RehitInterval { get; }
        public bool OwnedByAbility { get; }
        public GameplayTag StopTag { get; }
        public GameplayHitSweepPolicy SweepPolicy { get; }
        public int SamplingBudget { get; }
        public float MaxSweepDistance { get; }
        public float MaxSweepAngle { get; }
        public IReadOnlyList<IGameplaySkillAction> OnHit { get; }
        public IReadOnlyList<IGameplaySkillAction> OnWindowFinished { get; }
        public IReadOnlyList<IGameplaySkillAction> OnCancelled { get; }

        public GameplayHitWindowDefinition(GameplaySearchShape shape, GameplaySpatialPlane plane, GameplayHitAnchor anchor,
            float duration, float radius = 2, float angle = 120, float length = 2, float width = 1, float height = 1,
            GameplayPoint offset = default, float directionAngle = 0, bool followAnchor = true,
            GameplayFactionFilter faction = GameplayFactionFilter.Enemies, GameplayTagQuery tags = null, int layerMask = -1,
            GameplayTargetOrder order = GameplayTargetOrder.Nearest, int maxDistinctTargets = 1, int maxHitsPerTarget = 1,
            float rehitInterval = 0, bool ownedByAbility = true, GameplayTag stopTag = default,
            GameplayHitSweepPolicy sweepPolicy = GameplayHitSweepPolicy.Swept, int samplingBudget = 64,
            float maxSweepDistance = 12, float maxSweepAngle = 135, IEnumerable<IGameplaySkillAction> onHit = null,
            IEnumerable<IGameplaySkillAction> onWindowFinished = null, IEnumerable<IGameplaySkillAction> onCancelled = null)
        {
            if (shape != GameplaySearchShape.Circle && shape != GameplaySearchShape.Sector &&
                shape != GameplaySearchShape.Capsule && shape != GameplaySearchShape.Box)
                throw new ArgumentException("A hit window supports Circle, Sector, Capsule or Box.", nameof(shape));
            if (!Enum.IsDefined(typeof(GameplaySpatialPlane), plane) ||
                !Enum.IsDefined(typeof(GameplayFactionFilter), faction) ||
                !Enum.IsDefined(typeof(GameplayTargetOrder), order) ||
                !Enum.IsDefined(typeof(GameplayHitSweepPolicy), sweepPolicy))
                throw new ArgumentException("Invalid hit window enum.");
            foreach (float value in new[] { radius, length, width, height, duration, maxSweepDistance })
                if (!float.IsFinite(value) || value <= 0) throw new ArgumentException("Hit window dimensions, duration and sweep distance must be positive.");
            if (!float.IsFinite(angle) || angle <= 0 || angle > 180 || !float.IsFinite(directionAngle) ||
                !float.IsFinite(maxSweepAngle) || maxSweepAngle <= 0 || maxSweepAngle >= 180)
                throw new ArgumentException("Hit sectors support 0 < Angle ≤ 180; sweep cuts require 0 < MaxSweepAngle < 180.");
            if (maxDistinctTargets < 1 || maxHitsPerTarget < 1 || !float.IsFinite(rehitInterval) ||
                rehitInterval < 0 || maxHitsPerTarget > 1 && rehitInterval == 0 || samplingBudget < 8 || samplingBudget > 256)
                throw new ArgumentException("Hit limits must be positive, repeated hits need a positive interval, and sweep budget must be 8–256.");

            Shape = shape; Plane = plane; Anchor = anchor; Duration = duration;
            Radius = radius; Angle = angle; Length = length; Width = width; Height = height;
            Offset = offset; DirectionAngle = directionAngle; FollowAnchor = followAnchor;
            Faction = faction; Tags = tags ?? GameplayTagQuery.Empty; LayerMask = layerMask; Order = order;
            MaxDistinctTargets = maxDistinctTargets; MaxHitsPerTarget = maxHitsPerTarget; RehitInterval = rehitInterval;
            OwnedByAbility = ownedByAbility; StopTag = stopTag; SweepPolicy = sweepPolicy; SamplingBudget = samplingBudget;
            MaxSweepDistance = maxSweepDistance; MaxSweepAngle = maxSweepAngle;
            OnHit = GameplaySpatialMath.Actions(onHit);
            OnWindowFinished = GameplaySpatialMath.Actions(onWindowFinished);
            OnCancelled = GameplaySpatialMath.Actions(onCancelled);
        }

        /// <summary>Builds canonical search geometry from the same evaluated anchor used by runtime and preview.</summary>
        public GameplaySearchGeometry Geometry(GameplayHitPose pose)
        {
            var forward = pose.Forward;
            if (Plane == GameplaySpatialPlane.XY && (forward.X != 0 || forward.Y != 0))
                forward = new GameplayDirection(forward.X, forward.Y);
            else if (Plane == GameplaySpatialPlane.XZ && (forward.X != 0 || forward.Z != 0))
                forward = new GameplayDirection(forward.X, 0, forward.Z);
            GameplayHitContact.Basis(Plane, forward, out var right, out var up);
            var origin = GameplaySpatialMath.Offset(GameplaySpatialMath.Offset(
                GameplaySpatialMath.Offset(pose.Position, forward, Offset.X), right, Offset.Y), up, Offset.Z);
            if (DirectionAngle != 0)
            {
                float angle = DirectionAngle * ((float)Math.PI / 180);
                float cos = MathF.Cos(angle), sin = MathF.Sin(angle);
                forward = new GameplayDirection(forward.X * cos + right.X * sin, forward.Y * cos + right.Y * sin, forward.Z * cos + right.Z * sin);
            }
            return new GameplaySearchGeometry(Shape, Plane, origin, forward, Radius, Angle, Length, Width, Height);
        }

        internal bool Accepts(GameplayHitPose pose) => pose.Forward.IsValid &&
            (Plane == GameplaySpatialPlane.ThreeDimensional || (Plane == GameplaySpatialPlane.XY
                ? pose.Forward.X != 0 || pose.Forward.Y != 0
                : pose.Forward.X != 0 || pose.Forward.Z != 0));
    }

    public sealed partial class GameplayStandardActor
    {
        private GameplayHurtbox _hurtbox;
        /// <summary>Shape declaration supplied by the actual actor host. Radius zero uses the logical point.</summary>
        public GameplayHurtbox Hurtbox { get => HurtboxSource?.Invoke() ?? _hurtbox; set => _hurtbox = value; }
        /// <summary>Optional live shape from one explicitly assigned host collider or logical source.</summary>
        public Func<GameplayHurtbox> HurtboxSource { get; set; }
        /// <summary>Optional game/SDK override. Presentation providers are used when no override is bound.</summary>
        public IGameplayHitAnchorProvider HitAnchors { get; set; }
        internal long HitPoseRevision { get; private set; }
        /// <summary>Call at an authoritative teleport or view cut so neither weapon nor hurtbox sweeps across the jump.</summary>
        public void MarkHitPoseDiscontinuous() => HitPoseRevision++;

        public bool TrySampleHitAnchor(GameplayHitAnchor anchor, double worldTime, out GameplayHitPose pose)
        {
            if (anchor.Kind == GameplayHitAnchorKind.OwnerRoot)
            {
                pose = new GameplayHitPose(Position, Facing, HitPoseRevision);
                return true;
            }
            var provider = HitAnchors ?? Presentation as IGameplayHitAnchorProvider;
            pose = default;
            return provider != null && provider.TrySample(anchor, worldTime, out pose);
        }
    }

    public sealed partial class GameplayStandardWorld
    {
        internal double HitSampleTime { get; private set; }

        /// <summary>Opens one independently counted window under the cast's existing spatial lease.</summary>
        public GameplayHitWindowOperation OpenHitWindow(GameplayStandardActor owner, GameplayContext context, GameplayHitWindowDefinition definition)
        {
            if (IsDisposed || !owner.IsTargetable || !GameplayStandardActivation.TryFindGrant(owner, context, out var grant))
                return null;
            if (owner.World != this) throw new ArgumentException("The hit window requires an owner in this world.", nameof(owner));
            return RegisterSpatial(new GameplayHitWindowOperation(owner, context, grant, definition), grant);
        }
    }

    /// <summary>Standard action used directly by timelines and rules to open a hit window.</summary>
    public sealed class GameplayOpenHitWindowAction : IGameplaySkillAction
    {
        public GameplayHitWindowDefinition Definition { get; }
        public GameplayOpenHitWindowAction(GameplayHitWindowDefinition definition) =>
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
        public GameplaySkillActionResult Execute(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host) =>
            host is GameplayStandardActor owner && owner.World.OpenHitWindow(owner, context, Definition) is { IsDisposed: false }
                ? GameplaySkillActionResult.Succeeded : GameplaySkillActionResult.Rejected;
    }
}

