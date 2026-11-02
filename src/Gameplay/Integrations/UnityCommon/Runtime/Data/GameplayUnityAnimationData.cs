using FRFrame.Gameplay.Authoring.Graph;
using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Authoring;
using UnityEngine;
using UnityEngine.Animations;
using UnityEngine.Playables;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Actual Animator state or manual Playables clip. The subject may render sprites or
    /// meshes; the same definition and clip are used for preview and Runtime. Movement remains a rule action.</summary>
    [System.Serializable, UnityEngine.Scripting.Preserve]
    public sealed class GameplayUnityAnimationData:GameplayAnimationData
    {
        private static readonly HashSet<Animator> OwnedGraphs=new HashSet<Animator>();
        private static readonly Dictionary<Animator,RuntimeAnimatorController> OwnedControllers=new Dictionary<Animator,RuntimeAnimatorController>();
        // Unity includes the Animator's ordinary controller graph in hasBoundPlayables.
        // Reject a custom clip/mixer graph, while allowing the existing controller to resume after disposal.
        private static bool HasCustomGraph(Animator animator)
        {
            var graph=animator.playableGraph;if(!graph.IsValid())return false;
            bool controllerRoot=false;
            for(int i=0;i<graph.GetRootPlayableCount();i++)
            {
                var type=graph.GetRootPlayable(i).GetPlayableType();
                if(type==typeof(AnimatorControllerPlayable))controllerRoot=true;
                else if(type!=typeof(AnimationLayerMixerPlayable))return true;
            }
            // Unity's EditMode controller graph also exposes its layer mixer as a root.
            return graph.GetRootPlayableCount()>0&&!controllerRoot;
        }
        public global::FRFrame.Gameplay.Unity.GameplayUnityAnimationAsset.PlaybackBackend Backend;
        public GameObject PreviewPrefab;
        public AnimationClip Clip;
        public string StateName;
        public int Layer;
        [Min(0)]public float BlendDuration;
        public global::FRFrame.Gameplay.Unity.GameplayUnityAnimationAsset.LevelVariant[] Variants=Array.Empty<global::FRFrame.Gameplay.Unity.GameplayUnityAnimationAsset.LevelVariant>();
        public bool WeightedVariants;
        public override string BackendName=>Backend==global::FRFrame.Gameplay.Unity.GameplayUnityAnimationAsset.PlaybackBackend.AnimatorState?"Animator":"Playables";
        public override UnityEngine.Object SubjectResource=>PreviewPrefab;
        public override string ClipLabel=>Backend==global::FRFrame.Gameplay.Unity.GameplayUnityAnimationAsset.PlaybackBackend.AnimatorState?StateName:Clip?Clip.name:"待选择 Clip";
        public override GameObject CreatePreviewSubject()
        {
            if(PreviewPrefab==null)throw new InvalidOperationException("Animation preview prefab is required.");
            var subject=Instantiate(PreviewPrefab);var animator=subject.GetComponentInChildren<Animator>(true);
            if(animator!=null){animator.fireEvents=false;animator.enabled=false;}
            return subject;
        }
        protected override GameplayAnimationDefinition CreateDefinition(GameplayAuthoringBuildContext context)
        {
            var clip=Clip;var state=StateName;var layer=Layer;var backend=Backend;var loop=Loop;var restore=RestorePrevious;var blend=BlendDuration;var weighted=WeightedVariants;
            var variants=new global::FRFrame.Gameplay.Unity.GameplayUnityAnimationAsset.LevelVariant[Variants?.Length??0];
            for(int i=0;i<variants.Length;i++)variants[i]=Variants[i]==null?null:new global::FRFrame.Gameplay.Unity.GameplayUnityAnimationAsset.LevelVariant{MinimumLevel=Variants[i].MinimumLevel,Clip=Variants[i].Clip,StateName=Variants[i].StateName,Weight=Variants[i].Weight};
            if(clip==null||clip.length<=0||layer<0)throw new InvalidOperationException("Invalid Unity animation source.");
            return new GameplayAnimationDefinition(Channel,Priority,Speed.Build(),(subject,cast)=>
            {
                var animator=subject.GetComponentInChildren<Animator>(true);
                var selectedClip=clip;var selectedState=state;
                int selected=GameplayAnimationVariantSelector.Select(variants,cast,v=>v.MinimumLevel,v=>v.Weight,weighted);
                if(selected>=0){selectedClip=variants[selected].Clip;selectedState=variants[selected].StateName;}
                if(animator==null)throw new InvalidOperationException("Animation subject has no Animator.");
                if(backend==global::FRFrame.Gameplay.Unity.GameplayUnityAnimationAsset.PlaybackBackend.AnimatorState)
                {
                    var controller=animator.runtimeAnimatorController;
                    bool stateExists=controller!=null&&animator.HasState(layer,Animator.StringToHash(selectedState));
                    if(controller==null&&OwnedControllers.TryGetValue(animator,out controller)&&controller!=null){
                        // Validate the detached controller without binding an output or replacing the incumbent graph.
                        var validationGraph=PlayableGraph.Create("FRFrame animation preflight");
                        try{stateExists=AnimatorControllerPlayable.Create(validationGraph,controller).HasState(layer,Animator.StringToHash(selectedState));}
                        finally{validationGraph.Destroy();}
                    }
                    if(!stateExists)
                        throw new InvalidOperationException("Animator state was not found: "+selectedState);
                    if(Array.IndexOf(controller.animationClips,selectedClip)<0)
                        throw new InvalidOperationException("Authored clip is not part of the subject controller.");
                }
                else if(HasCustomGraph(animator)&&!OwnedGraphs.Contains(animator))throw new InvalidOperationException("Subject already has an external Playables graph.");
                return new Player(animator,selectedClip,selectedState,layer,backend,loop,restore,blend);
            },weighted&&variants.Length>0,writes:subject=>new[]{new GameplayAnimationWriteTarget(subject.GetComponentInChildren<Animator>(true),"Animator",backend==global::FRFrame.Gameplay.Unity.GameplayUnityAnimationAsset.PlaybackBackend.PlayablesClip?-1:layer)});
        }
        protected override void ValidateAnimation(GameplayAuthoringValidationContext validation,string path)
        {
            validation.Finite(BlendDuration,path+".BlendDuration");if(BlendDuration<0)validation.Error(path+".BlendDuration","Blend must be nonnegative.");
            if(Clip==null||Clip.length<=0)validation.Error(path+".Clip","A nonempty clip is required.");
            if(Layer<0)validation.Error(path+".Layer","Layer must be nonnegative.");
            if(Backend==global::FRFrame.Gameplay.Unity.GameplayUnityAnimationAsset.PlaybackBackend.AnimatorState&&string.IsNullOrWhiteSpace(StateName))validation.Error(path+".StateName","Animator state is required.");
            foreach(var variant in Variants??Array.Empty<global::FRFrame.Gameplay.Unity.GameplayUnityAnimationAsset.LevelVariant>())
                if(variant==null||variant.MinimumLevel<1||variant.Clip==null||variant.Clip.length<=0||WeightedVariants&&(!float.IsFinite(variant.Weight)||variant.Weight<=0)||Backend==global::FRFrame.Gameplay.Unity.GameplayUnityAnimationAsset.PlaybackBackend.AnimatorState&&string.IsNullOrWhiteSpace(variant.StateName))
                    validation.Error(path+".Variants","A level variant requires a positive level and a nonempty clip/state.");
        }
        private sealed class Player:IGameplayAnimationPlayer
        {
            private readonly Animator _animator;
            private readonly AnimationClip _clip;
            private readonly int _hash,_layer;
            private readonly global::FRFrame.Gameplay.Unity.GameplayUnityAnimationAsset.PlaybackBackend _backend;
            private readonly bool _restore;
            private readonly AnimationEvent[] _events;
            private AnimatorStateInfo _previous;
            private RuntimeAnimatorController _controller;
            private bool _enabled,_fireEvents,_rootMotion,_keepState,_started,_disposed;
            private GameplayAnimationSubjectLease _samplingLease;
            private float _last=-1;
            private PlayableGraph _graph;
            private AnimationClipPlayable _playable;
            private AnimationMixerPlayable _mixer;
            private readonly float _blend;
            private ParameterValue[] _parameters;
            private float[] _layerWeights;
            public float Duration=>_clip.length;
            public bool Loop { get; }
            public bool IsInterrupted=>_animator==null||(_started&&(_backend==global::FRFrame.Gameplay.Unity.GameplayUnityAnimationAsset.PlaybackBackend.AnimatorState
                ?!(_animator.IsInTransition(_layer)&&(_animator.GetNextAnimatorStateInfo(_layer).fullPathHash==_hash||_animator.GetNextAnimatorStateInfo(_layer).shortNameHash==_hash))&&_animator.GetCurrentAnimatorStateInfo(_layer).fullPathHash!=_hash
                    &&_animator.GetCurrentAnimatorStateInfo(_layer).shortNameHash!=_hash
                :!_graph.IsValid()||_animator.runtimeAnimatorController!=null));
            public event Action<string> AnimationEvent;
            internal Player(Animator animator,AnimationClip clip,string state,int layer,global::FRFrame.Gameplay.Unity.GameplayUnityAnimationAsset.PlaybackBackend backend,bool loop,bool restore,float blend)
            {_animator=animator;_clip=clip;_hash=Animator.StringToHash(state??"");_layer=layer;_backend=backend;Loop=loop;_restore=restore;_events=clip.events;_blend=blend;}
            public void Seek(float seconds)
            {
                if(_disposed)return;
                if(!float.IsFinite(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));
                if(!_started)
                {
                    if(_backend==global::FRFrame.Gameplay.Unity.GameplayUnityAnimationAsset.PlaybackBackend.PlayablesClip&&HasCustomGraph(_animator))
                        throw new InvalidOperationException("Subject has a conflicting Playables channel.");
                    _samplingLease=GameplayAnimationSubjectLease.Acquire(_animator,()=>{
                        _enabled=_animator.enabled;_fireEvents=_animator.fireEvents;_rootMotion=_animator.applyRootMotion;
                        _keepState=_animator.keepAnimatorStateOnDisable;_animator.keepAnimatorStateOnDisable=true;_animator.enabled=false;
                        _animator.fireEvents=false;_animator.applyRootMotion=false;
                    },()=>{_animator.fireEvents=_fireEvents;_animator.applyRootMotion=_rootMotion;_animator.enabled=_enabled;_animator.keepAnimatorStateOnDisable=_keepState;});
                    _animator.fireEvents=false;
                    if(_animator.runtimeAnimatorController!=null)_animator.Update(0);
                    _previous=_animator.runtimeAnimatorController!=null?_animator.GetCurrentAnimatorStateInfo(_layer):default;
                    _started=true;
                    if(_backend==global::FRFrame.Gameplay.Unity.GameplayUnityAnimationAsset.PlaybackBackend.PlayablesClip)
                    {
                        _parameters=ParameterValue.Capture(_animator);
                        _layerWeights=new float[_animator.layerCount];for(int i=0;i<_layerWeights.Length;i++)_layerWeights[i]=_animator.GetLayerWeight(i);
                        // A disabled Animator does not consume graph output. Pause its ordinary
                        // controller by detaching that controller, retaining an enabled graph target.
                        _controller=_animator.runtimeAnimatorController;_animator.runtimeAnimatorController=null;_animator.enabled=true;
                        _graph=PlayableGraph.Create("FRFrame animation lease");_graph.SetTimeUpdateMode(DirectorUpdateMode.Manual);
                        _playable=AnimationClipPlayable.Create(_graph,_clip);
                        _playable.SetApplyFootIK(false);_playable.SetApplyPlayableIK(false);_playable.SetSpeed(0);
                        var output=AnimationPlayableOutput.Create(_graph,"Animation",_animator);
                        if(_blend>0&&_controller!=null)
                        {
                            var previousController=AnimatorControllerPlayable.Create(_graph,_controller);previousController.SetSpeed(0);
                            foreach(var parameter in _parameters)parameter.Apply(previousController);
                            for(int i=0;i<_layerWeights.Length;i++)previousController.SetLayerWeight(i,_layerWeights[i]);
                            if(_previous.fullPathHash!=0)previousController.Play(_previous.fullPathHash,_layer,_previous.normalizedTime);
                            _mixer=AnimationMixerPlayable.Create(_graph,2,true);_graph.Connect(previousController,0,_mixer,0);_graph.Connect(_playable,0,_mixer,1);output.SetSourcePlayable(_mixer);
                        }
                        else output.SetSourcePlayable(_playable);
                        _graph.Play();OwnedGraphs.Add(_animator);OwnedControllers[_animator]=_controller;
                    }
                }
                float sample=Loop?seconds%Duration:Math.Min(seconds,Duration);
                if(_backend==global::FRFrame.Gameplay.Unity.GameplayUnityAnimationAsset.PlaybackBackend.AnimatorState)
                {
                    if(_blend>0&&seconds<_blend&&_previous.fullPathHash!=0)
                    {
                        _animator.Play(_previous.fullPathHash,_layer,_previous.normalizedTime);_animator.Update(0);
                        _animator.CrossFadeInFixedTime(_hash,_blend,_layer,sample,seconds/_blend);_animator.Update(0);
                    }
                    else{_animator.Play(_hash,_layer,sample/Duration);_animator.Update(0);}
                }
                else
                {
                    _playable.SetTime(sample);
                    if(_mixer.IsValid()){float weight=Math.Min(1,seconds/_blend);_mixer.SetInputWeight(0,1-weight);_mixer.SetInputWeight(1,weight);}
                    _graph.Evaluate(0);
                }
                // Clip events are typed signals rather than arbitrary SendMessage calls on gameplay objects.
                float previous=_last;_last=seconds;
                if(seconds<previous)return;
                foreach(var value in _events)
                {
                    if(_disposed)break;
                    if(!Loop){if(value.time>previous&&value.time<=seconds)AnimationEvent?.Invoke(value.functionName);}
                    else
                    {
                        double crossing=value.time+Math.Max(0,Math.Floor((previous-value.time)/Duration)+1)*Duration;
                        while(!_disposed&&crossing<=seconds){AnimationEvent?.Invoke(value.functionName);crossing+=Duration;}
                    }
                }
            }
            public void Dispose()
            {
                if(_disposed)return;_disposed=true;
                bool interrupted=IsInterrupted;
                if(_graph.IsValid())_graph.Destroy();
                if(_backend==global::FRFrame.Gameplay.Unity.GameplayUnityAnimationAsset.PlaybackBackend.PlayablesClip){OwnedGraphs.Remove(_animator);OwnedControllers.Remove(_animator);}
                if(!_started||_animator==null){_samplingLease?.Dispose();_samplingLease=null;return;}
                if(_backend==global::FRFrame.Gameplay.Unity.GameplayUnityAnimationAsset.PlaybackBackend.PlayablesClip&&!interrupted)
                {
                    _animator.runtimeAnimatorController=_controller;
                    if(_controller!=null)
                    {
                        foreach(var parameter in _parameters)parameter.Apply(_animator);
                        for(int i=0;i<_layerWeights.Length;i++)_animator.SetLayerWeight(i,_layerWeights[i]);
                    }
                }
                if(_restore&&!interrupted&&_previous.fullPathHash!=0&&_animator.runtimeAnimatorController!=null)
                {_animator.Play(_previous.fullPathHash,_layer,_previous.normalizedTime);_animator.Update(0);}
                _samplingLease?.Dispose();_samplingLease=null;
            }
            private readonly struct ParameterValue
            {
                private readonly AnimatorControllerParameterType _type;private readonly int _hash,_integer;private readonly float _number;private readonly bool _boolean;
                private ParameterValue(Animator animator,AnimatorControllerParameter parameter)
                {_type=parameter.type;_hash=parameter.nameHash;_integer=_type==AnimatorControllerParameterType.Int?animator.GetInteger(_hash):0;_number=_type==AnimatorControllerParameterType.Float?animator.GetFloat(_hash):0;_boolean=_type==AnimatorControllerParameterType.Bool&&animator.GetBool(_hash);}
                internal static ParameterValue[] Capture(Animator animator)
                {var parameters=animator.parameters;var values=new ParameterValue[parameters.Length];for(int i=0;i<values.Length;i++)values[i]=new ParameterValue(animator,parameters[i]);return values;}
                internal void Apply(Animator animator)
                {if(_type==AnimatorControllerParameterType.Float)animator.SetFloat(_hash,_number);else if(_type==AnimatorControllerParameterType.Int)animator.SetInteger(_hash,_integer);else if(_type==AnimatorControllerParameterType.Bool)animator.SetBool(_hash,_boolean);}
                internal void Apply(AnimatorControllerPlayable controller)
                {if(_type==AnimatorControllerParameterType.Float)controller.SetFloat(_hash,_number);else if(_type==AnimatorControllerParameterType.Int)controller.SetInteger(_hash,_integer);else if(_type==AnimatorControllerParameterType.Bool)controller.SetBool(_hash,_boolean);}
            }
        }
    
    }
}
