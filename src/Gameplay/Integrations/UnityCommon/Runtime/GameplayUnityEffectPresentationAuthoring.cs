using System;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Unity resources for an individual persistent Effect, authored on that Effect's card.</summary>
    [Serializable, Preserve]
    public sealed class GameplayUnityEffectPresentationAuthoring : GameplayEffectPresentationAuthoring
    {
        public GameObject StatusPrefab;
        public GameObject FirstApplicationVfx;
        public Vector3 FirstApplicationOffset;
        public float FirstApplicationSeconds = .5f;

        public override void Validate(GameplayAuthoringValidationContext validation, string path)
        {
            if (StatusPrefab == null && FirstApplicationVfx == null)
                validation.Error(path, "请至少绑定状态预制体或首次应用特效。");
            if (StatusPrefab != null && !StatusPrefab.TryGetComponent<GameplayEffectStatusView>(out _))
                validation.Error(path + ".StatusPrefab", "状态预制体根节点需要 GameplayEffectStatusView。");
            if (FirstApplicationVfx != null && (!float.IsFinite(FirstApplicationSeconds) || FirstApplicationSeconds <= 0))
                validation.Error(path + ".FirstApplicationSeconds", "首次应用特效时长必须大于零。");
        }

        public override IGameplayEffectPresentationDefinition Build() => new GameplayUnityEffectPresentation(
            StatusPrefab, FirstApplicationVfx == null ? null : GameplayEffectCue.AtTarget(
                FirstApplicationVfx, null, FirstApplicationOffset, FirstApplicationSeconds));
    }

    /// <summary>An ordered overflow action. It plays at the actual affected target and can outlive the Effect.</summary>
    [Serializable, Preserve]
    public sealed class GameplayPlayEffectCueOnOverflowAuthoring : GameplayEffectOverflowAuthoring
    {
        public GameObject Prefab;
        public AudioClip Clip;
        [Range(0, 1)] public float Volume = .72f;
        public Vector3 Offset;
        public float Duration = .65f;

        public override void Validate(GameplayAuthoringValidationContext validation, string path)
        {
            if (Prefab == null && Clip == null) validation.Error(path, "请至少绑定一个特效或音频。");
            if (!float.IsFinite(Duration) || Duration <= 0) validation.Error(path + ".Duration", "溢出表现时长必须大于零。");
            if (Clip != null && (!float.IsFinite(Volume) || Volume < 0 || Volume > 1)) validation.Error(path + ".Volume", "音量须为 0～1。");
        }

        public override IGameplayEffectOverflowExecution Build(GameplayAuthoringBuildContext context) =>
            new Playback(GameplayEffectCue.AtTarget(Prefab, Clip, Offset, Duration, Volume));

        private sealed class Playback : IGameplayEffectOverflowExecution
        {
            private readonly GameplayEffectCue _cue;
            internal Playback(GameplayEffectCue cue) => _cue = cue;
            public void Execute(GameplayEffectOverflowContext context)
            {
                if (context.Application.Target is GameplayStandardActor actor &&
                    actor.Presentation is GameplayUnityAnimationController controller)
                    controller.QueueEffectCue(_cue);
            }
        }
    }

    internal sealed class GameplayUnityEffectPresentation : IGameplayEffectPresentationDefinition
    {
        internal readonly GameObject StatusPrefab;
        internal readonly GameplayEffectCue FirstApplication;
        internal GameplayUnityEffectPresentation(GameObject statusPrefab, GameplayEffectCue firstApplication)
        { StatusPrefab = statusPrefab; FirstApplication = firstApplication; }
    }

    internal sealed class GameplayEffectCue
    {
        internal readonly GameplayPresentationCueDefinition Vfx, Audio;
        internal readonly float Duration;

        private GameplayEffectCue(GameplayPresentationCueDefinition vfx, GameplayPresentationCueDefinition audio, float duration)
        { Vfx = vfx; Audio = audio; Duration = duration; }

        internal static GameplayEffectCue AtTarget(GameObject prefab, AudioClip clip, Vector3 offset, float duration, float volume = 1)
        {
            var origin = GameplayPresentationCueAsset.PositionSource.Owner;
            var time = new GameplayConstantFloatValue(duration);
            return new GameplayEffectCue(
                prefab == null ? null : new GameplayPresentationCueDefinition(GameplayPresentationCueAsset.CueKind.Vfx,
                    prefab, null, origin, offset, false, 1, 1, false, time, ownedByAbility: false),
                clip == null ? null : new GameplayPresentationCueDefinition(GameplayPresentationCueAsset.CueKind.Audio,
                    null, clip, origin, offset, false, volume, 1, false, time, ownedByAbility: false),
                duration);
        }
    }
}
