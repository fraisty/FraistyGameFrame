using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Extensions;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Preserves the actual event's target snapshot, including captured point and multi-target list.</summary>
    public sealed class GameplaySkillContextTarget:IGameplaySkillTarget
    { public GameplayTargetData Resolve(IGameplaySkillHost host,GameplayEventData triggerEvent)=>triggerEvent.Context?.TargetData; }
    /// <summary>Explicit captured world point; absence of an actor target is meaningful.</summary>
    public sealed class GameplaySkillPointTarget:IGameplaySkillTarget
    {private readonly GameplayPoint _point;public GameplaySkillPointTarget(GameplayPoint point)=>_point=point;public GameplayTargetData Resolve(IGameplaySkillHost host,GameplayEventData data)=>new GameplayTargetData(default,_point);}
    public enum GameplaySpatialEndReason { Completed, HitLimit, Arrived, Range, Cancelled, TargetLost, Controlled, Interrupted, Died }
    /// <summary>Optional presentation only; receives rule positions and never owns a rule clock.</summary>
    public interface IGameplaySpatialView:IDisposable { void Sync(GameplayPoint position,GameplayDirection direction); }
    public interface IGameplaySpatialViewFactory { IGameplaySpatialView Create(GameplayStandardActor actor,GameplayContext context); }
    /// <summary>Optional view contract for the actual, already evaluated region geometry.</summary>
    public interface IGameplayAreaGeometryView
    {
        void SetGeometry(GameplaySearchGeometry geometry);
        void SyncLifetime(float age, float duration);
    }
    /// <summary>One cast-owned transient spatial operation. Definition is immutable; this instance
    /// stores motion/membership only. Effect/Ability/Sequence ownership remains in the existing containers.</summary>
    public abstract class GameplaySpatialOperation:IGameplayAbilityTerminationAwareLifetime
    {
        public GameplayStandardActor Owner {get;}
        public GameplayContext Context {get;}
        /// <summary>False means a committed entity is owned by World/actor cleanup, not its
        /// launching grant. Its captured source and callbacks survive End/Cancel.</summary>
        public bool OwnedByAbility {get;}
        public GameplaySourceId Id=>Context.EffectiveSourceId;
        public GameplayPoint Position {get;protected set;}
        public GameplayDirection Direction {get;protected set;}
        public bool IsDisposed {get;private set;}
        public GameplaySpatialEndReason? EndReason {get;private set;}
        public event Action<GameplaySpatialOperation> Changed;
        public event Action<GameplaySpatialOperation,GameplaySpatialEndReason> Ended;
        private readonly GameplayAbilityHandle _grant;
        private readonly IGameplaySpatialViewFactory _factory;
        private IGameplaySpatialView _view;
        protected IGameplaySpatialView View => _view;
        protected GameplaySpatialOperation(GameplayStandardActor owner,GameplayContext context,GameplayAbilityHandle grant,IGameplaySpatialViewFactory factory,bool ownedByAbility=true)
        {
            Owner=owner;Context=context.WithSource(owner.World.Sources.Allocate());_grant=grant;_factory=factory;
            Position=owner.Position;Direction=owner.Facing;
            OwnedByAbility=ownedByAbility;
        }
        internal void Start()
        {
            if(_factory!=null)_view=_factory.Create(Owner,Context)??throw new InvalidOperationException("Spatial backend returned no actual view.");
            if(IsDisposed){_view?.Dispose();_view=null;return;}
            if(_view!=null)ConfigureView(_view);
            Notify();OnStart();
        }
        protected virtual void ConfigureView(IGameplaySpatialView view) { }
        protected virtual void OnStart() { }
        internal abstract void Advance(float delta);
        protected void Notify(){if(IsDisposed)return;_view?.Sync(Position,Direction);Changed?.Invoke(this);}
        protected GameplayContext At(GameplaySourceId target,GameplayPoint point)=>Context.WithTarget(new GameplayTargetData(target,point,null,target.IsValid?new[]{target}:Array.Empty<GameplaySourceId>()));
        protected void Run(IReadOnlyList<IGameplaySkillAction> actions,GameplayContext context,string signal,float magnitude=1,bool ending=false)
        {
            if(Owner.IsDisposed||Owner.World.IsDisposed||(!ending&&IsDisposed))return;
            var data=new GameplayEventData(new GameplayTag(signal),context,magnitude);
            Owner.RunPresentationCallback(actions,context,data,()=>!Owner.IsDisposed&&!Owner.World.IsDisposed&&(ending||!IsDisposed));
            if(!Owner.IsDisposed&&!Owner.World.IsDisposed&&(ending||!IsDisposed))Owner.Events.Publish(data);
        }
        public void Finish(GameplaySpatialEndReason reason)
        {
            if(IsDisposed)return;IsDisposed=true;EndReason=reason;
            Owner.World.DetachSpatial(this);Owner.Abilities.ReleaseOwnedLifetime(_grant,this);
            List<Exception> errors=null;
            try{OnFinished(reason);}catch(Exception ex){(errors??(errors=new List<Exception>())).Add(ex);}
            try{_view?.Dispose();}catch(Exception ex){(errors??(errors=new List<Exception>())).Add(ex);}finally{_view=null;}
            try{Ended?.Invoke(this,reason);}catch(Exception ex){(errors??(errors=new List<Exception>())).Add(ex);}
            if(errors!=null)throw new AggregateException(errors);
        }
        protected virtual void OnFinished(GameplaySpatialEndReason reason) { }
        public void Dispose()=>Finish(GameplaySpatialEndReason.Cancelled);
        public void Terminate(GameplayAbilityTerminationObservation observation)=>
            Finish(observation.Reason==GameplayAbilityTerminationReason.Ended?GameplaySpatialEndReason.Completed:GameplaySpatialEndReason.Cancelled);
    }
    public sealed partial class GameplayStandardWorld
    {
        private readonly List<GameplaySpatialOperation> _spatial=new List<GameplaySpatialOperation>();
        public IReadOnlyList<GameplaySpatialOperation> GetSpatialSnapshot()=>_spatial.ToArray();
        internal void DetachSpatial(GameplaySpatialOperation operation)=>_spatial.Remove(operation);
        internal T RegisterSpatial<T>(T operation,GameplayAbilityHandle grant) where T:GameplaySpatialOperation
        {
            _spatial.Add(operation);
            if(operation.OwnedByAbility&&!operation.Owner.Abilities.TryOwnLifetime(grant,operation)){operation.Dispose();return null;}
            try{operation.Start();return operation;}catch{operation.Dispose();throw;}
        }
        internal void ValidateSpatialOwner(GameplayStandardActor owner,GameplayContext context,out GameplayAbilityHandle grant)
        {
            grant=default;
            if(IsDisposed||owner==null||owner.World!=this||owner.IsDisposed||!GameplayStandardActivation.TryFindGrant(owner,context,out grant))
                throw new InvalidOperationException("Spatial work requires an active cast in this world.");
        }
    }
    internal static class GameplaySpatialMath
    {
        internal static float Distance(GameplayPoint a,GameplayPoint b)=> (float)Math.Sqrt((double)(b.X-a.X)*(b.X-a.X)+(double)(b.Y-a.Y)*(b.Y-a.Y)+(double)(b.Z-a.Z)*(b.Z-a.Z));
        internal static GameplayPoint Lerp(GameplayPoint a,GameplayPoint b,float t)=>new GameplayPoint(a.X+(b.X-a.X)*t,a.Y+(b.Y-a.Y)*t,a.Z+(b.Z-a.Z)*t);
        internal static GameplayPoint Offset(GameplayPoint a,GameplayDirection d,float distance)=>new GameplayPoint(a.X+d.X*distance,a.Y+d.Y*distance,a.Z+d.Z*distance);
        internal static GameplayDirection Direction(GameplayPoint a,GameplayPoint b,GameplayDirection fallback)=>Distance(a,b)>0?new GameplayDirection(b.X-a.X,b.Y-a.Y,b.Z-a.Z):fallback;
        internal static float Positive(IGameplayFloatValue value,GameplayStandardActor owner,GameplayContext context,string name)
        {
            float result=Finite(value,owner,context);
            if(result<=0)throw new ArithmeticException(name+" must be positive and finite.");return result;
        }
        internal static float Finite(IGameplayFloatValue value,GameplayStandardActor owner,GameplayContext context)
        {
            var target=owner;
            if(context.TargetData.HasPrimaryTarget)owner.World.TryGetActor(context.TargetData.PrimaryTargetId,out target);
            target=target??owner;
            var targeted=context.WithTarget(new GameplayTargetData(target.Id));
            float result=GameplayValueEvaluator.Evaluate(value,new GameplayEffectApplicationContext(targeted,target,targeted.Values.AsSetByCallerValues(),owner.Attributes,owner.Id,instigatorTarget:owner));
            return result;
        }
        internal static IReadOnlyList<IGameplaySkillAction> Actions(IEnumerable<IGameplaySkillAction> actions)
        {var copy=new List<IGameplaySkillAction>(actions??Array.Empty<IGameplaySkillAction>());if(copy.Exists(a=>a==null))throw new ArgumentException("Callback actions cannot be null.");return copy.AsReadOnly();}
    }
}
