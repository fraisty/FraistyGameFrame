using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay.Standard
{
    public enum GameplayStandardActivationMode { Active, Passive }
    public enum GameplayStandardEventSubject { OwnerInstigator, OwnerTarget, OwnerEither, Any }
    /// <summary>One skill entry referencing the existing immutable Ability and Skill Definitions.
    /// No duplicate cost, animation, target or timeline fields are stored here.</summary>
    public sealed class GameplaySkillDocumentDefinition
    {
        public int SkillId { get; }
        public string DiagnosticDocumentId {get;}
        public GameplayAbilityDefinition Ability { get; }
        public GameplaySkillDefinition Rules { get; }
        public GameplayTag CastEvent { get; }
        public GameplayStandardActivationMode Mode { get; }
        /// <summary>Explicitly dispatches the primary CastEvent after passive binding. This allows
        /// a gated startup rule to wait for a persistent suppression cooldown without refusing activation.</summary>
        public bool RunRulesOnPassiveActivation {get;}
        public GameplayStandardEventSubject Subject { get; }
        public GameplayContextValues Defaults { get; }
        public IReadOnlyList<GameplaySkillDefinition> AdditionalRules { get; }
        public IReadOnlyList<GameplaySkillLearningRequirement> LearningRequirements {get;}
        /// <summary>Optional immutable diagnostic provenance. It does not affect execution.</summary>
        public IReadOnlyDictionary<IGameplaySkillAction,GameplaySkillTraceSource> ActionSources {get;}
        public GameplaySkillDocumentDefinition(int skillId, GameplayAbilityDefinition ability, GameplaySkillDefinition rules,
            GameplayTag castEvent, GameplayStandardActivationMode mode = GameplayStandardActivationMode.Active,
            GameplayStandardEventSubject subject = GameplayStandardEventSubject.OwnerTarget, GameplayContextValues defaults = null,
            IEnumerable<GameplaySkillDefinition> additionalRules = null,IEnumerable<GameplaySkillLearningRequirement> learningRequirements=null,
            IReadOnlyDictionary<IGameplaySkillAction,GameplaySkillTraceSource> actionSources=null,string diagnosticDocumentId=null,bool runRulesOnPassiveActivation=false)
        {
            if (skillId <= 0) throw new ArgumentOutOfRangeException(nameof(skillId));
            SkillId = skillId; Ability = ability ?? throw new ArgumentNullException(nameof(ability));
            DiagnosticDocumentId=diagnosticDocumentId;
            Rules = rules ?? throw new ArgumentNullException(nameof(rules));
            if (!Enum.IsDefined(typeof(GameplayStandardActivationMode), mode) || !Enum.IsDefined(typeof(GameplayStandardEventSubject), subject))
                throw new ArgumentOutOfRangeException(nameof(mode));
            if ((mode == GameplayStandardActivationMode.Active||runRulesOnPassiveActivation) && (!(rules.Trigger is GameplaySkillEventTriggerDefinition trigger) ||
                !castEvent.IsValid || trigger.EventTag != castEvent || rules.Once))
                throw new ArgumentException("Active skill requires its real CastEvent trigger and repeatable rules.");
            CastEvent = castEvent; Mode = mode; Subject = subject; Defaults = defaults ?? GameplayContextValues.Empty;
            if(runRulesOnPassiveActivation&&mode!=GameplayStandardActivationMode.Passive)throw new ArgumentException("Passive startup applies only to passive skills.");
            RunRulesOnPassiveActivation=runRulesOnPassiveActivation;
            var supplemental = new List<GameplaySkillDefinition>(additionalRules ?? Array.Empty<GameplaySkillDefinition>());
            var unique = new HashSet<GameplaySkillDefinition>();
            foreach (var rule in supplemental)
                if (rule == null || ReferenceEquals(rule, rules) || !unique.Add(rule)) throw new ArgumentException("Additional rules must be distinct and non-null.");
            AdditionalRules = supplemental.AsReadOnly();
            var prerequisites=new List<GameplaySkillLearningRequirement>(learningRequirements??Array.Empty<GameplaySkillLearningRequirement>());
            if(prerequisites.Exists(p=>p==null))throw new ArgumentException("Missing learning prerequisite.");
            LearningRequirements=prerequisites.AsReadOnly();
            var sources=new Dictionary<IGameplaySkillAction,GameplaySkillTraceSource>();
            if(actionSources!=null)foreach(var pair in actionSources)sources.Add(pair.Key,pair.Value);
            ActionSources=new System.Collections.ObjectModel.ReadOnlyDictionary<IGameplaySkillAction,GameplaySkillTraceSource>(sources);
        }
    }
    /// <summary>Per-actor installed skill. Core owns grant/Effect/Sequence state; this entry installs the
    /// real producer/subscription and input route. End/Cancel physically disposes the current binding,
    /// so passive rule cleanup is push-based. Upgrade only changes an inactive grant.</summary>
    public sealed class GameplayInstalledSkill : IDisposable
    {
        private readonly List<GameplaySkillRuntime> _bindings = new List<GameplaySkillRuntime>();
        public GameplayStandardActor Actor { get; }
        public GameplaySkillDocumentDefinition Definition { get; }
        public GameplayAbilityHandle Grant { get; }
        public bool IsDisposed { get; private set; }
        public GameplayContext LastCastContext { get; private set; }
        public GameplayAbilityStatus Status => Actor.Abilities.GetStatus(Grant);
        internal GameplayInstalledSkill(GameplayStandardActor actor, GameplaySkillDocumentDefinition definition, int level)
        { Actor = actor; Definition = definition; Grant = actor.Abilities.Grant(definition.Ability, level); }
        public bool TryUpgrade(int level) => !IsDisposed && Actor.Abilities.TrySetLevel(Grant, level);
        /// <summary>True means activation committed payment, even if its synchronous initial
        /// effects already ended/cancelled it. Such an activation has no binding or CastEvent;
        /// Status, termination observation and trace distinguish it from precommit refusal.</summary>
        public bool TryCast(GameplayTargetData target = null, GameplayContextValues input = null)
        {
            if (IsDisposed || Definition.Mode != GameplayStandardActivationMode.Active || !Actor.IsTargetable) return false;
            var request=new GameplayContext(Actor.Id,target??new GameplayTargetData(Actor.Id,Actor.Position),values:Definition.Defaults.Merge(input??GameplayContextValues.Empty));
            try
            {
                if (!Actor.Abilities.TryActivate(Grant,request)) return false;
                if (!CanBindCommittedActivation(request)) return true;
                Bind(target ?? new GameplayTargetData(Actor.Id, Actor.Position), input);
                Actor.Events.Publish(new GameplayEventData(Definition.CastEvent, LastCastContext, 1));
                return true;
            }
            catch { Actor.Abilities.Cancel(Grant); throw; }
        }
        /// <summary>Explicit passive activation/re-engagement. True includes a paid activation
        /// immediately ended by initial effects; no listener is installed for that dead grant.</summary>
        public bool ActivatePassive()
        {
            if (IsDisposed || Definition.Mode != GameplayStandardActivationMode.Passive || !Actor.IsTargetable) return false;
            var request=new GameplayContext(Actor.Id,new GameplayTargetData(Actor.Id,Actor.Position),values:Definition.Defaults);
            try { if (!Actor.Abilities.TryActivate(Grant,request)) return false; if (!CanBindCommittedActivation(request)) return true; Bind(new GameplayTargetData(Actor.Id, Actor.Position), null); if(Definition.RunRulesOnPassiveActivation)Actor.Events.Publish(new GameplayEventData(Definition.CastEvent,LastCastContext,1)); return true; }
            catch { Actor.Abilities.Cancel(Grant); throw; }
        }
        private bool CanBindCommittedActivation(GameplayContext request)
        {
            if (Actor.Abilities.IsActive(Grant)) return true;
            Unbind();
            LastCastContext = null; // Never expose an older cast as this already-ended activation.
            var termination = Actor.Abilities.LastTermination;
            Actor.RecordSkillTrace(new GameplaySkillTraceRecord(Actor.Id, request, default,
                GameplaySkillTraceStage.ActionSucceeded, "ActivationEndedBeforeBinding",
                detail: "Payment committed; no binding/CastEvent. " +
                    (termination != null && termination.Handle == Grant ? termination.Reason.ToString() : "Grant inactive")));
            return false;
        }
        private void Bind(GameplayTargetData target, GameplayContextValues input)
        {
            Unbind();
            if (!Actor.Abilities.TryCreateExecutionContext(Grant, target, out var root)) throw new InvalidOperationException("Cast grant became inactive before binding.");
            LastCastContext = root.WithValues(Definition.Defaults.Merge(input ?? GameplayContextValues.Empty));
            if(Actor.SpawnOriginSourceId.IsValid)LastCastContext=LastCastContext.WithOriginSource(Actor.SpawnOriginSourceId);
            GameplayContext captured = LastCastContext;
            BindRule(Definition.Rules, captured, true);
            foreach (var rule in Definition.AdditionalRules) BindRule(rule, captured, false);
        }
        private void BindRule(GameplaySkillDefinition rule, GameplayContext captured, bool primary)
        {
            var binding = Actor.InstallSkill(rule, (trigger, resolved) =>
            {
                if (primary && Definition.Mode == GameplayStandardActivationMode.Active &&
                    (trigger.InstigatorId != Actor.Id || trigger.Context == null ||
                     trigger.Context.OriginSourceId != captured.OriginSourceId ||
                     trigger.Context.ActivationSourceId!=captured.ActivationSourceId)) return null;
                if ((!primary || Definition.Mode == GameplayStandardActivationMode.Passive) && !SubjectMatches(trigger)) return null;
                var inherited = trigger.Context?.Values ?? GameplayContextValues.Empty;
                return captured.WithTarget(resolved.TargetData).WithValues(inherited.Merge(captured.Values)
                    .WithValue(GameplayContextValues.TriggerMagnitudeKey, trigger.Magnitude));
            });
            _bindings.Add(binding);
            if (!Actor.Abilities.TryOwnLifetime(Grant, binding)) { Actor.UninstallSkill(binding); _bindings.Remove(binding); throw new InvalidOperationException("Skill listener ownership was rejected."); }
        }
        private void Unbind() { foreach (var binding in _bindings) Actor.UninstallSkill(binding); _bindings.Clear(); }
        private bool SubjectMatches(GameplayEventData trigger) => Definition.Subject == GameplayStandardEventSubject.Any ||
            (Definition.Subject != GameplayStandardEventSubject.OwnerTarget && trigger.InstigatorId == Actor.Id) ||
            (Definition.Subject != GameplayStandardEventSubject.OwnerInstigator && trigger.TargetId == Actor.Id);
        public bool Cancel() => !IsDisposed && Actor.Abilities.Cancel(Grant);
        public void Dispose()
        {
            if (IsDisposed) return; IsDisposed = true;
            Actor.DetachDocument(this);
            Unbind();
            Actor.Abilities.Remove(Grant);
        }
    }
}
