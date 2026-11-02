using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Optional integration binding of a newly created actor and its view lease. It must
    /// use the existing world, create no second clock, and leave profile initialization to Standard.</summary>
    public interface IGameplaySummonFactory
    { GameplaySummonBinding Create(GameplayStandardActor owner,GameplayContext context,GameplayPoint position); }
    public sealed class GameplaySummonBinding
    {
        public GameplayStandardActor Actor {get;}
        public IDisposable View {get;}
        public GameplaySummonBinding(GameplayStandardActor actor,IDisposable view=null){Actor=actor??throw new ArgumentNullException(nameof(actor));View=view;}
    }
    /// <summary>Immutable first-party summoned unit composition. Its actor owns normal containers,
    /// skill levels are inherited/clamped, and lifetime advances in the existing spatial phase.</summary>
    public sealed class GameplaySummonDefinition
    {
        public GameplayActorProfile Profile {get;}public IGameplayFloatValue Duration {get;}
        public IReadOnlyList<GameplaySkillDocumentDefinition> Skills {get;}public bool AutoCast {get;}
        public IGameplaySummonFactory Factory {get;}public IReadOnlyList<IGameplaySkillAction> OnSpawn {get;}public IReadOnlyList<IGameplaySkillAction> OnEnd {get;}
        public GameplaySummonDefinition(GameplayActorProfile profile,IGameplayFloatValue duration,IEnumerable<GameplaySkillDocumentDefinition> skills=null,bool autoCast=false,
            IGameplaySummonFactory factory=null,IEnumerable<IGameplaySkillAction> onSpawn=null,IEnumerable<IGameplaySkillAction> onEnd=null)
        {
            Profile=profile??throw new ArgumentNullException(nameof(profile));Duration=duration??throw new ArgumentNullException(nameof(duration));
            var copy=new List<GameplaySkillDocumentDefinition>(skills??Array.Empty<GameplaySkillDocumentDefinition>());if(copy.Contains(null))throw new ArgumentException("Summon skill is null.");
            Skills=copy.AsReadOnly();AutoCast=autoCast;Factory=factory;OnSpawn=GameplaySpatialMath.Actions(onSpawn);OnEnd=GameplaySpatialMath.Actions(onEnd);
        }
    }
    /// <summary>Cast-owned actual summoned actor, not a pooled rule container. End/death/cancel
    /// remove the actor, its grants/controllers, and finally the optional view lease exactly once.</summary>
    public sealed class GameplaySummonOperation:GameplaySpatialOperation
    {
        private readonly GameplaySummonDefinition _definition;private readonly float _duration;private double _age;private GameplaySummonBinding _binding;
        public GameplayStandardActor SummonedActor=>_binding?.Actor;
        internal GameplaySummonOperation(GameplayStandardActor owner,GameplayContext context,GameplayAbilityHandle grant,GameplaySummonDefinition definition)
            :base(owner,context,grant,null)
        {_definition=definition;_duration=GameplaySpatialMath.Positive(definition.Duration,owner,context,"Summon duration");if(context.TargetData.HasPoint)Position=context.TargetData.Point;}
        protected override void OnStart()
        {
            var previous=new HashSet<GameplayStandardActor>(Owner.World.Actors);
            var binding=_definition.Factory?.Create(Owner,Context,Position)??new GameplaySummonBinding(Owner.World.CreateActor(Position,Owner.Faction,Owner.Layer));
            var actor=binding.Actor;
            if(actor.World!=Owner.World||actor.IsDisposed||actor.BorrowsContainers||previous.Contains(actor))
            {binding.View?.Dispose();throw new InvalidOperationException("Factory must create a fresh owned actor in the same world.");}
            _binding=binding;
            _definition.Profile.Initialize(actor);actor.SpawnOriginSourceId=Context.OriginSourceId;
            foreach(var skill in _definition.Skills)actor.InstallDocument(skill,Math.Min(Context.AbilityLevel,skill.Ability.MaxLevel));
            if(_definition.AutoCast)actor.Behaviour=new GameplaySkillSelectionPolicy(actor,s=>s.Definition.SkillId,s=>SelectTarget(actor));
            actor.CombatChanged+=OnCombat;Run(_definition.OnSpawn,At(actor.Id,actor.Position),"Event.Standard.Summon.Spawn");
        }
        private GameplayTargetData SelectTarget(GameplayStandardActor actor)
        {
            GameplayStandardActor selected=null;float nearest=float.PositiveInfinity;
            foreach(var target in Owner.World.Actors)
                if(target.IsTargetable&&target.Id!=actor.Id&&target.Faction!=actor.Faction)
                {float distance=GameplaySpatialMath.Distance(actor.Position,target.Position);if(distance<nearest){nearest=distance;selected=target;}}
            return selected==null?null:new GameplayTargetData(selected.Id,selected.Position);
        }
        private void OnCombat(GameplayCombatOutcome outcome){if(SummonedActor!=null&&!SummonedActor.IsTargetable)Finish(GameplaySpatialEndReason.Died);}
        internal override void Advance(float delta)
        {
            if(SummonedActor==null||SummonedActor.IsDisposed){Finish(GameplaySpatialEndReason.TargetLost);return;}
            if(!SummonedActor.IsTargetable){Finish(GameplaySpatialEndReason.Died);return;}
            Position=SummonedActor.Position;Direction=SummonedActor.Facing;Notify();_age=Math.Min(_duration,_age+delta);
            if(_age>=_duration)Finish(GameplaySpatialEndReason.Completed);
        }
        protected override void OnFinished(GameplaySpatialEndReason reason)
        {
            if(_binding==null)return;var actor=_binding.Actor;actor.CombatChanged-=OnCombat;
            try{Run(_definition.OnEnd,At(actor.Id,actor.Position),"Event.Standard.Summon.End",(float)reason,true);}
            finally{try{actor.Dispose();}finally{_binding.View?.Dispose();}}
        }
    }
    public sealed partial class GameplayStandardWorld
    {
        public GameplaySummonOperation SpawnSummon(GameplayStandardActor owner,GameplayContext context,GameplaySummonDefinition definition)
        {if(definition==null)throw new ArgumentNullException(nameof(definition));ValidateSpatialOwner(owner,context,out var grant);return RegisterSpatial(new GameplaySummonOperation(owner,context,grant,definition),grant);}
    }
    public sealed class GameplaySpawnSummonAction:IGameplaySkillAction
    {
        private readonly GameplaySummonDefinition _definition;
        public GameplaySpawnSummonAction(GameplaySummonDefinition definition)=>_definition=definition??throw new ArgumentNullException(nameof(definition));
        public GameplaySkillActionResult Execute(GameplayContext context,GameplayEventData triggerEvent,IGameplaySkillHost host)
        {if(!(host is GameplayStandardActor actor)||!GameplayStandardActivation.TryFindGrant(actor,context,out _))return GameplaySkillActionResult.Rejected;return actor.World.SpawnSummon(actor,context,_definition)!=null?GameplaySkillActionResult.Succeeded:GameplaySkillActionResult.Rejected;}
    }
}
