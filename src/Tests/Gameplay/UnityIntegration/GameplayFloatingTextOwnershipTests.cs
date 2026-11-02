using FRFrame.Gameplay;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;
using FRFrame.Gameplay.Unity;
using NUnit.Framework;
using UnityEngine;

namespace FRFrame.Gameplay.Tests.UnityIntegration
{
    public sealed class GameplayFloatingTextOwnershipTests
    {
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
