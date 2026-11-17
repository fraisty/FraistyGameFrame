using System;
using FRFrame.Gameplay.Standard;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Serialized inputs and factory for an immutable damage calculation. A scoped stage
    /// is usable only inside a formal graph of that scope. Shared effects accept common stages.</summary>
    [Serializable]
    public abstract class GameplayDamageCalculationStageAuthoring
    {
        public virtual string RequiredScope => null;
        public virtual bool ReplacesDefense => false;
        /// <summary>Shared by Editor discovery and validation of the persisted graph in Player.</summary>
        public bool IsAvailableInScope(string scope) => string.IsNullOrEmpty(RequiredScope) || RequiredScope == scope;
        public abstract void Validate(GameplayAuthoringValidationContext validation, string path);
        public abstract IGameplayDamageCalculation BuildCalculation();
        public virtual IGameplayFloatValue[] BuildParameters() => Array.Empty<IGameplayFloatValue>();

        /// <summary>Validates the existing numeric authoring source and checks known constants at edit time.</summary>
        protected static void ValidateParameter(GameplayFloatValueAuthoring value, GameplayAuthoringValidationContext validation,
            string path, float max = float.MaxValue)
        {
            if (value == null) validation.Error(path, "缺少数值来源。");
            else if (value is GameplayConstantFloatValueAuthoring constant)
            {
                if (!(constant.Value >= 0 && constant.Value <= max))
                    validation.Error(path, max == 1 ? "数值必须在 0 到 1 之间。" : "数值必须有限且非负。");
            }
            else value.Validate(validation, path);
        }
    }

    [Serializable, Preserve]
    public sealed class GameplayFixedBlockStageAuthoring : GameplayDamageCalculationStageAuthoring
    {
        [SerializeReference, InspectorName("固定减伤量")]
        public GameplayFloatValueAuthoring Amount = new GameplayConstantFloatValueAuthoring { Value = 20 };

        public override void Validate(GameplayAuthoringValidationContext v, string p) => ValidateParameter(Amount, v, p + ".Amount");
        public override IGameplayDamageCalculation BuildCalculation() => new GameplayFixedBlockCalculation();
        public override IGameplayFloatValue[] BuildParameters() => new[] { Amount.Build() };
    }

    [Serializable, Preserve]
    public sealed class GameplayArmorPenetrationStageAuthoring : GameplayDamageCalculationStageAuthoring
    {
        [SerializeReference, InspectorName("百分比穿透（0～1）")]
        public GameplayFloatValueAuthoring Percent = new GameplayConstantFloatValueAuthoring();
        [SerializeReference, InspectorName("固定穿透")]
        public GameplayFloatValueAuthoring Flat = new GameplayConstantFloatValueAuthoring();
        public override bool ReplacesDefense => true;

        public override void Validate(GameplayAuthoringValidationContext v, string p)
        {
            ValidateParameter(Percent, v, p + ".Percent", 1);
            ValidateParameter(Flat, v, p + ".Flat");
        }
        public override IGameplayDamageCalculation BuildCalculation() => new GameplayArmorPenetrationCalculation();
        public override IGameplayFloatValue[] BuildParameters() => new[] { Percent.Build(), Flat.Build() };
    }
}
