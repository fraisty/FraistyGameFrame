using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Extensions;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>One finite, cast-owned contact window with independent motion history and hit accounting.</summary>
    public sealed class GameplayHitWindowOperation : GameplaySpatialOperation
    {
        private readonly struct TargetSample
        {
            internal readonly GameplayPoint Position, Center;
            internal readonly float Radius;
            internal readonly long Revision;
            internal TargetSample(GameplayStandardActor actor)
            {
                var shape = actor.Hurtbox;
                Position = actor.Position;
                Center = shape.Center(Position);
                Radius = shape.Radius;
                Revision = actor.HitPoseRevision;
            }

            internal bool Matches(TargetSample other) => Revision == other.Revision && Radius == other.Radius &&
                SamePoint(Position, other.Position) && SamePoint(Center, other.Center);
        }

        private readonly struct TargetMotion
        {
            internal readonly GameplayStandardActor Actor;
            internal readonly GameplayPoint Start, End;
            internal readonly TargetSample Sample;
            internal readonly bool Sweep;

            internal TargetMotion(GameplayStandardActor actor, GameplayPoint start, GameplayPoint end, TargetSample sample, bool sweep)
            { Actor = actor; Start = start; End = end; Sample = sample; Sweep = sweep; }
        }

        private readonly struct Hit
        {
            internal readonly TargetMotion Target;
            internal readonly GameplayPoint Point;
            internal readonly double Time;
            internal readonly double Distance;
            internal Hit(TargetMotion target, GameplayPoint point, double time, double distance)
            { Target = target; Point = point; Time = time; Distance = distance; }
        }

        private readonly Dictionary<GameplaySourceId, TargetSample> _previousTargets = new();
        private readonly Dictionary<GameplaySourceId, (int count, double time)> _hits = new();
        private readonly SortedSet<Hit> _contacts;
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
        {
            Definition = definition;
            _contacts = new SortedSet<Hit>(Comparer<Hit>.Create((a, b) =>
            {
                int time = a.Time.CompareTo(b.Time);
                if (time != 0) return time;
                int distance = Definition.Order == GameplayTargetOrder.SourceId ? 0 : a.Distance.CompareTo(b.Distance);
                if (Definition.Order == GameplayTargetOrder.Farthest) distance = -distance;
                return distance != 0 ? distance : a.Target.Actor.Id.Value.CompareTo(b.Target.Actor.Id.Value);
            }));
        }

        protected override void OnStart() => Sample(0, 1);

        internal override void Advance(float delta)
        {
            if (delta == 0 || _scanning) return;
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
            if ((first || Definition.FollowAnchor) && !TryReadAnchor(out actual)) return;
            bool cut = !first && (_previousPose.Revision != actual.Revision || _ownerRevision != Owner.HitPoseRevision);
            if (cut) Diagnose(GameplaySkillTraceStage.ActionSucceeded, "Cut", "Sweep disconnected at pose discontinuity");
            var startPose = first || cut ? actual : _previousPose;
            var pose = first || cut || fraction == 1 ? actual : new GameplayHitContact.Motion(Definition.Plane, startPose, actual).At(fraction);
            var motion = new GameplayHitContact.Motion(Definition.Plane, startPose, pose);
            Geometry = Definition.Geometry(pose);
            Position = Geometry.Origin;
            Direction = Geometry.Direction;
            SampleCount++;
            _scanning = true;
            try
            {
                if (!cut || fraction == 1) Collect(motion, delta, fraction, first || cut);
                _previousPose = actual;
                _ownerRevision = Owner.HitPoseRevision;
                Notify();
                bool ranHit = false;
                bool ownerMotionIsCurrent = CanAttack() && (_contacts.Count == 0 || RefreshOwnerMotion());
                while (ownerMotionIsCurrent && _contacts.Count != 0)
                {
                    var hit = _contacts.Min;
                    _contacts.Remove(hit);
                    if (!TargetMotionIsCurrent(hit.Target)) continue;
                    var id = hit.Target.Actor.Id;
                    bool wasHit = _hits.TryGetValue(id, out var previous);
                    if (!wasHit && _hits.Count >= Definition.MaxDistinctTargets) { LimitedCandidates++; continue; }
                    // Update before invoking authored actions. A cancelled or reentered callback
                    // cannot claim this target a second time.
                    int count = previous.count + 1;
                    _hits[id] = (count, hit.Time);
                    HitCount++;
                    var context = At(id, hit.Point);
                    Diagnose(GameplaySkillTraceStage.ActionSucceeded, "Contact",
                        $"t={hit.Time:0.000}, target={id}, hurtbox={(hit.Target.Sample.Radius > 0 ? "Sphere" : "Point")}", context);
                    Run(Definition.OnHit, context, "Event.Standard.HitWindow.Hit");
                    ranHit = true;
                    ownerMotionIsCurrent = CanAttack() && RefreshOwnerMotion();
                    if (ownerMotionIsCurrent && count < Definition.MaxHitsPerTarget && TargetMotionIsCurrent(hit.Target))
                        QueueContact(hit.Target, motion, delta, hit.Time + Definition.RehitInterval);
                }
                // A callback can also move targets with no queued hit. Sample the resulting
                // state once for the next step so it never sweeps across the callback's jump.
                if (ranHit && !IsDisposed)
                    foreach (var actor in Owner.World.Actors)
                        if (_previousTargets.ContainsKey(actor.Id)) _previousTargets[actor.Id] = new TargetSample(actor);
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

        private bool CanTarget(GameplayStandardActor actor) =>
            GameplaySpatialSearchRequest.CanTarget(actor, Owner.Id, Owner.Faction, Definition.Faction, Definition.Tags, Definition.LayerMask, relations: Owner.World.ResolveFaction);

        private bool TargetMotionIsCurrent(TargetMotion target) =>
            CanTarget(target.Actor) && target.Sample.Matches(new TargetSample(target.Actor));

        private bool TryReadAnchor(out GameplayHitPose pose)
        {
            if (Owner.TrySampleHitAnchor(Definition.Anchor, Owner.World.HitSampleTime, out pose) && Definition.AcceptsPose(pose))
                return true;
            Diagnose(GameplaySkillTraceStage.ActionRejected, "AnchorUnavailable", Definition.Anchor.Kind + " " + Definition.Anchor.Path + " " + Definition.Anchor.Bone);
            Finish(GameplaySpatialEndReason.TargetLost);
            return false;
        }

        // A mutated anchor cuts the remaining queue. Its new pose seeds the next step
        // without inferring continuous motion through an edit at callback time.
        private bool RefreshOwnerMotion()
        {
            long revision = Owner.HitPoseRevision;
            bool sameRevision = revision == _ownerRevision;
            _ownerRevision = revision;
            if (!Definition.FollowAnchor) return sameRevision;
            if (!TryReadAnchor(out var current)) return false;
            var sampled = _previousPose;
            _previousPose = current;
            return sameRevision && sampled.Revision == current.Revision && SamePoint(sampled.Position, current.Position) &&
                SameDirection(sampled.Forward, current.Forward) && SameDirection(sampled.Up, current.Up);
        }

        private static bool SamePoint(GameplayPoint a, GameplayPoint b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;
        private static bool SameDirection(GameplayDirection a, GameplayDirection b) => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

        private void Collect(GameplayHitContact.Motion motion, float delta, float fraction, bool cut)
        {
            var definition = Definition;
            foreach (var actor in Owner.World.Actors)
            {
                if (!CanTarget(actor))
                {
                    _previousTargets.Remove(actor.Id);
                    continue;
                }
                var next = new TargetSample(actor);
                bool hasPrevious = _previousTargets.TryGetValue(actor.Id, out var old);
                _previousTargets[actor.Id] = next;
                bool targetCut = !hasPrevious || old.Revision != next.Revision || old.Radius != next.Radius;
                if (targetCut && fraction < 1) continue;
                var end = targetCut || fraction == 1 ? next.Center : GameplaySpatialMath.Lerp(old.Center, next.Center, fraction);
                bool repeat = _hits.TryGetValue(actor.Id, out var previous);
                if (repeat && previous.count >= definition.MaxHitsPerTarget ||
                    !repeat && _hits.Count >= definition.MaxDistinctTargets)
                { LimitedCandidates++; continue; }
                bool sweep = definition.SweepPolicy == GameplayHitSweepPolicy.Swept && !cut && !targetCut;
                var target = new TargetMotion(actor, sweep ? old.Center : end, end, next, sweep);
                QueueContact(target, motion, delta, repeat ? previous.time + definition.RehitInterval : _age);
            }
        }

        // Keep one pending hit per target. Only a callback that actually ran can schedule a
        // rehit, so cancel/death/teleport cannot leave a precomputed batch of damage behind.
        private void QueueContact(TargetMotion target, GameplayHitContact.Motion motion, float delta, double due)
        {
            double endTime = _age + delta;
            if (due > endTime) return;
            double startTime = Math.Max(_age, due);
            float fraction = target.Sweep && delta > 0 ? (float)((startTime - _age) / delta) : 1;
            var from = motion.At(fraction);
            var to = motion.At(1);
            var start = GameplaySpatialMath.Lerp(target.Start, target.End, fraction);
            bool found = GameplayHitContact.TryFind(Definition, from, to, start, target.End, target.Sample.Radius,
                target.Sweep && fraction < 1, out var t, out var point, out var exhausted);
            if (exhausted) Diagnose(GameplaySkillTraceStage.ActionRejected, "SweepBudget", "Candidate " + target.Actor.Id);
            if (!found) return;
            var origin = Definition.Geometry(motion.At(fraction + t * (1 - fraction))).Origin;
            _contacts.Add(new Hit(target, point, startTime + t * (endTime - startTime),
                GameplaySearchGeometry.DistanceSquared(origin, GameplaySpatialMath.Lerp(start, target.End, t))));
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

