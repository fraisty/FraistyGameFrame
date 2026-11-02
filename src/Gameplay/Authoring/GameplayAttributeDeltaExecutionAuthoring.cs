using System;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>First-party Instant/periodic attribute change, built into the normal Effect execution list.</summary>
    [Serializable, Preserve]
    public sealed class GameplayAttributeDeltaExecutionAuthoring : GameplayEffectExecutionAuthoring
    {
        public GameplayAttributeReference Attribute;
        [Tooltip("在本次 Effect Context 中计算增量，并加到目标属性 BaseValue；不隐含伤害/死亡规则。")]
        [SerializeReference] public GameplayFloatValueAuthoring Delta = new GameplayConstantFloatValueAuthoring();
        public override void Validate(GameplayAuthoringValidationContext validation, string path)
        {
            validation.Attribute(Attribute, path + ".Attribute");
            if (Delta == null) validation.Error(path + ".Delta", "Missing attribute delta.");
            else Delta.Validate(validation, path + ".Delta");
        }
        public override IGameplayEffectExecution Build() => new GameplayAttributeDeltaExecution(Attribute.Build(), Delta.Build());
    }
}
