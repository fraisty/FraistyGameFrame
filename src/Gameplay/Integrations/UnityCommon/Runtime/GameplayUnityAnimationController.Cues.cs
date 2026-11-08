using System;
using System.Collections.Generic;
using System.Globalization;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    public sealed partial class GameplayUnityAnimationController
    {
        private readonly List<CueLease> _cues=new List<CueLease>();
        private GameObjectPoolModule _ownedPool;
        private AudioManager _ownedAudio;
        /// <summary>Optional existing application services. When absent, standalone Labs instantiate
        /// the same FRFrame modules and dispose only those local modules, never a second implementation.</summary>
        public GameObjectPoolModule CuePool {get;set;}
        public AudioManager CueAudio {get;set;}
        /// <summary>Optional camera layer for spawned cue and status views in a scoped viewport.</summary>
        public int? ViewRenderLayer {get;set;}
        public int ActiveCueCount=>_cues.Count;
        internal GameObjectPoolModule SpatialViewPool=>CuePool??(_ownedPool??(_ownedPool=new GameObjectPoolModule()));

        private void ApplyViewRenderLayer(GameObject view)
        {
            if (ViewRenderLayer is not int layer) return;
            foreach (var child in view.GetComponentsInChildren<Transform>(true)) child.gameObject.layer = layer;
        }
        public bool TryPlayCue(GameplayPresentationCueDefinition definition,GameplayContext context)
        {
            if(_disposed||Actor.IsDisposed||Subject==null||!GameplayStandardActivation.TryFindGrant(Actor,context,out var grant))return false;
            float duration=0;
            if(!definition.RecycleWhenParticlesFinish){
                duration=GameplayValueEvaluator.Evaluate(definition.Duration,PresentationValueContext(context));
                if(!float.IsFinite(duration)||duration<=0)throw new ArithmeticException("Cue duration must be positive.");
            }
            Vector3 point;Transform parent=null;
            if(definition.Origin==GameplayPresentationCueAsset.PositionSource.ContextPoint)
            {if(!context.TargetData.HasPoint)return false;var p=context.TargetData.Point;point=new Vector3(p.X,p.Y,p.Z);}
            else
            {
                var target=Actor;
                if(definition.Origin==GameplayPresentationCueAsset.PositionSource.PrimaryTarget&&!Actor.World.TryGetActor(context.TargetData.PrimaryTargetId,out target))return false;
                var p=target.Position;point=new Vector3(p.X,p.Y,p.Z);
                parent=(target.Presentation as GameplayUnityAnimationController)?.Subject?.transform;
                if(parent!=null&&!string.IsNullOrEmpty(definition.Binding)){parent=parent.Find(definition.Binding)??parent;point=parent.position;}
            }
            return PlayCueAt(definition, point, parent, duration, grant);
        }

        private bool PlayCueAt(GameplayPresentationCueDefinition definition, Vector3 point, Transform parent,
            float duration, GameplayAbilityHandle grant=default)
        {
            var cue=new CueLease(this,grant,duration,definition.RecycleWhenParticlesFinish);
            try
            {
                if(definition.Kind==GameplayPresentationCueAsset.CueKind.Vfx)
                {
                    if(definition.Prefab==null)throw new InvalidOperationException("VFX prefab is missing.");
                    var pool=CuePool??(_ownedPool??(_ownedPool=new GameObjectPoolModule()));
                    var key=new ResourceKey("Gameplay.DirectPrefab",definition.Prefab.GetInstanceID().ToString(CultureInfo.InvariantCulture));
                    var view=pool.GetOrCreate(key,definition.Prefab.name,definition.Prefab);
                    cue.View=view;cue.Pool=pool;cue.Key=key;cue.Location=definition.Prefab.name;
                    ApplyViewRenderLayer(view);
                    var offset=definition.Offset;var scale=definition.Scale;
                    if(definition.MirrorFacing&&Subject.transform.localScale.x<0){offset.x=-offset.x;scale.x=-scale.x;}
                    view.transform.SetParent(definition.Follow?parent:null,false);view.transform.position=point+offset;view.transform.localScale=scale;view.transform.rotation=Quaternion.identity;
                    cue.Particles=view.GetComponentsInChildren<ParticleSystem>();
                    foreach(var particles in cue.Particles){var main=particles.main;main.stopAction=ParticleSystemStopAction.None;particles.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);particles.Play(false);particles.Pause(false);}
                }
                else
                {
                    if(definition.Clip==null)throw new InvalidOperationException("Audio clip is missing.");
                    var audio=CueAudio;
                    if(audio==null){if(_ownedAudio==null){_ownedAudio=new AudioManager();_ownedAudio.InitializeDirectPlayback();}audio=_ownedAudio;}
                    cue.Audio=audio.Play(definition.Clip,new AudioPlayOptions{Loop=definition.AudioLoop,Volume=definition.Volume,Pitch=definition.Pitch,Position=point+definition.Offset,FollowTarget=definition.Follow?parent:null});
                    if(!cue.Audio.IsValid){cue.Dispose();return false;}
                }
                _cues.Add(cue);
                if(definition.OwnedByAbility&&!Actor.Abilities.TryOwnLifetime(grant,cue)){cue.Dispose();return false;}
                return true;
            }
            catch{cue.Dispose();throw;}
        }
        private sealed class CueLease:IDisposable
        {
            private readonly GameplayUnityAnimationController _owner;
            private readonly GameplayAbilityHandle _grant;
            private readonly float _duration;
            private readonly bool _recycleWhenParticlesFinish;
            private double _elapsed;
            private bool _disposed;
            internal GameObject View;
            internal ParticleSystem[] Particles;
            internal GameObjectPoolModule Pool;
            internal ResourceKey Key;
            internal string Location;
            internal AudioHandle Audio;
            internal CueLease(GameplayUnityAnimationController owner,GameplayAbilityHandle grant,float duration,bool recycleWhenParticlesFinish)
            {_owner=owner;_grant=grant;_duration=duration;_recycleWhenParticlesFinish=recycleWhenParticlesFinish;}
            internal void Advance(float delta)
            {
                if(_disposed)return;
                bool finished=true;
                if(View!=null&&Particles!=null)foreach(var particles in Particles){
                    if(particles==null)continue;
                    particles.Simulate(delta,false,false,false);
                    // IsAlive stays true on the paused systems driven by Simulate. Unity keeps
                    // trail-only particles in particleCount until their points have expired.
                    if(_recycleWhenParticlesFinish&&(particles.time<particles.main.duration||particles.particleCount!=0))
                        finished=false;
                }
                if(_recycleWhenParticlesFinish){
                    if(finished)Dispose();
                    return;
                }
                _elapsed+=delta;if(_elapsed>=_duration)Dispose();
            }
            public void Dispose()
            {
                if(_disposed)return;_disposed=true;_owner._cues.Remove(this);if(_grant.IsValid)_owner.Actor.Abilities.ReleaseOwnedLifetime(_grant,this);
                Audio.Stop();
                if(View!=null){foreach(var particles in Particles??Array.Empty<ParticleSystem>())if(particles!=null)particles.Stop(false,ParticleSystemStopBehavior.StopEmittingAndClear);Pool.Release(Key,Location,View);View=null;}
            }
        }
    }
}
