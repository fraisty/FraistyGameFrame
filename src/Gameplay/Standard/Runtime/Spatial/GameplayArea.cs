using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Extensions;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Persistent region policy. Aura entries are contribution-owned by each region; ordinary
    /// enter/tick actions retain their own authored effect policies. Never shares membership in an SO.</summary>
    public sealed class GameplayAreaDefinition
    {
        public GameplaySearchShape Shape {get;}
        public GameplaySpatialPlane Plane {get;}
        public GameplaySearchOrigin Origin {get;}
        public GameplayPoint OriginOffset {get;}
        public IGameplayFloatValue Radius {get;}
        public IGameplayFloatValue Angle {get;}
        public IGameplayFloatValue Length {get;}
        public IGameplayFloatValue Width {get;}
        public IGameplayFloatValue Height {get;}
        public IGameplayFloatValue Duration {get;}
        public IGameplayFloatValue TickInterval {get;}
        public bool TickOnEnter {get;}
        public bool FollowOwner {get;}
        public bool EndOnOwnerDeath { get; }
        public IGameplayFloatValue ArmingDelay {get;}
        /// <summary>0 means unlimited entries. A positive limit finishes after that many successful
        /// enter callbacks, using the same cleanup path as expiry/cancel (trap placement semantics).</summary>
        public int TriggerLimit {get;}
        public GameplayFactionFilter Faction {get;}
        public GameplayTagQuery Tags {get;}
        public int LayerMask {get;}
        public IReadOnlyList<GameplayPoint> Cells {get;}
        public float CellSize {get;}
        public IReadOnlyList<GameplayEffectDefinition> Aura {get;}
        public IReadOnlyList<IGameplaySkillAction> OnEnter {get;}
        public IReadOnlyList<IGameplaySkillAction> OnTick {get;}
        public IReadOnlyList<IGameplaySkillAction> OnExit {get;}
        public IReadOnlyList<IGameplaySkillAction> OnEnd {get;}
        public IReadOnlyList<IGameplaySkillAction> OnStay {get;}
        public IGameplaySpatialViewFactory View {get;}
        public GameplayAreaDefinition(GameplaySearchShape shape,GameplaySpatialPlane plane,GameplaySearchOrigin origin,IGameplayFloatValue radius,
            IGameplayFloatValue angle,IGameplayFloatValue length,IGameplayFloatValue width,IGameplayFloatValue height,IGameplayFloatValue duration,IGameplayFloatValue tickInterval,
            bool tickOnEnter=true,bool followOwner=false,GameplayFactionFilter faction=GameplayFactionFilter.Enemies,GameplayTagQuery tags=null,int layerMask=-1,
            IEnumerable<GameplayEffectDefinition> aura=null,IEnumerable<IGameplaySkillAction> onEnter=null,IEnumerable<IGameplaySkillAction> onTick=null,
            IEnumerable<IGameplaySkillAction> onExit=null,IEnumerable<IGameplaySkillAction> onEnd=null,IEnumerable<GameplayPoint> cells=null,float cellSize=1,IGameplaySpatialViewFactory view=null,IEnumerable<IGameplaySkillAction> onStay=null,
            IGameplayFloatValue armingDelay=null,int triggerLimit=0,GameplayPoint originOffset=default,
            bool endOnOwnerDeath=false)
        {
            if(shape!=GameplaySearchShape.Circle&&shape!=GameplaySearchShape.Sector&&shape!=GameplaySearchShape.Box&&shape!=GameplaySearchShape.GridCells)throw new ArgumentOutOfRangeException(nameof(shape));
            if(!Enum.IsDefined(typeof(GameplaySpatialPlane),plane)||!Enum.IsDefined(typeof(GameplaySearchOrigin),origin)||!Enum.IsDefined(typeof(GameplayFactionFilter),faction))throw new ArgumentOutOfRangeException(nameof(plane));
            Shape=shape;Plane=plane;Origin=origin;Radius=radius??throw new ArgumentNullException(nameof(radius));Angle=angle??throw new ArgumentNullException(nameof(angle));
            OriginOffset=originOffset;
            Length=length??throw new ArgumentNullException(nameof(length));Width=width??throw new ArgumentNullException(nameof(width));Height=height??throw new ArgumentNullException(nameof(height));
            Duration=duration??throw new ArgumentNullException(nameof(duration));TickInterval=tickInterval??throw new ArgumentNullException(nameof(tickInterval));
            TickOnEnter=tickOnEnter;FollowOwner=followOwner;Faction=faction;Tags=tags??GameplayTagQuery.Empty;LayerMask=layerMask;
            EndOnOwnerDeath = endOnOwnerDeath;
            if(triggerLimit<0)throw new ArgumentOutOfRangeException(nameof(triggerLimit));TriggerLimit=triggerLimit;ArmingDelay=armingDelay??new GameplayConstantFloatValue(0);
            var effects=new List<GameplayEffectDefinition>(aura??Array.Empty<GameplayEffectDefinition>());
            if(effects.Exists(e=>e==null||e.DurationPolicy==GameplayEffectDurationPolicy.Instant))throw new ArgumentException("Aura requires persistent effect contributions.");
            Aura=effects.AsReadOnly();OnEnter=GameplaySpatialMath.Actions(onEnter);OnTick=GameplaySpatialMath.Actions(onTick);OnExit=GameplaySpatialMath.Actions(onExit);OnEnd=GameplaySpatialMath.Actions(onEnd);
            OnStay=GameplaySpatialMath.Actions(onStay);
            Cells=new List<GameplayPoint>(cells??Array.Empty<GameplayPoint>()).AsReadOnly();CellSize=cellSize;View=view;
            if(!float.IsFinite(cellSize)||cellSize<=0)throw new ArgumentOutOfRangeException(nameof(cellSize));
        }
    }
    /// <summary>World-owned persistent region. One query supplies enter/exit/tick membership each step.
    /// An aura contribution cleans up on exit, region end, cast cancellation and owner cleanup;
    /// overlapping sources and shared stack layers are never removed by a definition scan.</summary>
    public sealed class GameplayAreaOperation:GameplaySpatialOperation
    {
        private readonly GameplayAreaDefinition _definition;
        private readonly float _radius,_angle,_length,_width,_height,_duration,_period;
        private readonly float _arming;
        private readonly GameplayPoint _initialPosition;
        private readonly Dictionary<GameplaySourceId,GameplayEffectLease[]> _inside=new Dictionary<GameplaySourceId,GameplayEffectLease[]>();
        private double _age;
        private long _ticks;
        public int InsideCount=>_inside.Count;
        public bool IsArmed=>!IsDisposed&&_age>=_arming;
        public int TriggerCount {get;private set;}
        internal GameplayAreaOperation(GameplayStandardActor owner,GameplayContext context,GameplayAbilityHandle grant,GameplayAreaDefinition definition)
            :base(owner,context,grant,definition.View)
        {
            _definition=definition;
            _radius=definition.Shape==GameplaySearchShape.Circle||definition.Shape==GameplaySearchShape.Sector?GameplaySpatialMath.Positive(definition.Radius,owner,context,"Area radius"):1;
            _angle=definition.Shape==GameplaySearchShape.Sector?GameplaySpatialMath.Positive(definition.Angle,owner,context,"Area angle"):120;
            _length=definition.Shape==GameplaySearchShape.Box?GameplaySpatialMath.Positive(definition.Length,owner,context,"Area length"):1;
            _width=definition.Shape==GameplaySearchShape.Box?GameplaySpatialMath.Positive(definition.Width,owner,context,"Area width"):1;
            _height=GameplaySpatialMath.Positive(definition.Height,owner,context,"Area height");_duration=GameplaySpatialMath.Positive(definition.Duration,owner,context,"Area duration");
            _period=GameplaySpatialMath.Positive(definition.TickInterval,owner,context,"Area period");
            _arming=GameplaySpatialMath.Finite(definition.ArmingDelay,owner,context);
            if(_arming<0||_arming>=_duration)throw new ArithmeticException("Area arming delay must be nonnegative and shorter than its duration.");
            if(definition.Origin==GameplaySearchOrigin.ContextPoint)
            {if(!context.TargetData.HasPoint)throw new InvalidOperationException("Area requires a captured point.");Position=context.TargetData.Point;}
            else if(definition.Origin==GameplaySearchOrigin.PrimaryTarget)
            {if(!owner.World.TryGetActor(context.TargetData.PrimaryTargetId,out var target))throw new InvalidOperationException("Area target is missing.");Position=target.Position;}
            Position=new GameplayPoint(Position.X+definition.OriginOffset.X,Position.Y+definition.OriginOffset.Y,Position.Z+definition.OriginOffset.Z);
            _initialPosition=Position;
            Geometry(); // Validate before registration/visibility.
        }
        private GameplaySearchGeometry Geometry()
        {
            IReadOnlyList<GameplayPoint> cells=_definition.Cells;
            if(_definition.Shape==GameplaySearchShape.GridCells&&_definition.FollowOwner)
            {
                var shifted=new GameplayPoint[cells.Count];
                for(int i=0;i<shifted.Length;i++)shifted[i]=new GameplayPoint(cells[i].X+Position.X-_initialPosition.X,cells[i].Y+Position.Y-_initialPosition.Y,cells[i].Z+Position.Z-_initialPosition.Z);
                cells=shifted;
            }
            return new GameplaySearchGeometry(_definition.Shape,_definition.Plane,Position,Direction,_radius,_angle,_length,_width,_height,cells:cells,cellSize:_definition.CellSize);
        }
        protected override void ConfigureView(IGameplaySpatialView view)
        {
            if (view is IGameplayAreaGeometryView geometryView)
            {
                geometryView.SetGeometry(Geometry());
                geometryView.SyncLifetime(0,_duration);
            }
        }
        protected override void OnStart(){if(IsArmed)UpdateMembership();}
        internal override void Advance(float delta)
        {
            if(Owner.IsDisposed){Dispose();return;}
            if (_definition.EndOnOwnerDeath && !Owner.IsTargetable)
            {
                Finish(GameplaySpatialEndReason.Died);
                return;
            }
            double next=Math.Min(_duration,_age+(double)delta);
            double boundaries=Math.Floor(Math.Max(0,next-_arming)/_period);
            if(boundaries-_ticks>4096)throw new InvalidOperationException("Area step exceeds the callback budget; advance the world in smaller steps.");
            long due=(long)boundaries;
            float activeDelta=(float)(Math.Max(0,next-_arming)-Math.Max(0,_age-_arming));
            _age=next;
            if (View is IGameplayAreaGeometryView geometryView) geometryView.SyncLifetime((float)_age,_duration);
            if(_definition.FollowOwner){var origin=Owner.Position;var offset=_definition.OriginOffset;Position=new GameplayPoint(origin.X+offset.X,origin.Y+offset.Y,origin.Z+offset.Z);Direction=Owner.Facing;Notify();}
            if(!IsArmed)return;
            UpdateMembership();
            if(activeDelta>0&&_definition.OnStay.Count>0)foreach(var id in SortedInside())
                if(!IsDisposed&&Owner.World.TryGetActor(id,out var target)&&target.IsTargetable)
                    Run(_definition.OnStay,At(id,target.Position),"Event.Standard.Area.Stay",activeDelta);
            while(!IsDisposed&&_ticks<due)
            {
                _ticks++;
                foreach(var id in SortedInside())
                    if(!IsDisposed&&Owner.World.TryGetActor(id,out var target)&&target.IsTargetable)
                        Run(_definition.OnTick,At(id,target.Position),"Event.Standard.Area.Tick",(float)(_ticks*_period));
            }
            if(!IsDisposed&&_age>=_duration)Finish(GameplaySpatialEndReason.Completed);
        }
        private GameplaySourceId[] SortedInside(){var ids=new List<GameplaySourceId>(_inside.Keys);ids.Sort((a,b)=>a.Value.CompareTo(b.Value));return ids.ToArray();}
        private void UpdateMembership()
        {
            var selected=Owner.World.Spatial.Query(new GameplaySpatialSearchRequest(Context,Geometry(),Owner.Faction,_definition.Faction,_definition.Tags,_definition.LayerMask));
            var current=new HashSet<GameplaySourceId>(selected.Targets);
            foreach(var id in SortedInside())if(!current.Contains(id))Exit(id);
            foreach(var id in selected.Targets)
            {
                if(IsDisposed)return;
                if(!Owner.World.TryGetActor(id,out var target)||!target.IsTargetable)continue;
                bool entered=!_inside.TryGetValue(id,out var leases);
                if(entered){leases=new GameplayEffectLease[_definition.Aura.Count];_inside.Add(id,leases);}
                for(int i=0;i<leases.Length&&!IsDisposed;i++)
                {
                    if(leases[i]?.IsActive==true)continue;
                    leases[i]?.Dispose();leases[i]=null;
                    var result=Owner.ApplyEffect(At(id,target.Position),_definition.Aura[i]);
                    if(GameplayEffectLease.TryCapture(target.Effects,result,out var lease))
                    {if(IsDisposed)lease.Dispose();else leases[i]=lease;}
                }
                if(entered&&!IsDisposed)
                {
                    var context=At(id,target.Position);Run(_definition.OnEnter,context,"Event.Standard.Area.Enter");
                    if(IsDisposed)return;
                    TriggerCount++;
                    if(_definition.TickOnEnter&&!IsDisposed)Run(_definition.OnTick,context,"Event.Standard.Area.Tick",0);
                    if(!IsDisposed&&_definition.TriggerLimit>0&&TriggerCount>=_definition.TriggerLimit){Finish(GameplaySpatialEndReason.HitLimit);return;}
                }
            }
        }
        private void Exit(GameplaySourceId id)
        {
            if(!_inside.TryGetValue(id,out var leases))return;_inside.Remove(id);
            List<Exception> errors=null;
            foreach(var lease in leases)try{lease?.Dispose();}catch(Exception ex){(errors??(errors=new List<Exception>())).Add(ex);}
            var point=Owner.World.TryGetActor(id,out var target)?target.Position:Position;
            try{Run(_definition.OnExit,At(id,point),"Event.Standard.Area.Exit",1,IsDisposed);}catch(Exception ex){(errors??(errors=new List<Exception>())).Add(ex);}
            if(errors!=null)throw new AggregateException(errors);
        }
        protected override void OnFinished(GameplaySpatialEndReason reason)
        {
            List<Exception> errors=null;
            foreach(var id in SortedInside())try{Exit(id);}catch(Exception ex){(errors??(errors=new List<Exception>())).Add(ex);}
            try{Run(_definition.OnEnd,Context,"Event.Standard.Area.End",(float)reason,true);}catch(Exception ex){(errors??(errors=new List<Exception>())).Add(ex);}
            if(errors!=null)throw new AggregateException(errors);
        }
    }
    public sealed partial class GameplayStandardWorld
    {
        public GameplayAreaOperation SpawnArea(GameplayStandardActor owner,GameplayContext context,GameplayAreaDefinition definition)
        {if(definition==null)throw new ArgumentNullException(nameof(definition));ValidateSpatialOwner(owner,context,out var grant);return RegisterSpatial(new GameplayAreaOperation(owner,context,grant,definition),grant);}
    }
    public sealed class GameplaySpawnAreaAction:IGameplaySkillAction
    {
        public GameplayAreaDefinition Definition {get;}
        public GameplaySpawnAreaAction(GameplayAreaDefinition definition)=>Definition=definition??throw new ArgumentNullException(nameof(definition));
        public GameplaySkillActionResult Execute(GameplayContext context,GameplayEventData triggerEvent,IGameplaySkillHost host)
        {if(!(host is GameplayStandardActor actor)||!GameplayStandardActivation.TryFindGrant(actor,context,out var grant))return GameplaySkillActionResult.Rejected;return actor.World.SpawnArea(actor,context,Definition)!=null?GameplaySkillActionResult.Succeeded:GameplaySkillActionResult.Rejected;}
    }
}
