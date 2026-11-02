using System;
using System.Linq;
using FRFrame.Gameplay;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor.Timeline;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using System.Reflection;
using UnityEngine.UIElements;

namespace FRFrame.Tests.Gameplay.Editor
{
    public sealed class GameplaySequenceTimelineEditorTests
    {
        private const string Root = "Assets/GameRes/AzurLane/Config/Gameplay/";
        private static GameplaySequenceDefinitionAsset Sequence => AssetDatabase.LoadAssetAtPath<GameplaySequenceDefinitionAsset>(
            Root + "Sequences/TimedOverdriveSequence.asset");

        [Test]
        public void ProductionAssetBuildKeepsMarkerOrderAndEffectIdentity()
        {
            var asset = Sequence;
            Assert.That(asset, Is.Not.Null);
            Assert.That(asset.Markers.Select(GameplaySequenceTimelineModel.Track), Is.EqualTo(new[] { 1, 0, 1 }));
            Assert.That(asset.Markers.Select(m => m.Time), Is.EqualTo(new[] { 0f, .5f, .5f }));
            var effect = ((GameplaySequenceApplyEffectMarkerAuthoring)asset.Markers[1]).Effect;
            var context = new GameplayAuthoringBuildContext();
            var built = asset.Build(context);
            Assert.That(((GameplaySequenceApplyEffectMarkerDefinition)built.Markers[1]).Effect,
                Is.SameAs(effect.Build(context)));
            var skill = AssetDatabase.LoadAssetAtPath<GameplaySkillDefinitionAsset>(
                Root + "Skills/BattleStartTimedOverdrive.asset");
            var action = (GameplaySkillPlaySequenceAction)skill.Build(context).Actions.Single();
            Assert.That(action.Sequence, Is.SameAs(built));
            Assert.That(GameplaySequenceTimelineValidation.Check(asset), Is.Empty);
        }

