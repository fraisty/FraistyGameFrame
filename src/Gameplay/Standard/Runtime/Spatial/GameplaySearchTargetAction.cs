using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Extensions;

namespace FRFrame.Gameplay.Standard
{
    public enum GameplaySearchOrigin { Owner, ContextPoint, PrimaryTarget }
    /// <summary>Direction is resolved at the same sampling time as the query. Absolute angles
    /// and offsets use degrees from +X toward +Y in XY, or +X toward +Z in XZ/3D;
    /// point modes preserve all three axes.</summary>
    public enum GameplaySearchDirection { OwnerFacing, ToPrimaryTarget, ToContextPoint, AbsoluteAngle }
    /// <summary>Official target-search business action. Expressions are sampled at this action's rule time.
    /// Each callback sees one target; completed sees the full deterministic list. Empty output is explicit,
    /// never a fallback to Self. Nested callbacks share the Sequence cancellation guard.</summary>
    public sealed class GameplaySearchTargetAction : IGameplaySkillAction, IGameplaySkillContextAction, IGameplaySkillGuardedContextAction
    {
        private readonly GameplaySearchShape _shape;
        private readonly GameplaySpatialPlane _plane;
        private readonly GameplaySearchOrigin _origin;
        private readonly GameplayPoint _originOffset;
        private readonly GameplaySearchDirection _direction;
        private readonly IGameplayFloatValue _directionAngle;
        private readonly bool _lengthFromTarget;
        private readonly IGameplayFloatValue _radius, _angle, _length, _width, _height;
        private readonly GameplayFactionFilter _faction;
        private readonly GameplayTagQuery _tags;
        private readonly int _layerMask, _maxTargets;
        private readonly GameplayTargetOrder _order;
        private readonly bool _includeSelf;
        private readonly IReadOnlyList<GameplayPoint> _cells;
        private readonly float _cellSize;
        private readonly IReadOnlyList<IGameplaySkillAction> _each, _completed, _empty;
        public GameplaySearchTargetAction(GameplaySearchShape shape, GameplaySpatialPlane plane, GameplaySearchOrigin origin,
            IGameplayFloatValue radius, IGameplayFloatValue angle, IGameplayFloatValue length, IGameplayFloatValue width, IGameplayFloatValue height,
            GameplayFactionFilter faction, GameplayTagQuery tags, int layerMask, int maxTargets, GameplayTargetOrder order, bool includeSelf,
            IEnumerable<IGameplaySkillAction> each, IEnumerable<IGameplaySkillAction> completed, IEnumerable<IGameplaySkillAction> empty,
            IEnumerable<GameplayPoint> cells = null, float cellSize = 1,
            GameplaySearchDirection direction = GameplaySearchDirection.OwnerFacing,
            IGameplayFloatValue directionAngle = null, bool lengthFromTarget = false,GameplayPoint originOffset=default)
        {
            if (!Enum.IsDefined(typeof(GameplaySearchShape), shape) || !Enum.IsDefined(typeof(GameplaySpatialPlane), plane) ||
                !Enum.IsDefined(typeof(GameplaySearchOrigin), origin)) throw new ArgumentOutOfRangeException(nameof(shape));
            _shape = shape; _plane = plane; _origin = origin;
            _originOffset=originOffset;
            if (!Enum.IsDefined(typeof(GameplaySearchDirection), direction)) throw new ArgumentOutOfRangeException(nameof(direction));
            if (lengthFromTarget && (direction != GameplaySearchDirection.ToPrimaryTarget && direction != GameplaySearchDirection.ToContextPoint ||
                shape != GameplaySearchShape.Box && shape != GameplaySearchShape.Line && shape != GameplaySearchShape.Ray && shape != GameplaySearchShape.Capsule))
                throw new ArgumentException("Endpoint length requires a directional segment and a point/target direction.", nameof(lengthFromTarget));
            _direction = direction; _directionAngle = directionAngle ?? new GameplayConstantFloatValue(0); _lengthFromTarget = lengthFromTarget;
            _radius = radius ?? throw new ArgumentNullException(nameof(radius)); _angle = angle ?? throw new ArgumentNullException(nameof(angle));
            _length = length ?? throw new ArgumentNullException(nameof(length)); _width = width ?? throw new ArgumentNullException(nameof(width));
            _height = height ?? throw new ArgumentNullException(nameof(height));
            _faction = faction; _tags = tags; _layerMask = layerMask; _maxTargets = maxTargets; _order = order; _includeSelf = includeSelf;
            _each = Copy(each); _completed = Copy(completed); _empty = Copy(empty);
            _cells = new List<GameplayPoint>(cells ?? Array.Empty<GameplayPoint>()).AsReadOnly(); _cellSize = cellSize;
        }
        private static IReadOnlyList<IGameplaySkillAction> Copy(IEnumerable<IGameplaySkillAction> source)
        {
            var list = new List<IGameplaySkillAction>(source ?? Array.Empty<IGameplaySkillAction>());
            if (list.Contains(null)) throw new ArgumentException("Callback action cannot be null."); return list.AsReadOnly();
        }
        public GameplaySkillActionResult Execute(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host) => ExecuteWithContext(context, triggerEvent, host).Result;
        public GameplaySkillActionExecution ExecuteWithContext(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host) => ExecuteGuarded(context, triggerEvent, host, null);
        public GameplaySkillActionExecution ExecuteGuarded(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host, Func<bool> canContinue)
        {
            if (!(host is GameplayStandardActor actor) || actor.IsDisposed) throw new InvalidOperationException("Search requires the official standard actor/world host.");
            if (!TryCreateRequest(context, actor, out var request)) return new GameplaySkillActionExecution(GameplaySkillActionResult.Rejected, context);
            var targets = actor.World.Spatial.Query(request);
            var resultContext = context.WithTarget(targets);
            if (targets.Targets.Count == 0)
            {
                var result = GameplaySkillActionRunner.Execute(_empty, resultContext, triggerEvent, host, canContinue);
                if (result.Result == GameplaySkillActionResult.Rejected) return result;
            }
            else foreach (var id in targets.Targets)
            {
                if (!actor.World.TryGetActor(id, out var target)) continue;
                var single = context.WithTarget(new GameplayTargetData(id, target.Position, null, new[] { id }));
                var result = GameplaySkillActionRunner.Execute(_each, single, triggerEvent, host, canContinue);
                if (result.Result == GameplaySkillActionResult.Rejected) return result;
            }
            return GameplaySkillActionRunner.Execute(_completed, resultContext, triggerEvent, host, canContinue);
        }
        /// <summary>Side-effect-free canonical request used by both runtime query and Editor preview.
        /// Only dimensions used by the selected shape evaluate their AST; inactive fields are retained
        /// for authoring shape switches and cannot fail an unrelated search at runtime.</summary>
        public bool TryCreateRequest(GameplayContext context, GameplayStandardActor actor, out GameplaySpatialSearchRequest request)
        {
            if (context == null || actor == null) throw new ArgumentNullException();
            if (actor.IsDisposed) throw new ObjectDisposedException(nameof(actor));
            request = null;
            GameplayPoint origin = actor.Position;
            if (_origin == GameplaySearchOrigin.ContextPoint)
            {
                if (!context.TargetData.HasPoint) return false;
                origin = context.TargetData.Point;
            }
            if (_origin == GameplaySearchOrigin.PrimaryTarget)
            {
                if (!actor.World.TryGetActor(context.TargetData.PrimaryTargetId, out var primary)) return false;
                origin = primary.Position;
            }
            origin=new GameplayPoint(origin.X+_originOffset.X,origin.Y+_originOffset.Y,origin.Z+_originOffset.Z);
            var self = new GameplayEffectApplicationContext(context.WithTarget(new GameplayTargetData(actor.Id)), actor,
                context.Values.AsSetByCallerValues(), actor.Attributes, actor.Id);
            var direction = actor.Facing;
            float endpointLength = 0;
            bool directional = _shape == GameplaySearchShape.Sector || _shape == GameplaySearchShape.Box ||
                _shape == GameplaySearchShape.Line || _shape == GameplaySearchShape.Ray || _shape == GameplaySearchShape.Capsule;
            if (directional)
            {
                if (_direction == GameplaySearchDirection.ToPrimaryTarget || _direction == GameplaySearchDirection.ToContextPoint)
                {
                    GameplayPoint endpoint;
                    if (_direction == GameplaySearchDirection.ToContextPoint)
                    { if (!context.TargetData.HasPoint) return false; endpoint = context.TargetData.Point; }
                    else
                    { if (!actor.World.TryGetActor(context.TargetData.PrimaryTargetId, out var target)) return false; endpoint = target.Position; }
                    float x = endpoint.X - origin.X, y = endpoint.Y - origin.Y, z = endpoint.Z - origin.Z;
                    if (_plane == GameplaySpatialPlane.XY) z = 0;
                    else if (_plane == GameplaySpatialPlane.XZ) y = 0;
                    double distance = Math.Sqrt((double)x*x + (double)y*y + (double)z*z);
                    if (distance == 0 || distance > float.MaxValue) return false;
                    endpointLength = (float)distance; direction = new GameplayDirection(x,y,z);
                }
                double angle = GameplayValueEvaluator.Evaluate(_directionAngle,self) * Math.PI / 180;
                if (!double.IsFinite(angle)) throw new ArithmeticException("Search direction angle must be finite.");
                if (_direction == GameplaySearchDirection.AbsoluteAngle) direction = new GameplayDirection(1,0,0);
                double cosine = Math.Cos(angle), sine = Math.Sin(angle);
                direction = _plane == GameplaySpatialPlane.XY ?
                    new GameplayDirection((float)(direction.X*cosine-direction.Y*sine),(float)(direction.X*sine+direction.Y*cosine),direction.Z) :
                    new GameplayDirection((float)(direction.X*cosine-direction.Z*sine),direction.Y,(float)(direction.X*sine+direction.Z*cosine));
            }
            var geometry = new GameplaySearchGeometry(_shape, _plane, origin, direction,
                _shape == GameplaySearchShape.Circle || _shape == GameplaySearchShape.Sector || _shape == GameplaySearchShape.Capsule ? GameplayValueEvaluator.Evaluate(_radius, self) : 1,
                _shape == GameplaySearchShape.Sector ? GameplayValueEvaluator.Evaluate(_angle, self) : 120,
                _lengthFromTarget ? endpointLength : _shape == GameplaySearchShape.Box || _shape == GameplaySearchShape.Line || _shape == GameplaySearchShape.Ray || _shape == GameplaySearchShape.Capsule ? GameplayValueEvaluator.Evaluate(_length, self) : 1,
                _shape == GameplaySearchShape.Box || _shape == GameplaySearchShape.Line || _shape == GameplaySearchShape.Ray ? GameplayValueEvaluator.Evaluate(_width, self) : 1,
                GameplayValueEvaluator.Evaluate(_height, self), context.TargetData.PrimaryTargetId, _cells, _cellSize);
            request = new GameplaySpatialSearchRequest(context, geometry, actor.Faction,
                _faction, _tags, _layerMask, _maxTargets, _order, _includeSelf);
            return true;
        }
    }
}
