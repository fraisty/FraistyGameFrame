using FRFrame.Gameplay;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;
using FRFrame.Gameplay.Unity;
using System.Linq;
using NUnit.Framework;
using UnityEngine;

namespace FRFrame.Gameplay.Tests.UnityIntegration
{
    public sealed class GameplayFloatingTextOwnershipTests
    {
        [Test]
        public void AuthoredFeedbackClaimsOnlyItsOutcomeAndUnclaimedResultsKeepTheirDefault()
        {
            var subject = new GameObject("Combat text target");
            var asset = Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameplayFloatingTextAsset>(
                "Assets/GameRes/GameplayValidation/XH/Gameplay/Standard/Feedback1001.asset"));
            asset.Follow = true;
            var authored = Object.Instantiate(asset);
            try
            {
                authored.DamagePrefix = "Execute ";
                authored.NumberFormat = "0.##";
                using var world = new GameplayStandardWorld();
                var attacker = world.CreateActor(default);
                var target = world.CreateActor(default);
                var build = new GameplayAuthoringBuildContext();
                var controller = new GameplayUnityAnimationController(target, subject) { FloatingText = asset.Build(build) };
                target.Presentation = controller;
                var action = new GameplayShowCombatTextActionAuthoring { Source = authored }.Build(build);
                var targetData = new GameplayTargetData(target.Id);
                var first = new GameplayContext(attacker.Id, targetData);
                var second = new GameplayContext(attacker.Id, targetData);
                target.ReportCombat(new GameplayCombatOutcome(first, GameplayCombatOutcomeKind.Damage, default, 5.4f, 0, 100, 94.6f));
                target.ReportCombat(new GameplayCombatOutcome(second, GameplayCombatOutcomeKind.Damage, default, 12.25f, 0, 94.6f, 82.35f));
                Assert.That(action.Execute(first, default, attacker), Is.EqualTo(GameplaySkillActionResult.Rejected),
                    "A separate callback can share source IDs without owning the latest result.");
                Assert.That(action.Execute(second.WithSource(target.Id), default, attacker), Is.EqualTo(GameplaySkillActionResult.Rejected));
                Assert.That(action.Execute(second, default, attacker), Is.EqualTo(GameplaySkillActionResult.Succeeded));
                Assert.That(action.Execute(second, default, attacker), Is.EqualTo(GameplaySkillActionResult.Succeeded));
                Assert.That(controller.ActiveFloatingTextCount, Is.EqualTo(1));
                world.Advance(0);
                var labels = subject.GetComponentsInChildren<GameplayFloatingTextWidget>().Select(widget => widget.DisplayText);
                Assert.That(labels, Is.EquivalentTo(new[] { "Execute 12.25", "-5" }));
                Assert.That(action.Execute(second, default, attacker), Is.EqualTo(GameplaySkillActionResult.Rejected));
                world.Advance(2);
                Assert.That(controller.ActiveFloatingTextCount, Is.Zero);
            }
            finally { Object.DestroyImmediate(authored); Object.DestroyImmediate(asset); Object.DestroyImmediate(subject); }
        }

        /// <summary>Regression from real XH Stop Play: TMP destroys a cloned static font's borrowed
        /// asset atlas. Static fonts must remain borrowed; dynamic fonts must release their private atlas.</summary>
        [TestCase(false)][TestCase(true)]
        public void WidgetDestructionPreservesSharedFontAndReleasesOnlyPrivateFont(bool dynamic)
        {
            var shared=UnityEditor.AssetDatabase.LoadAssetAtPath<TMPro.TMP_FontAsset>(dynamic?
                "Assets/GameRes/Common/Font/font_main_zht SDF.asset":"Assets/Plugins/TextMesh Pro/Resources/Fonts & Materials/LiberationSans SDF.asset");
            var sharedAtlas=shared.atlasTextures[0];var sharedMaterial=shared.material;
            var prefab=UnityEditor.AssetDatabase.LoadAssetAtPath<GameObject>("Assets/GameRes/GameplayValidation/XH/Gameplay/Standard/XHCombatFloatingText.prefab");
            var root=Object.Instantiate(prefab);
            try{
                var text=root.GetComponentInChildren<TMPro.TMP_Text>(true);text.font=shared;
                var widget=root.GetComponentInChildren<GameplayFloatingTextWidget>(true);
                widget.OpenStandalone(new GameplayFloatingTextContent("+25",Color.green));
                var usedFont=text.font;var usedAtlas=usedFont.atlasTextures[0];var usedMaterial=text.fontSharedMaterial;
                Assert.That(usedFont==shared,Is.EqualTo(!dynamic));Assert.That(usedMaterial,Is.Not.SameAs(sharedMaterial));
                // EditMode does not dispatch OnDestroy for a runtime-only MonoBehaviour that has
                // never received Awake. Invoke that lifecycle boundary; real Stop Play is checked separately.
                typeof(GameplayFloatingTextWidget).GetMethod("OnDestroy",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic).Invoke(widget,null);
                Object.DestroyImmediate(root);
                Assert.That(shared!=null&&sharedAtlas!=null&&sharedMaterial!=null,Is.True);
                Assert.That(shared.atlasTextures[0],Is.SameAs(sharedAtlas));Assert.That(shared.material,Is.SameAs(sharedMaterial));
                Assert.That(usedMaterial==null,Is.True);
                if(dynamic)Assert.That(usedFont==null&&usedAtlas==null,Is.True);
            }finally{if(root!=null)Object.DestroyImmediate(root);}
        }
        /// <summary>Protects standalone UI initialization, actual pool reuse, and target cleanup.
        /// Text appearance is verified in the permanent Lab, not duplicated by formatting cases.</summary>
        [Test]
        public void FeedbackReusesInitializedWidgetAndTargetCleanupReleasesIt()
        {
            var subject=new GameObject("Floating feedback ownership");
            try
            {
                using(var world=new GameplayStandardWorld())
                {
                    var actor=world.CreateActor(default);var controller=new GameplayUnityAnimationController(actor,subject);actor.Presentation=controller;
                    var source=UnityEditor.AssetDatabase.LoadAssetAtPath<GameplayFloatingTextAsset>("Assets/GameRes/GameplayValidation/FRFrameSkillLab/Resources/FRFrameSkillLab/CombatFloatingText.asset");
                    var definition=source.Build(new GameplayAuthoringBuildContext());
                    var context=new GameplayContext(actor.Id,new GameplayTargetData(actor.Id));
                    var outcome=new GameplayCombatOutcome(context,GameplayCombatOutcomeKind.Heal,default,25,0,50,75);
                    Assert.That(controller.ShowFloatingText(definition,outcome),Is.True);
                    var widget=subject.GetComponentInChildren<GameplayFloatingTextWidget>();Assert.That(widget.IsInitialized,Is.True);
                    Assert.That(widget.IsVisible,Is.True);Assert.That(widget.DisplayText,Is.EqualTo("+25"));
                    controller.Advance(definition.Duration);Assert.That(controller.ActiveFloatingTextCount,Is.Zero);Assert.That(widget.IsVisible,Is.False);
                    controller.ShowFloatingText(definition,outcome);Assert.That(subject.GetComponentInChildren<GameplayFloatingTextWidget>(),Is.SameAs(widget));
                    actor.Dispose();Assert.That(controller.ActiveFloatingTextCount,Is.Zero);Assert.That(widget==null,Is.True);
                }
            }
            finally{Object.DestroyImmediate(subject);}
        }
    }
}
