using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>One finite, cast-owned contact window with independent motion history and hit accounting.</summary>
    public sealed class GameplayHitWindowOperation : GameplaySpatialOperation
    {
        private readonly struct TargetSample
        {
            internal readonly GameplayPoint Center;
            internal readonly float Radius;
            internal readonly long Revision;
            internal TargetSample(GameplayStandardActor actor)
            { var shape = actor.Hurtbox; Center = shape.Center(actor.Position); Radius = shape.Radius; Revision = actor.HitPoseRevision; }
        }

        private readonly struct Hit
        {
            internal readonly GameplayStandardActor Actor;
            internal readonly GameplayPoint Point;
            internal readonly float Time;
            internal readonly double Distance;
            internal Hit(GameplayStandardActor actor, GameplayPoint point, float time, double distance)
            { Actor = actor; Point = point; Time = time; Distance = distance; }
        }

        private readonly Dictionary<GameplaySourceId, TargetSample> _previousTargets = new();
        private readonly Dictionary<GameplaySourceId, (int count, double time)> _hits = new();
        private readonly List<Hit> _contacts = new();
        private GameplayHitPose _previousPose;
        private long _ownerRevision;
        private bool _scanning;
        private double _age;
        public GameplayHitWindowDefinition Definition { get; }
        public GameplaySearchGeometry Geometry { get; private set; }
        public float Age => (float)_age;
        public int HitCount { get; private set; }
        public int DistinctTargets => _hits.Count;
        public int SampleCount { get; private set; }
        public int LimitedCandidates { get; private set; }

        internal GameplayHitWindowOperation(GameplayStandardActor owner, GameplayContext context,
            GameplayAbilityHandle grant, GameplayHitWindowDefinition definition) : base(owner, context, grant, null, definition.OwnedByAbility)
        { Definition = definition; }

        protected override void OnStart() => Sample(0, 1);

        internal override void Advance(float delta)
        {
            if (delta == 0) return;
            float active = (float)Math.Min(delta, Definition.Duration - _age);
            Sample(active, active / delta);
            _age += active;
            if (!IsDisposed && (float)_age >= Definition.Duration) Finish(GameplaySpatialEndReason.Completed);
        }

        private bool CanAttack()
        {
            if (IsDisposed) return false;
            if (!Owner.IsTargetable) { Finish(GameplaySpatialEndReason.Died); return false; }
            if (Definition.StopTag.IsValid && Owner.Tags.HasTag(Definition.StopTag))
            { Finish(GameplaySpatialEndReason.Controlled); return false; }
            return true;
        }

        private void Sample(float delta, float fraction)
        {
            if (_scanning || !CanAttack()) return;
            GameplayHitPose actual = _previousPose;
            bool first = SampleCount == 0;
            if ((first || Definition.FollowAnchor) &&
                (!Owner.TrySampleHitAnchor(Definition.Anchor, Owner.World.HitSampleTime, out actual) || !Definition.Accepts(actual)))
            {
                Diagnose(GameplaySkillTraceStage.ActionRejected, "AnchorUnavailable", Definition.Anchor.Kind + " " + Definition.Anchor.Path + " " + Definition.Anchor.Bone);
                Finish(GameplaySpatialEndReason.TargetLost);
                return;
            }
            bool cut = !first && (_previousPose.Revision != actual.Revision || _ownerRevision != Owner.HitPoseRevision ||
                GameplaySpatialMath.Distance(_previousPose.Position, actual.Position) > Definition.MaxSweepDistance);
            float fullRotation = first || cut ? 0 : GameplayHitContact.Rotation(_previousPose, actual);
            cut |= fullRotation >= Definition.MaxSweepAngle * ((float)Math.PI / 180);
            if (cut) Diagnose(GameplaySkillTraceStage.ActionSucceeded, "Cut", "Sweep disconnected at pose discontinuity");
            var pose = first || cut || fraction == 1 ? actual : GameplayHitContact.Interpolate(_previousPose, actual, fraction, fullRotation);
            Geometry = Definition.Geometry(pose);
            Position = Geometry.Origin;
            Direction = Geometry.Direction;
            SampleCount++;
            _scanning = true;
            try
            {
                if (!cut || fraction == 1) Collect(pose, delta, fraction, fullRotation * fraction, first || cut);
                _previousPose = actual;
                _ownerRevision = Owner.HitPoseRevision;
                Notify();
                foreach (var hit in _contacts)
                {
                    if (!CanAttack()) break;
                    if (!hit.Actor.IsTargetable) continue;
                    var id = hit.Actor.Id;
                    bool wasHit = _hits.TryGetValue(id, out var previous);
                    if (!wasHit && _hits.Count >= Definition.MaxDistinctTargets) continue;
                    // Update before invoking authored actions. A cancelled or reentered callback
                    // cannot claim this target a second time.
                    _hits[id] = (previous.count + 1, _age + delta * hit.Time);
                    HitCount++;
                    var context = At(id, hit.Point);
                    Diagnose(GameplaySkillTraceStage.ActionSucceeded, "Contact",
                        $"t={_hits[id].time:0.000}, target={id}, hurtbox={(hit.Actor.Hurtbox.Radius > 0 ? "Sphere" : "Point")}", context);
                    Run(Definition.OnHit, context, "Event.Standard.HitWindow.Hit");
                }
                if (!IsDisposed) CanAttack();
            }
            catch
            {
                Finish(GameplaySpatialEndReason.Interrupted);
                throw;
            }
            finally
            {
                _contacts.Clear();
                _scanning = false;
            }
        }

        private void Collect(GameplayHitPose pose, float delta, float fraction, float rotation, bool cut)
        {
            var definition = Definition;
            foreach (var actor in Owner.World.Actors)
            {
                if (!GameplaySpatialSearchRequest.CanTarget(actor, Owner.Id, Owner.Faction, definition.Faction, definition.Tags, definition.LayerMask))
                {
                    _previousTargets.Remove(actor.Id);
                    continue;
                }
                var next = new TargetSample(actor);
                bool hasPrevious = _previousTargets.TryGetValue(actor.Id, out var old);
                _previousTargets[actor.Id] = next;
                bool targetCut = !hasPrevious || old.Revision != next.Revision || old.Radius != next.Radius ||
                    GameplaySpatialMath.Distance(old.Center, next.Center) > definition.MaxSweepDistance;
                if (targetCut && fraction < 1) continue;
                var end = targetCut || fraction == 1 ? next.Center : GameplaySpatialMath.Lerp(old.Center, next.Center, fraction);
                bool repeat = _hits.TryGetValue(actor.Id, out var previous);
                if (repeat && previous.count >= definition.MaxHitsPerTarget ||
                    !repeat && _hits.Count >= definition.MaxDistinctTargets)
                { LimitedCandidates++; continue; }
                var eligible = repeat ? previous.time + definition.RehitInterval - _age : 0;
                if (eligible > delta) continue;

                bool sweep = definition.SweepPolicy == GameplayHitSweepPolicy.Swept && !cut && !targetCut;
                float startFraction = eligible > 0 ? (float)(eligible / delta) : 0;
                var start = sweep ? GameplaySpatialMath.Lerp(old.Center, end, startFraction) : end;
                var startPose = sweep
                    ? GameplayHitContact.Interpolate(_previousPose, pose, startFraction, rotation)
                    : pose;
                bool contact = GameplayHitContact.TryFind(definition, startPose, pose, start, end, next.Radius,
                    sweep ? rotation * (1 - startFraction) : 0, sweep, out var t, out var point, out var exhausted);
                if (exhausted) Diagnose(GameplaySkillTraceStage.ActionRejected, "SweepBudget", "Candidate " + actor.Id);
                if (contact)
                {
                    var contactPose = sweep ? GameplayHitContact.Interpolate(startPose, pose, t, rotation * (1 - startFraction)) : pose;
                    var origin = definition.Geometry(contactPose).Origin;
                    _contacts.Add(new Hit(actor, point, startFraction + t * (1 - startFraction),
                        GameplaySearchGeometry.DistanceSquared(origin, GameplaySpatialMath.Lerp(start, end, t))));
                }
            }
            _contacts.Sort((a, b) =>
            {
                int time = a.Time.CompareTo(b.Time);
                if (time != 0) return time;
                int distance = definition.Order == GameplayTargetOrder.SourceId ? 0 : a.Distance.CompareTo(b.Distance);
                if (definition.Order == GameplayTargetOrder.Farthest) distance = -distance;
                return distance == 0 ? a.Actor.Id.Value.CompareTo(b.Actor.Id.Value) : distance;
            });
        }

        protected override void OnFinished(GameplaySpatialEndReason reason)
        {
            Diagnose(GameplaySkillTraceStage.ActionSucceeded, "Closed", $"{reason} · {HitCount} hits / {SampleCount} samples / {LimitedCandidates} hit-limit skips");
            var completed = reason == GameplaySpatialEndReason.Completed;
            Run(completed ? Definition.OnWindowFinished : Definition.OnCancelled, At(default, Position),
                completed ? "Event.Standard.HitWindow.Finished" : "Event.Standard.HitWindow.Cancelled", HitCount, true);
            _previousTargets.Clear();
        }

        /// <summary>An Ability ending early interrupts an active window. The natural callback belongs to its own duration.</summary>
        public override void Terminate(GameplayAbilityTerminationObservation observation) => Finish(GameplaySpatialEndReason.Cancelled);

        private void Diagnose(GameplaySkillTraceStage stage, string operation, string detail, GameplayContext context = null)
        {
            if (Owner.SkillTrace.Enabled)
                Owner.RecordSkillTrace(new GameplaySkillTraceRecord(Owner.Id, context ?? Context, default,
                    stage, "HitWindow." + operation, detail: detail));
        }
    }
}

