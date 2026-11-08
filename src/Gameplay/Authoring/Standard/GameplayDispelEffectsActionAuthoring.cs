using System;
using FRFrame.Gameplay.Standard;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Explicit effect-classification filter for rule, Timeline and spatial callbacks.</summary>
    [Serializable, Preserve]
    public sealed class GameplayDispelEffectsActionAuthoring : GameplaySkillActionAuthoring
    {
        public override bool SupportsEffectLifecycle => true;
        public GameplayTagQueryAuthoring EffectTags = new GameplayTagQueryAuthoring();
        public override void Validate(GameplayAuthoringValidationContext validation, string path)
        {
            if (EffectTags == null) { validation.Error(path + ".EffectTags", "请选择效果分类。"); return; }
            EffectTags.Validate(validation, path + ".EffectTags");
            if ((EffectTags.All?.Length ?? 0) == 0 && (EffectTags.Any?.Length ?? 0) == 0)
                validation.Error(path + ".EffectTags", "至少选择一个 All/Any 效果分类；不会默认驱散全部。");
        }
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context) =>
            new GameplayDispelEffectsAction(new GameplayEffectQuery(EffectTags.Build()));
    }
}
