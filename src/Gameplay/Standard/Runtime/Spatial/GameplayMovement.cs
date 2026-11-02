using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Extensions;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Actual displacement using existing request semantics. One target has one active
    /// command; equal/higher priority interrupts, lower priority is ignored. Direction is captured
    /// at start unless pull explicitly follows the moving source. Detached commands remain world-owned.</summary>
    public sealed class GameplayMovementOperation:GameplaySpatialOperation
    {
        public GameplayStandardActor Target {get;}
        public GameplayDisplacementRequest Request {get;}
        public int Priority {get;}
        public float Travelled {get;private set;}
        private readonly GameplayTagQuery _blocked;
        private readonly IReadOnlyList<IGameplaySkillAction> _arrive,_cancel;
        private readonly float _distance;
        private readonly bool _followSource;
        private readonly float _minimumSeparation, _duration;
        private float _age;
        internal GameplayMovementOperation(GameplayStandardActor owner,GameplayContext context,GameplayAbilityHandle grant,
            GameplayStandardActor target,GameplayDisplacementRequest request,int priority,GameplayTagQuery blocked,
            IEnumerable<IGameplaySkillAction> arrive,IEnumerable<IGameplaySkillAction> cancelled,bool cancelOnAbilityEnd=true,
            bool followSource=false,float minimumSeparation=0,float duration=0):base(owner,context,grant,null,cancelOnAbilityEnd)
        {
            _followSource=followSource;_minimumSeparation=minimumSeparation;_duration=duration;
            Target=target;Request=request;Priority=priority;_blocked=blocked;
            _arrive=GameplaySpatialMath.Actions(arrive);_cancel=GameplaySpatialMath.Actions(cancelled);Position=target.Position;
            Direction=request.Mode==GameplayDisplacementMode.Directional?request.Direction.Value:
                request.Mode==GameplayDisplacementMode.PushFromSource?GameplaySpatialMath.Direction(owner.Position,target.Position,owner.Facing):GameplaySpatialMath.Direction(target.Position,owner.Position,owner.Facing);
            _distance=request.Mode==GameplayDisplacementMode.PullToSource&&!followSource?Math.Min(request.Distance,Math.Max(0,GameplaySpatialMath.Distance(target.Position,owner.Position)-minimumSeparation)):request.Distance;
        }
        protected override void OnStart(){if(_distance==0)Finish(GameplaySpatialEndReason.Arrived);}
        internal override void Advance(float delta)
        {
            if(Target.IsDisposed||!Target.IsTargetable){Finish(GameplaySpatialEndReason.TargetLost);return;}
            if(_blocked!=null&&_blocked.Matches(Target.Tags)){Finish(GameplaySpatialEndReason.Controlled);return;}
            if(_followSource&&Request.Mode==GameplayDisplacementMode.PullToSource)
                Direction=GameplaySpatialMath.Direction(Target.Position,Owner.Position,Direction);
            float step=Math.Min(_distance-Travelled,Request.Speed*delta);
            if(Request.Mode==GameplayDisplacementMode.PullToSource)
                step=Math.Min(step,Math.Max(0,GameplaySpatialMath.Distance(Target.Position,Owner.Position)-_minimumSeparation));
            if(_duration>0)step=Math.Min(step,Request.Speed*Math.Max(0,_duration-_age));
            _age+=delta;
            Target.Position=GameplaySpatialMath.Offset(Target.Position,Direction,step);Target.Facing=Direction;
            Position=Target.Position;Travelled+=step;Notify();
            if(!IsDisposed&&(Travelled>=_distance||Request.Mode==GameplayDisplacementMode.PullToSource&&GameplaySpatialMath.Distance(Target.Position,Owner.Position)<=_minimumSeparation||_duration>0&&_age>=_duration))Finish(GameplaySpatialEndReason.Arrived);
        }
        protected override void OnFinished(GameplaySpatialEndReason reason)
        {
            Owner.World.DetachMovement(this);
            if(reason==GameplaySpatialEndReason.Arrived)Run(_arrive,At(Target.Id,Position),"Event.Standard.Move.Arrive",Travelled,true);
            else if(reason!=GameplaySpatialEndReason.Completed)Run(_cancel,At(Target.Id,Position),"Event.Standard.Move.Cancelled",(float)reason,true);
        }
    }
    public sealed partial class GameplayStandardWorld
    {
        private readonly Dictionary<GameplaySourceId,GameplayMovementOperation> _movements=new Dictionary<GameplaySourceId,GameplayMovementOperation>();
        internal void DetachMovement(GameplayMovementOperation operation)
        {if(_movements.TryGetValue(operation.Target.Id,out var current)&&ReferenceEquals(current,operation))_movements.Remove(operation.Target.Id);}
        public GameplayMovementOperation StartMovement(GameplayStandardActor owner,GameplayDisplacementRequest request,int priority=10,
            GameplayTagQuery blocked=null,IEnumerable<IGameplaySkillAction> onArrive=null,IEnumerable<IGameplaySkillAction> onCancelled=null,
            bool cancelOnAbilityEnd=true,bool followSource=false,float minimumSeparation=0,float duration=0)
        {
            if(!float.IsFinite(minimumSeparation)||minimumSeparation<0||!float.IsFinite(duration)||duration<0)throw new ArgumentOutOfRangeException(nameof(minimumSeparation));
            if(request==null)throw new ArgumentNullException(nameof(request));ValidateSpatialOwner(owner,request.Context,out var grant);
            if(!TryGetActor(request.TargetId,out var target)||!target.IsTargetable||blocked!=null&&blocked.Matches(target.Tags))return null;
            _movements.TryGetValue(target.Id,out var previous);
            if(previous!=null&&!previous.IsDisposed&&previous.Priority>priority)return null;
            var operation=new GameplayMovementOperation(owner,request.Context,grant,target,request,priority,blocked,onArrive,onCancelled,cancelOnAbilityEnd,followSource,minimumSeparation,duration);
            _movements[target.Id]=operation;
            try
            {
                previous?.Finish(GameplaySpatialEndReason.Interrupted);
                if(operation.IsDisposed)return null;
                return RegisterSpatial(operation,grant);
            }
            catch{operation.Dispose();throw;}
        }
    }
    /// <summary>Immediate heading command for the actual actor authority, including 2D/3D/logical bindings.</summary>
    public sealed class GameplayRotateToPointAction:IGameplaySkillAction
    {
        public bool RotateOwner {get;}
        public GameplayRotateToPointAction(bool rotateOwner=true)=>RotateOwner=rotateOwner;
        public GameplaySkillActionResult Execute(GameplayContext context,GameplayEventData triggerEvent,IGameplaySkillHost host)
        {
            if(!(host is GameplayStandardActor owner))return GameplaySkillActionResult.Rejected;
            var target=owner;if(!RotateOwner&&!owner.World.TryGetActor(context.TargetData.PrimaryTargetId,out target))return GameplaySkillActionResult.Rejected;
            var point=context.TargetData.HasPoint?context.TargetData.Point:owner.Position;
            target.Facing=GameplaySpatialMath.Direction(target.Position,point,target.Facing);return GameplaySkillActionResult.Succeeded;
        }
    }
    public sealed class GameplayMoveTargetsAction:IGameplaySkillAction
    {
        public GameplayDisplacementMode Mode {get;}
        public IGameplayFloatValue Distance {get;}
        public IGameplayFloatValue Speed {get;}
        public int Priority {get;}
        public GameplayTagQuery Blocked {get;}
        public IReadOnlyList<IGameplaySkillAction> OnArrive {get;}
        public IReadOnlyList<IGameplaySkillAction> OnCancelled {get;}
        public bool CancelOnAbilityEnd {get;}
        public bool FollowSource {get;}
        public float MinimumSeparation {get;}
        public float Duration {get;}
        public GameplayMoveTargetsAction(GameplayDisplacementMode mode,IGameplayFloatValue distance,IGameplayFloatValue speed,int priority=10,GameplayTagQuery blocked=null,
            IEnumerable<IGameplaySkillAction> onArrive=null,IEnumerable<IGameplaySkillAction> onCancelled=null,
            bool cancelOnAbilityEnd=true,bool followSource=false,float minimumSeparation=0,float duration=0)
        {
            if(!float.IsFinite(minimumSeparation)||minimumSeparation<0||!float.IsFinite(duration)||duration<0)throw new ArgumentOutOfRangeException(nameof(minimumSeparation));
            CancelOnAbilityEnd=cancelOnAbilityEnd;FollowSource=followSource;MinimumSeparation=minimumSeparation;Duration=duration;
            if(!Enum.IsDefined(typeof(GameplayDisplacementMode),mode))throw new ArgumentOutOfRangeException(nameof(mode));
            Mode=mode;Distance=distance??throw new ArgumentNullException(nameof(distance));Speed=speed??throw new ArgumentNullException(nameof(speed));
            Priority=priority;Blocked=blocked;OnArrive=GameplaySpatialMath.Actions(onArrive);OnCancelled=GameplaySpatialMath.Actions(onCancelled);
        }
        public GameplaySkillActionResult Execute(GameplayContext context,GameplayEventData triggerEvent,IGameplaySkillHost host)
        {
            if(!(host is GameplayStandardActor actor)||!GameplayStandardActivation.TryFindGrant(actor,context,out var grant))return GameplaySkillActionResult.Rejected;
            var ids=context.TargetData.Targets.Count>0?context.TargetData.Targets:context.TargetData.HasPrimaryTarget?new[]{context.TargetData.PrimaryTargetId}:Array.Empty<GameplaySourceId>();
            foreach(var id in ids)
            {
                if(!GameplayStandardActivation.TryFindGrant(actor,context,out grant))break;
                if(!actor.World.TryGetActor(id,out var target))continue;
                var targeted=context.WithTarget(new GameplayTargetData(id,target.Position));
                float distance=GameplaySpatialMath.Positive(Distance,actor,targeted,"Movement distance"),speed=GameplaySpatialMath.Positive(Speed,actor,targeted,"Movement speed");
                actor.World.StartMovement(actor,new GameplayDisplacementRequest(targeted,id,Mode,distance,speed,Mode==GameplayDisplacementMode.Directional?(GameplayDirection?)actor.Facing:null),Priority,Blocked,OnArrive,OnCancelled,CancelOnAbilityEnd,FollowSource,MinimumSeparation,Duration);
            }
            return GameplaySkillActionResult.Succeeded;
        }
    }
}
