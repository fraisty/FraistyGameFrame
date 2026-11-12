using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Per-actor presentation only. Higher priority preempts; equal priority is an explicit
    /// replay; lower priority is ignored. Uses the existing host delta, samples before new markers,
    /// and joins the exact cast lifetime. No rule container or second gameplay clock is created.</summary>
    public sealed partial class GameplayUnityAnimationController : IGameplayPresentationDriver
    {
        public GameplayStandardActor Actor { get; }
        public GameObject Subject { get; }
        /// <summary>Presentation-only variant stream. Kept separate from World combat randomness;
        /// explicit captured RollKey inputs bypass it and are shared with Editor preview.</summary>
        public IGameplayRandomSource AnimationRandom {get;set;}=new GameplaySeededRandomSource(1);
        private readonly Dictionary<string,Playback> _channels = new Dictionary<string,Playback>();
        private static readonly HashSet<Playback> PhysicalPlayers=new HashSet<Playback>();
        private static readonly HashSet<GameplayUnityAnimationController> Controllers=new HashSet<GameplayUnityAnimationController>();
        private static int ReplacingPhysicalPlayers;
        private bool _disposed;
        /// <summary>Observable presentation refusal; does not change combat eligibility or cost.</summary>
        public string LastAnimationRejection {get;private set;}
        public event Action<GameplayContext,string> AnimationRejected;
        /// <summary>Currently sampled cast-owned player resources. Unreported backends still expose their actual write target.</summary>
        public IEnumerable<(string Channel, string Source, GameplaySourceId Activation)> ActiveAnimationSources()
        {
            foreach (var playback in _channels.Values)
                if (playback.IsPlaying)
                    yield return (playback.Channel, playback.Source, playback.Activation);
        }
        private bool RejectAnimation(GameplayAnimationDefinition definition,GameplayContext context,string reason)
        {
            LastAnimationRejection=$"{reason}; source={context?.EffectiveSourceId}; channel={definition.Channel}; priority={definition.Priority}";
            Actor.RecordSkillTrace(new GameplaySkillTraceRecord(Actor.Id,context,default,GameplaySkillTraceStage.ActionRejected,
                "AnimationRejected",detail:LastAnimationRejection));
            if(AnimationRejected!=null)foreach(Action<GameplayContext,string> observer in AnimationRejected.GetInvocationList())
                try{observer(context,LastAnimationRejection);}catch{ /* Diagnostics cannot gate gameplay. */ }
            return false;
        }
        public GameplayUnityAnimationController(GameplayStandardActor actor,GameObject subject)
        {
            Actor=actor??throw new ArgumentNullException(nameof(actor));Subject=subject!=null?subject:throw new ArgumentNullException(nameof(subject));
            Actor.CombatChanged+=OnFloatingCombat;
            Actor.Effects.PresentationChanged+=ObserveEffectState;Actor.Effects.Removed+=ObserveEffectRemoval;
            Controllers.Add(this);
        }
        public bool TryPlay(GameplayAnimationDefinition definition,GameplayContext context)
        {
            if(definition==null)throw new ArgumentNullException(nameof(definition));
            if(_disposed||Actor.IsDisposed)return RejectAnimation(definition,context,"OwnerDisposed");
            if(Subject==null)return RejectAnimation(definition,context,"SubjectMissing");
            if(context==null||!GameplayStandardActivation.TryFindGrant(Actor,context,out var grant))return RejectAnimation(definition,context,"ActiveGrantMissing");
            if(_channels.TryGetValue(definition.Channel,out var previous)&&previous.Priority>definition.Priority)return RejectAnimation(definition,context,"PriorityDenied by "+previous.Priority);
            GameplayAnimationWriteTarget[] writes;
            try{writes=definition.ResolveWrites(Subject);}catch(Exception ex){return RejectAnimation(definition,context,"PhysicalTargetMissing: "+ex.Message);}
            var conflicts=new List<Playback>();
            foreach(var existing in PhysicalPlayers)
                if(existing.Owner==this&&existing.Channel==definition.Channel||Conflicts(existing.Writes,writes))
                {
                    if(existing.Priority>definition.Priority)return RejectAnimation(definition,context,"PhysicalPriorityDenied by "+existing.Channel+"/"+existing.Priority+" on "+string.Join(",",Array.ConvertAll(writes,w=>w.ToString())));
                    conflicts.Add(existing);
                }
            var application=PresentationValueContext(context);
            float speed=GameplayValueEvaluator.Evaluate(definition.Speed,application);
            if(!float.IsFinite(speed)||speed<=0)throw new ArithmeticException("Animation speed must be positive.");
            if(definition.CaptureVariantRoll)context=GameplayAnimationVariantSelector.Capture(context,AnimationRandom);
            // Preflight factory before replacing a valid clip. A malformed resource cannot erase the previous channel.
            IGameplayAnimationPlayer player;
            try {player=definition.Create(Subject,context);}
            catch(Exception ex){RejectAnimation(definition,context,"BackendOrResourceFailure: "+ex.Message);throw;}
            if(player==null){RejectAnimation(definition,context,"BackendUnsupported: no player");throw new InvalidOperationException("Animation backend returned no real player.");}
            if(!float.IsFinite(player.Duration)||player.Duration<=0){player.Dispose();RejectAnimation(definition,context,"InvalidResourceDuration");throw new InvalidOperationException("Animation duration must be positive.");}
            _replacingChannels.Add(definition.Channel);
            ReplacingPhysicalPlayers++;
            foreach(var conflict in conflicts)_replacingChannels.Add(conflict.Channel);
            try{foreach(var conflict in conflicts)conflict.Dispose();}
            finally{_replacingChannels.Clear();ReplacingPhysicalPlayers--;}
            var playback=new Playback(this,definition.Channel,definition.Priority,context,grant,player,speed,writes);
            _channels[definition.Channel]=playback;
            PhysicalPlayers.Add(playback);
            if(!Actor.Abilities.TryOwnLifetime(grant,playback)){playback.Dispose();return RejectAnimation(definition,context,"GrantLifetimeEnded");}
            try {player.Seek(0);playback.Sampled=true;PromoteQueue(definition.Channel);return true;}catch{playback.Dispose();throw;}
        }
        public void Advance(float amount)
        {
            if(_disposed)return;
            if(!float.IsFinite(amount)||amount<0)throw new ArgumentOutOfRangeException(nameof(amount));
            var cues=_cues.ToArray();
            var floating=_floating.ToArray();
            foreach(var playback in new List<Playback>(_channels.Values))playback.Advance(amount);
            foreach(var cue in cues)cue.Advance(amount);
            foreach(var text in floating)text.Advance(amount);
            AdvanceEffectPresentation(amount);
            FlushCombatText();
            _ownedAudio?.Update(amount,amount);
        }
        public void Dispose()
        {
            if(_disposed)return;_disposed=true;
            Controllers.Remove(this);
            Actor.CombatChanged-=OnFloatingCombat;
            _pendingCombatText.Clear();
            foreach(var playback in new List<Playback>(_channels.Values))playback.Dispose();_channels.Clear();
            DisposeQueues();
            foreach(var cue in _cues.ToArray())cue.Dispose();
            foreach(var text in _floating.ToArray())text.Dispose();
            DisposeEffectPresentation();
            _ownedPool?.ClearAll();_ownedAudio?.Destroy();
        }
        private sealed class Playback:IDisposable
        {
            private readonly GameplayUnityAnimationController _owner;
            private readonly string _channel;
            private readonly GameplayContext _context;
            private readonly GameplayAbilityHandle _grant;
            private readonly IGameplayAnimationPlayer _player;
            private readonly float _speed;
            private double _elapsed;
            private bool _disposed;
            internal int Priority { get; }
            internal string Channel=>_channel;
            internal GameplayUnityAnimationController Owner=>_owner;
            internal GameplayAnimationWriteTarget[] Writes {get;}
            internal bool Sampled { get; set; }
            internal bool IsPlaying => Sampled && !_disposed && !_player.IsInterrupted;
            internal string Source => (_player as IGameplayAnimationPlaybackSource)?.PlaybackSource ??
                (Writes.Length > 0 ? Writes[0].Domain + " · " + Writes[0].Target.name : "后端未报告资源");
            internal GameplaySourceId Activation => _context.ActivationSourceId;
            internal Playback(GameplayUnityAnimationController owner,string channel,int priority,GameplayContext context,GameplayAbilityHandle grant,IGameplayAnimationPlayer player,float speed,GameplayAnimationWriteTarget[] writes)
            {_owner=owner;_channel=channel;Priority=priority;_context=context;_grant=grant;_player=player;_speed=speed;Writes=writes;_player.AnimationEvent+=OnEvent;}
            private void OnEvent(string name)
            {
                if(_disposed)return;
                // Native event names are exposed through a typed signal for actual game/definition event mapping.
                _owner.AnimationEvent?.Invoke(_context,name);
            }
            internal void Advance(float delta)
            {
                if(_disposed)return;
                if(_player.IsInterrupted){Dispose();return;}
                _elapsed+=(double)delta*_speed;
                float time=(float)Math.Min(float.MaxValue,_elapsed);
                // Looping backends receive absolute time so they can preserve event crossings.
                _player.Seek(_player.Loop?time:Math.Min(time,_player.Duration));
                if(!_player.Loop&&_elapsed>=_player.Duration)Dispose();
            }
            public void Dispose()
            {
                if(_disposed)return;_disposed=true;_player.AnimationEvent-=OnEvent;
                if(_owner._channels.TryGetValue(_channel,out var current)&&ReferenceEquals(current,this))_owner._channels.Remove(_channel);
                PhysicalPlayers.Remove(this);
                _owner.Actor.Abilities.ReleaseOwnedLifetime(_grant,this);_player.Dispose();
                _owner.PromoteQueue(_channel);
            }
        }
        public event Action<GameplayContext,string> AnimationEvent;
        private static bool Conflicts(GameplayAnimationWriteTarget[] a,GameplayAnimationWriteTarget[] b)
        {foreach(var first in a)foreach(var second in b)if(first.Conflicts(second))return true;return false;}
        private GameplayEffectApplicationContext PresentationValueContext(GameplayContext context)
        {
            var target=Actor;
            if(!context.TargetData.HasPrimaryTarget)context=context.WithTarget(new GameplayTargetData(Actor.Id,context.TargetData.HasPoint?(GameplayPoint?)context.TargetData.Point:null));
            if(context.TargetData.HasPrimaryTarget&&!Actor.World.TryGetActor(context.TargetData.PrimaryTargetId,out target))
                throw new InvalidOperationException("Presentation value target is not present in this world.");
            return new GameplayEffectApplicationContext(context,target,context.Values.AsSetByCallerValues(),Actor.Attributes,Actor.Id,instigatorTarget:Actor);
        }
    }
}
