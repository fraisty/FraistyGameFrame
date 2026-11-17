using System;
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
    public enum GameplayDamageStageSource { Builtin, Extension }
    [Serializable, Preserve]
    public sealed class GameplayDamageStageAuthoring
    {
        public GameplayDamageStageKind Kind;
        [SerializeReference] public GameplayFloatValueAuthoring Value = new GameplayConstantFloatValueAuthoring { Value = 1 };
        public GameplayDamageStageSource Source;
        [SerializeReference] public GameplayDamageCalculationStageAuthoring Extension;
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
        [Tooltip("可选。伤害提交后用标准治疗回复施法者；仅在实际扣除 HP 时执行。公式可读取 Context.Data.Standard.Combat.AppliedDamage。")]
        [SerializeReference] public GameplayFloatValueAuthoring HealInstigatorAmount;
        public override void Validate(GameplayAuthoringValidationContext v, string p)
        {
            v.Enum(DamageType,p+".DamageType");
            if (Amount == null) v.Error(p + ".Amount", "Missing damage amount."); else Amount.Validate(v, p + ".Amount");
            Immunity?.Validate(v, p + ".Immunity");
            HealInstigatorAmount?.Validate(v, p + ".HealInstigatorAmount");
            if (Stages == null) { v.Error(p + ".Stages", "Missing stage list."); return; }
            int shields = 0, defenses = 0, replacements = 0;
            for (int i = 0; i < Stages.Length; i++)
            {
                string path = p + $".Stages[{i}]";
                var stage = Stages[i];
                if (stage == null) { v.Error(path, "Missing stage."); continue; }
                v.Enum(stage.Source, path + ".Source");
                if (stage.Source == GameplayDamageStageSource.Extension)
                {
                    var extension = stage.Extension;
                    if (extension == null) { v.Error(path + ".Extension", "缺少扩展阶段或其已序列化类型。"); continue; }
                    if (!extension.IsAvailableInScope(v.DamageStageScope))
                        v.Error(path + ".Extension", "此阶段只可内嵌在 " + extension.RequiredScope + " Scope 的技能中。");
                    extension.Validate(v, path + ".Extension");
                    if (extension.ReplacesDefense) replacements++;
                    continue;
                }
                v.Enum(stage.Kind, path + ".Kind");
                if (stage.Kind == GameplayDamageStageKind.Shield) shields++;
                if (stage.Kind == GameplayDamageStageKind.Defense) defenses++;
                if (stage.Value == null) v.Error(path + ".Value", "Missing stage value.");
                else stage.Value.Validate(v, path + ".Value");
            }
            if (shields > 1) v.Error(p + ".Stages", "Shield stage must not appear twice.");
            if (replacements > 0 && replacements + defenses > 1)
                v.Error(p + ".Stages", "护甲穿透替代防御，整条管线只保留一个防御阶段。");
        }
        public override IGameplayEffectExecution Build()
        {
            var stages = new GameplayDamageStage[Stages.Length];
            for (int i = 0; i < Stages.Length; i++)
            {
                var stage = Stages[i];
                switch (stage.Source)
                {
                    case GameplayDamageStageSource.Builtin:
                        stages[i] = new GameplayDamageStage(stage.Kind, stage.Value.Build());
                        break;
                    case GameplayDamageStageSource.Extension:
                        var extension = stage.Extension ?? throw new InvalidOperationException($"Stages[{i}].Extension: missing stage type.");
                        try
                        {
                            stages[i] = new GameplayDamageStage(extension.BuildCalculation(), extension.ReplacesDefense, extension.BuildParameters());
                        }
                        catch (Exception error) when (error is ArgumentException || error is InvalidOperationException)
                        {
                            throw new InvalidOperationException($"Stages[{i}].Extension: {error.Message}", error);
                        }
                        break;
                    default:
                        throw new InvalidOperationException($"Stages[{i}].Source: unsupported damage stage source.");
                }
            }
            return new GameplayDamageExecution(Amount.Build(), stages, Immunity?.Build(), DamageType, HealInstigatorAmount?.Build());
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
