using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay
{
    /// <summary>Owns marker timing and execution. The host explicitly advances it; Effects keep their own lifetime.</summary>
    public sealed class GameplaySequenceContainer
    {
        private readonly object _handleOwner = new object();
        private readonly List<Entry> _active = new List<Entry>();
        private readonly List<GameplayCueRequest> _cues = new List<GameplayCueRequest>();
        private long _lastId;

        public int ActiveCount => _active.Count;
        /// <summary>Emitted once after detaching a completed/cancelled/cleared sequence, for owned cleanup.</summary>
        public event Action<GameplaySequenceTermination> Terminated;

        /// <summary>Captures marker progress in Start order without executing any marker.</summary>
        public IReadOnlyList<GameplayActiveSequenceSnapshot> GetActiveEntriesSnapshot()
        {
            var result = new List<GameplayActiveSequenceSnapshot>(_active.Count);
            foreach (Entry entry in _active)
            {
                float? next = entry.NextIndex < entry.Definition.Markers.Count
                    ? entry.Definition.Markers[entry.NextIndex].Time : (float?)null;
                result.Add(new GameplayActiveSequenceSnapshot(entry.Handle, entry.Definition,
                    entry.Context, (float)entry.Elapsed, entry.NextIndex, next));
            }
            return result.AsReadOnly();
        }

        /// <summary>Starts Cue/ApplyEffect content without an event. SkillStep content requires StartSkill.</summary>
        public GameplaySequenceHandle Start(GameplaySequenceDefinition definition, GameplayContext context,
            IGameplaySkillHost host) => StartInternal(definition, context, host, null);

        /// <summary>Captures the actual Skill trigger event; marker-time steps never synthesize an event.</summary>
        public GameplaySequenceHandle StartSkill(GameplaySequenceDefinition definition, GameplayContext context,
            GameplayEventData triggerEvent, IGameplaySkillHost host)
        {
            if (!triggerEvent.EventTag.IsValid)
                throw new ArgumentException("Skill-started Sequence requires a valid trigger event.", nameof(triggerEvent));
            return StartInternal(definition, context, host, triggerEvent);
        }

        private GameplaySequenceHandle StartInternal(GameplaySequenceDefinition definition, GameplayContext context,
            IGameplaySkillHost host, GameplayEventData? triggerEvent)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (!triggerEvent.HasValue)
                foreach (var marker in definition.Markers)
                    if (marker is GameplaySequenceSkillStepMarkerDefinition)
                        throw new InvalidOperationException("SkillStep requires StartSkill with the original trigger event.");
            var handle = new GameplaySequenceHandle(_handleOwner, checked(++_lastId));
            var entry = new Entry(handle, definition, context, host, triggerEvent);
            _active.Add(entry);
            ExecuteThrough(entry, 0f);
            if (Complete(entry)) Finish(entry, GameplaySequenceTerminationReason.Completed);
            return handle;
        }

        /// <summary>Advances entries that existed before this call; Start itself never consumes a past delta.</summary>
        public void Advance(float deltaTime)
        {
            if (!float.IsFinite(deltaTime) || deltaTime < 0f) throw new ArgumentOutOfRangeException(nameof(deltaTime));
            // Actions may start or cancel Sequences. Snapshot membership, not mutable list indexes.
            foreach (Entry entry in _active.ToArray())
            {
                if (entry.Cancelled) continue;
                double next = Math.Min((double)entry.Definition.Duration, entry.Elapsed + (double)deltaTime);
                ExecuteThrough(entry, next);
                entry.Elapsed = next;
            }
            foreach (var entry in _active.ToArray())
                if (Complete(entry)) Finish(entry, GameplaySequenceTerminationReason.Completed);
        }

        /// <summary>Moves this rule step's Cue requests to the host output in deterministic execution order.</summary>
        public void DrainCues(ICollection<GameplayCueRequest> destination)
        {
            if (destination == null) throw new ArgumentNullException(nameof(destination));
            foreach (var cue in _cues) destination.Add(cue);
            _cues.Clear();
        }

        public bool IsActive(GameplaySequenceHandle handle) =>
            handle.Owner == _handleOwner && _active.Exists(entry => entry.Handle.Id == handle.Id);

        public bool Cancel(GameplaySequenceHandle handle)
        {
            if (handle.Owner != _handleOwner) return false;
            Entry entry = _active.Find(item => item.Handle.Id == handle.Id);
            if (entry == null) return false;
            Finish(entry, GameplaySequenceTerminationReason.Cancelled);
            return true;
        }

        /// <summary>Idempotent cancellation relation only; neither advances nor owns Sequence rule time.</summary>
        public IDisposable CreateCancellationLease(GameplaySequenceHandle handle)
        {
            if (handle.Owner != _handleOwner) throw new ArgumentException("Sequence handle belongs to another container.", nameof(handle));
            return new CancellationLease(this, handle);
        }
        private sealed class CancellationLease : IDisposable
        {
            private GameplaySequenceContainer _container;
            private readonly GameplaySequenceHandle _handle;
            internal CancellationLease(GameplaySequenceContainer container, GameplaySequenceHandle handle) { _container = container; _handle = handle; }
            public void Dispose() { var container = _container; _container = null; container?.Cancel(_handle); }
        }

        /// <summary>Discards pending marker work and undrained Cues; does not remove Effects already applied.</summary>
        public void Clear()
        {
            var entries = _active.ToArray();
            foreach (Entry entry in entries) entry.Cancelled = true;
            _active.Clear();
            _cues.Clear();
            List<Exception> errors = null;
            foreach (var entry in entries)
                try { Finish(entry, GameplaySequenceTerminationReason.Cleared); }
                catch (Exception ex) { (errors ?? (errors = new List<Exception>())).Add(ex); }
            if (errors != null) throw new AggregateException(errors);
        }

        private void Finish(Entry entry, GameplaySequenceTerminationReason reason)
        {
            if (entry.Terminated) return;
            entry.Terminated = true; entry.Cancelled = true; _active.Remove(entry);
            var observation = new GameplaySequenceTermination(entry.Handle, entry.Context, reason);
            List<Exception> errors = null;
            if (Terminated != null)
                foreach (Action<GameplaySequenceTermination> observer in Terminated.GetInvocationList())
                    try { observer(observation); } catch (Exception ex) { (errors ?? (errors = new List<Exception>())).Add(ex); }
            if (errors != null) throw new AggregateException(errors);
        }

        private void ExecuteThrough(Entry entry, double time)
        {
            var markers = entry.Definition.Markers;
            // Content/API time is float. Compare its rounded projection, but never accumulate that rounding.
            // This preserves short authored boundaries such as 3/30 + 1/30 == 4/30 in the public time domain.
            float authoredTime = (float)time;
            while (!entry.Cancelled && entry.NextIndex < markers.Count && markers[entry.NextIndex].Time <= authoredTime)
            {
                var marker = markers[entry.NextIndex++];
                if (marker is GameplaySequenceCueMarkerDefinition cue)
                    _cues.Add(new GameplayCueRequest(cue.CueTag, entry.Context, cue.Magnitude, cue.Duration));
                else if (marker is GameplaySequenceApplyEffectMarkerDefinition effect)
                {
                    if (entry.Context.TargetData.PrimaryTargetId != entry.Host.OwnerId)
                        throw new InvalidOperationException("Sequence ApplyEffect marker currently requires Self target.");
                    if (entry.Host is IGameplaySkillEffectHost routed)
                        routed.ApplyEffect(entry.Context, effect.Effect, GameplaySetByCallerValues.Empty);
                    else
                        entry.Host.Effects.Apply(effect.Effect, new GameplayEffectApplicationContext(
                            entry.Context, entry.Host, GameplaySetByCallerValues.Empty));
                }
                else if (marker is GameplaySequenceSkillStepMarkerDefinition step)
                    ExecuteStep(entry, step);
            }
        }

        private static void ExecuteStep(Entry entry, GameplaySequenceSkillStepMarkerDefinition step)
        {
            GameplayEventData triggerEvent = entry.TriggerEvent.Value;
            foreach (var condition in step.Conditions)
                if (entry.Cancelled || !GameplaySkillActionRunner.Evaluate(condition, entry.Context, triggerEvent, entry.Host)) return;
            if (entry.Cancelled) return;
            GameplayTargetData target = step.Target.Resolve(entry.Host, triggerEvent);
            if (target == null || entry.Cancelled) return;
            var context = entry.Context.WithTarget(target);
            GameplaySkillActionRunner.Execute(step.Actions, context, triggerEvent, entry.Host, () => !entry.Cancelled);
        }

        private static bool Complete(Entry entry) =>
            (float)entry.Elapsed >= entry.Definition.Duration && entry.NextIndex >= entry.Definition.Markers.Count;

        private sealed class Entry
        {
            public bool Terminated;
            public readonly GameplaySequenceHandle Handle;
            public readonly GameplaySequenceDefinition Definition;
            public readonly GameplayContext Context;
            public readonly IGameplaySkillHost Host;
            public readonly GameplayEventData? TriggerEvent;
            public bool Cancelled;
            // Keep one precise rule timeline: per-step float rounding can delay a long authored marker by a frame.
            // Authored times and public snapshots remain float; never feed snapshot rounding back into scheduling.
            public double Elapsed;
            public int NextIndex;
            public Entry(GameplaySequenceHandle handle, GameplaySequenceDefinition definition,
                GameplayContext context, IGameplaySkillHost host, GameplayEventData? triggerEvent)
            { Handle = handle; Definition = definition; Context = context; Host = host; TriggerEvent = triggerEvent; }
        }
    }
}
