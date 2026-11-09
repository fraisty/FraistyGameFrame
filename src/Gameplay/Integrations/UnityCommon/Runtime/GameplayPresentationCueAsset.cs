using FRFrame.Gameplay.Authoring.Graph;
using System;
using FRFrame.Gameplay.Authoring;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Direct Unity resource binding, not a resource path in Gameplay Core. A captured
    /// definition supplies the same prefab/clip and lifetime policy to real presentation consumers.</summary>
    [CreateAssetMenu(menuName="FRFrame/Gameplay/Presentation/VFX or Audio")]
    public sealed class GameplayPresentationCueAsset:GameplayDefinitionAsset
    {
        public CueKind Kind;

        public GameObject Prefab;

        public AudioClip Clip;

        public PositionSource Origin;

        public Vector3 Offset;

        public string Binding;

        public Vector3 Scale=Vector3.one;

        public bool MirrorFacing;

        public bool Follow;

        public bool OwnedByAbility=true;

        public float Volume=1,Pitch=1;

        public bool AudioLoop;

        /// <summary>Opt-in completion policy for non-looping particle prefabs.</summary>
        public bool RecycleWhenParticlesFinish;

        [SerializeReference] public GameplayFloatValueAuthoring Duration=new GameplayConstantFloatValueAuthoring{Value=1};
        [SerializeReference] public GameplayFloatValueAuthoring Intensity;
        public OutcomeRequirement Outcome;
        public GameplayTagQueryAuthoring TargetRequirements;

        public enum CueKind { Vfx, Audio }

        public enum PositionSource { Owner, PrimaryTarget, ContextPoint, CallbackSegment, TargetToOwner }
        public enum OutcomeRequirement { Any, ConfirmedHit, HealthDamage }
        public override object BuildSharedDefinition(GameplayAuthoringBuildContext context) => Build(context);
        public override GameplayLocalDefinitionPayload CaptureGraphValues() => CaptureValues();
        private GameplayPresentationCueData CaptureValues() => new GameplayPresentationCueData { Kind = Kind, Prefab = Prefab, Clip = Clip, Origin = Origin, Offset = Offset, Binding = Binding, Scale = Scale, MirrorFacing = MirrorFacing, Follow = Follow, OwnedByAbility = OwnedByAbility, Volume = Volume, Pitch = Pitch, AudioLoop = AudioLoop, Duration = Duration, RecycleWhenParticlesFinish = RecycleWhenParticlesFinish, Intensity = Intensity, Outcome = Outcome, TargetRequirements = TargetRequirements };
        public GameplayPresentationCueDefinition Build(GameplayAuthoringBuildContext context) => context.Build(this, () => CaptureValues().Build(context));
        protected override void ValidateContent(GameplayAuthoringValidationContext validation, string path) => CaptureValues().Validate(validation, path);

    }
    public sealed class GameplayPresentationCueDefinition
    {
        public GameplayPresentationCueAsset.CueKind Kind {get;}
        public GameObject Prefab {get;}
        public AudioClip Clip {get;}
        public GameplayPresentationCueAsset.PositionSource Origin {get;}
        public Vector3 Offset {get;}
        public string Binding {get;}
        public Vector3 Scale {get;}
        public bool MirrorFacing {get;}
        public bool Follow {get;}
        public bool OwnedByAbility {get;}
        public float Volume {get;}
        public float Pitch {get;}
        public bool AudioLoop {get;}
        public bool RecycleWhenParticlesFinish {get;}
        /// <summary>Timer value for timed cues. Particle completion does not evaluate a timer.</summary>
        public IGameplayFloatValue Duration {get;}
        public IGameplayFloatValue Intensity { get; }
        public GameplayPresentationCueAsset.OutcomeRequirement Outcome { get; }
        public GameplayTagQuery TargetRequirements { get; }
        public GameplayPresentationCueDefinition(GameplayPresentationCueAsset.CueKind kind,GameObject prefab,AudioClip clip,
            GameplayPresentationCueAsset.PositionSource origin,Vector3 offset,bool follow,float volume,float pitch,bool audioLoop,IGameplayFloatValue duration,bool ownedByAbility=true,string binding=null,Vector3? scale=null,bool mirrorFacing=false,bool recycleWhenParticlesFinish=false,
            IGameplayFloatValue intensity=null, GameplayPresentationCueAsset.OutcomeRequirement outcome=default,
            GameplayTagQuery targetRequirements=null)
        {
            Kind=kind;Prefab=prefab;Clip=clip;Origin=origin;Offset=offset;Follow=follow;Volume=volume;Pitch=pitch;AudioLoop=audioLoop;
            RecycleWhenParticlesFinish=kind==GameplayPresentationCueAsset.CueKind.Vfx&&recycleWhenParticlesFinish;
            Duration=RecycleWhenParticlesFinish?null:duration??throw new ArgumentNullException(nameof(duration));
            OwnedByAbility=ownedByAbility;Binding=binding;Scale=scale??Vector3.one;MirrorFacing=mirrorFacing;
            Intensity=intensity;Outcome=outcome;TargetRequirements=targetRequirements??GameplayTagQuery.Empty;
        }
    }
}
