using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Standard;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Unity;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Playables;

namespace FRFrame.Gameplay.Tests.UnityIntegration
{
    public sealed class GameplayAnimationOwnershipTests
    {
        [Test]
        public void CommittedEffectCuesSurviveSameFrameRemovalWithoutCastOrReplayAuthority()
        {
            var subject = new GameObject("Effect Cue target");
            var prefab = new GameObject("Effect Cue prefab");
            var asset = ScriptableObject.CreateInstance<GameplayPresentationCueAsset>();
            try
            {
                asset.Prefab = prefab;
                asset.Origin = GameplayPresentationCueAsset.PositionSource.PrimaryTarget;
                using (var serialized = new UnityEditor.SerializedObject(asset))
                {
                    serialized.FindProperty("definitionId").stringValue = "Tests.EffectLifecycleCue";
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                var build = new GameplayAuthoringBuildContext();
                var cue = new GameplayPlayPresentationCueActionAuthoring { Cue = asset }.Build(build);
                var probe = new EffectCueProbe();
                var actions = new GameplayEffectLifecycleActions(new[] { cue, probe });
                var overflow = new GameplayEffectStackingDefinition(new GameplayTag("Effect.Test.LifecycleCue"),
                    GameplayEffectStackAggregation.AggregateByInstigator, 1, GameplayEffectStackDurationRefreshPolicy.NeverRefresh,
                    GameplayEffectStackPeriodResetPolicy.NeverReset, GameplayEffectStackOverflowPolicy.ExecuteOverflow,
                    overflowExecutions: new[] { (IGameplayEffectOverflowExecution)actions, new GameplayRemoveCurrentOnOverflowAuthoring().Build(build) });
                var effect = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 4, 0,
                    Array.Empty<GameplayEffectAttributeModifierDefinition>(), Array.Empty<GameplayTag>(), Array.Empty<IGameplayEffectExecution>(),
                    overflow, onApplied: actions);
                using (var world = new GameplayStandardWorld())
                {
                    var caster = world.CreateActor(default, 1);
                    var target = world.CreateActor(default, 2);
                    var controller = new GameplayUnityAnimationController(target, subject);
                    target.Presentation = controller;
                    var context = new GameplayContext(caster.Id, new GameplayTargetData(target.Id, target.Position));
                    Assert.That(controller.TryPlayCue(asset.Build(build), context), Is.False);
                    caster.ApplyEffect(context, effect);
                    caster.ApplyEffect(context, effect);
                    Assert.That(target.Effects.GetActiveEntriesSnapshot(), Is.Empty);
                    Assert.That(probe.Kinds, Is.EqualTo(new[] { GameplayEffectLifecycleKind.Applied, GameplayEffectLifecycleKind.Overflow }));
                    Assert.That(probe.Last.EffectLifecycle.IsExecuting, Is.False);
                    Assert.That(cue.Execute(context, probe.Last, target), Is.EqualTo(GameplaySkillActionResult.Rejected));
                    world.Advance(.01f);
                    Assert.That(controller.ActiveCueCount, Is.EqualTo(2), "Both committed events play once despite the removed handle.");
                    world.Advance(.01f);
                    Assert.That(controller.ActiveCueCount, Is.EqualTo(2));
                    world.Advance(1.1f);
                    Assert.That(controller.ActiveCueCount, Is.Zero);
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(asset);
                UnityEngine.Object.DestroyImmediate(prefab);
                UnityEngine.Object.DestroyImmediate(subject);
            }
        }

        private sealed class EffectCueProbe : IGameplaySkillAction
        {
            internal readonly List<GameplayEffectLifecycleKind> Kinds = new List<GameplayEffectLifecycleKind>();
            internal GameplayEventData Last;
            public GameplaySkillActionResult Execute(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host)
            {
                Last = triggerEvent;
                Kinds.Add(triggerEvent.EffectLifecycle.Kind);
                return GameplaySkillActionResult.Succeeded;
            }
        }

        /// <summary>Protects Timeline preview against ignoring the formal speed/context expression.</summary>
        [Test]
        public void PreviewSpeedUsesCapturedInputsAndSamplesActualAnimatorTime()
        {
            var original=UnityEditor.AssetDatabase.LoadAssetAtPath<GameplayUnityAnimationAsset>("Assets/GameRes/GameplayValidation/FRFrameSkillLab/Resources/FRFrameSkillLab/AnimationAnimator.asset");
            var source=UnityEngine.Object.Instantiate(original);
            try
            {
                source.Speed=new GameplayFormulaFloatValueAuthoring{Expression="Context.Data.Standard.Critical * Level"};
                using(var preview=new FRFrame.Gameplay.Unity.Editor.GameplayAnimationPreviewSession())
                {
                    preview.SetValueContext(null,null,GameplayContextValues.Empty.WithValue(GameplayDamageExecution.CriticalKey,2));
                    preview.UpdateContext(new FRFrame.Gameplay.Editor.SkillAuthoring.GameplaySkillPreviewContext(null,null,null,presentationSource:source,presentationStartSeconds:.3f));
                    preview.Seek(.55f);Assert.That(preview.Duration,Is.EqualTo(.3f+source.Clip.length/2).Within(.0001f));
                    var animator=preview.PreviewSubject.GetComponentInChildren<Animator>();
                    Assert.That(animator.GetCurrentAnimatorStateInfo(source.Layer).normalizedTime,Is.EqualTo(.5f/source.Clip.length).Within(.0001f));
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(source);}
            var weighted=UnityEngine.Object.Instantiate(UnityEditor.AssetDatabase.LoadAssetAtPath<GameplayUnityAnimationAsset>("Assets/GameRes/GameplayValidation/FRFrameSkillLab/Resources/FRFrameSkillLab/AnimationAnimatorWeighted.asset"));
            try
            {
                foreach(var variant in weighted.Variants)variant.MinimumLevel=1;
                using(var preview=new FRFrame.Gameplay.Unity.Editor.GameplayAnimationPreviewSession())
                {
                    preview.SetValueContext(null,null,GameplayContextValues.Empty.WithValue(GameplayAnimationVariantSelector.RollKey,.2499f));
                    preview.UpdateContext(new FRFrame.Gameplay.Editor.SkillAuthoring.GameplaySkillPreviewContext(null,null,null,presentationSource:weighted));preview.Seek(.5f);
                    var animator=preview.PreviewSubject.GetComponentInChildren<Animator>();
                    Assert.That(animator.GetCurrentAnimatorStateInfo(0).fullPathHash,Is.EqualTo(Animator.StringToHash("Base Layer.Scale")));
                    preview.SetValueContext(null,null,GameplayContextValues.Empty.WithValue(GameplayAnimationVariantSelector.RollKey,.25f));preview.Seek(.5f);
                    animator=preview.PreviewSubject.GetComponentInChildren<Animator>();
                    Assert.That(animator.GetCurrentAnimatorStateInfo(0).fullPathHash,Is.EqualTo(Animator.StringToHash("Base Layer.Turn")),"Exact 1:3 weight boundary selects the second real state.");
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(weighted);}
        }
        [Test]
        public void CubismPreviewUsesSupportedCameraAndKeepsSubjectInDepthRange()
        {
            var source=UnityEditor.AssetDatabase.LoadAssetAtPath<GameplayAnimationAsset>("Assets/GameRes/GameplayValidation/FRFrameSkillLab/Resources/FRFrameSkillLab/AnimationLive2D.asset");
            using(var preview=new FRFrame.Gameplay.Unity.Editor.GameplayAnimationPreviewSession())
            {
                preview.UpdateContext(new FRFrame.Gameplay.Editor.SkillAuthoring.GameplaySkillPreviewContext(null,null,null,presentationSource:source));
                preview.Seek(.5f);
                var camera=preview.PreviewCamera;
                Assert.That(camera,Is.Not.Null);Assert.That(preview.Duration,Is.GreaterThan(0));
                Assert.That(camera.cameraType,Is.EqualTo(CameraType.Game),"Installed Cubism URP skips CameraType.Preview.");
                float subjectDepth=camera.WorldToViewportPoint(Vector3.zero).z;
                Assert.That(camera.nearClipPlane,Is.LessThan(subjectDepth-1));
                Assert.That(camera.farClipPlane,Is.GreaterThan(subjectDepth+1),"A far plane at the subject centre clipped most Cubism parts.");
            }
        }
        [Test]
        public void PlayablesSamplesRealControllerSubjectAndReleasesItsGraph()
        {
            var source=UnityEditor.AssetDatabase.LoadAssetAtPath<GameplayUnityAnimationAsset>("Assets/GameRes/GameplayValidation/FRFrameSkillLab/Resources/FRFrameSkillLab/AnimationPlayables.asset");
            Assert.That(source,Is.Not.Null,"Permanent animation Lab content is required.");
            var subject=source.CreatePreviewSubject();IGameplayAnimationPlayer player=null;
            try
            {
                var animator=subject.GetComponentInChildren<Animator>();
                var controller=animator.runtimeAnimatorController;bool enabled=animator.enabled;
                player=source.Build(new GameplayAuthoringBuildContext()).Create(subject,new GameplayContext(new GameplaySourceId(1),new GameplayTargetData(default)));
                int events=0;player.AnimationEvent+=name=>events++;
                player.Seek(0);player.Seek(.5f);
                Assert.That(subject.transform.localScale.x,Is.GreaterThan(1.1f));
                Assert.That(events,Is.EqualTo(1),"The 8f event fires once across the sampled interval.");
                player.Dispose();player=null;
                Assert.That(animator.enabled,Is.EqualTo(enabled));Assert.That(animator.runtimeAnimatorController,Is.SameAs(controller));
                // The ordinary controller graph may remain bound. A manual clip graph must not remain.
                var graph=animator.playableGraph;
                if(graph.IsValid())for(int i=0;i<graph.GetRootPlayableCount();i++)
                    Assert.That(graph.GetRootPlayable(i).GetPlayableType(),Is.Not.EqualTo(typeof(UnityEngine.Animations.AnimationClipPlayable)));
            }
            finally{player?.Dispose();UnityEngine.Object.DestroyImmediate(subject);}
        }
        /// <summary>Guards priority rejection before allocation, replay cleanup, absolute loop event
        /// time and exactly-once cancellation. Visual clip correctness is verified in the permanent Lab.</summary>
        [Test]
        public void AnimationPriorityReplayAndCancellationOwnOnlyTheirCast()
        {
            var subject=new GameObject("Animation ownership guard");
            try
            {
                using(var world=new GameplayStandardWorld())
                {
                    var actor=world.CreateActor(default,1);
                    var signal=new GameplayTag("Event.Test.Animation.Cast");
                    var rules=new GameplaySkillDefinition(new GameplaySkillEventTriggerDefinition(signal),Array.Empty<IGameplaySkillCondition>(),new GameplaySkillSelfTarget(),new[]{(IGameplaySkillAction)new Noop()},false);
                    var skill=actor.InstallDocument(new GameplaySkillDocumentDefinition(1,new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>()),rules,signal));
                    Assert.That(skill.TryCast(),Is.True);
                    var target=world.CreateActor(new GameplayPoint(2,0),2);
                    var cast=skill.LastCastContext.WithTarget(new GameplayTargetData(target.Id));
                    var controller=new GameplayUnityAnimationController(actor,subject);actor.Presentation=controller;
                    var first=new Player();var replay=new Player();int rejectedFactory=0;
                    var high=new GameplayAnimationDefinition("Body",10,new GameplayConstantFloatValue(1),(s,c)=>first);
                    var low=new GameplayAnimationDefinition("Body",1,new GameplayConstantFloatValue(1),(s,c)=>{rejectedFactory++;return new Player();});
                    Assert.That(controller.TryPlay(high,cast),Is.True);
                    Assert.That(controller.TryPlay(low,cast),Is.False);
                    Assert.That(controller.LastAnimationRejection,Does.Contain("PriorityDenied"));
                    Assert.That(controller.LastAnimationRejection,Does.Contain("channel=Body"));
                    Assert.That(rejectedFactory,Is.Zero);
                    world.Advance(.25f);Assert.That(first.LastSeek,Is.EqualTo(.25f));
                    Assert.That(controller.TryPlay(new GameplayAnimationDefinition("Body",10,new GameplayConstantFloatValue(1),(s,c)=>replay),cast),Is.True);
                    Assert.That(first.Disposals,Is.EqualTo(1));Assert.That(replay.LastSeek,Is.Zero);
                    world.Advance(2.5f);Assert.That(replay.LastSeek,Is.EqualTo(2.5f),"Loop backends need absolute time for crossing events.");
                    skill.Cancel();Assert.That(replay.Disposals,Is.EqualTo(1));
                    Assert.That(controller.TryPlay(low,cast),Is.False);
                    Assert.That(controller.LastAnimationRejection,Does.Contain("ActiveGrantMissing"));
                    world.Advance(1);controller.Dispose();Assert.That(replay.Disposals,Is.EqualTo(1));
                    Assert.That(actor.Abilities.GetGrantedEntriesSnapshot()[0].OwnedLifetimesCount,Is.Zero);
                    Assert.That(skill.TryCast(),Is.True);cast=skill.LastCastContext;
                    var finite=new Player{Repeats=false};var queued=new Player{Repeats=false};int queuedFactories=0;
                    controller=new GameplayUnityAnimationController(actor,subject);actor.Presentation=controller;
                    var random=new PresentationRandom();controller.AnimationRandom=random;
                    Assert.That(controller.TryPlay(new GameplayAnimationDefinition("Body",10,new GameplayConstantFloatValue(1),(s,c)=>finite),cast),Is.True);
                    float capturedRoll=-1;
                    Assert.That(controller.TryQueue(new GameplayAnimationDefinition("Body",10,new GameplayConstantFloatValue(1),(s,c)=>{queuedFactories++;c.Values.TryGetValue(GameplayAnimationVariantSelector.RollKey,out capturedRoll);return queued;},captureVariantRoll:true),cast),Is.True);
                    Assert.That(random.Calls,Is.EqualTo(1));
                    Assert.That(queuedFactories,Is.Zero);Assert.That(controller.QueuedCount,Is.EqualTo(1));
                    world.Advance(1);Assert.That(queuedFactories,Is.EqualTo(1));Assert.That(queued.LastSeek,Is.Zero,"A promoted request cannot consume the previous clip's delta.");
                    Assert.That(capturedRoll,Is.EqualTo(.75f));Assert.That(random.Calls,Is.EqualTo(1),"Promotion/SDK sampling must not redraw queued variants.");
                    world.Advance(.25f);Assert.That(queued.LastSeek,Is.EqualTo(.25f));
                    controller.TryQueue(low,cast);skill.Cancel();Assert.That(controller.QueuedCount,Is.Zero);Assert.That(queued.Disposals,Is.EqualTo(1));
                }
            }
            finally{UnityEngine.Object.DestroyImmediate(subject);}
        }
        /// <summary>Prevents cross-channel physical overwrites and queue starvation while allowing
        /// genuinely separate native tracks/layers to progress together.</summary>
        [Test]
        public void PhysicalSlotsArbitrateAcrossChannelsAndPromoteFifo()
        {
            var subject=new GameObject("Physical slot guard");
            try{using(var world=new GameplayStandardWorld()){
                var actor=world.CreateActor(default,1);
                var handle=actor.Abilities.Grant(new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>()));
                var cast=new GameplayContext(actor.Id,new GameplayTargetData(actor.Id));Assert.That(actor.Abilities.TryActivate(handle,cast),Is.True);
                Assert.That(actor.Abilities.TryCreateExecutionContext(handle,new GameplayTargetData(actor.Id),out cast),Is.True);
                var controller=new GameplayUnityAnimationController(actor,subject);actor.Presentation=controller;
                var high=new Player{Repeats=false};var parallel=new Player();var first=new Player{Repeats=false};var second=new Player{Repeats=false};int denied=0;
                Func<string,int,int,Player,GameplayAnimationDefinition> source=(channel,priority,slot,player)=>new GameplayAnimationDefinition(channel,priority,new GameplayConstantFloatValue(1),(s,c)=>player,
                    writes:s=>new[]{new GameplayAnimationWriteTarget(s,"Track",slot)});
                Assert.That(controller.TryPlay(source("High",100,0,high),cast),Is.True);
                Assert.That(controller.TryPlay(new GameplayAnimationDefinition("Low",1,new GameplayConstantFloatValue(1),(s,c)=>{denied++;return new Player();},writes:s=>new[]{new GameplayAnimationWriteTarget(s,"Track",0)}),cast),Is.False);
                Assert.That(controller.LastAnimationRejection,Does.Contain("PhysicalPriorityDenied"));Assert.That(denied,Is.Zero);
                Assert.That(controller.TryPlay(source("Parallel",1,1,parallel),cast),Is.True);
                Assert.That(controller.TryQueue(source("First",1,0,first),cast),Is.True);
                Assert.That(controller.TryQueue(source("Second",1,0,second),cast),Is.True);
                controller.Advance(1);Assert.That(high.Disposals,Is.EqualTo(1));Assert.That(first.LastSeek,Is.Zero);Assert.That(controller.QueuedCount,Is.EqualTo(1));
                controller.Advance(1);Assert.That(first.Disposals,Is.EqualTo(1));Assert.That(second.LastSeek,Is.Zero);Assert.That(parallel.Disposals,Is.Zero);
                actor.Abilities.Cancel(handle);Assert.That(second.Disposals,Is.EqualTo(1));Assert.That(parallel.Disposals,Is.EqualTo(1));Assert.That(controller.QueuedCount,Is.Zero);
            }}finally{UnityEngine.Object.DestroyImmediate(subject);}
        }
        private sealed class Noop:IGameplaySkillAction
        {public GameplaySkillActionResult Execute(GameplayContext c,GameplayEventData e,IGameplaySkillHost h)=>GameplaySkillActionResult.Succeeded;}
        private sealed class PresentationRandom:IGameplayRandomSource
        {internal int Calls;public double NextUnitInterval(){Calls++;return .75;}}
        private sealed class Player:IGameplayAnimationPlayer
        {
            public float Duration=>1;
            public bool Repeats=true;
            public bool Loop=>Repeats;
            public bool IsInterrupted=>false;
            public float LastSeek;
            public int Disposals;
            public event Action<string> AnimationEvent {add{}remove{}}
            public void Seek(float seconds)=>LastSeek=seconds;
            public void Dispose()=>Disposals++;
        }
    }
}
