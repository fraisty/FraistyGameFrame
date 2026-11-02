using FRFrame.Gameplay.Authoring.Graph;
using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Unity;
using Live2D.Cubism.Core;
using Live2D.Cubism.Framework;
using Live2D.Cubism.Framework.Pose;
using Live2D.Cubism.Rendering;
using UnityEngine;

namespace FRFrame.Gameplay.Live2D
{
    /// <summary>Actual Cubism motion clip sampling and native model update, driven by the existing
    /// presentation delta. Automatic procedural/motion writers are paused only while this lease owns the model.
    /// This deterministic clip backend does not reproduce Cubism's motion fade controller.</summary>
    [System.Serializable, UnityEngine.Scripting.Preserve]
    public sealed class GameplayLive2DAnimationData:GameplayAnimationData
    {
        public GameObject ModelPrefab;
        public AnimationClip Motion;
        public override string BackendName=>"Live2D Cubism";
        public override string ClipLabel=>Motion?Motion.name:"待选择 Motion";
        public override string CapabilityNote=>"Cubism AnimationClip 采样：Preview 与 Runtime 同源，支持速度、循环、优先级与结束恢复。当前不支持 Cubism MotionFade、多层混合或权重变体；同一模型的冲突通道会明确拒绝。";
        public override void ConfigurePreviewCamera(Camera camera)
        {
            // Installed Cubism URP explicitly filters out CameraType.Preview. Use its actual Game
            // render path on this isolated temporary camera instead of rendering picking meshes.
            camera.cameraType=CameraType.Game;
        }
        public override UnityEngine.Object SubjectResource=>ModelPrefab;
        public override GameObject CreatePreviewSubject()
        {if(ModelPrefab==null)throw new InvalidOperationException("Cubism prefab is required.");return Instantiate(ModelPrefab);}
        protected override GameplayAnimationDefinition CreateDefinition(GameplayAuthoringBuildContext context)
        {
            var clip=Motion;var loop=Loop;var restore=RestorePrevious;
            if(clip==null||clip.length<=0)throw new InvalidOperationException("A nonempty Cubism motion is required.");
            return new GameplayAnimationDefinition(Channel,Priority,Speed.Build(),(subject,cast)=>
            {
                var model=subject.GetComponentInChildren<CubismModel>(true);
                if(model==null)throw new InvalidOperationException("Subject has no real Cubism model.");
                return new Player(model,clip,loop,restore);
            },writes:subject=>new[]{new GameplayAnimationWriteTarget(subject.GetComponentInChildren<CubismModel>(true),"CubismModel",-1)});
        }
        protected override void ValidateAnimation(GameplayAuthoringValidationContext validation,string path)
        {
            if(ModelPrefab==null||ModelPrefab.GetComponentInChildren<CubismModel>(true)==null)validation.Error(path+".ModelPrefab","Real Cubism model prefab is required.");
            if(Motion==null||Motion.length<=0)validation.Error(path+".Motion","A nonempty Cubism motion is required.");
        }
        private sealed class Player:IGameplayAnimationPlayer
        {
            private static readonly Dictionary<CubismModel,Player> Owners=new Dictionary<CubismModel,Player>();
            private readonly CubismModel _model;
            private readonly AnimationClip _clip;
            private readonly bool _restore;
            private readonly List<(Behaviour component,bool enabled)> _writers=new List<(Behaviour,bool)>();
            private float[] _parameters,_parts;
            private CubismPoseController _pose;
            private bool _poseManaged;
            private float _last=-1;
            private readonly AnimationEvent[] _events;
            private bool _started,_disposed;
            public float Duration=>_clip.length;
            public bool Loop { get; }
            public bool IsInterrupted=>_model==null||(_started&&(!Owners.TryGetValue(_model,out var owner)||!ReferenceEquals(owner,this)));
            public event Action<string> AnimationEvent;
            internal Player(CubismModel model,AnimationClip clip,bool loop,bool restore){_model=model;_clip=clip;Loop=loop;_restore=restore;_events=clip.events;}
            public void Seek(float seconds)
            {
                if(_disposed)return;
                if(!float.IsFinite(seconds)||seconds<0)throw new ArgumentOutOfRangeException(nameof(seconds));
                if(!_started)
                {
                    if(Owners.ContainsKey(_model))throw new InvalidOperationException("Cubism model has a conflicting animation channel.");
                    _parameters=Array.ConvertAll(_model.Parameters,p=>p.Value);_parts=Array.ConvertAll(_model.Parts,p=>p.Opacity);
                    _pose=_model.GetComponent<CubismPoseController>();
                    if(_pose!=null){_poseManaged=_pose.HasUpdateController;_pose.HasUpdateController=true;}
                    foreach(var component in _model.GetComponents<Behaviour>())
                        if(component is Animator||component is CubismUpdateController||component is ICubismUpdatable&&!(component is CubismRenderController)&&!(component is CubismPoseController))
                        {_writers.Add((component,component.enabled));component.enabled=false;}
                    Owners.Add(_model,this);_started=true;
                }
                if(IsInterrupted)return;
                _clip.SampleAnimation(_model.gameObject,Loop?seconds%Duration:Math.Min(seconds,Duration));
                // Pose performs opacity/link resolution without advancing a clock. It is necessary
                // for clipped parts to match Runtime even when no Editor frame has run yet.
                _pose?.OnLateUpdate();
                RefreshModel();
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
            private void RefreshModel()
            {_model.ForceUpdateNow();var renderer=_model.GetComponent<CubismRenderController>();if(renderer!=null)renderer.OnLateUpdate();}
            public void Dispose()
            {
                if(_disposed)return;_disposed=true;
                if(!_started||_model==null)return;
                if(!IsInterrupted)
                {
                    Owners.Remove(_model);
                    if(_restore)
                    {
                        for(int i=0;i<_parameters.Length;i++)_model.Parameters[i].Value=_parameters[i];
                        for(int i=0;i<_parts.Length;i++)_model.Parts[i].Opacity=_parts[i];
                        RefreshModel();
                    }
                    foreach(var writer in _writers)if(writer.component!=null)writer.component.enabled=writer.enabled;
                    if(_pose!=null)_pose.HasUpdateController=_poseManaged;
                }
                _writers.Clear();
            }
        }
    
    }
}
