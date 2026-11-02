using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Standard;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    [Serializable]
    public sealed class GameplayCombatMappingAuthoring
    {
        public GameplayAttributeReference Health = new GameplayAttributeReference("Health");
        public GameplayAttributeReference MaxHealth = new GameplayAttributeReference("MaxHealth");
        public GameplayAttributeReference Shield = new GameplayAttributeReference("Shield");
        public GameplayAttributeReference Defense = new GameplayAttributeReference("Defense");
        public GameplayAttributeReference MagicDefense;
        public GameplayCombatAttributeMap Build() => new GameplayCombatAttributeMap(Health.Build(), MaxHealth.Build(),
            string.IsNullOrWhiteSpace(Shield.Value) ? default : Shield.Build(), string.IsNullOrWhiteSpace(Defense.Value) ? default : Defense.Build(),string.IsNullOrWhiteSpace(MagicDefense.Value)?default:MagicDefense.Build());
    }
    [Serializable]
    public sealed class GameplayInitialAttributeAuthoring { public GameplayAttributeReference Attribute; public float Value; }
    [Serializable, Preserve]
    public sealed class GameplayDamageStageAuthoring
    {
        public GameplayDamageStageKind Kind;
        [SerializeReference] public GameplayFloatValueAuthoring Value = new GameplayConstantFloatValueAuthoring { Value = 1 };
    }
    [Serializable, Preserve]
    public sealed class GameplayDamageExecutionAuthoring : GameplayEffectExecutionAuthoring
    {
        public GameplayDamageType DamageType;
        [SerializeReference] public GameplayFloatValueAuthoring Amount = new GameplayFormulaFloatValueAuthoring { Expression = "Caster.Attack * (1.45 + Level * 0.15)" };
        [Tooltip("列表顺序就是结算顺序；护盾最多一项，全部计算成功后才一次提交 HP/Shield。")]
        public GameplayDamageStageAuthoring[] Stages = {
            new GameplayDamageStageAuthoring { Kind = GameplayDamageStageKind.Immunity },
            new GameplayDamageStageAuthoring { Kind = GameplayDamageStageKind.Miss },
            new GameplayDamageStageAuthoring { Kind = GameplayDamageStageKind.Defense },
            new GameplayDamageStageAuthoring { Kind = GameplayDamageStageKind.Critical, Value = new GameplayConstantFloatValueAuthoring { Value = 2 } },
            new GameplayDamageStageAuthoring { Kind = GameplayDamageStageKind.Shield } };
        public GameplayTagQueryAuthoring Immunity = new GameplayTagQueryAuthoring();
        public override void Validate(GameplayAuthoringValidationContext v, string p)
        {
            v.Enum(DamageType,p+".DamageType");
            if (Amount == null) v.Error(p + ".Amount", "Missing damage amount."); else Amount.Validate(v, p + ".Amount");
            Immunity?.Validate(v, p + ".Immunity");
            if (Stages == null) { v.Error(p + ".Stages", "Missing stage list."); return; }
            int shields = 0;
            for (int i = 0; i < Stages.Length; i++)
            {
                if (Stages[i] == null) { v.Error(p + $".Stages[{i}]", "Missing stage."); continue; }
                v.Enum(Stages[i].Kind, p + $".Stages[{i}].Kind");
                if (Stages[i].Kind == GameplayDamageStageKind.Shield) shields++;
                if (Stages[i].Value == null) v.Error(p + $".Stages[{i}].Value", "Missing stage value.");
                else Stages[i].Value.Validate(v, p + $".Stages[{i}].Value");
            }
            if (shields > 1) v.Error(p + ".Stages", "Shield stage must not appear twice.");
        }
        public override IGameplayEffectExecution Build()
        {
            var stages = new List<GameplayDamageStage>(); foreach (var stage in Stages) stages.Add(new GameplayDamageStage(stage.Kind, stage.Value.Build()));
            return new GameplayDamageExecution(Amount.Build(), stages, Immunity?.Build(),DamageType);
        }
    }
    [Serializable, Preserve]
    public sealed class GameplayHealExecutionAuthoring : GameplayEffectExecutionAuthoring
    {
        [SerializeReference] public GameplayFloatValueAuthoring Amount = new GameplayConstantFloatValueAuthoring { Value = 50 };
        public override void Validate(GameplayAuthoringValidationContext v, string p) { if (Amount == null) v.Error(p, "Missing heal value."); else Amount.Validate(v, p + ".Amount"); }
        public override IGameplayEffectExecution Build() => new GameplayHealExecution(Amount.Build());
    }
    [Serializable, Preserve]
    public sealed class GameplayShieldExecutionAuthoring : GameplayEffectExecutionAuthoring
    {
        [SerializeReference] public GameplayFloatValueAuthoring Amount = new GameplayConstantFloatValueAuthoring { Value = 50 };
        public override void Validate(GameplayAuthoringValidationContext v, string p) { if (Amount == null) v.Error(p, "Missing shield value."); else Amount.Validate(v, p + ".Amount"); }
        public override IGameplayEffectExecution Build() => new GameplayShieldExecution(Amount.Build());
    }
}
