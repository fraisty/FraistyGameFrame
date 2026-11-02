using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using FRFrame.Gameplay.Extensions;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Extensions
{
    public sealed class GameplayExtensionProtocolTests
    {
        private static readonly GameplayAttributeId Speed=new GameplayAttributeId("Test.Speed");
        private sealed class Host : IGameplayAbilityHost,IGameplaySkillHost
        {
            public GameplaySourceIdAllocator Ids=new GameplaySourceIdAllocator();
            public GameplaySourceId OwnerId { get; }
            public GameplaySourceId TargetId=>OwnerId;
            public AttributeSet Attributes { get; }=new AttributeSet();
            public GameplayTagContainer Tags { get; }=new GameplayTagContainer();
            public GameplayEffectContainer Effects { get; }
            public GameplayAbilityContainer Abilities { get; }
            public Host() { OwnerId=Ids.Allocate(); Attributes.Register(Speed,100); Effects=new GameplayEffectContainer(Attributes,Ids,Tags); Abilities=new GameplayAbilityContainer(this,Ids); }
            public GameplayContext Context=>new GameplayContext(OwnerId,new GameplayTargetData(OwnerId));
            public GameplayEffectApplicationResult Apply(GameplayEffectDefinition d)=>Effects.Apply(d,new GameplayEffectApplicationContext(Context.WithSource(Ids.Allocate()),this,GameplaySetByCallerValues.Empty));
            public GameplayAbilityHandle Grant()=>Abilities.Grant(new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),new[]{new GameplayTag("State.Blocked")},Array.Empty<GameplayTag>()));
        }
        [Test]
        public void LeaseCleanupIsExactAcrossOverlapReplaceDispelAndExpiry()
        {
            var host=new Host();
            var stack=new GameplayEffectStackingDefinition(new GameplayTag("Stack.Test"),GameplayEffectStackAggregation.AggregateByTarget,2,
                GameplayEffectStackDurationRefreshPolicy.NeverRefresh,GameplayEffectStackPeriodResetPolicy.NeverReset,GameplayEffectStackOverflowPolicy.Reject);
            var definition=new GameplayEffectDefinition(GameplayEffectDurationPolicy.Infinite,0,0,
                new[]{new GameplayEffectAttributeModifierDefinition(Speed,GameplayModifierOperation.Add,10)},Array.Empty<GameplayTag>(),Array.Empty<IGameplayEffectExecution>(),stack);
            var a=host.Apply(definition); var b=host.Apply(definition);
            Assert.That(GameplayEffectLease.TryCapture(host.Effects,a,out var leaseA),Is.True);
            Assert.That(GameplayEffectLease.TryCapture(host.Effects,b,out var leaseB),Is.True);
            Assert.That(GameplayEffectLease.TryCapture(host.Effects,host.Apply(definition),out _),Is.False);
            var setA=new GameplayEffectLeaseSet(); var setB=new GameplayEffectLeaseSet();
            setA.AddOrReplace(host.OwnerId,leaseA); setB.AddOrReplace(host.OwnerId,leaseB);
            Assert.That(host.Attributes.GetCurrentValue(Speed),Is.EqualTo(120));
            setA.Remove(host.OwnerId); leaseA.Dispose();
            Assert.That(host.Attributes.GetCurrentValue(Speed),Is.EqualTo(110));
            Assert.That(host.Effects.LastRemoval,Is.Null,"Removing one shared layer does not remove the other source's entry.");
            host.Effects.Remove(b.ActiveHandle,GameplayEffectRemovalCause.Dispel); setB.Clear(); setB.Clear();
            Assert.That(host.Attributes.GetCurrentValue(Speed),Is.EqualTo(100));
            var duration=new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration,1,Array.Empty<GameplayEffectAttributeModifierDefinition>());
            GameplayEffectLease.TryCapture(host.Effects,host.Apply(duration),out var expiring); host.Effects.Advance(1); expiring.Dispose(); expiring.Dispose();
            var oldHandle=host.Effects.Apply(new GameplayEffectDefinition(GameplayEffectDurationPolicy.Infinite,0,Array.Empty<GameplayEffectAttributeModifierDefinition>()));
            var newHandle=host.Effects.Apply(new GameplayEffectDefinition(GameplayEffectDurationPolicy.Infinite,0,Array.Empty<GameplayEffectAttributeModifierDefinition>()));
            setA.AddOrReplace(host.OwnerId,GameplayEffectLease.OwnEntry(host.Effects,oldHandle)); setA.AddOrReplace(host.OwnerId,GameplayEffectLease.OwnEntry(host.Effects,newHandle));
            Assert.That(host.Effects.IsActive(oldHandle),Is.False); Assert.That(host.Effects.IsActive(newHandle),Is.True); setA.Clear();
            Assert.That(host.Effects.LastRemoval.Cause,Is.EqualTo(GameplayEffectRemovalCause.OwnerCleanup));
        }
        [Test]
        public void AbilityCommandsForwardAndCommitRechecksCurrentContainerGates()
        {
            var host=new Host(); var grant=host.Grant(); var context=host.Context;
            bool Execute(GameplayAbilityCommandKind kind)=>GameplayAbilityCommandExecutor.Execute(host.Abilities,new GameplayAbilityCommand(kind,grant,context));
            Assert.That(Execute(GameplayAbilityCommandKind.Begin),Is.True);
            host.Tags.AddTag(new GameplayTag("State.Blocked"),host.OwnerId);
            Assert.That(Execute(GameplayAbilityCommandKind.Commit),Is.False); Assert.That(host.Abilities.IsActive(grant),Is.False);
            host.Tags.RemoveTagsFromSource(host.OwnerId);
            Assert.That(Execute(GameplayAbilityCommandKind.Begin),Is.True); Assert.That(Execute(GameplayAbilityCommandKind.Commit),Is.True);
            Assert.That(host.Abilities.TryGetActiveContext(grant,out var active),Is.True); Assert.That(active,Is.SameAs(context));
            Assert.That(Execute(GameplayAbilityCommandKind.Cancel),Is.True); Assert.That(Execute(GameplayAbilityCommandKind.Activate),Is.True);
            Assert.That(Execute(GameplayAbilityCommandKind.End),Is.True);
            Assert.That(GameplayAbilityCommandExecutor.Execute(host.Abilities,new GameplayAbilityCommand(GameplayAbilityCommandKind.Activate,default)),Is.False);
        }
        [Test]
        public void SpatialRequestIsImmutableNormalizedAndRejectsAmbiguousDimensions()
        {
            var host=new Host(); var context=host.Context;
            var query=new GameplaySpatialQuery(context,new GameplayPoint(2,3),new GameplayDirection(3,4),GameplaySpatialShape.Sector,5,radius:2,angle:180);
            Assert.That(query.Context,Is.SameAs(context)); Assert.That(query.Direction.X,Is.EqualTo(.6f)); Assert.That(query.Direction.Y,Is.EqualTo(.8f)); Assert.That(query.Origin.X,Is.EqualTo(2));
            Assert.Throws<ArgumentException>(()=>new GameplayDirection(0,0));
            Assert.Throws<ArgumentException>(()=>new GameplaySpatialQuery(context,default,default,GameplaySpatialShape.Circle,1,radius:2));
            Assert.Throws<ArgumentException>(()=>new GameplaySpatialQuery(context,default,new GameplayDirection(1,0),GameplaySpatialShape.Line,1,length:4,width:1,radius:2));
            Assert.Throws<ArgumentException>(()=>new GameplaySpatialQuery(context,default,new GameplayDirection(1,0),GameplaySpatialShape.Sector,1,radius:2,angle:361));
        }
        private sealed class Probe : IGameplaySkillAction
        {
            public int Count; public GameplayContext Context; public GameplayEventData Event;
            public GameplaySkillActionResult Execute(GameplayContext c,GameplayEventData e,IGameplaySkillHost h) { Count++; Context=c; Event=e; return GameplaySkillActionResult.Succeeded; }
        }
        private sealed class Stop : IGameplaySkillAction
        {
            public Action Callback;
            public GameplaySkillActionResult Execute(GameplayContext c,GameplayEventData e,IGameplaySkillHost h) { Callback(); return GameplaySkillActionResult.Succeeded; }
        }
        [Test]
        public void ChildBindingCarriesRichIdentityAndStopsCallbacksAndPendingActionsOnDispose()
        {
            var host=new Host(); var key=new GameplayTag("Data.Test.Power"); var combo=new GameplayTag("Data.Test.Combo"); var crit=new GameplayTag("Data.Test.Crit");
            var original=host.Context.WithValues(GameplayContextValues.Empty.WithValue(key,40).WithValue(combo,2));
            var child=new GameplayChildRuleBinding(original,host.Ids.Allocate(),host); var target=new GameplayTargetData(host.Ids.Allocate(),new GameplayPoint(2,4));
            var probe=new Probe(); var flow=new GameplayContinuationDefinition(new[]{probe});
            var result=child.Signal(new GameplayTag("Event.Test.Hit"),target,flow,magnitude:3);
            Assert.That(result.Result,Is.EqualTo(GameplaySkillActionResult.Succeeded)); Assert.That(probe.Context.InstigatorId,Is.EqualTo(original.InstigatorId));
            Assert.That(probe.Context.SourceId,Is.EqualTo(child.BaseContext.SourceId)); Assert.That(probe.Context.TargetData,Is.SameAs(target));
            Assert.That(probe.Context.Values,Is.SameAs(original.Values)); Assert.That(probe.Event.Context,Is.SameAs(probe.Context));
            child.Signal(new GameplayTag("Event.Test.Hit"),target,flow,Array.Empty<KeyValuePair<GameplayTag,float>>());
            Assert.That(probe.Context.Values,Is.SameAs(original.Values));
            var overrides=new[]{new KeyValuePair<GameplayTag,float>(crit,1),new KeyValuePair<GameplayTag,float>(combo,3)};
            result=child.Signal(new GameplayTag("Event.Test.Hit"),target,flow,overrides,magnitude:3);
            Assert.That(probe.Context.Values.GetRequiredValue(key),Is.EqualTo(40));
            Assert.That(probe.Context.Values.GetRequiredValue(crit),Is.EqualTo(1));
            Assert.That(probe.Context.Values.GetRequiredValue(combo),Is.EqualTo(3));
            Assert.That(child.BaseContext.Values,Is.SameAs(original.Values));
            Assert.That(child.BaseContext.Values.GetRequiredValue(key),Is.EqualTo(40));
            Assert.That(child.BaseContext.Values.GetRequiredValue(combo),Is.EqualTo(2));
            Assert.That(child.BaseContext.Values.TryGetValue(crit,out _),Is.False);
            Assert.That(probe.Context.InstigatorId,Is.EqualTo(original.InstigatorId));
            Assert.That(probe.Context.SourceId,Is.EqualTo(child.BaseContext.SourceId));
            Assert.That(probe.Context.TargetData,Is.SameAs(target));
            Assert.That(probe.Event.Context,Is.SameAs(probe.Context));
            Assert.That(result.Context,Is.SameAs(probe.Event.Context));
            Assert.Throws<ArgumentException>(()=>child.Signal(new GameplayTag("Event.Test.Hit"),target,flow,new[]{new KeyValuePair<GameplayTag,float>(default,1)}));
            Assert.Throws<ArgumentException>(()=>child.Signal(new GameplayTag("Event.Test.Hit"),target,flow,new[]{new KeyValuePair<GameplayTag,float>(crit,float.NaN)}));
            var grant=host.Grant(); host.Abilities.TryActivate(grant,original); Assert.That(host.Abilities.TryOwnLifetime(grant,child),Is.True);
            var stopping=new GameplayContinuationDefinition(new IGameplaySkillAction[]{new Stop { Callback=()=>host.Abilities.Cancel(grant) },probe});
            Assert.That(child.Signal(new GameplayTag("Event.Test.Hit"),target,stopping).Result,Is.EqualTo(GameplaySkillActionResult.Rejected));
            child.Dispose(); Assert.That(child.Signal(new GameplayTag("Event.Test.Hit"),target,flow,overrides).Result,Is.EqualTo(GameplaySkillActionResult.Rejected)); Assert.That(probe.Count,Is.EqualTo(3));
        }
        private sealed class Driver : IGameplayDisplacementDriver,IDisposable
        { public GameplayDisplacementRequest Received; public IDisposable Start(GameplayDisplacementRequest r) { Received=r; return this; } public void Dispose() { } }
        [Test]
        public void DisplacementHasOneRateModelAndDriverReceivesExactSnapshot()
        {
            var host=new Host(); var driver=new Driver();
            var request=new GameplayDisplacementRequest(host.Context,host.OwnerId,GameplayDisplacementMode.Directional,3,2,new GameplayDirection(1,0));
            driver.Start(request); Assert.That(driver.Received,Is.SameAs(request)); Assert.That(request.EffectiveSourceId,Is.EqualTo(host.OwnerId));
            Assert.Throws<ArgumentException>(()=>new GameplayDisplacementRequest(host.Context,host.OwnerId,GameplayDisplacementMode.Directional,3,2));
            Assert.Throws<ArgumentException>(()=>new GameplayDisplacementRequest(host.Context,host.OwnerId,GameplayDisplacementMode.PullToSource,3,2,new GameplayDirection(1,0)));
            Assert.Throws<ArgumentOutOfRangeException>(()=>new GameplayDisplacementRequest(host.Context,host.OwnerId,GameplayDisplacementMode.PushFromSource,3,0));
        }
    }
}
