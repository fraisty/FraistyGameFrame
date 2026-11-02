using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Standard;

namespace FRFrame.Gameplay.Unity
{
    public sealed partial class GameplayUnityAnimationController
    {
        private readonly Dictionary<string,List<QueuedPlayback>> _queues=new Dictionary<string,List<QueuedPlayback>>();
        private readonly HashSet<string> _replacingChannels=new HashSet<string>();
        private static long _queueOrder;
        private static bool _promotingQueues;
        /// <summary>Queued source factory failures are observable; a bad request is released, and
        /// the next surviving request may start. No gameplay hit/cost is driven by this event.</summary>
        public event Action<GameplayContext,string> AnimationQueueFailed;
        public int QueuedCount {get{int count=0;foreach(var queue in _queues.Values)count+=queue.Count;return count;}}
        /// <summary>FIFO on the authored channel, bound to the request's exact active cast. Native
        /// resources are acquired only when promoted. A looping current playback needs explicit replay/cancel.
        /// Each channel is bounded to 32 requests so authoring mistakes cannot accumulate indefinitely.</summary>
        public bool TryQueue(GameplayAnimationDefinition definition,GameplayContext context)
        {
            if(definition==null)throw new ArgumentNullException(nameof(definition));
            if(_disposed||Actor.IsDisposed)return RejectAnimation(definition,context,"OwnerDisposed");
            if(Subject==null)return RejectAnimation(definition,context,"SubjectMissing");
            if(context==null||!GameplayStandardActivation.TryFindGrant(Actor,context,out var grant))return RejectAnimation(definition,context,"ActiveGrantMissing");
            GameplayAnimationWriteTarget[] writes;
            try{writes=definition.ResolveWrites(Subject);}catch(Exception ex){return RejectAnimation(definition,context,"PhysicalTargetMissing: "+ex.Message);}
            bool occupied=_channels.ContainsKey(definition.Channel);
            foreach(var playback in PhysicalPlayers)occupied|=Conflicts(playback.Writes,writes);
            occupied|=EarlierQueuedConflict(writes,long.MaxValue);
            if(!occupied)return TryPlay(definition,context);
            if(!_queues.TryGetValue(definition.Channel,out var queue)){queue=new List<QueuedPlayback>();_queues.Add(definition.Channel,queue);}
            if(queue.Count>=32)return RejectAnimation(definition,context,"QueueFull (32)");
            if(definition.CaptureVariantRoll)context=GameplayAnimationVariantSelector.Capture(context,AnimationRandom);
            var request=new QueuedPlayback(this,definition,context,grant,++_queueOrder);queue.Add(request);
            if(!Actor.Abilities.TryOwnLifetime(grant,request)){request.Dispose();return RejectAnimation(definition,context,"GrantLifetimeEnded");}PromoteQueue(definition.Channel);return true;
        }
        private static bool EarlierQueuedConflict(GameplayAnimationWriteTarget[] writes,long order)
        {
            foreach(var controller in Controllers)foreach(var queue in controller._queues.Values)foreach(var request in queue)
                if(request.Order<order&&GameplayStandardActivation.TryFindGrant(controller.Actor,request.Context,out _)){
                    try{if(Conflicts(request.Definition.ResolveWrites(controller.Subject),writes))return true;}
                    catch{ /* Invalid targets are diagnosed and released when their channel head promotes. */ }
                }
            return false;
        }
        private void PromoteQueue(string channel)
        {
            if(ReplacingPhysicalPlayers>0||_promotingQueues)return;
            _promotingQueues=true;
            try
            {
                bool changed;
                do
                {
                    changed=false;
                    var fronts=new List<QueuedPlayback>();foreach(var controller in Controllers)foreach(var queue in controller._queues.Values)if(queue.Count>0)fronts.Add(queue[0]);
                    fronts.Sort((a,b)=>a.Order.CompareTo(b.Order));
                    foreach(var request in fronts)
                    {
                        var owner=request.Owner;
                        if(!GameplayStandardActivation.TryFindGrant(owner.Actor,request.Context,out _)){request.Dispose();changed=true;continue;}
                        try
                        {
                            var writes=request.Definition.ResolveWrites(owner.Subject);
                            bool occupied=owner._channels.ContainsKey(request.Definition.Channel);
                            foreach(var playback in PhysicalPlayers)occupied|=Conflicts(playback.Writes,writes);
                            occupied|=EarlierQueuedConflict(writes,request.Order);
                            if(occupied)continue;
                            request.Dispose();changed=true;owner.TryPlay(request.Definition,request.Context);
                        }
                        catch(Exception ex){request.Dispose();changed=true;owner.RejectAnimation(request.Definition,request.Context,"QueueBackendFailure: "+ex.Message);owner.AnimationQueueFailed?.Invoke(request.Context,ex.Message);}
                    }
                }while(changed);
            }
            finally{_promotingQueues=false;}
        }
        private void DisposeQueues()
        {
            var requests=new List<QueuedPlayback>();foreach(var queue in _queues.Values)requests.AddRange(queue);
            foreach(var request in requests)request.Dispose();_queues.Clear();
        }
        private sealed class QueuedPlayback:IDisposable
        {
            private readonly GameplayUnityAnimationController _owner;
            private readonly GameplayAbilityHandle _grant;
            private bool _disposed;
            internal GameplayAnimationDefinition Definition {get;}
            internal GameplayContext Context {get;}
            internal long Order {get;}
            internal GameplayUnityAnimationController Owner=>_owner;
            internal QueuedPlayback(GameplayUnityAnimationController owner,GameplayAnimationDefinition definition,GameplayContext context,GameplayAbilityHandle grant,long order)
            {_owner=owner;Definition=definition;Context=context;_grant=grant;Order=order;}
            public void Dispose()
            {
                if(_disposed)return;_disposed=true;
                if(_owner._queues.TryGetValue(Definition.Channel,out var queue)){queue.Remove(this);if(queue.Count==0)_owner._queues.Remove(Definition.Channel);}
                _owner.Actor.Abilities.ReleaseOwnedLifetime(_grant,this);
            }
        }
    }
}
