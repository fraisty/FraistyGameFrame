using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Extensions;

namespace FRFrame.Gameplay.Standard
{
    public enum GameplayProjectileMotionKind { Straight, Homing, Arc, Parabolic, Bezier }
    /// <summary>Direction preserves legacy range shots; CapturedDestination stops at the launch point/target snapshot.</summary>
    public enum GameplayStraightDestinationMode { Direction, CapturedDestination }
    public enum GameplayBezierControlMode { FixedOffsets, RelativeQuadratic }
    public enum GameplayCurveSide { Positive, Negative, RandomAtLaunch }
    public enum GameplayProjectileContactMode { Swept, FixedEndpointNearest }
    /// <summary>Immutable launch inputs. Duration/Speed/Height are captured at spawn; a homing
    /// target's actual current position is supplied at sampling time, not copied into a shared asset.</summary>
    public readonly struct GameplayProjectileLaunch
    {
        public GameplayPoint Origin {get;}
        public GameplayPoint Destination {get;}
        public GameplayDirection Direction {get;}
        public GameplaySpatialPlane Plane {get;}
        public float Speed {get;}
        public float Duration {get;}
        public float Height {get;}
        public float CurveSign {get;}
        public GameplayProjectileLaunch(GameplayPoint origin,GameplayPoint destination,GameplayDirection direction,GameplaySpatialPlane plane,float speed,float duration,float height,float curveSign=1)
        {Origin=origin;Destination=destination;Direction=direction;Plane=plane;Speed=speed;Duration=duration;Height=height;CurveSign=curveSign;}
    }
    /// <summary>Per-projectile motion instance. A custom strategy receives the world's existing delta
    /// and absolute local age; it must not create a timer, move a Unity object or invoke another world step.</summary>
    public interface IGameplayProjectileMotion
    {GameplayPoint Sample(double age,float delta,GameplayPoint current,GameplayPoint? liveTarget);bool Arrived {get;}}
    public interface IGameplayProjectileMotionDefinition { IGameplayProjectileMotion Create(GameplayProjectileLaunch launch); }
    /// <summary>Standard straight, homing, planar arc, world-up parabola and Bezier sampling.
    /// Positive relative height scale replaces the quadratic control point's fixed Y offset
    /// with a clamped proportion of the launch-to-destination distance.</summary>
    public sealed class GameplayProjectileMotionDefinition:IGameplayProjectileMotionDefinition
    {
        public GameplayProjectileMotionKind Kind {get;}
        public GameplayPoint Control1Offset {get;}
        public GameplayPoint Control2Offset {get;}
        public GameplayStraightDestinationMode StraightDestination {get;}
        public GameplayBezierControlMode BezierControl {get;}
        public GameplayCurveSide CurveSide {get;}
        public float ControlAlong {get;}
        public GameplayPoint RelativeOffset {get;}
        private readonly float _relativeHeightScale, _relativeHeightMin, _relativeHeightMax;
        public GameplayProjectileMotionDefinition(GameplayProjectileMotionKind kind,GameplayPoint control1Offset=default,GameplayPoint control2Offset=default,
            GameplayStraightDestinationMode straightDestination=GameplayStraightDestinationMode.Direction,
            GameplayBezierControlMode bezierControl=GameplayBezierControlMode.FixedOffsets,float controlAlong=.5f,
            GameplayPoint relativeOffset=default,GameplayCurveSide curveSide=GameplayCurveSide.Positive,
            float relativeHeightScale=0,float relativeHeightMin=0,float relativeHeightMax=0)
        {if(!Enum.IsDefined(typeof(GameplayProjectileMotionKind),kind)||!Enum.IsDefined(typeof(GameplayStraightDestinationMode),straightDestination))throw new ArgumentOutOfRangeException(nameof(kind));Kind=kind;Control1Offset=control1Offset;Control2Offset=control2Offset;StraightDestination=straightDestination;
            if (!(controlAlong >= 0 && controlAlong <= 1)) throw new ArgumentOutOfRangeException(nameof(controlAlong));
            BezierControl=bezierControl; ControlAlong=controlAlong; RelativeOffset=relativeOffset; CurveSide=curveSide;
            _relativeHeightScale=relativeHeightScale; _relativeHeightMin=relativeHeightMin; _relativeHeightMax=relativeHeightMax;
        }
        public IGameplayProjectileMotion Create(GameplayProjectileLaunch launch)=>new Motion(this,launch);
        private sealed class Motion:IGameplayProjectileMotion
        {
            private readonly GameplayProjectileMotionDefinition _definition;
            private readonly GameplayProjectileLaunch _launch;
            public bool Arrived {get;private set;}
            private readonly GameplayPoint _control1, _control2;
            internal Motion(GameplayProjectileMotionDefinition definition,GameplayProjectileLaunch launch)
            {
                _definition=definition; _launch=launch;
                if (definition.Kind != GameplayProjectileMotionKind.Bezier) return;
                var a = launch.Origin;
                var d = launch.Destination;
                if (definition.BezierControl == GameplayBezierControlMode.RelativeQuadratic)
                {
                    var q = GameplaySpatialMath.Lerp(a, d, definition.ControlAlong);
                    var offset = definition.RelativeOffset;
                    float height = definition._relativeHeightScale > 0
                        ? Math.Clamp(GameplaySpatialMath.Distance(a, d) * definition._relativeHeightScale, definition._relativeHeightMin, definition._relativeHeightMax)
                        : offset.Y;
                    q = new GameplayPoint(q.X + offset.X * launch.CurveSign, q.Y + height * launch.CurveSign, q.Z + offset.Z * launch.CurveSign);
                    _control1 = GameplaySpatialMath.Lerp(a, q, 2f / 3);
                    _control2 = GameplaySpatialMath.Lerp(d, q, 2f / 3);
                }
                else
                {
                    var b = definition.Control1Offset;
                    var c = definition.Control2Offset;
                    _control1 = new GameplayPoint(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
                    _control2 = new GameplayPoint(d.X + c.X, d.Y + c.Y, d.Z + c.Z);
                }
            }
            public GameplayPoint Sample(double age,float delta,GameplayPoint current,GameplayPoint? liveTarget)
            {
                if(_definition.Kind==GameplayProjectileMotionKind.Straight)
                {
                    if(_definition.StraightDestination==GameplayStraightDestinationMode.Direction)
                        return GameplaySpatialMath.Offset(current,_launch.Direction,_launch.Speed*delta);
                    float remaining=GameplaySpatialMath.Distance(current,_launch.Destination);
                    float travel=Math.Min(remaining,_launch.Speed*delta);Arrived=travel>=remaining;
                    return Arrived?_launch.Destination:GameplaySpatialMath.Offset(current,_launch.Direction,travel);
                }
                if(_definition.Kind==GameplayProjectileMotionKind.Homing)
                {
                    var target=liveTarget??_launch.Destination;float distance=GameplaySpatialMath.Distance(current,target);
                    float travel=Math.Min(distance,_launch.Speed*delta);Arrived=travel>=distance;
                    return GameplaySpatialMath.Offset(current,GameplaySpatialMath.Direction(current,target,_launch.Direction),travel);
                }
                float t=(float)Math.Min(1,age/_launch.Duration);Arrived=t>=1;
                if(_definition.Kind==GameplayProjectileMotionKind.Bezier)
                {
                    var a=_launch.Origin;var d=_launch.Destination;
                    var b=_control1;
                    var c=_control2;
                    return GameplaySpatialMath.Lerp(GameplaySpatialMath.Lerp(GameplaySpatialMath.Lerp(a,b,t),GameplaySpatialMath.Lerp(b,c,t),t),
                        GameplaySpatialMath.Lerp(GameplaySpatialMath.Lerp(b,c,t),GameplaySpatialMath.Lerp(c,d,t),t),t);
                }
                var point=GameplaySpatialMath.Lerp(_launch.Origin,_launch.Destination,t);
                float height=_launch.Height*4*t*(1-t);
                if(_definition.Kind==GameplayProjectileMotionKind.Parabolic)return new GameplayPoint(point.X,point.Y+height,point.Z);
                // Arc bends within the selected logical plane; Parabolic uses actual world-up Y.
                if(_launch.Plane==GameplaySpatialPlane.XZ)return new GameplayPoint(point.X-_launch.Direction.Z*height,point.Y,point.Z+_launch.Direction.X*height);
                return new GameplayPoint(point.X-_launch.Direction.Y*height,point.Y+_launch.Direction.X*height,point.Z);
            }
        }
    }
    public sealed class GameplayProjectileDefinition
    {
        public IGameplayProjectileMotionDefinition Motion {get;}
        public IGameplayFloatValue Speed {get;}
        public IGameplayFloatValue Duration {get;}
        public IGameplayFloatValue HitRadius {get;}
        public IGameplayFloatValue MaxRange {get;}
        public IGameplayFloatValue Height {get;}
        public GameplaySpatialPlane Plane {get;}
        public GameplayFactionFilter Faction {get;}
        public GameplayTagQuery Tags {get;}
        public int LayerMask {get;}
        /// <summary>Total distinct targets allowed, including the first hit; never a per-frame counter.</summary>
        public int MaxHits {get;}
        public bool RequiresLiveTarget {get;}
        public bool OnlyHitPrimaryTarget { get; }
        public IReadOnlyList<IGameplaySkillAction> OnHit {get;}
        public IReadOnlyList<IGameplaySkillAction> OnArrive {get;}
        public IReadOnlyList<IGameplaySkillAction> OnRange {get;}
        public IReadOnlyList<IGameplaySkillAction> OnCancelled {get;}
        public IGameplaySpatialViewFactory View {get;}
        public bool CancelOnAbilityEnd {get;}
        /// <summary>Ends a launched projectile when its owner is no longer targetable, even if the World owns it.</summary>
        public bool EndOnOwnerDeath {get;}
        public GameplayPoint LaunchOffset {get;}
        public GameplayPoint DestinationOffset {get;}
        public float DirectionAngle {get;}
        public float ForwardLaunchOffset {get;}
        public bool UseOwnerFacing {get;}
        public GameplayProjectileContactMode ContactMode {get;}
        public float ContactStep {get;}
        public bool ContinueAfterHit {get;}
        public GameplayProjectileDefinition(IGameplayProjectileMotionDefinition motion,IGameplayFloatValue speed,IGameplayFloatValue duration,
            IGameplayFloatValue hitRadius,IGameplayFloatValue maxRange,IGameplayFloatValue height,GameplaySpatialPlane plane=GameplaySpatialPlane.XY,
            GameplayFactionFilter faction=GameplayFactionFilter.Enemies,GameplayTagQuery tags=null,int layerMask=-1,int maxHits=1,bool requiresLiveTarget=false,
            IEnumerable<IGameplaySkillAction> onHit=null,IEnumerable<IGameplaySkillAction> onArrive=null,IEnumerable<IGameplaySkillAction> onRange=null,IEnumerable<IGameplaySkillAction> onCancelled=null,IGameplaySpatialViewFactory view=null,
            bool cancelOnAbilityEnd=true,GameplayPoint launchOffset=default,GameplayPoint destinationOffset=default,float directionAngle=0,
            bool onlyHitPrimaryTarget=false, float forwardLaunchOffset=0, bool useOwnerFacing=false,
            GameplayProjectileContactMode contactMode=GameplayProjectileContactMode.Swept, float contactStep=.02f,
            bool continueAfterHit=false, bool endOnOwnerDeath=false)
        {
            Motion=motion??throw new ArgumentNullException(nameof(motion));Speed=speed??throw new ArgumentNullException(nameof(speed));Duration=duration??throw new ArgumentNullException(nameof(duration));
            HitRadius=hitRadius??throw new ArgumentNullException(nameof(hitRadius));MaxRange=maxRange??throw new ArgumentNullException(nameof(maxRange));Height=height??throw new ArgumentNullException(nameof(height));
            if(!Enum.IsDefined(typeof(GameplaySpatialPlane),plane)||!Enum.IsDefined(typeof(GameplayFactionFilter),faction)||maxHits<1)throw new ArgumentOutOfRangeException(nameof(maxHits));
            Plane=plane;Faction=faction;Tags=tags??GameplayTagQuery.Empty;LayerMask=layerMask;MaxHits=maxHits;RequiresLiveTarget=requiresLiveTarget;
            OnlyHitPrimaryTarget = onlyHitPrimaryTarget;
            OnHit=GameplaySpatialMath.Actions(onHit);OnArrive=GameplaySpatialMath.Actions(onArrive);OnRange=GameplaySpatialMath.Actions(onRange);OnCancelled=GameplaySpatialMath.Actions(onCancelled);View=view;
            CancelOnAbilityEnd=cancelOnAbilityEnd;LaunchOffset=launchOffset;DestinationOffset=destinationOffset;
            EndOnOwnerDeath=endOnOwnerDeath;
            if(!float.IsFinite(directionAngle))throw new ArgumentOutOfRangeException(nameof(directionAngle));DirectionAngle=directionAngle;
            if (contactMode == GameplayProjectileContactMode.FixedEndpointNearest && (!float.IsFinite(contactStep) || contactStep <= 0))
                throw new ArgumentOutOfRangeException(nameof(contactStep));
            ForwardLaunchOffset=forwardLaunchOffset; UseOwnerFacing=useOwnerFacing;
            ContactMode=contactMode; ContactStep=contactStep; ContinueAfterHit=continueAfterHit;
        }
    }
    /// <summary>World-owned projectile; swept centre geometry prevents tunnelling for each sampled
    /// segment. Curves use at most 1/60s segments. Hit identities, level and callbacks survive target/source derivation.</summary>
    public sealed class GameplayProjectile:GameplaySpatialOperation
    {
        private readonly GameplayProjectileDefinition _definition;
        private readonly IGameplayProjectileMotion _motion;
        private readonly float _duration,_radius,_range;
        private readonly GameplaySourceId _target;
        private readonly HashSet<GameplaySourceId> _hits=new HashSet<GameplaySourceId>();
        private double _age, _pending;
        /// <summary>Curve side resolved once at real launch.</summary>
        public float CurveSign { get; } = 1;
        public float DistanceTravelled {get;private set;}
        public int HitCount=>_hits.Count;
        internal GameplayProjectile(GameplayStandardActor owner,GameplayContext context,GameplayAbilityHandle grant,GameplayProjectileDefinition definition,
            GameplayPoint? launchPoint = null)
            :base(owner,context,grant,definition.View,definition.CancelOnAbilityEnd)
        {
            _definition=definition;_duration=GameplaySpatialMath.Positive(definition.Duration,owner,context,"Projectile duration");
            var launch = GameplaySpatialMath.Offset(launchPoint ?? Position, owner.Facing, definition.ForwardLaunchOffset);
            Position=new GameplayPoint(launch.X+definition.LaunchOffset.X,launch.Y+definition.LaunchOffset.Y,launch.Z+definition.LaunchOffset.Z);
            _radius=GameplaySpatialMath.Positive(definition.HitRadius,owner,context,"Hit radius");_range=GameplaySpatialMath.Positive(definition.MaxRange,owner,context,"Max range");
            float speed=GameplaySpatialMath.Positive(definition.Speed,owner,context,"Speed");
            float height=GameplaySpatialMath.Finite(definition.Height,owner,context);
            _target=context.TargetData.PrimaryTargetId==owner.Id?default:context.TargetData.PrimaryTargetId;
            GameplayPoint destination=GameplaySpatialMath.Offset(Position,Direction,_range);
            if(_target!=owner.Id&&owner.World.TryGetActor(_target,out var target))destination=target.Position;
            else if(context.TargetData.HasPoint)destination=context.TargetData.Point;
            destination=new GameplayPoint(destination.X+definition.DestinationOffset.X,destination.Y+definition.DestinationOffset.Y,destination.Z+definition.DestinationOffset.Z);
            if(definition.Motion is GameplayProjectileMotionDefinition pointMotion && pointMotion.Kind==GameplayProjectileMotionKind.Straight &&
                pointMotion.StraightDestination==GameplayStraightDestinationMode.CapturedDestination &&
                !context.TargetData.HasPoint && !owner.World.TryGetActor(_target,out _))
                throw new InvalidOperationException("Straight captured destination requires an explicit point or target.");
            if(definition.RequiresLiveTarget&&(_target==owner.Id||!owner.World.TryGetActor(_target,out var live)||!live.IsTargetable))throw new InvalidOperationException("Homing projectile requires a live explicit target.");
            Direction=definition.UseOwnerFacing ? owner.Facing : GameplaySpatialMath.Direction(Position,destination,Direction);
            if(definition.DirectionAngle!=0){
                double radians=definition.DirectionAngle*Math.PI/180,c=Math.Cos(radians),s=Math.Sin(radians);
                float x=Direction.X,y=Direction.Y,z=Direction.Z;
                Direction=definition.Plane==GameplaySpatialPlane.XY?new GameplayDirection((float)(x*c-y*s),(float)(x*s+y*c),z):
                    definition.Plane==GameplaySpatialPlane.XZ?new GameplayDirection((float)(x*c-z*s),y,(float)(x*s+z*c)):
                    new GameplayDirection(x,(float)(y*c-z*s),(float)(y*s+z*c));
            }
            if (definition.Motion is GameplayProjectileMotionDefinition curve && curve.Kind == GameplayProjectileMotionKind.Bezier &&
                curve.BezierControl == GameplayBezierControlMode.RelativeQuadratic)
                CurveSign = curve.CurveSide switch
                {
                    GameplayCurveSide.Negative => -1,
                    GameplayCurveSide.RandomAtLaunch => owner.World.Random.NextUnitInterval() < .5 ? -1 : 1,
                    _ => 1
                };
            _motion=definition.Motion.Create(new GameplayProjectileLaunch(Position,destination,Direction,definition.Plane,speed,_duration,height,CurveSign))??throw new InvalidOperationException("Motion definition returned no actual sampler.");
        }
        internal override void Advance(float delta)
        {
            if (Owner.IsDisposed) { Finish(GameplaySpatialEndReason.Cancelled); return; }
            if (_definition.EndOnOwnerDeath && !Owner.IsTargetable) { Finish(GameplaySpatialEndReason.Died); return; }
            if (_definition.ContactMode == GameplayProjectileContactMode.FixedEndpointNearest)
            {
                _pending += delta;
                while (!IsDisposed && _age < _duration)
                {
                    double step = Math.Min(_definition.ContactStep, _duration - _age);
                    if (_pending + 1e-8 < step) break;
                    _pending -= step;
                    AdvanceContact(_age + step, true);
                }
            }
            else
            {
                double endpoint = Math.Min(_duration, _age + delta);
                bool linear = _definition.Motion is GameplayProjectileMotionDefinition standard &&
                    (standard.Kind == GameplayProjectileMotionKind.Straight || standard.Kind == GameplayProjectileMotionKind.Homing);
                while (_age < endpoint && !IsDisposed)
                    AdvanceContact(linear || endpoint - _age <= 1d / 60 ? endpoint : _age + 1d / 60, false);
            }
            if (!IsDisposed && _age >= _duration) Finish(GameplaySpatialEndReason.Completed);
        }

