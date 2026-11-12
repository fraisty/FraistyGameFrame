using FRFrame.Gameplay.Authoring.Graph;
using System;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Unity;
using Spine.Unity;
using UnityEngine;

namespace FRFrame.Gameplay.Spine
{
    /// <summary>One real Spine animation source shared by Runtime and preview. Level variants select
    /// the highest eligible minimum level; optional weights share captured selection with Preview.
    /// No mutable TrackEntry lives here.</summary>
    [System.Serializable, UnityEngine.Scripting.Preserve]
    public sealed class GameplaySpineAnimationData : GameplayAnimationData
    {
        public SkeletonDataAsset Skeleton;
        public string AnimationName;
        public int TrackIndex;
        [Min(0)]public float BlendDuration;
        /// <summary>Optional starting pose of the disposable preview/Lab subject. Runtime animation
        /// commands always blend from the actual host track; this does not replace AnimationName.</summary>
        public string PreviewInitialAnimation;
        public string[] SkinNames = Array.Empty<string>();
        public Material Material;
        public global::FRFrame.Gameplay.Spine.GameplaySpineAnimationAsset.LevelVariant[] Variants = Array.Empty<global::FRFrame.Gameplay.Spine.GameplaySpineAnimationAsset.LevelVariant>();
        public bool WeightedVariants;
        public override string BackendName=>"Spine";
        public override string ClipLabel=>AnimationName;
        public override UnityEngine.Object SubjectResource=>Skeleton;
        public override GameObject CreatePreviewSubject()
        {
            if(Skeleton==null)throw new InvalidOperationException("Spine skeleton is required.");
            var component=SkeletonAnimation.NewSkeletonAnimationGameObject(Skeleton,true);
            component.enabled=false;
            if(!string.IsNullOrWhiteSpace(PreviewInitialAnimation))component.AnimationState.SetAnimation(TrackIndex,PreviewInitialAnimation,true);
            var skin=BuildSkin(Skeleton,SkinNames??Array.Empty<string>());
            if(skin!=null){component.Skeleton.SetSkin(skin);component.Skeleton.SetSlotsToSetupPose();}
            var renderer=component.GetComponent<Renderer>();
            if(Material!=null&&renderer!=null&&renderer.sharedMaterial!=null)component.CustomMaterialOverride[renderer.sharedMaterial]=Material;
            component.Update(0);component.LateUpdate();
            return component.gameObject;
        }
        private static global::Spine.Skin BuildSkin(SkeletonDataAsset skeleton,string[] skins)
        {
            if(skins.Length==0)return null;
            var result=new global::Spine.Skin("Gameplay animation skin");
            foreach(var key in skins){var part=skeleton.GetSkeletonData(false).FindSkin(key);if(part==null)throw new InvalidOperationException("Spine skin was not found: "+key);result.AddSkin(part);}
            return result;
        }
        protected override GameplayAnimationDefinition CreateDefinition(GameplayAuthoringBuildContext context)
        {
            var skeleton=Skeleton;var name=AnimationName;var track=TrackIndex;var loop=Loop;var restore=RestorePrevious;var blend=BlendDuration;var weighted=WeightedVariants;
            var skins=(string[])(SkinNames??Array.Empty<string>()).Clone();var material=Material;
            var variants=new global::FRFrame.Gameplay.Spine.GameplaySpineAnimationAsset.LevelVariant[Variants?.Length??0];
            for(int i=0;i<variants.Length;i++)
                variants[i]=Variants[i]==null?null:new global::FRFrame.Gameplay.Spine.GameplaySpineAnimationAsset.LevelVariant{MinimumLevel=Variants[i].MinimumLevel,AnimationName=Variants[i].AnimationName,Weight=Variants[i].Weight};
            if(skeleton==null||track<0)throw new InvalidOperationException("Invalid Spine source.");
            return new GameplayAnimationDefinition(Channel,Priority,Speed.Build(),(subject,cast)=>
            {
                int index=GameplayAnimationVariantSelector.Select(variants,cast,v=>v.MinimumLevel,v=>v.Weight,weighted);
                string selected=index<0?name:variants[index].AnimationName;
                var component=subject.GetComponentInChildren<SkeletonAnimation>(true);
                if(component==null||component.skeletonDataAsset!=skeleton)
                    throw new InvalidOperationException("Subject does not use the authored Spine skeleton.");
                var animation=skeleton.GetSkeletonData(false)?.FindAnimation(selected);
                if(animation==null||animation.Duration<=0)throw new InvalidOperationException("Spine animation is missing or empty: "+selected);
                var skin=BuildSkin(skeleton,skins);
                return new Player(component,animation,track,loop,restore,skin,material,blend);
            },weighted&&variants.Length>0,writes:subject=>new[]{new GameplayAnimationWriteTarget(subject.GetComponentInChildren<SkeletonAnimation>(true),"Spine",skins.Length>0||material!=null?-1:track)});
        }
        protected override void ValidateAnimation(GameplayAuthoringValidationContext validation,string path)
        {
            validation.Finite(BlendDuration,path+".BlendDuration");if(BlendDuration<0)validation.Error(path+".BlendDuration","Blend must be nonnegative.");
            if(!string.IsNullOrWhiteSpace(PreviewInitialAnimation))ValidateName(PreviewInitialAnimation,path+".PreviewInitialAnimation",validation);
            if(Skeleton==null)validation.Error(path+".Skeleton","Spine skeleton is required.");
            if(TrackIndex<0)validation.Error(path+".TrackIndex","Track index must be nonnegative.");
            ValidateName(AnimationName,path+".AnimationName",validation);
            foreach(var skin in SkinNames??Array.Empty<string>())
                if(string.IsNullOrWhiteSpace(skin)||Skeleton!=null&&Skeleton.GetSkeletonData(false)?.FindSkin(skin)==null)
                    validation.Error(path+".SkinNames","Spine skin was not found: "+skin);
            for(int i=0;i<(Variants?.Length??0);i++)
            {
                var variant=Variants[i];
                if(variant==null||variant.MinimumLevel<1)validation.Error(path+".Variants["+i+"]","Variant requires a positive minimum level.");
                if(variant!=null&&WeightedVariants&&(!float.IsFinite(variant.Weight)||variant.Weight<=0))validation.Error(path+".Variants["+i+"].Weight","Weight must be positive and finite.");
                if(variant!=null)ValidateName(variant.AnimationName,path+".Variants["+i+"].AnimationName",validation);
            }
        }
        private void ValidateName(string name,string path,GameplayAuthoringValidationContext validation)
        {
            if(string.IsNullOrWhiteSpace(name))validation.Error(path,"Animation name is required.");
            else if(Skeleton!=null&&Skeleton.GetSkeletonData(false)?.FindAnimation(name)==null)validation.Error(path,"Spine animation was not found: "+name);
        }
        private sealed class Player:IGameplayAnimationPlayer, IGameplayAnimationPlaybackSource
        {
            private readonly SkeletonAnimation _component;
            private readonly global::Spine.Animation _animation;
            private readonly int _track;
            private readonly float _blend;
            private readonly bool _restore;
            private readonly global::Spine.Skin _skin;
            private readonly Material _material;
            private global::Spine.Skin _previousSkin;
            private Renderer _renderer;
            private Material _previousMaterial;
            private Material _previousOverride;
            private bool _hadOverride;
            private global::Spine.TrackEntry _entry;
            private global::Spine.Animation _previousAnimation;
            private float _previousTime;
            private bool _previousLoop,_enabled,_started,_disposed,_interrupted;
            private float _time=-1;
            private GameplayAnimationSubjectLease _samplingLease;
            public float Duration=>_animation.Duration;
            public bool Loop { get; }
            public bool IsInterrupted=>_component==null||_interrupted||(_started&&!ReferenceEquals(_component.AnimationState.GetCurrent(_track),_entry));
            public string PlaybackSource => "Spine · " + _component.SkeletonDataAsset.name + " / " + _animation.Name + " / Track " + _track;
            public event Action<string> AnimationEvent;
            internal Player(SkeletonAnimation component,global::Spine.Animation animation,int track,bool loop,bool restore,global::Spine.Skin skin,Material material,float blend)
            {_component=component;_animation=animation;_track=track;Loop=loop;_restore=restore;_skin=skin;_material=material;_blend=blend;}
            public void Seek(float seconds)
            {
                if(_disposed)return;
                if(!float.IsFinite(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));
                if(!_started)
                {
                    _component.Initialize(false);
                    _previousSkin=_component.Skeleton.Skin;
                    if(_skin!=null){_component.Skeleton.SetSkin(_skin);_component.Skeleton.SetSlotsToSetupPose();}
                    _renderer=_component.GetComponent<Renderer>();
                    if(_material!=null&&_renderer!=null&&_renderer.sharedMaterial!=null)
                    {_previousMaterial=_renderer.sharedMaterial;_hadOverride=_component.CustomMaterialOverride.TryGetValue(_previousMaterial,out _previousOverride);_component.CustomMaterialOverride[_previousMaterial]=_material;}
                    var previous=_component.AnimationState.GetCurrent(_track);
                    if(previous!=null){_previousAnimation=previous.Animation;_previousTime=previous.TrackTime;_previousLoop=previous.Loop;}
                    _samplingLease=GameplayAnimationSubjectLease.Acquire(_component,()=>{_enabled=_component.enabled;_component.enabled=false;},()=>_component.enabled=_enabled);_started=true;
                    StartTrack();
                }
                if(IsInterrupted)return;
                // A backwards Editor scrub starts a new sampling interval. Runtime remains monotonic.
                if(seconds<_time)
                {
                    _entry.Event-=OnEvent;_entry.Interrupt-=OnInterrupt;
                    if(_previousAnimation!=null){var previous=_component.AnimationState.SetAnimation(_track,_previousAnimation,_previousLoop);previous.TrackTime=_previousTime;previous.MixDuration=0;}
                    else _component.AnimationState.ClearTrack(_track);
                    StartTrack();
                }
                _entry.TrackTime=Loop?seconds:Math.Min(seconds,Duration);
                _entry.MixTime=Math.Min(seconds,_blend);
                _component.Update(0);
                if(!_disposed&&!IsInterrupted)_component.LateUpdate();
                _time=seconds;
            }
            private void StartTrack()
            {
                if(_entry!=null){_entry.Event-=OnEvent;_entry.Interrupt-=OnInterrupt;}
                _entry=_component.AnimationState.SetAnimation(_track,_animation,Loop);
                _interrupted=false;_entry.MixDuration=_blend;_entry.TimeScale=0;_entry.Event+=OnEvent;_entry.Interrupt+=OnInterrupt;
            }
            private void OnInterrupt(global::Spine.TrackEntry entry){_interrupted=true;}
            private void OnEvent(global::Spine.TrackEntry entry,global::Spine.Event value)
            {if(!_disposed&&ReferenceEquals(entry,_entry))AnimationEvent?.Invoke(value.Data.Name);}
            public void Dispose()
            {
                if(_disposed)return;_disposed=true;
                if(_entry!=null){_entry.Event-=OnEvent;_entry.Interrupt-=OnInterrupt;}
                if(!_started||_component==null){_samplingLease?.Dispose();_samplingLease=null;return;}
                if(_material!=null&&_previousMaterial!=null&&_component.CustomMaterialOverride.TryGetValue(_previousMaterial,out var currentMaterial)&&currentMaterial==_material)
                {if(_hadOverride)_component.CustomMaterialOverride[_previousMaterial]=_previousOverride;else _component.CustomMaterialOverride.Remove(_previousMaterial);}
                if(_restore&&_skin!=null&&ReferenceEquals(_component.Skeleton.Skin,_skin))
                {_component.Skeleton.SetSkin(_previousSkin);_component.Skeleton.SetSlotsToSetupPose();}
                // An external animation may have preempted this track. Preserve that new owner.
                if(!_interrupted&&ReferenceEquals(_component.AnimationState.GetCurrent(_track),_entry))
                {
                    _component.AnimationState.ClearTrack(_track);
                    if(_restore&&_previousAnimation!=null)
                    {
                        var restored=_component.AnimationState.SetAnimation(_track,_previousAnimation,_previousLoop);
                        restored.TrackTime=_previousTime;restored.MixDuration=0;
                    }
                    else _component.Skeleton.SetToSetupPose();
                    _component.Update(0);_component.LateUpdate();
                }
                _samplingLease?.Dispose();_samplingLease=null;
            }
        }
    
    }
}
