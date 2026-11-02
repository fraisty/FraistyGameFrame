using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Standard resource payment; costs mutate owner Attribute BaseValue atomically.
    /// Values may use Level, self attributes/resources and captured Context/Caller input. Both scalar
    /// namespaces read the same captured cast input for costs; no additional mutable cost state exists.</summary>
    [Serializable, Preserve]
    public sealed class GameplayAttributeAbilityCostAuthoring : GameplayAbilityCostAuthoring
    {
        public Entry[] Resources = Array.Empty<Entry>();
        [Serializable]
        public sealed class Entry
        {
            public GameplayAttributeReference Attribute;
            [Min(0)]public float MinimumBalance;
            [Tooltip("负数扣除资源，正数增加资源；余额以 BaseValue 为准。")]
            [SerializeReference] public GameplayFloatValueAuthoring Delta = new GameplayConstantFloatValueAuthoring { Value = -20 };
        }
        public override void Validate(GameplayAuthoringValidationContext validation, string path)
        {
            if (Resources == null || Resources.Length == 0) { validation.Error(path + ".Resources", "Configure at least one resource."); return; }
            for (int i = 0; i < Resources.Length; i++)
            {
                string p = path + $".Resources[{i}]";
                if (Resources[i] == null) { validation.Error(p, "Missing resource entry."); continue; }
                validation.Attribute(Resources[i].Attribute, p + ".Attribute");
                validation.Finite(Resources[i].MinimumBalance,p+".MinimumBalance");if(Resources[i].MinimumBalance<0)validation.Error(p+".MinimumBalance","余额门禁不能为负。");
                if (Resources[i].Delta == null) validation.Error(p + ".Delta", "Missing signed delta.");
                else
                {
                    Resources[i].Delta.Validate(validation, p + ".Delta");
                    try { new GameplayAttributeAbilityCost(new[] { new GameplayAttributeCostEntry(Resources[i].Attribute.Build(), Resources[i].Delta.Build()) }); }
                    catch (Exception ex) when (ex is ArgumentException || ex is FormatException || ex is ArithmeticException)
                    { validation.Error(p + ".Delta", ex.Message); }
                }
            }
        }
        public override IGameplayAbilityCost Build(GameplayAuthoringBuildContext context)
        {
            var entries = new List<GameplayAttributeCostEntry>();
            foreach (var entry in Resources) entries.Add(new GameplayAttributeCostEntry(entry.Attribute.Build(), entry.Delta.Build(),entry.MinimumBalance));
            return new GameplayAttributeAbilityCost(entries);
        }
    }
}
