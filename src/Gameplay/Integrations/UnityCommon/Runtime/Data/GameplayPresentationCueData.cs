using FRFrame.Gameplay.Authoring.Graph;
using System;
using FRFrame.Gameplay.Authoring;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Direct Unity resource binding, not a resource path in Gameplay Core. A captured
    /// definition supplies the same prefab/clip and lifetime policy to real presentation consumers.</summary>
    [System.Serializable, UnityEngine.Scripting.Preserve]
    public sealed class GameplayPresentationCueData:GameplayLocalDefinitionPayload
    {
        public override System.Type DefinitionType => typeof(GameplayPresentationCueDefinition);
        public override object BuildDefinition(GameplayAuthoringBuildContext context) => Build(context);

        public global::FRFrame.Gameplay.Unity.GameplayPresentationCueAsset.CueKind Kind;
        public GameObject Prefab;
        public AudioClip Clip;
        public global::FRFrame.Gameplay.Unity.GameplayPresentationCueAsset.PositionSource Origin;
        public Vector3 Offset;
        public string Binding;
        public Vector3 Scale=Vector3.one;
        public bool MirrorFacing;
        public bool Follow;
        public bool OwnedByAbility=true;
        public float Volume=1,Pitch=1;
        public bool AudioLoop;
        [SerializeReference] public GameplayFloatValueAuthoring Duration=new GameplayConstantFloatValueAuthoring{Value=1};
        public GameplayPresentationCueDefinition Build(GameplayAuthoringBuildContext context)=>context.Build(this,()=>
            new GameplayPresentationCueDefinition(Kind,Prefab,Clip,Origin,Offset,Follow,Volume,Pitch,AudioLoop,Duration.Build(),OwnedByAbility,Binding,Scale,MirrorFacing));
        protected override void ValidateContent(GameplayAuthoringValidationContext validation,string path)
        {
            if(!Enum.IsDefined(typeof(global::FRFrame.Gameplay.Unity.GameplayPresentationCueAsset.CueKind),Kind)||!Enum.IsDefined(typeof(global::FRFrame.Gameplay.Unity.GameplayPresentationCueAsset.PositionSource),Origin))validation.Error(path,"Invalid cue policy.");
            if(Kind==global::FRFrame.Gameplay.Unity.GameplayPresentationCueAsset.CueKind.Vfx&&Prefab==null)validation.Error(path+".Prefab","VFX prefab is required.");
            if(Kind==global::FRFrame.Gameplay.Unity.GameplayPresentationCueAsset.CueKind.Audio&&Clip==null)validation.Error(path+".Clip","Audio clip is required.");
            if(Duration==null)validation.Error(path+".Duration","Duration is required.");else Duration.Validate(validation,path+".Duration");
            if(!float.IsFinite(Volume)||Volume<0||Volume>1||!float.IsFinite(Pitch)||Pitch<=0)validation.Error(path,"Invalid volume/pitch.");
            if(!float.IsFinite(Offset.x)||!float.IsFinite(Offset.y)||!float.IsFinite(Offset.z))validation.Error(path+".Offset","Offset must be finite.");
            if(!float.IsFinite(Scale.x)||!float.IsFinite(Scale.y)||!float.IsFinite(Scale.z))validation.Error(path+".Scale","Scale must be finite.");
        }
    
    }
}
