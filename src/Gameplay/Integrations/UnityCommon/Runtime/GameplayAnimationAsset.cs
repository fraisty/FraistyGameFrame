using System;
using FRFrame.Gameplay.Authoring;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>SDK-neutral Unity animation source. Actual SDK assets implement the player factory;
    /// backend-specific resources/variants are authored once here for both Runtime and Editor preview.
    /// Runtime state lives in a per-cast player/lease, never in this shared asset.</summary>
    public abstract class GameplayAnimationAsset : GameplayDefinitionAsset
    {
        public string Channel = "Body";
        public int Priority = 10;
        public bool Loop;
        public bool RestorePrevious = true;
        [SerializeReference] public GameplayFloatValueAuthoring Speed = new GameplayConstantFloatValueAuthoring { Value = 1 };
        public abstract string BackendName { get; }
        public abstract GameObject CreatePreviewSubject();
        /// <summary>Optional SDK render policy for the disposable preview camera; never changes project renderer assets.</summary>
        public virtual void ConfigurePreviewCamera(Camera camera) { }
        protected abstract GameplayAnimationDefinition CreateDefinition(GameplayAuthoringBuildContext context);
        public override object BuildSharedDefinition(GameplayAuthoringBuildContext context) => Build(context);
        public GameplayAnimationDefinition Build(GameplayAuthoringBuildContext context)=>context.Build(this,()=>CreateDefinition(context));
        protected override void ValidateContent(GameplayAuthoringValidationContext validation,string path)
        {
            if(string.IsNullOrWhiteSpace(Channel))validation.Error(path+".Channel","Channel is required.");
            if(Speed==null)validation.Error(path+".Speed","Speed is required.");else Speed.Validate(validation,path+".Speed");
            ValidateAnimation(validation,path);
        }
        protected abstract void ValidateAnimation(GameplayAuthoringValidationContext validation,string path);
    }
    /// <summary>Built immutable playback factory and common policy. The Source asset is not mutated by sampling.</summary>
    public sealed class GameplayAnimationDefinition
    {
        public string Channel { get; }
        public int Priority { get; }
        public IGameplayFloatValue Speed { get; }
        public bool CaptureVariantRoll {get;}
        private readonly Func<GameObject,GameplayContext,IGameplayAnimationPlayer> _create;
        private readonly Func<GameObject,GameplayAnimationWriteTarget[]> _writes;
        public GameplayAnimationDefinition(string channel,int priority,IGameplayFloatValue speed,Func<GameObject,GameplayContext,IGameplayAnimationPlayer> create,bool captureVariantRoll=false,
            Func<GameObject,GameplayAnimationWriteTarget[]> writes=null)
        {if(string.IsNullOrWhiteSpace(channel))throw new ArgumentException("Animation channel required.",nameof(channel));Channel=channel;Priority=priority;Speed=speed??throw new ArgumentNullException(nameof(speed));_create=create??throw new ArgumentNullException(nameof(create));CaptureVariantRoll=captureVariantRoll;_writes=writes;}
        /// <summary>Resolves actual SDK write slots before allocating native resources. Unknown
        /// custom factories conservatively claim the whole subject.</summary>
        public GameplayAnimationWriteTarget[] ResolveWrites(GameObject subject)=>_writes!=null?_writes(subject):new[]{new GameplayAnimationWriteTarget(subject,"Subject",-1)};
        public IGameplayAnimationPlayer Create(GameObject subject,GameplayContext context)=>_create(subject,context);
    }
    /// <summary>Actual SDK writer identity. Slot -1 occupies a whole component/domain;
    /// distinct Animator layers and Spine tracks can otherwise run concurrently.</summary>
    public readonly struct GameplayAnimationWriteTarget
    {
        public UnityEngine.Object Target {get;}
        public string Domain {get;}
        public int Slot {get;}
        public GameplayAnimationWriteTarget(UnityEngine.Object target,string domain,int slot)
        {Target=target!=null?target:throw new InvalidOperationException("Animation physical target is missing: "+domain);Domain=domain;Slot=slot;}
        internal bool Conflicts(GameplayAnimationWriteTarget other)=>Domain=="Subject"?Contains(Target,other.Target):other.Domain=="Subject"?Contains(other.Target,Target):
            (Target==other.Target&&Domain==other.Domain&&(Slot<0||other.Slot<0||Slot==other.Slot));
        private static bool Contains(UnityEngine.Object subject,UnityEngine.Object target)
        {
            if(subject==target)return true;
            var root=(subject as GameObject)?.transform;
            var transform=(target as Component)?.transform??(target as GameObject)?.transform;
            return root!=null&&transform!=null&&transform.IsChildOf(root);
        }
        public override string ToString()=>Domain+"#"+Target.GetInstanceID()+"/"+Slot;
    }
    /// <summary>A real per-playback SDK player. Seek samples the given absolute clip time; Dispose
    /// releases only resources owned by this player. Event callbacks retain the captured cast context.</summary>
    public interface IGameplayAnimationPlayer : IDisposable
    {
        float Duration { get; }
        bool Loop { get; }
        bool IsInterrupted { get; }
        void Seek(float seconds);
        event Action<string> AnimationEvent;
    }
}