        [Test]
        public void DragSnapWritesRealMarkerTimeAndUndoRestoresAuthoredArray()
        {
            var asset = ScriptableObject.CreateInstance<GameplaySequenceDefinitionAsset>();
            try
            {
                asset.Duration = 1f;
                asset.Markers = new GameplaySequenceMarkerAuthoring[] {
                    new GameplaySequenceCueMarkerAuthoring { Time = .1f },
                    new GameplaySequenceApplyEffectMarkerAuthoring { Time = .2f },
                    new GameplaySequenceCueMarkerAuthoring { Time = .3f } };
                GameplaySequenceTimelineModel.SetTime(asset, 1, .493f, true);
                Assert.That(asset.Markers[1].Time, Is.EqualTo(.5f).Within(.0001f));
                Assert.That(asset.Markers.Select(GameplaySequenceTimelineModel.Track), Is.EqualTo(new[] { 1, 0, 1 }));
                Undo.PerformUndo();
                Assert.That(asset.Markers[1].Time, Is.EqualTo(.2f).Within(.0001f));
                GameplaySequenceTimelineModel.SetTime(asset,1,.246f,true,30);
                Assert.That(asset.Markers[1].Time,Is.EqualTo(7f/30).Within(.0001f),"XH snaps to its display FPS rather than silently using 60.");
                Assert.That(GameplaySequenceTimelineModel.Snap(12f/30,true,30),Is.EqualTo(.4f));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void MarkerGestureRecordsOneUndoAndCancelRestoresOriginalTime()
        {
            var asset = ScriptableObject.CreateInstance<GameplaySequenceDefinitionAsset>();
            try
            {
                asset.Duration = 1f;
                asset.Markers = new GameplaySequenceMarkerAuthoring[] {
                    new GameplaySequenceCueMarkerAuthoring { Time = .2f } };
                using (var drag = GameplaySequenceTimelineModel.BeginDrag(asset, 0))
                {
                    Undo.FlushUndoRecordObjects(); // MouseDown can end its editor frame before movement.
                    drag.Update(.3f, false);
                    Undo.FlushUndoRecordObjects();
                    drag.Update(.4f, false);
                    Undo.FlushUndoRecordObjects();
                    drag.Update(.5f, false);
                }
                Assert.That(asset.Markers[0].Time, Is.EqualTo(.5f));
                Undo.PerformUndo();
                Assert.That(asset.Markers[0].Time, Is.EqualTo(.2f));
                Undo.PerformRedo();
                Assert.That(asset.Markers[0].Time, Is.EqualTo(.5f));
                Undo.PerformUndo();
                Assert.That(asset.Markers[0].Time, Is.EqualTo(.2f));
                using (var drag = GameplaySequenceTimelineModel.BeginDrag(asset, 0))
                {
                    drag.Update(.8f, false);
                    drag.Cancel();
                }
                Assert.That(asset.Markers[0].Time, Is.EqualTo(.2f));
                Undo.IncrementCurrentGroup();GameplaySequenceTimelineModel.SetTime(asset,0,.35f,false);Undo.FlushUndoRecordObjects();Undo.IncrementCurrentGroup();EditorUtility.ClearDirty(asset);
                using(var cancelled=GameplaySequenceTimelineModel.BeginDrag(asset,0)){cancelled.Update(.8f,false);Undo.FlushUndoRecordObjects();cancelled.Cancel();}
                Assert.That(asset.Markers[0].Time,Is.EqualTo(.35f));Assert.That(EditorUtility.IsDirty(asset),Is.False);
                Undo.PerformUndo();Assert.That(asset.Markers[0].Time,Is.EqualTo(.2f),"One Undo must reach the previous valid edit, not a cancelled gesture.");
                Undo.PerformRedo();Assert.That(asset.Markers[0].Time,Is.EqualTo(.35f));
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void CoincidentMarkersUseTheirOwnVisibleRectForHitTesting()
        {
            var markers = new GameplaySequenceMarkerAuthoring[] {
                new GameplaySequenceCueMarkerAuthoring { Time = .5f },
                new GameplaySequenceCueMarkerAuthoring { Time = .5f } };
            var canvas = new Rect(0f, 0f, 600f, 120f);
            var entries = GameplaySequenceMarkerLayout.Build(markers, canvas, 142f, 200f);
            Assert.That(entries, Has.Count.EqualTo(2));
            Assert.That(entries[0].Rect.Overlaps(entries[1].Rect), Is.False);
            var content = new Rect(142f, 32f, 458f, 84f);
            Assert.That(GameplaySequenceMarkerLayout.HitTest(entries, entries[0].Rect.center, content), Is.Zero);
            Assert.That(GameplaySequenceMarkerLayout.HitTest(entries, entries[1].Rect.center, content), Is.EqualTo(1));
        }

        [Test]
        public void NearbyMarkersKeepDistinctDrawAndHitRects()
        {
            var markers = new GameplaySequenceMarkerAuthoring[] {
                new GameplaySequenceCueMarkerAuthoring { Time = .5f },
                new GameplaySequenceCueMarkerAuthoring { Time = .505f } };
            var canvas = new Rect(0f, 0f, 600f, 120f);
            var entries = GameplaySequenceMarkerLayout.Build(markers, canvas, 142f, 200f);
            var content = new Rect(142f, 32f, 458f, 84f);
            Assert.That(entries[0].Rect.Overlaps(entries[1].Rect), Is.False);
            for (int i = 0; i < entries.Count; i++)
                Assert.That(GameplaySequenceMarkerLayout.HitTest(entries, entries[i].Rect.center, content),
                    Is.EqualTo(entries[i].Index));
        }

        [Test]
        public void LosingTimelineFocusCancelsDragAndReleasesHotControl()
        {
            var asset = ScriptableObject.CreateInstance<GameplaySequenceDefinitionAsset>();
            int previousHotControl = GUIUtility.hotControl;
            try
            {
                asset.Duration = 1f;
                asset.Markers = new GameplaySequenceMarkerAuthoring[] {
                    new GameplaySequenceCueMarkerAuthoring { Time = .2f } };
                var panel = new GameplaySequenceTimelinePanel(() => { });
                panel.Select(asset);
                var drag = GameplaySequenceTimelineModel.BeginDrag(asset, 0);
                drag.Update(.6f, false);
                var flags = BindingFlags.Instance | BindingFlags.NonPublic;
                typeof(GameplaySequenceTimelinePanel).GetField("_drag", flags).SetValue(panel, drag);
                typeof(GameplaySequenceTimelinePanel).GetField("_dragging", flags).SetValue(panel, 0);
                typeof(GameplaySequenceTimelinePanel).GetField("_dragControl", flags).SetValue(panel, 1234);
                GUIUtility.hotControl = 1234;

                panel.CancelDrag(); // Workbench and standalone window call this on focus loss.
                Assert.That(asset.Markers[0].Time, Is.EqualTo(.2f));
                Assert.That(GUIUtility.hotControl, Is.Zero);
                Assert.That(typeof(GameplaySequenceTimelinePanel).GetField("_drag", flags).GetValue(panel), Is.Null);
                Assert.That(typeof(GameplaySequenceTimelinePanel).GetField("_dragging", flags).GetValue(panel), Is.EqualTo(-1));
            }
            finally
            {
                GUIUtility.hotControl = previousHotControl;
                UnityEngine.Object.DestroyImmediate(asset);
            }
        }



        [Test]
        public void MarkerOperationsUseSerializedManagedReferencesAndCanUndo()
        {
            var asset = ScriptableObject.CreateInstance<GameplaySequenceDefinitionAsset>();
            string path="Assets/Editor/FRFrameGameplay/TimelineGuard-"+Guid.NewGuid().ToString("N")+".asset";
            try
            {
                AssetDatabase.CreateAsset(asset,path);
                asset.Duration = 1f;
                int index = GameplaySequenceTimelineModel.Add(asset,
                    new GameplaySequenceCueMarkerAuthoring { Time = .25f });
                Assert.That(index, Is.Zero);
                Assert.That(new SerializedObject(asset).FindProperty("Markers").GetArrayElementAtIndex(0)
                    .managedReferenceValue, Is.TypeOf<GameplaySequenceCueMarkerAuthoring>());
                int copy = GameplaySequenceTimelineModel.Duplicate(asset, 0);
                Assert.That(copy, Is.EqualTo(1));
                Assert.That(asset.Markers.Select(m => m.Time), Is.EqualTo(new[] { .25f, .25f }));
                Undo.IncrementCurrentGroup();
                DeleteMarkerAfterAudit(asset, 1);
                Assert.That(asset.Markers, Has.Length.EqualTo(1));
                Undo.PerformUndo();
                Assert.That(asset.Markers, Has.Length.EqualTo(2));
                var panel=new GameplaySequenceTimelinePanel(()=>{});panel.Select(asset);panel.SelectMarker(1);
                Undo.IncrementCurrentGroup();DeleteMarkerAfterAudit(asset,0);panel.Validate();
                Assert.That(panel.SelectedMarker,Is.Zero,"Same-time markers retain exact managed-reference identity when indices shift.");
                Undo.PerformUndo();panel.OnUndo();Assert.That(panel.SelectedMarker,Is.EqualTo(1));

            }
            finally { Undo.ClearUndo(asset);AssetDatabase.DeleteAsset(path); }
        }

        private static void DeleteMarkerAfterAudit(GameplaySequenceDefinitionAsset asset,int index)=>GameplaySequenceTimelineModel.Delete(asset,index);

        [Test]
        public void ValidationReportsInvalidTimeMissingEffectAndUnregisteredCue()
        {
            var asset = ScriptableObject.CreateInstance<GameplaySequenceDefinitionAsset>();
            try
            {
                var serialized = new SerializedObject(asset);
                serialized.FindProperty("definitionId").stringValue = "sequence.test.invalid";
                serialized.ApplyModifiedPropertiesWithoutUndo();
                asset.Duration = .5f;
                asset.Markers = new GameplaySequenceMarkerAuthoring[] {
                    new GameplaySequenceApplyEffectMarkerAuthoring { Time = .6f },
                    new GameplaySequenceCueMarkerAuthoring { Time = 0f,
                        CueTag = new GameplayTagReference("Cue.Test.Unregistered") } };
                var issues = GameplaySequenceTimelineValidation.Check(asset);
                Assert.That(issues.Any(i => i.Contains("Time")), Is.True);
                Assert.That(issues.Any(i => i.Contains("Effect")), Is.True);
                Assert.That(issues.Any(i => i.Contains("Unregistered") || i.Contains("No GameplayReferenceCatalog")), Is.True);
            }
            finally { UnityEngine.Object.DestroyImmediate(asset); }
        }

        [Test]
        public void ReimportedProductionAssetRetainsTimeAndReferencePickerField()
        {
            string path = AssetDatabase.GetAssetPath(Sequence);
            AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceUpdate);
            var reloaded = AssetDatabase.LoadAssetAtPath<GameplaySequenceDefinitionAsset>(path);
            Assert.That(reloaded.Markers.Select(m => m.Time), Is.EqualTo(new[] { 0f, .5f, .5f }));
            var property = new SerializedObject(reloaded).FindProperty("Markers").GetArrayElementAtIndex(0);
            Assert.That(property.FindPropertyRelative("CueTag"), Is.Not.Null);
            Assert.That(property.FindPropertyRelative("CueTag").type, Is.EqualTo(nameof(GameplayTagReference)));
        }
    }
}
