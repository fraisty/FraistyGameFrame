using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Extensions;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>First-party host composition for real-time or turn-driven games. Reuses the existing rule
    /// containers; Advance is the only world entry for their clocks. Backends/presenters never call it.
    /// No persistent player data, Unity object, resource path or game-specific unit type is stored here.</summary>
    public sealed partial class GameplayStandardWorld : IDisposable
    {
        private readonly List<GameplayStandardActor> _actors = new List<GameplayStandardActor>();
        private readonly List<GameplayStandardActor> _stepActors = new List<GameplayStandardActor>();
        private readonly List<GameplaySpatialOperation> _stepSpatial = new List<GameplaySpatialOperation>();
        private readonly List<GameplayEffectContainer> _stepEffects = new List<GameplayEffectContainer>();
        private readonly IReadOnlyList<GameplayStandardActor> _actorView;
        private bool _advancing;
        private bool _publishingCombat;
        private readonly Queue<CombatDispatch> _combatQueue = new Queue<CombatDispatch>();
        private int _combatDepth, _combatAccepted, _combatAllowedDepth;
        private GameplaySourceId _combatRoot;
        /// <summary>Per causal chain limits; independent attacks start a fresh budget.</summary>
        public const int CombatChainDepthLimit = 16, CombatChainOutcomeLimit = 128;
        /// <summary>Latest bounded-chain diagnostic, also recorded in the actual reporter's trace.</summary>
        public string LastCombatDispatchDiagnostic { get; private set; }
        private readonly struct CombatDispatch
        {
            public readonly GameplayStandardActor Reporter;
            public readonly GameplayCombatOutcome Outcome;
            public readonly int Depth;
            public CombatDispatch(GameplayStandardActor reporter, GameplayCombatOutcome outcome, int depth)
            { Reporter = reporter; Outcome = outcome; Depth = depth; }
        }
        public GameplaySourceIdAllocator Sources { get; }
        public IGameplayRandomSource Random {get;}
        public IReadOnlyList<GameplayStandardActor> Actors => _actorView;
        public IGameplaySpatialQueryBackend Spatial { get; set; }
        public GameplayEventDispatcher Events { get; } = new GameplayEventDispatcher();
        /// <summary>Committed outcomes drain in stable FIFO order in the current rule domain.
        /// Nested reactions enqueue without recursive dispatcher calls. Only exhausted causal
        /// chains are suppressed; a later independent attack receives a fresh budget.</summary>
        internal void PublishCombat(GameplayStandardActor reporter,GameplayCombatOutcome outcome)
        {
            if (IsDisposed) return;
            int depth = _publishingCombat ? _combatDepth + 1 : 0;
            if (!_publishingCombat)
            {
                _combatAccepted = 0; _combatRoot = outcome.Context.EffectiveSourceId; _combatAllowedDepth = CombatChainDepthLimit;
                if(outcome.Context.Values.TryGetValue(GameplayCombatEvents.MaxChainDepth,out float authoredDepth))
                    _combatAllowedDepth=(int)Math.Max(0,Math.Min(CombatChainDepthLimit,Math.Floor(authoredDepth)));
            }
            if (depth > _combatAllowedDepth || _combatAccepted >= CombatChainOutcomeLimit)
            {
                LastCombatDispatchDiagnostic = $"CombatChainBudgetExceeded root={_combatRoot} source={outcome.Context.EffectiveSourceId} depth={depth}/{_combatAllowedDepth} accepted={_combatAccepted}/{CombatChainOutcomeLimit}";
                reporter.RecordSkillTrace(new GameplaySkillTraceRecord(reporter.Id, outcome.Context, GameplayCombatEvents.Damage,
                    GameplaySkillTraceStage.ActionRejected, "CombatDispatch", detail: LastCombatDispatchDiagnostic));
                return;
            }
            _combatAccepted++;
            _combatQueue.Enqueue(new CombatDispatch(reporter, outcome, depth));
            if (_publishingCombat) return;
            _publishingCombat = true;
            try
            {
                while (!IsDisposed && _combatQueue.Count > 0)
                {
                    var pending = _combatQueue.Dequeue();
                    reporter = pending.Reporter; outcome = pending.Outcome; _combatDepth = pending.Depth;
                    if (reporter.IsDisposed) continue;
                    var tag = outcome.Kind == GameplayCombatOutcomeKind.Damage ? GameplayCombatEvents.Damage :
                        outcome.Kind == GameplayCombatOutcomeKind.Heal ? GameplayCombatEvents.Heal : GameplayCombatEvents.Shield;
                    // Borrowed actors may use their game's actual dispatcher. Route to both real
                    // actor domains, once when shared, rather than publishing into a disconnected bus.
                    var dispatchers=new List<GameplayEventDispatcher>();
                    if(TryGetActor(outcome.Context.InstigatorId,out var instigator))dispatchers.Add(instigator.Events);
                    if(!dispatchers.Contains(reporter.Events))dispatchers.Add(reporter.Events);
                    foreach(var dispatcher in dispatchers){
                        if(IsDisposed)break;
                        dispatcher.Publish(new GameplayEventData(tag,outcome.Context,outcome.Applied));
                        if (!IsDisposed && outcome.Kind == GameplayCombatOutcomeKind.Damage && !outcome.Damage.IsMiss && !outcome.Damage.IsImmune)
                            dispatcher.Publish(new GameplayEventData(GameplayCombatEvents.Hit,outcome.Context,outcome.Applied));
                    }
                }
            }
            finally { _publishingCombat = false; _combatQueue.Clear(); _combatDepth = 0; }
        }
        public bool IsDisposed { get; private set; }
        public GameplayStandardWorld(GameplaySourceIdAllocator sources = null,IGameplayRandomSource random=null)
        { _actorView = _actors.AsReadOnly(); Sources = sources ?? new GameplaySourceIdAllocator();Random=random??new GameplaySeededRandomSource(1); Spatial = new GameplayLogicalSpatialBackend(() => _actors); }
        /// <summary>Creates an owned actor. An optional live position pair lets an Integration bind
        /// the real presentation/physics anchor without storing Unity objects or caching a mirror here.</summary>
        public GameplayStandardActor CreateActor(GameplayPoint position, int faction = 0, int layer = 0,
            Func<GameplayPoint> readPosition = null, Action<GameplayPoint> writePosition = null,
            Func<GameplayDirection> readFacing = null, Action<GameplayDirection> writeFacing = null)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(GameplayStandardWorld));
            if ((readPosition == null) != (writePosition == null)) throw new ArgumentException("Supply both live position accessors or neither.");
            if ((readFacing == null) != (writeFacing == null)) throw new ArgumentException("Supply both live facing accessors or neither.");
            var actor = new GameplayStandardActor(this, Sources.Allocate(), position, faction, layer, readPosition: readPosition, writePosition: writePosition, readFacing: readFacing, writeFacing: writeFacing);
            _actors.Add(actor); return actor;
        }
        public bool TryGetActor(GameplaySourceId id, out GameplayStandardActor actor)
        {
            foreach(var candidate in _actors)
                if(candidate.Id==id&&!candidate.IsDisposed){actor=candidate;return true;}
            actor=null;return false;
        }
        /// <summary>Wraps an existing game's actual containers/resources without copying or ticking them.
        /// Caller supplies that domain's source allocator to the World constructor and advances borrowed
        /// containers in its existing host loop; this world advances only newly installed skill bindings.</summary>
        public GameplayStandardActor RegisterBorrowedActor(IGameplayAbilityHost host, GameplayAbilityContainer abilities,
            GameplaySequenceContainer sequences, GameplayEventDispatcher events, Func<GameplayPoint> position,
            Action<GameplayPoint> move, int faction, int layer = 0,
            Func<GameplayDirection> readFacing = null, Action<GameplayDirection> writeFacing = null)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(GameplayStandardWorld));
            if (position == null) throw new ArgumentNullException(nameof(position));
            if (host == null || _actors.Exists(a => a.Id == host.OwnerId)) throw new ArgumentException("Borrowed host must have a unique owner.");
            var actor = new GameplayStandardActor(this, host.OwnerId, position(), faction, layer,
                host, abilities, sequences, events, position, move, readFacing, writeFacing); _actors.Add(actor); return actor;
        }
        public void Advance(float amount) => Advance(amount, null);
        /// <summary>One step with an optional existing host's borrowed Sequence phase. Borrowed
        /// Abilities must already have advanced. Effects normally advance only owned actors; an
        /// explicit game-owned effect phase may combine these with its actual borrowed containers.
        /// That callback must advance every supplied owned container exactly once. Spatial work precedes all sequence markers;
        /// newborn work never consumes the delta before its creation.</summary>
        public void Advance(float amount, Action<float> advanceBorrowedSequences,
            Action<IReadOnlyList<GameplayEffectContainer>,float> advanceEffects = null)
        {
            if (!float.IsFinite(amount) || amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            if (IsDisposed) return;
            if (_advancing || _publishingCombat) throw new InvalidOperationException("World Advance cannot be nested inside a rule callback.");
            _advancing = true;
            try { AdvanceStep(amount, advanceBorrowedSequences, advanceEffects); }
            finally { _stepActors.Clear(); _stepSpatial.Clear(); _stepEffects.Clear(); _advancing = false; }
        }
        private void AdvanceStep(float amount, Action<float> advanceBorrowedSequences,
            Action<IReadOnlyList<GameplayEffectContainer>,float> advanceEffects)
        {
            // Snapshot membership once before callbacks, but reuse private storage between steps.
            // Advance rejects reentry; finally releases references even after callbacks throw/dispose.
            var actors = _stepActors; actors.AddRange(_actors);
            var spatial = _stepSpatial; spatial.AddRange(_spatial);
            foreach (var actor in actors) if (!actor.IsDisposed && !actor.BorrowsContainers) actor.Presentation?.Advance(amount);
            // Existing lifecycle clocks finish before marker/input actions. A target effect created by
            // this step's Sequence/Skill must never consume a delta that preceded its creation.
            var effectContainers = _stepEffects;
            foreach (var actor in actors) if (!actor.IsDisposed && !actor.BorrowsContainers) effectContainers.Add(actor.Effects);
            if(advanceEffects==null)GameplayEffectContainer.AdvanceTogether(effectContainers, amount);
            else advanceEffects(new List<GameplayEffectContainer>(effectContainers).AsReadOnly(), amount); // External callbacks may retain their detached list.
            foreach (var actor in actors) if (!actor.IsDisposed && !actor.BorrowsContainers) actor.Abilities.Advance(amount);
            foreach (var operation in spatial) if (!operation.IsDisposed) operation.Advance(amount);
            advanceBorrowedSequences?.Invoke(amount);
            foreach (var actor in actors) if (!actor.IsDisposed && !actor.BorrowsContainers) actor.Sequences.Advance(amount);
            foreach (var actor in actors) if (!actor.IsDisposed) actor.AdvanceSkills(amount);
            // Decisions run after this step's clocks. A cast chosen here starts at age zero.
            foreach (var actor in actors) if (!actor.IsDisposed) actor.Behaviour?.Advance(amount);
        }
        internal void Detach(GameplayStandardActor actor)
        {
            _actors.Remove(actor);
            List<Exception> errors=null;
            foreach(var operation in _spatial.ToArray())
                if(ReferenceEquals(operation.Owner,actor))
                    try { operation.Dispose(); } catch(Exception ex) { (errors??(errors=new List<Exception>())).Add(ex); }
            if(errors!=null)throw new AggregateException(errors);
        }
        public void Dispose()
        {
            if (IsDisposed) return; IsDisposed = true;
            _combatQueue.Clear();
            List<Exception> errors = null;
            foreach (var operation in _spatial.ToArray())
                try { operation.Dispose(); } catch (Exception ex) { (errors ?? (errors = new List<Exception>())).Add(ex); }
            foreach (var actor in _actors.ToArray())
                try { actor.Dispose(); } catch (Exception ex) { (errors ?? (errors = new List<Exception>())).Add(ex); }
            _actors.Clear();
            if (errors != null) throw new AggregateException(errors);
        }
    }

    /// <summary>Per-actor mutable state and default Skill/Ability/Effect/Sequence host. Static Definitions
    /// are shared, subscriptions/handles are owned here, and removal cleans them without definition scans.</summary>
    public sealed partial class GameplayStandardActor : IGameplayAbilityHost, IGameplaySkillHost, IGameplaySkillEffectHost,
        IGameplaySequenceHost, IGameplayResourceTarget, IGameplaySpatialActor, IGameplaySkillTraceSink, IGameplaySkillTraceSourceProvider, IDisposable
    {
        private readonly List<GameplaySkillRuntime> _skills = new List<GameplaySkillRuntime>();
        private readonly List<GameplayInstalledSkill> _documents = new List<GameplayInstalledSkill>();
        private readonly IReadOnlyList<GameplayInstalledSkill> _documentView;
        private readonly List<GameplaySkillRuntime> _stepSkills = new List<GameplaySkillRuntime>();
        public IReadOnlyList<GameplayInstalledSkill> InstalledSkills => _documentView;
        /// <summary>Optional bounded diagnostics. Enable from the actual host/Debugger;
        /// it observes rules and continuations using their original contexts.</summary>
        public GameplaySkillTraceBuffer SkillTrace {get;}=new GameplaySkillTraceBuffer();
        public void RecordSkillTrace(GameplaySkillTraceRecord record){if(!IsDisposed)SkillTrace.Record(record);}
        public bool TryGetActionSource(IGameplaySkillAction action,GameplayContext context,out GameplaySkillTraceSource source)
        {
            foreach(var document in _documents)
                if(document.Definition.ActionSources.TryGetValue(action,out source)&&
                    document.LastCastContext!=null&&context!=null&&document.LastCastContext.ActivationSourceId==context.ActivationSourceId){
                    source=new GameplaySkillTraceSource(source.DefinitionId,source.NodeId,document.Definition.DiagnosticDocumentId);return true;
                }
            source=default;return false;
        }
        private int _layer;
        private GameplayPoint _position;
        private readonly Func<GameplayPoint> _readPosition;
        private readonly Action<GameplayPoint> _writePosition;
        private GameplayDirection _facing = new GameplayDirection(1,0);
        private readonly Func<GameplayDirection> _readFacing;
        private readonly Action<GameplayDirection> _writeFacing;
        public bool BorrowsContainers { get; }
        public GameplayStandardWorld World { get; }
        public GameplaySourceId Id { get; }
        public GameplaySourceId OwnerId => Id;
        /// <summary>Optional immutable birth provenance for a summoned actor's later casts.</summary>
        public GameplaySourceId SpawnOriginSourceId {get;internal set;}
        public GameplaySourceId TargetId => Id;
        public GameplayPoint Position { get => _readPosition != null ? _readPosition() : _position; set {
            if (_writePosition != null) _writePosition(value);
            else if (_readPosition != null) throw new InvalidOperationException("This actor's position binding is read-only.");
            else _position = value; } }
        /// <summary>Live heading when an integration supplies accessors; rotation writes the actual
        /// game/view authority rather than a cache overwritten by the next host update.</summary>
        public GameplayDirection Facing { get => _readFacing != null ? _readFacing() : _facing; set {
            if (!value.IsValid) throw new ArgumentException("Facing must be valid.");
            if (_writeFacing != null) _writeFacing(value);
            else if (_readFacing != null) throw new InvalidOperationException("This actor's facing binding is read-only.");
            else _facing=value; } }
        public int Faction { get; set; }
        public int Layer { get => _layer; set { if (value < 0 || value > 31) throw new ArgumentOutOfRangeException(nameof(value)); _layer = value; } }
        public bool Enabled { get; set; } = true;
        public bool IsTargetable => Enabled && !IsDisposed && (CombatAttributes == null ||
            (Resources.TryGetBalance(CombatAttributes.Health, out float health) && health > 0));
        public GameplayCombatAttributeMap CombatAttributes { get; set; }
        /// <summary>Timed consumable shield contributions over the actual resource and Effect clock.</summary>
        public GameplayShieldContributions Shields { get; }
        public GameplayCombatOutcome? LastCombatOutcome { get; private set; }
        private (GameplayContext Targeted, GameplayContext Input) _effectExecution;
        /// <summary>Optional view driver. The scene/game binding supplies it; no presentation object is stored in definitions.</summary>
        public IGameplayPresentationDriver Presentation { get; set; }
        /// <summary>Optional behaviour driver over an existing host FSM/controller. World supplies
        /// its delta for owned and borrowed actors; a borrowed FSM adapter can opt out of updating.</summary>
        public IGameplayBehaviourDriver Behaviour { get; set; }
        public event Action<GameplayCombatOutcome> CombatChanged;
        public void ReportCombat(GameplayCombatOutcome outcome)
        {
            if (_effectExecution.Input != null && ReferenceEquals(_effectExecution.Targeted, outcome.Context))
                outcome = outcome.WithExecutionContext(_effectExecution.Input);
            LastCombatOutcome = outcome;
            World.PublishCombat(this,outcome);
            CombatChanged?.Invoke(outcome);
        }
        public bool IsDisposed { get; private set; }
        public AttributeSet Attributes { get; } = new AttributeSet();
        public GameplayTagContainer Tags { get; } = new GameplayTagContainer();
        public GameplayEffectContainer Effects { get; }
        public GameplayAbilityContainer Abilities { get; }
        public GameplaySequenceContainer Sequences { get; } = new GameplaySequenceContainer();
        public GameplayEventDispatcher Events { get; }
        public IGameplayResourceStore Resources { get; }
        internal GameplayStandardActor(GameplayStandardWorld world, GameplaySourceId id, GameplayPoint position, int faction, int layer,
            IGameplayAbilityHost borrowed = null, GameplayAbilityContainer abilities = null, GameplaySequenceContainer sequences = null,
            GameplayEventDispatcher events = null, Func<GameplayPoint> readPosition = null, Action<GameplayPoint> writePosition = null,
            Func<GameplayDirection> readFacing = null, Action<GameplayDirection> writeFacing = null)
        {
            _documentView = _documents.AsReadOnly();
            World = world; Id = id; Position = position; Faction = faction; Layer = layer;
            BorrowsContainers = borrowed != null;
            if (borrowed != null)
            {
                if (borrowed.OwnerId != id || borrowed.TargetId != id || abilities == null || !abilities.IsBoundTo(borrowed, world.Sources))
                    throw new ArgumentException("Borrowed owner/container identity must match.");
                Attributes = borrowed.Attributes; Tags = borrowed.Tags; Effects = borrowed.Effects;
                Abilities = abilities ?? throw new ArgumentNullException(nameof(abilities));
                Sequences = sequences ?? throw new ArgumentNullException(nameof(sequences));
            }
            else
            {
                Effects = new GameplayEffectContainer(Attributes, world.Sources, Tags);
                Abilities = new GameplayAbilityContainer(this, world.Sources);
            }
            Events = events ?? world.Events; Resources = borrowed is IGameplayResourceTarget resourceTarget ? resourceTarget.Resources : Attributes;
            Shields=new GameplayShieldContributions(this);
            _readPosition = readPosition; _writePosition = writePosition; _readFacing = readFacing; _writeFacing = writeFacing;
        }
        public GameplaySkillRuntime InstallSkill(GameplaySkillDefinition definition,
            Func<GameplayEventData, GameplayContext, GameplayContext> executionContext = null)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(GameplayStandardActor));
            var runtime = new GameplaySkillRuntime(definition, this, Events, executionContext: executionContext); _skills.Add(runtime); return runtime;
        }
        /// <summary>Installs a unique actor skill. Disable passive activation when restoring an explicitly
        /// inactive loadout entry; ordinary profile installations retain automatic activation.</summary>
        public GameplayInstalledSkill InstallDocument(GameplaySkillDocumentDefinition definition, int level = 1, bool activatePassive = true)
        {
            if (IsDisposed) throw new ObjectDisposedException(nameof(GameplayStandardActor));
            if (definition == null || _documents.Exists(d => d.Definition.SkillId == definition.SkillId)) throw new ArgumentException("Skill ID must be unique on this actor.");
            var installed = new GameplayInstalledSkill(this, definition, level); _documents.Add(installed);
            if (activatePassive && definition.Mode == GameplayStandardActivationMode.Passive)
                try { installed.ActivatePassive(); } catch { installed.Dispose(); throw; }
            return installed;
        }
        internal void DetachDocument(GameplayInstalledSkill document) => _documents.Remove(document);
        public bool UninstallSkill(GameplaySkillRuntime runtime) { if (!_skills.Remove(runtime)) return false; runtime.Dispose(); return true; }
        /// <summary>Applies to this World using an explicit Caller snapshot, or a detached copy
        /// of the captured chain inputs when omitted. Explicit Empty remains an empty override.</summary>
        public GameplayEffectApplicationResult ApplyEffect(GameplayContext context, GameplayEffectDefinition effect, GameplaySetByCallerValues values=null) =>
            ApplyEffect(context, effect, values, null);

        internal GameplayEffectApplicationResult ApplyEffect(GameplayContext context, GameplayEffectDefinition effect,
            GameplaySetByCallerValues values, GameplayContext executionContext)
        {
            if (IsDisposed || context.InstigatorId != Id) throw new InvalidOperationException("Effect instigator must be this active actor.");
            if (!World.TryGetActor(context.TargetData.PrimaryTargetId, out var target)) throw new InvalidOperationException("Effect target left the world.");
            var application = new GameplayEffectApplicationContext(context, target,
                values ?? context.Values.AsSetByCallerValues(), Attributes, Id, instigatorTarget: this);
            if (executionContext == null) return target.Effects.Apply(effect, application);
            var previous = target._effectExecution;
            target._effectExecution = (context, executionContext);
            try { return target.Effects.Apply(effect, application); }
            finally { target._effectExecution = previous; }
        }
        internal void AdvanceSkills(float amount)
        {
            _stepSkills.AddRange(_skills);
            try { foreach (var skill in _stepSkills) if (!IsDisposed && !skill.IsDisposed) skill.Advance(amount); }
            finally { _stepSkills.Clear(); }
        }
        public void Dispose()
        {
            if (IsDisposed) return; IsDisposed = true;
            List<Exception> errors = null;
            try { World.Detach(this); } catch (Exception ex) { (errors ?? (errors = new List<Exception>())).Add(ex); }
            foreach (var document in _documents.ToArray())
                try { document.Dispose(); } catch (Exception ex) { (errors ?? (errors = new List<Exception>())).Add(ex); }
            try { Behaviour?.Dispose(); } catch (Exception ex) { (errors ?? (errors = new List<Exception>())).Add(ex); }
            Behaviour=null;
            try { Shields.Dispose(); } catch (Exception ex) { (errors ?? (errors = new List<Exception>())).Add(ex); }
            if (BorrowsContainers)
            {
                foreach (var skill in _skills.ToArray())
                    try { skill.Dispose(); } catch (Exception ex) { (errors ?? (errors = new List<Exception>())).Add(ex); }
                _skills.Clear();
                try { Presentation?.Dispose(); } catch (Exception ex) { (errors ?? (errors = new List<Exception>())).Add(ex); }
                Presentation=null;
                if (errors != null) throw new AggregateException(errors); return;
            }
            foreach (var grant in Abilities.GetGrantedEntriesSnapshot())
                try { Abilities.Remove(grant.Handle); } catch (Exception ex) { (errors ?? (errors = new List<Exception>())).Add(ex); }
            try { Sequences.Clear(); } catch (Exception ex) { (errors ?? (errors = new List<Exception>())).Add(ex); }
            foreach (var skill in _skills)
                try { skill.Dispose(); } catch (Exception ex) { (errors ?? (errors = new List<Exception>())).Add(ex); }
            _skills.Clear();
            foreach (var effect in Effects.GetActiveEntriesSnapshot())
                try { Effects.Remove(effect.Handle, GameplayEffectRemovalCause.OwnerCleanup); } catch (Exception ex) { (errors ?? (errors = new List<Exception>())).Add(ex); }
            try { Presentation?.Dispose(); } catch (Exception ex) { (errors ?? (errors = new List<Exception>())).Add(ex); }
            Presentation=null;
            if (errors != null) throw new AggregateException(errors);
        }
    }
}
