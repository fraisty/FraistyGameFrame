using System;
using FRFrame.Gameplay.Authoring;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Persistent status appearance owned by the actual Effect handle. One-shot Cue actions are authored on its event ports.</summary>
    [Serializable, Preserve]
    public sealed class GameplayUnityEffectPresentationAuthoring : GameplayEffectPresentationAuthoring
    {
        public GameObject StatusPrefab;

        public override void Validate(GameplayAuthoringValidationContext validation, string path)
        {
            if (StatusPrefab == null || !StatusPrefab.TryGetComponent<GameplayEffectStatusView>(out _))
                validation.Error(path + ".StatusPrefab", "请选择根节点带 GameplayEffectStatusView 的状态预制体。");
        }

        public override IGameplayEffectPresentationDefinition Build() => new GameplayUnityEffectPresentation(StatusPrefab);
    }

    internal sealed class GameplayUnityEffectPresentation : IGameplayEffectPresentationDefinition
    {
        internal readonly GameObject StatusPrefab;
        internal GameplayUnityEffectPresentation(GameObject statusPrefab) => StatusPrefab = statusPrefab;
    }
}
