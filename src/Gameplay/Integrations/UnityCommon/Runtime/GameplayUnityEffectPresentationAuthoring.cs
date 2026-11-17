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
        public Vector3 Offset;
        [Tooltip("空值跟随角色位置，可填可见模型子节点路径。")]
        public string Binding;
        [Tooltip("同组有效效果共用一个徽标；留空按实际 Effect 定义合并。")]
        public string DisplayGroup;

        public override void Validate(GameplayAuthoringValidationContext validation, string path)
        {
            if (StatusPrefab == null || !StatusPrefab.TryGetComponent<GameplayEffectStatusView>(out _))
                validation.Error(path + ".StatusPrefab", "请选择根节点带 GameplayEffectStatusView 的状态预制体。");
            validation.Finite(Offset.x,path+".Offset.x");validation.Finite(Offset.y,path+".Offset.y");validation.Finite(Offset.z,path+".Offset.z");
        }

        public override IGameplayEffectPresentationDefinition Build() => new GameplayUnityEffectPresentation(StatusPrefab,Offset,Binding,DisplayGroup);
    }

    internal sealed class GameplayUnityEffectPresentation : IGameplayEffectPresentationDefinition
    {
        internal readonly GameObject StatusPrefab;
        internal readonly Vector3 Offset;
        internal readonly string Binding, DisplayGroup;
        internal GameplayUnityEffectPresentation(GameObject statusPrefab, Vector3 offset, string binding, string displayGroup)
        { StatusPrefab=statusPrefab; Offset=offset; Binding=binding; DisplayGroup=displayGroup; }
    }
}
