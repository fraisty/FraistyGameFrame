using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Tests.Gameplay.Editor
{
    /// <summary>External assembly cost used only in temporary editor assets; no game resource enters FRFrame.</summary>
    [Serializable, Preserve]
    public sealed class AbilityAuthoringTestCost : GameplayAbilityCostAuthoring
    {
        public float Amount = 2f;
        public GameplayEffectDefinitionAsset SharedEffect;
        public GameplayAbilityDefinitionAsset Dependency;
        public override void Validate(GameplayAuthoringValidationContext v, string p)
        {
            v.Finite(Amount, p + ".Amount");
            if (SharedEffect != null) v.Asset(SharedEffect, p + ".SharedEffect");
            if (Dependency != null) v.Asset(Dependency, p + ".Dependency");
        }
        public override IGameplayAbilityCost Build(GameplayAuthoringBuildContext context)
        {
            Dependency?.Build(context);
            return new AbilityAuthoringTestBuiltCost(Amount, SharedEffect?.Build(context));
        }
    }

    public sealed class AbilityAuthoringTestBuiltCost : IGameplayAbilityCost
    {
        public readonly float Amount;
        public readonly GameplayEffectDefinition Effect;
        public AbilityAuthoringTestBuiltCost(float amount, GameplayEffectDefinition effect) { Amount = amount; Effect = effect; }
        public bool CanPay(IGameplayAbilityHost host) => true;
        public bool TryCommit(IGameplayAbilityHost host) => true;
    }

    public sealed class GameplayAbilityAuthoringTests
    {
        private const string Folder = "Assets/Tests/GameplayAbilityG2Temp";
        private readonly List<UnityEngine.Object> _objects = new List<UnityEngine.Object>();
        private static readonly string[] TagFields = { "RequiredTags", "BlockedTags", "ActivationOwnedTags", "AbilityTags",
            "CancelAbilitiesWithTags", "BlockAbilitiesWithTags", "OngoingBlockedTags" };

        [TearDown]
        public void Cleanup()
        {
            if (AssetDatabase.IsValidFolder(Folder)) AssetDatabase.DeleteAsset(Folder);
            foreach (var obj in _objects) if (obj != null) UnityEngine.Object.DestroyImmediate(obj);
            _objects.Clear();
        }

        [Test]
        public void FullContractUsesSharedBuildSessionAndCopiesAllSevenTagSets()
        {
            var ability = Asset<GameplayAbilityDefinitionAsset>();
            foreach (string field in TagFields) typeof(GameplayAbilityDefinitionAsset).GetField(field)
                .SetValue(ability, new[] { new GameplayTagReference("G2." + field) });
            var cooldown = Effect(GameplayEffectDurationPolicy.Duration, "G2.Cooldown");
            var active = Effect(GameplayEffectDurationPolicy.Infinite, "G2.Buff");
            ability.CooldownEffect = cooldown;
            ability.ActivationEffects = new[] { active };
            ability.Cost = new AbilityAuthoringTestCost { Amount = 3f, SharedEffect = active };
            var context = new GameplayAuthoringBuildContext();
            var built = ability.Build(context);
            foreach (string field in TagFields)
            {
                var actual = (IReadOnlyList<GameplayTag>)typeof(GameplayAbilityDefinition).GetProperty(field).GetValue(built);
                Assert.That(actual.Select(t => t.ToString()), Is.EqualTo(new[] { "G2." + field }));
            }
            Assert.That(built.CooldownEffect, Is.SameAs(cooldown.Build(context)));
            Assert.That(built.ActivationEffects.Single(), Is.SameAs(active.Build(context)));
            var cost = (AbilityAuthoringTestBuiltCost)built.Cost;
            Assert.That(cost.Amount, Is.EqualTo(3f));
            Assert.That(cost.Effect, Is.SameAs(built.ActivationEffects[0]));
            Assert.That(ability.Build(context), Is.SameAs(built));
            ability.RequiredTags[0] = new GameplayTagReference("G2.Changed");
            Assert.That(built.RequiredTags[0].ToString(), Is.EqualTo("G2.RequiredTags"));
            Assert.That(ability.Build(new GameplayAuthoringBuildContext()), Is.Not.SameAs(built));
        }

        // Prevents normal cooldown activation/reset from using different full-duration authorities.
        [Test]
        public void CooldownAcceptsFrozenDurationButRejectsPeriodOverridesAcrossAllLayers()
        {
            var ability = Asset<GameplayAbilityDefinitionAsset>();
            var cooldown = Effect(GameplayEffectDurationPolicy.Duration, "G2.CD");
            cooldown.DurationValue = new GameplayConstantFloatValueAuthoring { Value = 4f };
            ability.CooldownEffect = cooldown;
            var builtEffect = cooldown.Build(new GameplayAuthoringBuildContext());
            var validation = Validate(ability);
            Assert.That(validation.HasErrors,Is.False,"H1 evaluates and freezes DurationValue at successful activation.");
            Assert.DoesNotThrow(()=>new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),builtEffect));
            Assert.DoesNotThrow(()=>ability.Build(new GameplayAuthoringBuildContext()));
            cooldown.PeriodValue=new GameplayConstantFloatValueAuthoring{Value=1};
            cooldown.Period=1;
            cooldown.Executions=new GameplayEffectExecutionAuthoring[]{new GameplayAttributeDeltaExecutionAuthoring{Attribute=new GameplayAttributeReference("Health"),Delta=new GameplayConstantFloatValueAuthoring{Value=0}}};
            builtEffect=cooldown.Build(new GameplayAuthoringBuildContext());validation=Validate(ability);

            bool runtimeRejected = false, buildRejected = false;
            try
            {
                new GameplayAbilityDefinition(Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>(),
                    Array.Empty<GameplayTag>(), builtEffect);
            }
            catch (ArgumentException) { runtimeRejected = true; }
            try { ability.Build(new GameplayAuthoringBuildContext()); }
            catch (InvalidOperationException) { buildRejected = true; }
            bool pathError = validation.Issues.Any(issue =>
                issue.Path == "Ability.CooldownEffect.PeriodValue" &&
                issue.Severity == GameplayAuthoringIssueSeverity.Error);
            Assert.That(new[] { runtimeRejected, pathError, buildRejected }, Is.EqualTo(new[] { true, true, true }),
                "Cooldown remains a simple tag-only effect; all layers reject a dynamic period.");
        }

        [Test]
        public void ChargeBuildAndMutualExclusionHaveFieldPaths()
        {
            var ability = Asset<GameplayAbilityDefinitionAsset>();
            ability.Charge = new GameplayAbilityChargeAuthoring { MaxCharges = 3, RechargeDuration = 2f };
            var built = ability.Build(new GameplayAuthoringBuildContext());
            Assert.That(built.Charge.MaxCharges, Is.EqualTo(3));
            Assert.That(built.Charge.RechargeDuration, Is.EqualTo(2f));
            Assert.That(built.CooldownEffect, Is.Null);
            ability.CooldownEffect = Effect(GameplayEffectDurationPolicy.Duration, "G2.CD");
            Assert.That(Validate(ability).Issues.Any(i => i.Path == "Ability.Charge"), Is.True);
            ability.Charge.MaxCharges = 0; ability.Charge.RechargeDuration = float.NaN;
            var paths = Validate(ability).Issues.Select(i => i.Path).ToArray();
            Assert.That(paths, Does.Contain("Ability.Charge.MaxCharges"));
            Assert.That(paths, Does.Contain("Ability.Charge.RechargeDuration"));
            Assert.Throws<InvalidOperationException>(() => ability.Build(new GameplayAuthoringBuildContext()));
        }

        [Test]
        public void BrokenTagsCooldownAndActivationReferencesReportPathsBeforeBuild()
        {
            var ability = Asset<GameplayAbilityDefinitionAsset>();
            ability.RequiredTags = new[] { new GameplayTagReference("bad..tag") };
            var cooldown = Effect(GameplayEffectDurationPolicy.Infinite, "G2.CD");
            cooldown.GrantedTags = Array.Empty<GameplayTagReference>();
            cooldown.Modifiers = new[] { new GameplayEffectModifierAuthoring { Attribute = new GameplayAttributeReference("G2.Power"),
                Magnitude = new GameplayConstantFloatValueAuthoring { Value = 1 } } };
            cooldown.Stacking = new GameplayEffectStackingAuthoring { StackKey = new GameplayTagReference("G2.Stack") };
            cooldown.ApplicationRequirements = new GameplayTagQueryAuthoring { All = new[] { new GameplayTagReference("G2.Require") } };
            ability.CooldownEffect = cooldown;
            var instant = Effect(GameplayEffectDurationPolicy.Instant, "G2.Instant");
            ability.ActivationEffects = new[] { null, instant, cooldown };
            ability.Cost = new AbilityAuthoringTestCost { Amount = float.NaN };
            string[] paths = Validate(ability).Issues.Select(i => i.Path).ToArray();
            foreach (string path in new[] { "RequiredTags[0]", "CooldownEffect.DurationPolicy", "CooldownEffect.GrantedTags",
                "CooldownEffect.Modifiers", "CooldownEffect.Stacking", "CooldownEffect.ApplicationRequirements",
                "ActivationEffects[0]", "ActivationEffects[1].DurationPolicy", "ActivationEffects[2].Stacking", "Cost.Amount" })
                Assert.That(paths, Does.Contain("Ability." + path));
            Assert.That(paths.Count(p => p == "Ability.ActivationEffects[0]"), Is.EqualTo(1));
            Assert.Throws<InvalidOperationException>(() => ability.Build(new GameplayAuthoringBuildContext()));
        }

        [Test]
        public void ExternalCostCyclesAreRejectedWithoutPoisoningBuildSession()
        {
            var a = Asset<GameplayAbilityDefinitionAsset>(); var b = Asset<GameplayAbilityDefinitionAsset>();
            a.Cost = new AbilityAuthoringTestCost { Dependency = b };
            b.Cost = new AbilityAuthoringTestCost { Dependency = a };
            Assert.That(Validate(a).Issues.Any(i => i.Path == "Ability.Cost.Dependency.Cost.Dependency" && i.Message.Contains("Circular")), Is.True);
            var context = new GameplayAuthoringBuildContext();
            Assert.Throws<InvalidOperationException>(() => a.Build(context));
            b.Cost = null;
            Assert.That(a.Build(context).Cost, Is.TypeOf<AbilityAuthoringTestBuiltCost>());
        }

        [Test]
        public void CostPickerRejectsEditorTestTypesAndManagedReferencesStillSerialize()
        {
            var ability = Asset<GameplayAbilityDefinitionAsset>(); Persist(ability, "Ability");
            Assert.That(GameplayAbilityCostEditor.Types(), Has.No.Member(typeof(AbilityAuthoringTestCost)));
            Assert.Throws<ArgumentException>(() => GameplayAbilityCostEditor.SetCost(ability, typeof(AbilityAuthoringTestCost)));
            Assert.That(ability.Cost, Is.Null);
            Undo.IncrementCurrentGroup();
            // This temporary test asset exercises serialization independently of production picker policy.
            Undo.RecordObject(ability, "Test managed reference serialization");
            var serialized = new SerializedObject(ability);
            serialized.FindProperty("Cost").managedReferenceValue = new AbilityAuthoringTestCost();
            serialized.ApplyModifiedProperties();
            EditorUtility.SetDirty(ability);
            Undo.FlushUndoRecordObjects();
            Assert.That(EditorUtility.IsDirty(ability), Is.True);
            Assert.That(new SerializedObject(ability).FindProperty("Cost").managedReferenceValue, Is.TypeOf<AbilityAuthoringTestCost>());
            Undo.PerformUndo(); Assert.That(ability.Cost, Is.Null);
            Undo.PerformRedo(); Assert.That(ability.Cost, Is.TypeOf<AbilityAuthoringTestCost>());
            ((AbilityAuthoringTestCost)ability.Cost).Amount = 7f; EditorUtility.SetDirty(ability);
            AssetDatabase.SaveAssets();
            var clone = Asset<GameplayAbilityDefinitionAsset>();
            EditorJsonUtility.FromJsonOverwrite(EditorJsonUtility.ToJson(ability), clone);
            Assert.That(clone.Cost, Is.TypeOf<AbilityAuthoringTestCost>());
            Assert.That(((AbilityAuthoringTestBuiltCost)clone.Build(new GameplayAuthoringBuildContext()).Cost).Amount, Is.EqualTo(7f));
            AssetDatabase.ImportAsset(Folder + "/Ability.asset", ImportAssetOptions.ForceUpdate);
            Assert.That(((AbilityAuthoringTestCost)AssetDatabase.LoadAssetAtPath<GameplayAbilityDefinitionAsset>(Folder + "/Ability.asset").Cost).Amount, Is.EqualTo(7f));
        }

        [Test]
        public void GenericScannerFindsAbilitySevenTagsAndReferencedEffectDiagnostics()
        {
            var ability = Asset<GameplayAbilityDefinitionAsset>(); Persist(ability, "Ability");
            foreach (string field in TagFields) typeof(GameplayAbilityDefinitionAsset).GetField(field)
                .SetValue(ability, new[] { new GameplayTagReference("G2." + field) });
            Assert.That(GameplayReferenceScanner.FindDefinitionAssets(), Does.Contain(ability));
            var refs = GameplayReferenceScanner.ScanAssets(new[] { ability });
            Assert.That(refs.Count, Is.EqualTo(7));
            var catalog = Asset<GameplayReferenceCatalogAsset>();
            catalog.Tags = TagFields.Select(f => "G2." + f).ToArray();
            var report = GameplayReferenceDiagnostics.Scan(catalog, new[] { ability }, Array.Empty<GameplayCodeLiteralReference>());
            Assert.That(report.Errors, Is.Empty);
            var preview = GameplayTagRefactorService.PreviewRename(catalog, "G2", "G2Renamed", new[] { ability }, Array.Empty<GameplayCodeLiteralReference>());
            Assert.That(preview.AffectedReferences.Count, Is.EqualTo(7));
            ability.CooldownEffect = Effect(GameplayEffectDurationPolicy.Duration, "G2.CD");
            ability.CooldownEffect.Duration = -1f;
            report = GameplayReferenceDiagnostics.Scan(catalog, new[] { ability }, Array.Empty<GameplayCodeLiteralReference>());
            Assert.That(report.Errors.Any(e => e.Location.EndsWith(".CooldownEffect.Duration")), Is.True);
        }

        private T Asset<T>() where T : ScriptableObject
        {
            var asset = ScriptableObject.CreateInstance<T>(); _objects.Add(asset);
            var definition = asset as GameplayDefinitionAsset;
            if (definition != null)
            {
                var so = new SerializedObject(asset); so.FindProperty("definitionId").stringValue = "G2." + _objects.Count;
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return asset;
        }
        private GameplayEffectDefinitionAsset Effect(GameplayEffectDurationPolicy policy, string tag)
        {
            var effect = Asset<GameplayEffectDefinitionAsset>(); effect.DurationPolicy = policy; effect.Duration = 1f;
            effect.GrantedTags = new[] { new GameplayTagReference(tag) }; return effect;
        }
        private static GameplayAuthoringValidationContext Validate(GameplayAbilityDefinitionAsset asset)
        {
            var v = new GameplayAuthoringValidationContext(); v.Asset(asset, "Ability"); return v;
        }
        private static void Persist(GameplayDefinitionAsset asset, string name)
        {
            if (!AssetDatabase.IsValidFolder(Folder)) AssetDatabase.CreateFolder("Assets/Tests", "GameplayAbilityG2Temp");
            AssetDatabase.CreateAsset(asset, Folder + "/" + name + ".asset");
        }
    }
}
