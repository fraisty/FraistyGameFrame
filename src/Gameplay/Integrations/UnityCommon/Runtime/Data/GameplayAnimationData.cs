using FRFrame.Gameplay.Authoring.Graph;
using System;
using FRFrame.Gameplay.Authoring;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>SDK-neutral Unity animation source. Actual SDK assets implement the player factory;
    /// backend-specific resources/variants are authored once here for both Runtime and Editor preview.
    /// Runtime state lives in a per-cast player/lease, never in this shared asset.</summary>
    [System.Serializable, UnityEngine.Scripting.Preserve]
    public abstract class GameplayAnimationData : GameplayLocalDefinitionPayload
    {
        public override System.Type DefinitionType => typeof(GameplayAnimationDefinition);
        public override object BuildDefinition(GameplayAuthoringBuildContext context) => Build(context);

        public string Channel = "Body";
        public int Priority = 10;
        public bool Loop;
        public bool RestorePrevious = true;
        [SerializeReference] public GameplayFloatValueAuthoring Speed = new GameplayConstantFloatValueAuthoring { Value = 1 };
        public abstract string BackendName { get; }
        /// <summary>Actual authored clip/state name for Timeline and Inspector summaries.</summary>
        public abstract string ClipLabel {get;}
        /// <summary>Actual model/skeleton identity used by a host to reject incompatible subjects.</summary>
        public abstract UnityEngine.Object SubjectResource {get;}
        public abstract GameObject CreatePreviewSubject();
        /// <summary>Optional SDK render policy for the disposable preview camera; never changes project renderer assets.</summary>
        public virtual void ConfigurePreviewCamera(Camera camera) { }
        protected abstract GameplayAnimationDefinition CreateDefinition(GameplayAuthoringBuildContext context);
        public GameplayAnimationDefinition Build(GameplayAuthoringBuildContext context)=>context.Build(this,()=>CreateDefinition(context));
        protected override void ValidateContent(GameplayAuthoringValidationContext validation,string path)
        {
            if(string.IsNullOrWhiteSpace(Channel))validation.Error(path+".Channel","Channel is required.");
            if(Speed==null)validation.Error(path+".Speed","Speed is required.");else Speed.Validate(validation,path+".Speed");
            ValidateAnimation(validation,path);
        }
        protected abstract void ValidateAnimation(GameplayAuthoringValidationContext validation,string path);
    
    }
}
