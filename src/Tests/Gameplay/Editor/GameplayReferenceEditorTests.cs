using System;
using System.Linq;
using FRFrame.Gameplay;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Tests.Gameplay.Editor
{
    public sealed class GameplayReferenceEditorTests
    {
        private const string CatalogPath = "Assets/Settings/FRFrameGameplayReferenceCatalog.asset";
        private const string TempFolder = "Assets/Tests/GameplayReferenceTemp";

        private static GameplayReferenceCatalogAsset ProductionCatalog =>
            AssetDatabase.LoadAssetAtPath<GameplayReferenceCatalogAsset>(CatalogPath);

        [TearDown]
        public void Cleanup()
        {
            if (AssetDatabase.IsValidFolder(TempFolder)) AssetDatabase.DeleteAsset(TempFolder);
        }

        [Test]
        public void CatalogValidationAndTreeUseOnlyCanonicalOrdinalPaths()
        {
            var catalog = ScriptableObject.CreateInstance<GameplayReferenceCatalogAsset>();
            try
            {
                catalog.Tags = new[] { "Event.Combat.Battle.Start", "Event.Combat", "event.Combat", "Event.Combat" };
                catalog.Attributes = new[] { "AzurLane.Attack", "AzurLane.Attack", " " };
                Assert.That(catalog.Validate().Count, Is.EqualTo(3));
                var tree = GameplayReferenceTree.Project(new[] { "Event.Combat.Battle.Start" });
                Assert.That(tree.Children["Event"].IsDefined, Is.False);
                Assert.That(tree.Children["Event"].Children["Combat"].Children["Battle"].IsDefined, Is.False);
                Assert.That(tree.Children["Event"].Children["Combat"].Children["Battle"].Children["Start"].IsDefined, Is.True);
                Assert.That(GameplayTagRefactorService.Matches("Event.Combatant", "Event.Combat"), Is.False);
                Assert.That(GameplayTagRefactorService.Matches("Event.Combat.Battle", "Event.Combat"), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(catalog); }
        }

        [Test]
        public void ProductionCatalogCoversSourceAndAuthoringWithoutDiagnosticsErrors()
        {
            Assert.That(ProductionCatalog, Is.Not.Null);
            Assert.That(ProductionCatalog.Tags.Length, Is.GreaterThanOrEqualTo(23));
            Assert.That(ProductionCatalog.Attributes, Does.Contain("AzurLane.Attack"));
            Assert.That(ProductionCatalog.Attributes, Does.Contain("AzurLane.Defense"));
            Assert.That(ProductionCatalog.Attributes, Does.Contain("AzurLane.MaxHealth"));
            Assert.That(GameplayReferenceCatalogProvider.Find(out string error), Is.SameAs(ProductionCatalog));
            Assert.That(error, Is.Null);
            var report = GameplayReferenceDiagnostics.Scan();
            Assert.That(report.AssetCount, Is.GreaterThanOrEqualTo(3));
            Assert.That(report.SourceLiteralCount, Is.GreaterThanOrEqualTo(22));
            Assert.That(report.Errors, Is.Empty);
            Assert.That(report.Warnings.Any(issue=>issue.Location.Contains("ReviewPointProjectile.asset.StraightDestination")),Is.True,
                "The formal point sample retains destination conflict guidance without turning valid content into a Catalog error.");
        }

        [Test]
        public void SerializedScannerFindsNestedAndManagedReferencesInProductionAssets()
        {
            var refs = GameplayReferenceScanner.ScanAssets(GameplayReferenceScanner.FindDefinitionAssets());
            Assert.That(refs.Any(x => x.AssetPath.EndsWith("BattleStartAttackBoost.asset") &&
                                      x.Value == "Event.Combat.Battle.Start" && x.Kind == GameplayReferenceKind.Tag), Is.True);
            Assert.That(refs.Count(x => x.AssetPath.EndsWith("PlayerAttackBoost.asset") &&
                                        x.Value == "AzurLane.Attack" && x.Kind == GameplayReferenceKind.Attribute), Is.EqualTo(2));
            Assert.That(refs.Any(x => x.AssetPath.EndsWith("WeaponFiredSpecialBarrage.asset") &&
                                      x.Value == "Event.Combat.Weapon.Fired"), Is.True);
            var skill = ScriptableObject.CreateInstance<GameplaySkillDefinitionAsset>();
            try
            {
                skill.Actions = new GameplaySkillActionAuthoring[]
                    { new TestExternalActionAuthoring { Marker = new GameplayTagReference("Test.External.Marker") } };
                var external = GameplayReferenceScanner.ScanAssets(new GameplayDefinitionAsset[] { skill });
                Assert.That(external.Any(x => x.Value == "Test.External.Marker" && x.Kind == GameplayReferenceKind.Tag), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(skill); }
        }

        [Test]
        public void DiagnosticsSeparateAuthoringSyntaxFromCatalogMembership()
        {
            var effect = ScriptableObject.CreateInstance<GameplayEffectDefinitionAsset>();
            try
            {
                SetId(effect, "effect.test.diagnostics");
                effect.DurationPolicy = GameplayEffectDurationPolicy.Duration;
                effect.Duration = 1f;
                effect.GrantedTags = new[] { new GameplayTagReference("Bad..Tag"), new GameplayTagReference("Test.Unregistered") };
                effect.Modifiers = new[] { new GameplayEffectModifierAuthoring
                {
                    Attribute = new GameplayAttributeReference("Test.UnknownAttribute"),
                    Operation = GameplayModifierOperation.Add,
                    Magnitude = new GameplayConstantFloatValueAuthoring { Value = 1f }
                } };
                var report = GameplayReferenceDiagnostics.Scan(ProductionCatalog,
                    new GameplayDefinitionAsset[] { effect }, Array.Empty<GameplayCodeLiteralReference>());
                Assert.That(report.AuthoringValidationErrors, Is.GreaterThanOrEqualTo(1));
                Assert.That(report.MissingTagReferences, Is.EqualTo(1));
                Assert.That(report.MissingAttributeReferences, Is.EqualTo(1));
            }
            finally { UnityEngine.Object.DestroyImmediate(effect); }
        }

        [Test]
        public void PrefixRefactorUpdatesOnlyTypedSerializedReferencesWithUndo()
        {
            AssetDatabase.CreateFolder("Assets/Tests", "GameplayReferenceTemp");
            var catalog = ScriptableObject.CreateInstance<GameplayReferenceCatalogAsset>();
            catalog.Tags = new[] { "Test.Refactor.Old", "Test.Refactor.Old.Child", "Test.Refactor.Older" };
            catalog.Attributes = Array.Empty<string>();
            AssetDatabase.CreateAsset(catalog, TempFolder + "/Catalog.asset");
            var effect = ScriptableObject.CreateInstance<GameplayEffectDefinitionAsset>();
            SetId(effect, "effect.test.refactor");
            effect.DurationPolicy = GameplayEffectDurationPolicy.Duration;
            effect.Duration = 1f;
            AssetDatabase.CreateAsset(effect, TempFolder + "/Effect.asset");
            var skill = ScriptableObject.CreateInstance<GameplaySkillDefinitionAsset>();
            SetId(skill, "skill.test.refactor");
            skill.Trigger = new GameplaySkillEventTriggerAuthoring { EventTag = new GameplayTagReference("Test.Refactor.Old") };
            skill.Conditions = new GameplaySkillConditionAuthoring[] { new GameplaySkillTagConditionAuthoring
            {
                Required = new[] { new GameplayTagReference("Test.Refactor.Old.Child") },
                Blocked = new[] { new GameplayTagReference("Test.Refactor.Older") }
            } };
            skill.Target = new GameplaySkillSelfTargetAuthoring();
            skill.Actions = new GameplaySkillActionAuthoring[] { new GameplaySkillApplyEffectActionAuthoring { Effect = effect } };
            AssetDatabase.CreateAsset(skill, TempFolder + "/Skill.asset");
            AssetDatabase.SaveAssets();
            var preview = GameplayTagRefactorService.PreviewRename(catalog, "Test.Refactor.Old", "Test.Refactor.New",
                new GameplayDefinitionAsset[] { skill, effect }, Array.Empty<GameplayCodeLiteralReference>());
            Assert.That(preview.CanApply, Is.True, string.Join(" | ", preview.Errors));
            Assert.That(preview.AffectedCatalogTags.Count, Is.EqualTo(2));
            Assert.That(preview.AffectedReferences.Count, Is.EqualTo(2));
            GameplayTagRefactorService.Apply(preview);
            Assert.That(catalog.Tags, Does.Contain("Test.Refactor.New").And.Contain("Test.Refactor.New.Child")
                .And.Contain("Test.Refactor.Older"));
            Assert.That(((GameplaySkillEventTriggerAuthoring)skill.Trigger).EventTag.Name, Is.EqualTo("Test.Refactor.New"));
            Assert.That(((GameplaySkillTagConditionAuthoring)skill.Conditions[0]).Required[0].Name,
                Is.EqualTo("Test.Refactor.New.Child"));
            Assert.That(((GameplaySkillTagConditionAuthoring)skill.Conditions[0]).Blocked[0].Name,
                Is.EqualTo("Test.Refactor.Older"));
            Undo.PerformUndo();
            Assert.That(catalog.Tags, Does.Contain("Test.Refactor.Old"));
        }

        [Test]
        public void RealCodeLiteralBlocksRenameAndReferencedDelete()
        {
            var rename = GameplayTagRefactorService.PreviewRename(ProductionCatalog,
                "Event.Combat.Battle.Start", "Event.Combat.Battle.Begin");
            Assert.That(rename.CanApply, Is.False);
            Assert.That(rename.AffectedCode.Any(x => x.SourcePath.EndsWith("CombatEvents.cs") &&
                                                      x.Value == "Event.Combat.Battle.Start"), Is.True);
            Assert.Throws<InvalidOperationException>(() => GameplayTagRefactorService.Apply(rename));
            var delete = GameplayTagRefactorService.PreviewDelete(ProductionCatalog, "Event.Combat.Battle.Start");
            Assert.That(delete.CanApply, Is.False);
            Assert.That(delete.AffectedReferences.Count, Is.GreaterThan(0));
        }

        private static void SetId(GameplayDefinitionAsset asset, string id)
        {
            var serialized = new SerializedObject(asset);
            serialized.FindProperty("definitionId").stringValue = id;
            serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        [Serializable]
        private sealed class TestExternalActionAuthoring : GameplaySkillActionAuthoring
        {
            public GameplayTagReference Marker;
            public override void Validate(GameplayAuthoringValidationContext validation, string path) =>
                validation.Tag(Marker, path + ".Marker");
            public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context) =>
                throw new NotSupportedException("Scanner-only test action.");
        }
    }
}