        private void AdvanceContact(double nextAge, bool fixedContacts)
        {
            GameplayPoint? liveTarget = null;
            if (_definition.RequiresLiveTarget)
            {
                if (!Owner.World.TryGetActor(_target, out var live) || !live.IsTargetable)
                { Finish(GameplaySpatialEndReason.TargetLost); return; }
                liveTarget = live.Position;
            }

            var previous = Position;
            float step = (float)(nextAge - _age);
            _age = nextAge;
            var next = _motion.Sample(_age, step, Position, liveTarget);
            float distance = GameplaySpatialMath.Distance(previous, next);
            float rangeLeft = _range - DistanceTravelled;
            bool reachedRange = distance >= rangeLeft;
            // Both modes stop at MaxRange. Fixed contacts sample only the clipped endpoint.
            bool rangeClipped = distance > rangeLeft;
            if (rangeClipped && distance > 0)
            { next = GameplaySpatialMath.Lerp(previous, next, rangeLeft / distance); distance = rangeLeft; }
            Direction = GameplaySpatialMath.Direction(previous, next, Direction);
            Position = next;
            DistanceTravelled += distance;
            Notify();

            var geometry = !fixedContacts && distance > 0
                ? new GameplaySearchGeometry(GameplaySearchShape.Capsule, _definition.Plane, previous, Direction, _radius, length: distance, height: _radius * 2)
                : new GameplaySearchGeometry(GameplaySearchShape.Circle, _definition.Plane, Position, Direction, _radius, height: _radius * 2);
            var selected = Owner.World.Spatial.Query(new GameplaySpatialSearchRequest(Owner.World.ResolveFaction, Context, geometry, Owner.Faction, _definition.Faction,
                _definition.Tags, _definition.LayerMask, maxTargets: fixedContacts ? 1 : int.MaxValue,
                excluded: fixedContacts ? id => _hits.Contains(id) || _definition.OnlyHitPrimaryTarget && id != _target : null,
                useActorCenters: fixedContacts));
            foreach (var id in selected.Targets)
            {
                if (IsDisposed) break;
                if (_definition.OnlyHitPrimaryTarget && id != _target ||
                    !Owner.World.TryGetActor(id, out var target) || !target.IsTargetable || !_hits.Add(id)) continue;
                Run(_definition.OnHit, At(id, target.Position), "Event.Standard.Projectile.Hit", DistanceTravelled);
                if (!IsDisposed && !_definition.ContinueAfterHit && _hits.Count >= _definition.MaxHits)
                    Finish(GameplaySpatialEndReason.HitLimit);
            }
            if (IsDisposed) return;
            if (_motion.Arrived && !rangeClipped) Finish(GameplaySpatialEndReason.Arrived);
            else if (reachedRange) Finish(GameplaySpatialEndReason.Range);
        }
        protected override void OnFinished(GameplaySpatialEndReason reason)
        {
            if(reason==GameplaySpatialEndReason.Arrived)Run(_definition.OnArrive,At(_target,Position),"Event.Standard.Projectile.Arrive",DistanceTravelled,true);
            else if(reason==GameplaySpatialEndReason.Range)Run(_definition.OnRange,At(_target,Position),"Event.Standard.Projectile.Range",DistanceTravelled,true);
            else if(reason==GameplaySpatialEndReason.Cancelled||reason==GameplaySpatialEndReason.TargetLost)Run(_definition.OnCancelled,At(_target,Position),"Event.Standard.Projectile.Cancelled",DistanceTravelled,true);
        }
    }
    public sealed partial class GameplayStandardWorld
    {
        public GameplayProjectile SpawnProjectile(GameplayStandardActor owner,GameplayContext context,GameplayProjectileDefinition definition)
        {if(definition==null)throw new ArgumentNullException(nameof(definition));ValidateSpatialOwner(owner,context,out var grant);return RegisterSpatial(new GameplayProjectile(owner,context,grant,definition),grant);}

