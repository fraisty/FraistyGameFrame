using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Skill
{
    /// <summary>Guards immutable carry, exactly-one branch and nested cancellation; child visuals are verified in the permanent Lab.</summary>
    public sealed class GameplaySkillRuleFlowTests
    {
        private static readonly GameplayTag Key=new GameplayTag("Data.Test.Flow");
        private static readonly GameplayTag Signal=new GameplayTag("Event.Test.Flow");
        private sealed class Host : IGameplaySkillHost,IGameplaySkillTraceSink
        {
            public readonly List<GameplaySkillTraceRecord> Trace=new List<GameplaySkillTraceRecord>();
            public bool ThrowTrace;
            public void RecordSkillTrace(GameplaySkillTraceRecord record){if(ThrowTrace)throw new InvalidOperationException("diagnostic failure");Trace.Add(record);}
            public GameplaySourceIdAllocator Ids { get; }=new GameplaySourceIdAllocator();
            public GameplaySourceId OwnerId { get; }
            public GameplaySourceId TargetId=>OwnerId;
            public AttributeSet Attributes { get; }=new AttributeSet();
            public GameplayTagContainer Tags { get; }=new GameplayTagContainer();
            public GameplayEffectContainer Effects { get; }
            public Host() { OwnerId=Ids.Allocate(); Effects=new GameplayEffectContainer(Attributes,Ids,Tags); }
            public GameplayContext Context()=>new GameplayContext(OwnerId,new GameplayTargetData(OwnerId));
            public GameplayEventData Event()=>new GameplayEventData(Signal,OwnerId,OwnerId,7);
        }
        private sealed class Legacy : IGameplaySkillAction
        {
            private readonly Func<GameplayContext,GameplaySkillActionResult> _run;
            public Legacy(Func<GameplayContext,GameplaySkillActionResult> run) { _run=run; }
            public GameplaySkillActionResult Execute(GameplayContext context,GameplayEventData evt,IGameplaySkillHost host)=>_run(context);
        }
        private sealed class ContextAction : IGameplaySkillAction,IGameplaySkillContextAction
        {
            private readonly Func<GameplayContext,GameplaySkillActionExecution> _run;
            public ContextAction(Func<GameplayContext,GameplaySkillActionExecution> run) { _run=run; }
            public GameplaySkillActionResult Execute(GameplayContext context,GameplayEventData evt,IGameplaySkillHost host)=>throw new AssertionException("Context interface was not used");
            public GameplaySkillActionExecution ExecuteWithContext(GameplayContext context,GameplayEventData evt,IGameplaySkillHost host)=>_run(context);
        }
        private static Legacy Read(Action<GameplayContext> action)=>new Legacy(c=> { action(c); return GameplaySkillActionResult.Succeeded; });

        [Test]
        public void LegacyRunnerAndSkillKeepOrderContextAndOwnerEventSemantics()
        {
            var h=new Host(); var order=new List<int>(); GameplayContext first=null;
            var actions=new IGameplaySkillAction[] { Read(c=> { first=c; order.Add(1); }),Read(c=> { Assert.That(c,Is.SameAs(first)); order.Add(2); }) };
            var input=h.Context(); var execution=GameplaySkillActionRunner.Execute(actions,input,h.Event(),h);
            Assert.That(execution.Context,Is.SameAs(input)); Assert.That(order,Is.EqualTo(new[]{1,2})); order.Clear();
            var events=new GameplayEventDispatcher(); var definition=new GameplaySkillDefinition(new GameplaySkillEventTriggerDefinition(Signal),Array.Empty<IGameplaySkillCondition>(),new GameplaySkillSelfTarget(),actions,false);
            using(var skill=new GameplaySkillRuntime(definition,h,events))
            {
                events.Publish(new GameplayEventData(Signal,input.WithSource(h.Ids.Allocate()),9));
                Assert.That(order,Is.EqualTo(new[]{1,2})); Assert.That(skill.LastExecutionContext,Is.SameAs(first));
                Assert.That(skill.LastExecutionContext.SourceId,Is.EqualTo(h.OwnerId),"Ordinary Skill still constructs its owner context.");
                Assert.That(skill.LastExecutionContext.Values.GetRequiredValue(GameplayContextValues.TriggerMagnitudeKey),Is.EqualTo(9));
            }
        }
        [Test]
        public void ContextOutputFeedsNextActionAndRejectedStopsWithoutChangingInput()
        {
            var h=new Host(); var input=h.Context(); var child=h.Ids.Allocate(); var target=new GameplayTargetData(h.Ids.Allocate()); int calls=0;
            var actions=new IGameplaySkillAction[] {
                new ContextAction(c=>new GameplaySkillActionExecution(GameplaySkillActionResult.Succeeded,c.WithSource(child).WithTarget(target).WithValues(c.Values.WithValue(Key,42)))),
                Read(c=> { calls++; Assert.That(c.SourceId,Is.EqualTo(child)); Assert.That(c.TargetData,Is.SameAs(target)); Assert.That(c.Values.GetRequiredValue(Key),Is.EqualTo(42)); }),
                new ContextAction(c=>new GameplaySkillActionExecution(GameplaySkillActionResult.Rejected,c)),Read(c=>calls++) };
            var result=new GameplayContinuationDefinition(actions).Execute(input,h.Event(),h);
            Assert.That(result.Result,Is.EqualTo(GameplaySkillActionResult.Rejected)); Assert.That(calls,Is.EqualTo(1));
            Assert.That(input.SourceId.IsValid,Is.False); Assert.That(input.Values.TryGetValue(Key,out _),Is.False);
            Assert.That(h.Trace.Count,Is.EqualTo(6));Assert.That(h.Trace[1].Context.SourceId,Is.EqualTo(child));Assert.That(h.Trace[5].Stage,Is.EqualTo(GameplaySkillTraceStage.ActionRejected));
            h.ThrowTrace=true;
            Assert.That(new GameplayContinuationDefinition(actions).Execute(input,h.Event(),h).Result,Is.EqualTo(GameplaySkillActionResult.Rejected),"Diagnostic failures never change ordered execution or rejection.");
            var ruleError=new InvalidOperationException("actual rule error");
            Assert.That(Assert.Throws<InvalidOperationException>(()=>GameplaySkillActionRunner.Execute(new IGameplaySkillAction[]{new Legacy(c=>throw ruleError)},input,h.Event(),h)),Is.SameAs(ruleError));
            Assert.Throws<InvalidOperationException>(()=>GameplaySkillActionRunner.Execute(new IGameplaySkillAction[]{new ContextAction(c=>default)},input,h.Event(),h));
            var events=new GameplayEventDispatcher(); var skillDefinition=new GameplaySkillDefinition(new GameplaySkillEventTriggerDefinition(Signal),Array.Empty<IGameplaySkillCondition>(),new GameplaySkillSelfTarget(),new IGameplaySkillAction[]{new GameplaySkillSetContextValueAction(Key,25)},false);
            using(var skill=new GameplaySkillRuntime(skillDefinition,h,events)) { events.Publish(h.Event()); Assert.That(skill.LastExecutionContext.Values.GetRequiredValue(Key),Is.EqualTo(25)); }
        }
        [TestCase(2,20)][TestCase(3,30)]
        public void BranchChoosesExactlyOneAndMergeReadsItsOutput(int inputValue,int expected)
        {
            var h=new Host(); var context=h.Context().WithValues(GameplayContextValues.Empty.WithValue(Key,inputValue)); int yes=0,no=0,merge=0;
            var branch=new GameplaySkillBranchAction(new GameplaySkillContextValueCondition(Key,GameplaySkillContextComparison.Equal,2),
                new IGameplaySkillAction[]{Read(c=>yes++),new GameplaySkillSetContextValueAction(Key,20)},
                new IGameplaySkillAction[]{Read(c=>no++),new GameplaySkillSetContextValueAction(Key,30)});
            var result=GameplaySkillActionRunner.Execute(new IGameplaySkillAction[]{branch,Read(c=> { merge++; Assert.That(c.Values.GetRequiredValue(Key),Is.EqualTo(expected)); })},context,h.Event(),h);
            Assert.That(yes,Is.EqualTo(inputValue==2?1:0)); Assert.That(no,Is.EqualTo(inputValue==3?1:0)); Assert.That(merge,Is.EqualTo(1));
            Assert.That(result.Context.Values.GetRequiredValue(Key),Is.EqualTo(expected)); Assert.That(context.Values.GetRequiredValue(Key),Is.EqualTo(inputValue));
            var empty=new GameplaySkillBranchAction(new GameplaySkillContextValueCondition(Key,GameplaySkillContextComparison.Equal,inputValue),Array.Empty<IGameplaySkillAction>(),Array.Empty<IGameplaySkillAction>());
            Assert.That(empty.ExecuteWithContext(context,h.Event(),h).Context,Is.SameAs(context));
        }
        [Test]
        public void RichEventAndDerivationPreserveIdentityGeometryAndImmutableScalars()
        {
            var h=new Host(); var target=h.Ids.Allocate(); var original=h.Context().WithValues(GameplayContextValues.Empty.WithValue(Key,5));
            var data=new GameplayTargetData(target,new GameplayPoint(4,2),new GameplayArea(new GameplayPoint(4,2),3),new[]{target,h.OwnerId});
            var child=original.WithSource(h.Ids.Allocate()).WithTarget(data).WithValues(original.Values.WithValue(Key,9));
            var events=new GameplayEventDispatcher(); GameplayEventData observed=default; events.Subscribe(Signal,e=>observed=e);
            events.Publish(new GameplayEventData(Signal,child,10));
            Assert.That(observed.Context,Is.SameAs(child)); Assert.That(observed.InstigatorId,Is.EqualTo(original.InstigatorId)); Assert.That(observed.TargetId,Is.EqualTo(target));
            Assert.That(observed.Context.TargetData,Is.SameAs(data)); Assert.That(observed.Context.TargetData.Point.X,Is.EqualTo(4)); Assert.That(data.Targets.Count,Is.EqualTo(2));
            Assert.That(observed.Context.Values.GetRequiredValue(Key),Is.EqualTo(9)); Assert.That(original.Values.GetRequiredValue(Key),Is.EqualTo(5));
            Assert.That(child.WithSource(default).EffectiveSourceId,Is.EqualTo(original.InstigatorId)); Assert.That(h.Event().HasContext,Is.False);
        }
        [TestCase("Cancel")][TestCase("Clear")]
        public void SkillStepDataflowStaysLocalAndNestedBranchHonorsCancellation(string termination)
        {
            var h=new Host(); var sequences=new GameplaySequenceContainer(); var root=h.Context().WithValues(GameplayContextValues.Empty.WithValue(Key,1));
            GameplayContext first=null,second=null;
            Func<float,IGameplaySkillAction[],GameplaySequenceSkillStepMarkerDefinition> step=(time,actions)=>new GameplaySequenceSkillStepMarkerDefinition(time,Array.Empty<IGameplaySkillCondition>(),new GameplaySkillSelfTarget(),actions);
            var definition=new GameplaySequenceDefinition(2,new GameplaySequenceMarkerDefinition[]{
                step(1,new IGameplaySkillAction[]{new GameplaySkillSetContextValueAction(Key,8),Read(c=>first=c)}),step(2,new IGameplaySkillAction[]{Read(c=>second=c)}) });
            sequences.StartSkill(definition,root,h.Event(),h); sequences.Advance(2);
            Assert.That(first.Values.GetRequiredValue(Key),Is.EqualTo(8)); Assert.That(second.Values.GetRequiredValue(Key),Is.EqualTo(1)); Assert.That(root.Values.GetRequiredValue(Key),Is.EqualTo(1));
            int pending=0; GameplaySequenceHandle handle=default; var branch=new GameplaySkillBranchAction(new GameplaySkillContextValueCondition(Key,GameplaySkillContextComparison.Equal,1),
                new IGameplaySkillAction[]{Read(c=> { if(termination=="Cancel") sequences.Cancel(handle); else sequences.Clear(); }),Read(c=>pending++)},Array.Empty<IGameplaySkillAction>());
            handle=sequences.StartSkill(new GameplaySequenceDefinition(2,new GameplaySequenceMarkerDefinition[]{step(1,new IGameplaySkillAction[]{branch,Read(c=>pending++)}),step(2,new IGameplaySkillAction[]{Read(c=>pending++)})}),root,h.Event(),h);
            sequences.Advance(2); Assert.That(pending,Is.Zero); Assert.That(sequences.ActiveCount,Is.Zero);
        }
    }
}
