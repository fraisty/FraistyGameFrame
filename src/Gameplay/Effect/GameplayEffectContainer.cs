using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay
{
    /// <summary>Single lifecycle owner for aggregate effects and exact layer contributions; time is supplied by the host.</summary>
    public sealed partial class GameplayEffectContainer
    {
        private readonly AttributeSet _target;
        private readonly GameplaySourceIdAllocator _sourceIdAllocator;
        private readonly GameplayTagContainer _targetTags;
        private readonly object _handleOwner = new object();
        private readonly Dictionary<GameplayEffectHandle, Entry> _entries = new Dictionary<GameplayEffectHandle, Entry>();
        private long _lastSequence;
        internal AttributeSet TargetAttributes => _target;
        /// <summary>Synchronous post-cleanup observation. G3 remains the natural-expiry continuation authority.</summary>
        public event Action<GameplayEffectRemovalObservation> Removed;
        public GameplayEffectRemovalObservation LastRemoval { get; private set; }
        public GameplayEffectContainer(AttributeSet target, GameplaySourceIdAllocator sourceIdAllocator) : this(target, sourceIdAllocator, null) { }
        public GameplayEffectContainer(AttributeSet target, GameplaySourceIdAllocator sourceIdAllocator, GameplayTagContainer targetTags)
        { _target = target ?? throw new ArgumentNullException(nameof(target)); _sourceIdAllocator = sourceIdAllocator ?? throw new ArgumentNullException(nameof(sourceIdAllocator)); _targetTags = targetTags; }

        private List<Entry> OrderedEntries()
        { var entries = new List<Entry>(_entries.Values); entries.Sort((a,b) => a.Handle.Sequence.CompareTo(b.Handle.Sequence)); return entries; }
        /// <summary>Detached ordered capture without advancing rule time.</summary>
        public IReadOnlyList<GameplayActiveEffectSnapshot> GetActiveEntriesSnapshot()
        {
            var result = new List<GameplayActiveEffectSnapshot>();
            foreach (var e in OrderedEntries())
            {
                var layers = new List<GameplayEffectLayerSnapshot>();
                foreach (var l in e.Layers) layers.Add(new GameplayEffectLayerSnapshot(l.Handle, l.Application,
                    e.Finite ? (float?)Math.Max(0d, (e.Independent ? l.ExpiresAt : e.ExpiresAt) - e.Elapsed) : null, l.Duration));
                result.Add(new GameplayActiveEffectSnapshot(e.Handle, e.Definition, e.SourceId, e.Application?.Context,
                    e.StackCount, e.Finite ? (float?)e.Remaining : null, e.Period > 0 ? (float?)e.PeriodRemaining : null,
                    e.Duration, e.Period, layers));
            }
            return result.AsReadOnly();
        }
        public IReadOnlyList<GameplayEffectHandle> GetMatchingEffects(GameplayEffectQuery query)
        {
            if (query == null) throw new ArgumentNullException(nameof(query));
            var result = new List<GameplayEffectHandle>();
            foreach (var e in OrderedEntries()) if (query.Matches(e.Definition)) result.Add(e.Handle);
            return result.AsReadOnly();
        }
        /// <summary>Snapshots handles before callbacks; new entries created during removal are never included.</summary>
        public int RemoveMatching(GameplayEffectQuery query, GameplayEffectRemovalCause cause = GameplayEffectRemovalCause.Dispel)
        {
            ValidateRemovalRequest(cause);
            var handles = GetMatchingEffects(query); int count = 0;
            foreach (var handle in handles) if (RemoveEntry(handle, cause)) count++;
            return count;
        }
        /// <summary>Legacy entry for nonperiodic/nonstacked persistent effects with context-free values.</summary>
        public GameplayEffectHandle Apply(GameplayEffectDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (definition.DurationPolicy == GameplayEffectDurationPolicy.Instant || definition.Period > 0 || definition.Stacking != null || definition.CompletionExecutions.Count > 0)
                throw new InvalidOperationException("This effect requires context-aware Apply.");
            foreach (var m in definition.Modifiers) if (m.MagnitudeValue.RequiresApplicationContext) throw new InvalidOperationException("Dynamic modifiers require context.");
            if (definition.DurationValue?.RequiresApplicationContext == true || definition.PeriodValue?.RequiresApplicationContext == true)
                throw new InvalidOperationException("Dynamic timing requires context.");
            if (!MeetsRequirements(definition)) return default;
            return CreateEntry(definition, null, EvaluateTiming(definition, null)).Handle;
        }
        /// <summary>Evaluates and freezes incoming timing before mutation. Execution exceptions propagate after commit.</summary>
        public GameplayEffectApplicationResult Apply(GameplayEffectDefinition definition, GameplayEffectApplicationContext application)
            => ApplyPersistent(definition, application, false);
        /// <summary>Ability commit stages only reversible contributions. Initial executions remain
        /// pending until the grant's cost/state transaction has succeeded.</summary>
        internal GameplayEffectApplicationResult StageActivation(GameplayEffectDefinition definition, GameplayEffectApplicationContext application)
            => ApplyPersistent(definition, application, true);
        private GameplayEffectApplicationResult ApplyPersistent(GameplayEffectDefinition definition, GameplayEffectApplicationContext application,bool deferInitial)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (application == null) throw new ArgumentNullException(nameof(application));
            if (!ReferenceEquals(application.Target.Attributes, _target) || (_targetTags != null && !ReferenceEquals(application.Target.Tags, _targetTags)))
                throw new ArgumentException("Execution target must belong to this container.", nameof(application));
            if (!MeetsRequirements(definition)) return new GameplayEffectApplicationResult(GameplayEffectApplicationOutcome.RejectedByRequirements, default, 0);
            if (definition.DurationPolicy == GameplayEffectDurationPolicy.Instant)
            {
                foreach (var execution in definition.Executions) execution.Execute(application.WithStackCount(1));
                return new GameplayEffectApplicationResult(GameplayEffectApplicationOutcome.InstantExecuted, default, 0);
            }
            var existing = definition.Stacking == null ? null : FindStackEntry(definition, application.Context);
            application = application.WithStackCount(existing == null ? 1 : Math.Min(existing.StackCount + 1, definition.Stacking.MaxStacks));
            var timing = EvaluateTiming(definition, application);
            if (existing != null) return ReapplyStack(existing, application, timing);
            var entry = CreateEntry(definition, application, timing);
            var layer = entry.Layers[0].Handle;
            entry.ActivationInitialPending=deferInitial;
            if(!deferInitial)ExecuteInitial(entry, application);
            return new GameplayEffectApplicationResult(GameplayEffectApplicationOutcome.NewActiveEffect, entry.Handle, 1, layer);
        }
        private static Timing EvaluateTiming(GameplayEffectDefinition d, GameplayEffectApplicationContext app)
        {
            float duration = d.DurationValue == null ? d.Duration : GameplayValueEvaluator.Evaluate(d.DurationValue, app);
            float period = d.PeriodValue == null ? d.Period : GameplayValueEvaluator.Evaluate(d.PeriodValue, app);
            if (d.DurationPolicy == GameplayEffectDurationPolicy.Duration && (!float.IsFinite(duration) || duration <= 0)) throw new ArgumentOutOfRangeException(nameof(duration));
            if (d.Period > 0 && (!float.IsFinite(period) || period <= 0)) throw new ArgumentOutOfRangeException(nameof(period));
            return new Timing(duration, period);
        }
        private Entry CreateEntry(GameplayEffectDefinition d, GameplayEffectApplicationContext app, Timing timing)
        {
            if (d.GrantedTags.Count > 0 && _targetTags == null) throw new InvalidOperationException("Granted tags need a tag container.");
            var source = _sourceIdAllocator.Allocate(); long sequence = checked(_lastSequence + 1);
            var e = new Entry(new GameplayEffectHandle(_handleOwner, sequence), d, source, app, timing);
            try
            {
                e.Layers.Add(CreateLayer(e, app, timing));
                foreach (var tag in d.GrantedTags) _targetTags.AddTag(tag, source);
                _entries.Add(e.Handle, e);
            }
            catch { try { _target.RemoveModifiersFromSource(source); } finally { _targetTags?.RemoveTagsFromSource(source); } throw; }
            _lastSequence = sequence; return e;
        }
        private Layer CreateLayer(Entry e, GameplayEffectApplicationContext app, Timing timing)
        {
            var layer = new Layer(new GameplayEffectLayerHandle(e.Handle, checked(e.LastLayerSequence + 1)), app, timing.Duration, e.Elapsed + timing.Duration);
            try
            {
                foreach (var m in e.Definition.Modifiers)
                {
                    var attribute = _target.GetAttribute(m.AttributeId);
                    var handle = attribute.AddModifier(new GameplayAttributeModifier(m.Operation, GameplayValueEvaluator.Evaluate(m.MagnitudeValue, app), e.SourceId));
                    layer.Contributions.Add(new Contribution(attribute, handle));
                }
            }
            catch { CleanupLayer(layer); throw; }
            e.LastLayerSequence = layer.Handle.LocalSequence; return layer;
        }
        private bool MeetsRequirements(GameplayEffectDefinition d) =>
            (d.ApplicationRequirements.IsEmpty || d.ApplicationRequirements.Matches(RequireTags())) &&
            (d.OngoingRequirements.IsEmpty || d.OngoingRequirements.Matches(RequireTags()));
        private GameplayTagContainer RequireTags() => _targetTags ?? throw new InvalidOperationException("Requirements need target tags.");
        private bool MeetsOngoing(Entry e) => e.Definition.OngoingRequirements.IsEmpty || e.Definition.OngoingRequirements.Matches(RequireTags());
        private Entry FindStackEntry(GameplayEffectDefinition d, GameplayContext context)
        {
            var incoming = d.Stacking;
            foreach (var e in _entries.Values)
            {
                var active = e.Definition.Stacking;
                if (active == null || active.StackKey != incoming.StackKey) continue;
                if (active.Aggregation != incoming.Aggregation) throw new InvalidOperationException("Stack key collides with a different aggregation policy.");
                if (incoming.Aggregation == GameplayEffectStackAggregation.AggregateBySource && e.Application.Context.EffectiveSourceId != context.EffectiveSourceId) continue;
                if (incoming.Aggregation == GameplayEffectStackAggregation.AggregateByInstigator && e.Application.Context.InstigatorId != context.InstigatorId) continue;
                if (!ReferenceEquals(e.Definition, d)) throw new InvalidOperationException("Stack key collides with a different Effect definition.");
                return e;
            }
            return null;
        }
        private GameplayEffectApplicationResult ReapplyStack(Entry e, GameplayEffectApplicationContext app, Timing timing)
        {
            var s = e.Definition.Stacking;
            if (e.StackCount >= s.MaxStacks)
            {
                if (s.Overflow == GameplayEffectStackOverflowPolicy.Reject) return new GameplayEffectApplicationResult(GameplayEffectApplicationOutcome.RejectedAtLimit, e.Handle, e.StackCount);
                if (s.Overflow == GameplayEffectStackOverflowPolicy.ExecuteOverflow)
                {
                    var context = new GameplayEffectOverflowContext(this, e.Handle, app, e.StackCount);
                    foreach (var rule in s.OverflowExecutions) rule.Execute(context);
                    bool active = _entries.ContainsKey(e.Handle);
                    return new GameplayEffectApplicationResult(GameplayEffectApplicationOutcome.OverflowExecuted, active ? e.Handle : default, active ? e.StackCount : 0);
                }
                CommitReapplication(e, app, timing); ExecuteInitial(e, app);
                return new GameplayEffectApplicationResult(GameplayEffectApplicationOutcome.RefreshedAtLimit, e.Handle, e.StackCount);
            }
            var layer = CreateLayer(e, app, timing); e.Layers.Add(layer);
            CommitReapplication(e, app, timing); int count = e.StackCount; ExecuteInitial(e, app);
            return new GameplayEffectApplicationResult(GameplayEffectApplicationOutcome.StackIncreased, e.Handle, count, layer.Handle);
        }
        private static void CommitReapplication(Entry e, GameplayEffectApplicationContext app, Timing timing)
        {
            // Independent schedules always use the newest surviving real contribution; refresh-at-limit has no layer.
            e.Application = e.Independent ? e.Layers[e.Layers.Count - 1].Application.WithStackCount(e.StackCount) : app;
            var s = e.Definition.Stacking;
            if (e.Finite && s.DurationRefresh == GameplayEffectStackDurationRefreshPolicy.RefreshOnSuccessfulApplication)
            { e.Duration = timing.Duration; e.ExpiresAt = e.Elapsed + timing.Duration; }
            if (e.Period > 0 && s.PeriodReset == GameplayEffectStackPeriodResetPolicy.ResetOnSuccessfulApplication)
            { e.Period = timing.Period; e.NextPeriodAt = e.Elapsed + timing.Period; }
        }
        private static void ExecuteInitial(Entry e, GameplayEffectApplicationContext app)
        { if (e.Definition.PeriodicStart == GameplayEffectPeriodicStartPolicy.ExecuteOnApplication) foreach (var rule in e.Definition.Executions) rule.Execute(app); }
        internal void CommitActivationInitial(GameplayEffectHandle handle)
        {
            if(!_entries.TryGetValue(handle,out var entry)||!entry.ActivationInitialPending)return;
            entry.ActivationInitialPending=false; // Exactly once, including exceptions and reentry.
            ExecuteInitial(entry,entry.Application);
        }
        /// <summary>Aborts unpublished activation contributions without a removal notification or
        /// completion callback. No initial execution has run, so this is a real reversible cleanup.</summary>
        internal void AbortActivation(GameplayEffectHandle handle)
        {
            if(!_entries.TryGetValue(handle,out var entry))return;
            _target.RemoveModifiersFromSource(entry.SourceId);_targetTags?.RemoveTagsFromSource(entry.SourceId);_entries.Remove(handle);
        }
        public bool Remove(GameplayEffectHandle handle) => Remove(handle, GameplayEffectRemovalCause.Explicit);
        /// <summary>Only interruptions are accepted publicly; natural expiry is clock-owned.</summary>
        public bool Remove(GameplayEffectHandle handle, GameplayEffectRemovalCause cause)
        { ValidateRemovalRequest(cause); return RemoveEntry(handle, cause); }
        private static void ValidateRemovalRequest(GameplayEffectRemovalCause cause)
        {
            if (!Enum.IsDefined(typeof(GameplayEffectRemovalCause), cause) || cause == GameplayEffectRemovalCause.NaturalExpiry || cause == GameplayEffectRemovalCause.OngoingRequirementMismatch)
                throw new ArgumentOutOfRangeException(nameof(cause));
        }
        private bool RemoveEntry(GameplayEffectHandle handle, GameplayEffectRemovalCause cause)
        {
            if (!_entries.TryGetValue(handle, out var e)) return false;
            _target.RemoveModifiersFromSource(e.SourceId); _targetTags?.RemoveTagsFromSource(e.SourceId); _entries.Remove(handle);
            var observation = new GameplayEffectRemovalObservation(e.Definition, handle, e.SourceId, e.Application, e.StackCount, cause);
            LastRemoval = observation;
            try
            {
                if (cause == GameplayEffectRemovalCause.NaturalExpiry && e.Definition.CompletionExecutions.Count > 0)
                {
                    var context = new GameplayEffectCompletionContext(this, e.Definition, handle, e.SourceId, e.Application);
                    foreach (var rule in e.Definition.CompletionExecutions) rule.Execute(context);
                }
            }
            finally { Removed?.Invoke(observation); }
            return true;
        }
        /// <summary>Exact contribution cleanup. Presence tags last until the final surviving layer is removed.</summary>
        public bool RemoveLayer(GameplayEffectLayerHandle handle, GameplayEffectRemovalCause cause = GameplayEffectRemovalCause.Explicit)
        {
            ValidateRemovalRequest(cause);
            if (!_entries.TryGetValue(handle.EffectHandle, out var e)) return false;
            var layer = e.Layers.Find(x => x.Handle == handle);
            if (layer == null) return false;
            if (e.StackCount == 1) return RemoveEntry(e.Handle, cause);
            CleanupLayer(layer); e.Layers.Remove(layer); SelectSurvivingApplication(e); return true;
        }
        private static void CleanupLayer(Layer layer)
        {
            var groups = new Dictionary<GameplayAttribute, List<GameplayModifierHandle>>();
            foreach (var contribution in layer.Contributions)
            {
                if (!groups.TryGetValue(contribution.Attribute, out var handles))
                { handles = new List<GameplayModifierHandle>(); groups.Add(contribution.Attribute, handles); }
                handles.Add(contribution.Handle);
            }
            foreach (var group in groups) group.Key.ValidateModifierRemoval(group.Value);
            foreach (var group in groups) group.Key.RemoveModifiers(group.Value);
        }
        private static void SelectSurvivingApplication(Entry e) => e.Application = e.Layers[e.Layers.Count - 1].Application?.WithStackCount(e.StackCount);
        /// <summary>Double timeline with public float crossing; projection never feeds elapsed. Period executes before coincident layer expiry.</summary>
        public void Advance(float amount)
        {
            if (!float.IsFinite(amount) || amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (_groupAdvancing || _singleAdvancing || _activationAttempts>0) throw new InvalidOperationException("An Effect step cannot be advanced again from a callback or activation commit.");
            _singleAdvancing = true;
            try { AdvanceEntries(amount); }
            finally { _singleAdvancing = false; }
        }
        private void AdvanceEntries(float amount)
        {
            foreach (var e in OrderedEntries())
            {
                if (!_entries.ContainsKey(e.Handle)) continue;
                if (!MeetsOngoing(e)) { RemoveEntry(e.Handle, GameplayEffectRemovalCause.OngoingRequirementMismatch); continue; }
                double end = e.Elapsed + amount;
                while (_entries.ContainsKey(e.Handle))
                {
                    double expiry = e.Finite ? e.NextExpiry : double.PositiveInfinity;
                    double boundary = Math.Min(e.Period > 0 ? e.NextPeriodAt : double.PositiveInfinity, expiry);
                    if (double.IsPositiveInfinity(boundary) || (float)boundary > (float)end) { e.Elapsed = end; break; }
                    e.Elapsed = Math.Min(boundary, end);
                    AdvanceBoundary(e, boundary);
                }
            }
        }
        private void AdvanceBoundary(Entry e, double boundary)
        {
            bool periodDue = e.Period > 0 && (float)e.NextPeriodAt <= (float)boundary;
            bool expiryDue = e.Finite && (float)e.NextExpiry <= (float)boundary;
            // Capture due layers before callbacks; a new contribution cannot inherit an old expiry.
            var due = new List<Layer>();
            if (expiryDue && e.Independent) foreach (var layer in e.Layers) if ((float)layer.ExpiresAt <= (float)boundary) due.Add(layer);
            if (periodDue)
            {
                e.NextPeriodAt += e.Period;
                var application = e.Application.WithStackCount(e.StackCount);
                try { foreach (var rule in e.Definition.Executions) rule.Execute(application); }
                catch { if (expiryDue) RemoveEntry(e.Handle, GameplayEffectRemovalCause.Explicit); throw; }
            }
            if (!_entries.ContainsKey(e.Handle)) return;
            if (!e.Independent && e.Finite && (float)e.ExpiresAt <= (float)boundary)
                RemoveEntry(e.Handle, GameplayEffectRemovalCause.NaturalExpiry);
            else if (e.Independent && expiryDue)
                foreach (var layer in due)
                {
                    if (!_entries.ContainsKey(e.Handle)) break;
                    if (!e.Layers.Contains(layer)) continue;
                    if (e.StackCount == 1) RemoveEntry(e.Handle, GameplayEffectRemovalCause.NaturalExpiry);
                    else { CleanupLayer(layer); e.Layers.Remove(layer); SelectSurvivingApplication(e); }
                }
            if (_entries.ContainsKey(e.Handle) && !MeetsOngoing(e)) RemoveEntry(e.Handle, GameplayEffectRemovalCause.OngoingRequirementMismatch);
        }
        public bool IsActive(GameplayEffectHandle handle) => _entries.ContainsKey(handle);
        /// <summary>Tests the exact live contribution, rather than just its shared entry. Area aura
        /// leases use this to reapply an expired/dispelled contribution without duplicating surviving layers.</summary>
        public bool IsLayerActive(GameplayEffectLayerHandle handle) =>
            _entries.TryGetValue(handle.EffectHandle, out var entry) && entry.Layers.Exists(layer => layer.Handle == handle);
        // Ability normal cooldown edits preserve this container's sole timeline authority.
        internal bool TrySetSimpleDurationRemaining(GameplayEffectHandle handle, float remaining)
        {
            if (!_entries.TryGetValue(handle, out var entry) || !entry.Finite || entry.Definition.Stacking != null ||
                entry.Period != 0 || entry.Definition.Modifiers.Count != 0 || entry.Definition.CompletionExecutions.Count != 0)
                return false;
            if (remaining == 0) return Remove(handle);
            entry.ExpiresAt = entry.Elapsed + remaining;
            return true;
        }
        /// <summary>Ability's explicit cooldown edit reuses the original simple Definition
        /// and this container's ownership, with an already resolved positive duration.</summary>
        internal GameplayEffectHandle ApplySimpleDuration(GameplayEffectDefinition definition,GameplayEffectApplicationContext application,float duration)
        {
            if(!float.IsFinite(duration)||duration<=0)throw new ArgumentOutOfRangeException(nameof(duration));
            if(definition.DurationPolicy!=GameplayEffectDurationPolicy.Duration||definition.Period!=0||definition.PeriodValue!=null||definition.Stacking!=null||definition.Modifiers.Count!=0||definition.Executions.Count!=0||definition.CompletionExecutions.Count!=0||!definition.ApplicationRequirements.IsEmpty||!definition.OngoingRequirements.IsEmpty)
                throw new ArgumentException("Explicit cooldown duration requires a simple tag-only Effect.");
            if(application!=null&&!ReferenceEquals(application.Target.Attributes,_target))throw new ArgumentException("Cooldown application must target this container.");
            return CreateEntry(definition,application,new Timing(duration,0)).Handle;
        }
        internal bool TryGetAppliedDuration(GameplayEffectHandle handle,out float duration)
        {if(_entries.TryGetValue(handle,out var entry)){duration=entry.Duration;return true;}duration=0;return false;}
        public bool TryGetRemainingDuration(GameplayEffectHandle handle, out float remaining)
        { if (_entries.TryGetValue(handle, out var e) && e.Finite) { remaining = e.Remaining; return true; } remaining = 0; return false; }
        public bool TryGetPeriodRemaining(GameplayEffectHandle handle, out float remaining)
        { if (_entries.TryGetValue(handle, out var e) && e.Period > 0) { remaining = e.PeriodRemaining; return true; } remaining = 0; return false; }
        public bool TryGetStackCount(GameplayEffectHandle handle, out int count)
        { if (_entries.TryGetValue(handle, out var e)) { count = e.StackCount; return true; } count = 0; return false; }
        private readonly struct Timing
        { internal readonly float Duration, Period; internal Timing(float duration, float period) { Duration = duration; Period = period; } }
        private readonly struct Contribution
        { internal readonly GameplayAttribute Attribute; internal readonly GameplayModifierHandle Handle; internal Contribution(GameplayAttribute a, GameplayModifierHandle h) { Attribute = a; Handle = h; } }
        private sealed class Layer
        {
            internal readonly GameplayEffectLayerHandle Handle;
            internal readonly GameplayEffectApplicationContext Application;
            internal readonly float Duration;
            internal readonly double ExpiresAt;
            internal readonly List<Contribution> Contributions = new List<Contribution>();
            internal Layer(GameplayEffectLayerHandle h, GameplayEffectApplicationContext a, float duration, double expiresAt)
            { Handle = h; Application = a; Duration = duration; ExpiresAt = expiresAt; }
        }
        private sealed class Entry
        {
            internal readonly GameplayEffectHandle Handle;
            internal readonly GameplayEffectDefinition Definition;
            internal readonly GameplaySourceId SourceId;
            internal readonly List<Layer> Layers = new List<Layer>();
            internal GameplayEffectApplicationContext Application;
            internal double Elapsed, ExpiresAt, NextPeriodAt;
            internal float Duration, Period;
            internal long LastLayerSequence;
            internal bool ActivationInitialPending;
            internal bool Finite => Definition.DurationPolicy == GameplayEffectDurationPolicy.Duration;
            internal bool Independent => Definition.Stacking?.Lifetime == GameplayEffectStackLifetimePolicy.IndependentLayerLifetime;
            internal int StackCount => Layers.Count;
            internal double NextExpiry { get { if (!Independent) return ExpiresAt; double next = double.PositiveInfinity; foreach (var l in Layers) next = Math.Min(next, l.ExpiresAt); return next; } }
            internal float Remaining { get { double last = ExpiresAt; if (Independent) { last = 0; foreach (var l in Layers) last = Math.Max(last, l.ExpiresAt); } return (float)Math.Max(0d, last - Elapsed); } }
            internal float PeriodRemaining => (float)Math.Max(0d, NextPeriodAt - Elapsed);
            internal Entry(GameplayEffectHandle h, GameplayEffectDefinition d, GameplaySourceId s, GameplayEffectApplicationContext a, Timing t)
            { Handle = h; Definition = d; SourceId = s; Application = a; Duration = t.Duration; Period = t.Period; ExpiresAt = t.Duration; NextPeriodAt = t.Period; }
        }
    }
}