        internal GameplayProjectile SpawnProjectileFromAction(GameplayStandardActor owner, GameplayContext context,
            GameplayProjectileDefinition definition, GameplayPoint? launchPoint, GameplayContext trigger)
        {
            if (IsDisposed || owner.IsDisposed) return null;
            if (!GameplayStandardActivation.TryFindGrant(owner, context, out var grant) &&
                (definition.CancelOnAbilityEnd || trigger == null || trigger.ActivationSourceId != context.ActivationSourceId ||
                 !_spatial.Exists(operation => operation.Owner == owner && !operation.OwnedByAbility && operation.Id == trigger.EffectiveSourceId)))
                return null;
            return RegisterSpatial(new GameplayProjectile(owner, context, grant, definition, launchPoint), grant);
        }
    }
    public sealed class GameplaySpawnProjectileAction:IGameplaySkillAction
    {
        public GameplayProjectileDefinition Definition {get;}
        private readonly bool _launchFromTriggerPoint;
        public GameplaySpawnProjectileAction(GameplayProjectileDefinition definition, bool launchFromTriggerPoint = false)
        {
            Definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _launchFromTriggerPoint = launchFromTriggerPoint;
        }
        public GameplaySkillActionResult Execute(GameplayContext context,GameplayEventData triggerEvent,IGameplaySkillHost host)
        {
            if (!(host is GameplayStandardActor actor) || Definition.EndOnOwnerDeath && !actor.IsTargetable ||
                ((Definition.RequiresLiveTarget || Definition.OnlyHitPrimaryTarget) &&
                 (!actor.World.TryGetActor(context.TargetData.PrimaryTargetId, out var target) || !target.IsTargetable || target == actor)))
                return GameplaySkillActionResult.Rejected;
            var trigger = triggerEvent.Context;
            if (_launchFromTriggerPoint && (trigger == null || !trigger.TargetData.HasPoint)) return GameplaySkillActionResult.Rejected;
            var projectile = actor.World.SpawnProjectileFromAction(actor, context, Definition,
                _launchFromTriggerPoint ? trigger.TargetData.Point : (GameplayPoint?)null, trigger);
            return projectile != null ? GameplaySkillActionResult.Succeeded : GameplaySkillActionResult.Rejected;
        }
    }
}
