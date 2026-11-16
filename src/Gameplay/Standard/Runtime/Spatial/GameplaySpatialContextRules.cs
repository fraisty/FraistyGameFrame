using System;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Tests one selected point against the actor's facing without selecting another target.</summary>
    public sealed class GameplayFacingCondition : IGameplaySkillCondition, IGameplaySkillContextCondition
    {
        private readonly GameplaySpatialPlane _plane;
        private readonly float _angle;
        private readonly double _cosine;
        private readonly bool _useLiveTarget;

        public GameplayFacingCondition(GameplaySpatialPlane plane, float angle, bool useLiveTarget = false)
        {
            if (!Enum.IsDefined(typeof(GameplaySpatialPlane), plane)) throw new ArgumentOutOfRangeException(nameof(plane));
            if (!(angle > 0 && angle <= 360)) throw new ArgumentOutOfRangeException(nameof(angle));
            _plane = plane;
            _angle = angle;
            _cosine = Math.Cos(angle * Math.PI / 360);
            _useLiveTarget = useLiveTarget;
        }

        public bool Evaluate(IGameplaySkillHost host, GameplayEventData triggerEvent) =>
            EvaluateContext(triggerEvent.Context, host, triggerEvent);

        public bool EvaluateContext(GameplayContext context, IGameplaySkillHost host, GameplayEventData triggerEvent)
        {
            if (!(host is GameplayStandardActor actor) || context == null) return false;
            var data = context.TargetData;
            GameplayPoint point;
            if (_useLiveTarget)
            {
                if (!actor.World.TryGetActor(data.PrimaryTargetId, out var target) || !target.IsTargetable) return false;
                point = target.Position;
            }
            else
            {
                if (!data.HasPoint) return false;
                point = data.Point;
            }

            if (_angle == 360) return true;
            var origin = actor.Position;
            var facing = actor.Facing;
            double x = point.X - origin.X, y = _plane == GameplaySpatialPlane.XZ ? 0 : point.Y - origin.Y, z = _plane == GameplaySpatialPlane.XY ? 0 : point.Z - origin.Z;
            double fx = facing.X, fy = _plane == GameplaySpatialPlane.XZ ? 0 : facing.Y, fz = _plane == GameplaySpatialPlane.XY ? 0 : facing.Z;
            double length = x * x + y * y + z * z;
            double facingLength = fx * fx + fy * fy + fz * fz;
            return length < 1e-6 || facingLength > 0 &&
                (x * fx + y * fy + z * fz) / Math.Sqrt(length * facingLength) >= _cosine - 1e-7;
        }
    }

    /// <summary>Resolves a pure world point once. An explicit point wins over a nearest-actor
    /// query, which falls back to the caster's current position and facing.</summary>
    public sealed class GameplayCaptureWorldPointAction : IGameplaySkillAction, IGameplaySkillContextAction
    {
        private readonly GameplaySpatialPlane _plane;
        private readonly IGameplayFloatValue _range;
        private readonly GameplayFactionFilter _faction;

        public GameplayCaptureWorldPointAction(GameplaySpatialPlane plane, IGameplayFloatValue range, GameplayFactionFilter faction)
        {
            _plane = plane;
            _range = range ?? throw new ArgumentNullException(nameof(range));
            _faction = faction;
        }

        public GameplaySkillActionResult Execute(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host) =>
            ExecuteWithContext(context, triggerEvent, host).Result;

        public GameplaySkillActionExecution ExecuteWithContext(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host)
        {
            if (!(host is GameplayStandardActor actor))
                return new GameplaySkillActionExecution(GameplaySkillActionResult.Rejected, context);
            var data = context.TargetData;
            if (data.HasPoint && !data.HasPrimaryTarget)
                return new GameplaySkillActionExecution(GameplaySkillActionResult.Succeeded, context);

            float range = GameplaySpatialMath.Positive(_range, actor, context, "Point capture range");
            var geometry = new GameplaySearchGeometry(GameplaySearchShape.Circle, _plane, actor.Position, actor.Facing, range, height: range * 2);
            var hit = actor.World.Spatial.Query(new GameplaySpatialSearchRequest(context, geometry, actor.Faction, _faction,
                maxTargets: 1, useActorCenters: true));
            var point = actor.World.TryGetActor(hit.PrimaryTargetId, out var target)
                ? target.Position
                : GameplaySpatialMath.Offset(actor.Position, actor.Facing, range);
            return new GameplaySkillActionExecution(GameplaySkillActionResult.Succeeded,
                context.WithTarget(new GameplayTargetData(default, point)));
        }
    }
}
