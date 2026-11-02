using System;
using System.Linq;
using FRFrame.Gameplay;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using FRFrame.Gameplay.Editor.Timeline;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Tests.Gameplay.Editor
{
    public sealed class GameplaySequenceSkillStepEditorTests
    {
        [Test]
        public void NestedNodesBuildUsingSameEffectIdentityAndReferencesAreTraversed()
        {
            var sequence = Create<GameplaySequenceDefinitionAsset>("Test.Step");
            var skill = Create<GameplaySkillDefinitionAsset>("Test.Skill");
            var effect = Create<GameplayEffectDefinitionAsset>("Test.Effect");
            effect.Duration = 1f;
            try
            {
                sequence.Duration = 1f;
                var step = ValidStep(effect);
                sequence.Markers = new GameplaySequenceMarkerAuthoring[] { step };
                skill.Actions = new GameplaySkillActionAuthoring[] { new GameplaySkillPlaySequenceActionAuthoring { Sequence = sequence } };
                var build = new GameplayAuthoringBuildContext();
                var built = (GameplaySequenceSkillStepMarkerDefinition)sequence.Build(build).Markers[0];
                Assert.That(((GameplaySkillApplyEffectAction)built.Actions[0]).Effect, Is.SameAs(effect.Build(build)));
                Assert.That(built.Conditions.Count, Is.EqualTo(1));
                Assert.That(built.Target, Is.TypeOf<GameplaySkillSelfTarget>());
                var references = GameplayReferenceScanner.ScanAssets(new[] { sequence });
                Assert.That(references.Any(r => r.PropertyPath.Contains("Conditions") && r.Value == "State.Test.Ready"), Is.True);
                // A nested reference cycle is diagnosed, and editor traversal terminates.
                step.Actions = new GameplaySkillActionAuthoring[] { new GameplaySkillPlaySequenceActionAuthoring { Sequence = sequence } };
                var validation = new GameplayAuthoringValidationContext(); validation.Asset(sequence, "Sequence");
                Assert.That(validation.Issues.Any(i => i.Message.Contains("Circular")), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(sequence); UnityEngine.Object.DestroyImmediate(skill); UnityEngine.Object.DestroyImmediate(effect); }
        }

        [Test]
        public void ValidationReportsNestedNullNodesAndInvalidChildReferencesWithPaths()
        {
            var asset = Create<GameplaySequenceDefinitionAsset>("Test.BadStep");
            try
            {
                asset.Duration = 1f;
                asset.Markers = new GameplaySequenceMarkerAuthoring[] { new GameplaySequenceSkillStepMarkerAuthoring {
                    Time = .5f, Conditions = new GameplaySkillConditionAuthoring[] { null }, Target = null,
                    Actions = new GameplaySkillActionAuthoring[] { null, new GameplaySkillApplyEffectActionAuthoring() } } };
                var v = new GameplayAuthoringValidationContext(); v.Asset(asset, "Sequence");
                Assert.That(v.Issues.Any(i => i.Path.EndsWith("Conditions[0]")), Is.True);
                Assert.That(v.Issues.Any(i => i.Path.EndsWith(".Target")), Is.True);
                Assert.That(v.Issues.Any(i => i.Path.EndsWith("Actions[0]")), Is.True);
                Assert.That(v.Issues.Any(i => i.Path.EndsWith("Actions[1].Effect")), Is.True);
                Assert.Throws<InvalidOperationException>(() => asset.Build(new GameplayAuthoringBuildContext()));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void TimelineAddsSelectsEditsDuplicatesAndSerializesNestedSkillNodes()
        {
            var asset = Create<GameplaySequenceDefinitionAsset>("Test.TimelineStep");
            var effect = AssetDatabase.LoadAssetAtPath<GameplayEffectDefinitionAsset>(
                "Assets/GameRes/AzurLane/Config/Gameplay/Effects/TimedOverdrive.asset");
            Assert.That(effect, Is.Not.Null); // Timeline duplicates authored persistent references.
            try
            {
                asset.Duration = 1f;
                int index = GameplaySequenceTimelineModel.Add(asset, ValidStep(effect));
                Assert.That(GameplaySequenceTimelineModel.Track(asset.Markers[index]), Is.EqualTo(2));
                var area = new Rect(0, 0, 700, 180);
                var layout = GameplaySequenceMarkerLayout.Build(asset.Markers, area, 142f, 200f);
                Assert.That(GameplaySequenceMarkerLayout.HitTest(layout, layout[0].Rect.center, new Rect(142, 32, 558, 126)), Is.EqualTo(index));
                GameplaySequenceTimelineModel.SetTime(asset, index, .7f, false);
                int copy = GameplaySequenceTimelineModel.Duplicate(asset, index);
                var cloned = (GameplaySequenceSkillStepMarkerAuthoring)asset.Markers[copy];
                Assert.That(cloned.Time, Is.EqualTo(.7f));
                Assert.That(cloned, Is.Not.SameAs(asset.Markers[index]));
                Assert.That(cloned.Target, Is.TypeOf<GameplaySkillSelfTargetAuthoring>());
                Assert.That(((GameplaySkillApplyEffectActionAuthoring)cloned.Actions[0]).Effect, Is.SameAs(effect));
                var serialized = new SerializedObject(asset);
                var node = serialized.FindProperty("Markers").GetArrayElementAtIndex(copy).FindPropertyRelative("Actions").GetArrayElementAtIndex(0);
                Assert.That(node.managedReferenceValue, Is.TypeOf<GameplaySkillApplyEffectActionAuthoring>());
                node.FindPropertyRelative("Effect").objectReferenceValue = null;
                serialized.ApplyModifiedProperties();
                Assert.That(((GameplaySkillApplyEffectActionAuthoring)cloned.Actions[0]).Effect, Is.Null);
                Assert.That(((GameplaySkillApplyEffectActionAuthoring)((GameplaySequenceSkillStepMarkerAuthoring)asset.Markers[index]).Actions[0]).Effect, Is.SameAs(effect));
            }
            finally { Undo.ClearUndo(asset); UnityEngine.Object.DestroyImmediate(asset); }
        }

        private static GameplaySequenceSkillStepMarkerAuthoring ValidStep(GameplayEffectDefinitionAsset effect) =>
            new GameplaySequenceSkillStepMarkerAuthoring {
                Time = .5f, Conditions = new GameplaySkillConditionAuthoring[] {
                    new GameplaySkillTagConditionAuthoring { Required = new[] { new GameplayTagReference("State.Test.Ready") } } },
                Target = new GameplaySkillSelfTargetAuthoring(),
                Actions = new GameplaySkillActionAuthoring[] { new GameplaySkillApplyEffectActionAuthoring { Effect = effect } } };
        private static T Create<T>(string id) where T : GameplayDefinitionAsset
        {
            var asset = ScriptableObject.CreateInstance<T>(); asset.name = id;
            var so = new SerializedObject(asset); so.FindProperty("definitionId").stringValue = id; so.ApplyModifiedPropertiesWithoutUndo();
            return asset;
        }
    }
}
