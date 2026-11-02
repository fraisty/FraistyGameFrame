using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using NUnit.Framework;
namespace FRFrame.Tests.Gameplay.Effect
{
 public sealed class GameplayEffectCompletionTests
 {
  static readonly GameplayTag Active=new GameplayTag("Test.Completion.Active");
  static readonly GameplayAttributeId Value=new GameplayAttributeId("Test.Completion.Value");
  sealed class Completion:IGameplayEffectCompletionExecution { readonly Action<GameplayEffectCompletionContext> action; public Completion(Action<GameplayEffectCompletionContext> a){action=a;}public void Execute(GameplayEffectCompletionContext c)=>action(c); }
  sealed class Period:IGameplayEffectExecution { readonly Action<GameplayEffectApplicationContext> action; public Period(Action<GameplayEffectApplicationContext> a){action=a;}public void Execute(GameplayEffectApplicationContext c)=>action(c); }
  sealed class Host:IGameplayAbilityHost
  {
   public GameplaySourceIdAllocator Ids=new GameplaySourceIdAllocator();
   public GameplaySourceId OwnerId{get;}public GameplaySourceId TargetId=>OwnerId;
   public AttributeSet Attributes{get;}=new AttributeSet();public GameplayTagContainer Tags{get;}=new GameplayTagContainer();
   public GameplayEffectContainer Effects{get;}public GameplayEffectApplicationContext Application{get;}
   public Host(){OwnerId=Ids.Allocate();Attributes.Register(Value,10);Effects=new GameplayEffectContainer(Attributes,Ids,Tags);Application=new GameplayEffectApplicationContext(new GameplayContext(OwnerId,new GameplayTargetData(OwnerId),Ids.Allocate()),this,new GameplaySetByCallerValues(new[]{new KeyValuePair<GameplayTag,float>(Active,7)}));}
  }
  static GameplayEffectDefinition Definition(IGameplayEffectCompletionExecution[] completions=null,IGameplayEffectExecution period=null,GameplayTagQuery ongoing=null)=>new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration,3,period==null?0:1,new[]{new GameplayEffectAttributeModifierDefinition(Value,GameplayModifierOperation.Add,5)},new[]{Active},period==null?Array.Empty<IGameplayEffectExecution>():new[]{period},null,null,ongoing,completions);
  [Test]public void DurationNaturalExpiryRunsCompletionExactlyOnce()
  {
   var host = new Host();
   int calls = 0;
   GameplayEffectDefinition definition = null;
   definition = Definition(new[] { new Completion(completion =>
   {
    calls++;
    Assert.That(completion.Definition, Is.SameAs(definition));
   }) });
   var handle = host.Effects.Apply(definition, host.Application).ActiveHandle;
   host.Effects.Advance(2.9f);
   Assert.That(calls, Is.Zero);
   host.Effects.Advance(.2f);
   host.Effects.Advance(100);
   Assert.That(calls, Is.EqualTo(1));
   Assert.That(host.Effects.IsActive(handle), Is.False);
  }
  [Test]public void CleanupPrecedesAuthoredCompletionOrder()
  {var h=new Host();var order=new List<int>();GameplayEffectHandle handle=default;var d=Definition(new[]{new Completion(c=>{Assert.That(h.Effects.IsActive(handle),Is.False);Assert.That(h.Tags.HasTagExact(Active),Is.False);Assert.That(h.Attributes.GetCurrentValue(Value),Is.EqualTo(10));Assert.That(c.Handle,Is.EqualTo(handle));order.Add(1);}),new Completion(c=>order.Add(2))});handle=h.Effects.Apply(d,h.Application).ActiveHandle;h.Effects.Advance(3);Assert.That(order,Is.EqualTo(new[]{1,2}));}
  [Test]public void ExplicitAndStaleRemoveDoNotRunCompletion()
  {var h=new Host();int calls=0;var handle=h.Effects.Apply(Definition(new[]{new Completion(c=>calls++)}),h.Application).ActiveHandle;Assert.That(h.Effects.Remove(handle),Is.True);Assert.That(h.Effects.Remove(handle),Is.False);Assert.That(h.Effects.Remove(default),Is.False);h.Effects.Advance(10);Assert.That(calls,Is.Zero);}
  [Test]public void OngoingMismatchDoesNotRunCompletion()
  {var h=new Host();int calls=0;var requirement=new GameplayTagQuery(Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),new[]{Active});var handle=h.Effects.Apply(Definition(new[]{new Completion(c=>calls++)},ongoing:requirement),h.Application).ActiveHandle;h.Effects.Advance(100);Assert.That(h.Effects.IsActive(handle),Is.False);Assert.That(calls,Is.Zero);}
  [TestCase(false)][TestCase(true)]public void AbilityEndOrCancelDoesNotRunCompletion(bool cancel)
  {var h=new Host();int calls=0;var ability=new GameplayAbilityContainer(h,h.Ids);var handle=ability.Grant(new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),activationEffects:new[]{Definition(new[]{new Completion(c=>calls++)})}));Assert.That(ability.TryActivate(handle),Is.True);if(cancel)ability.Cancel(handle);else ability.End(handle);h.Effects.Advance(10);Assert.That(calls,Is.Zero);Assert.That(h.Effects.GetActiveEntriesSnapshot(),Is.Empty);}
  [Test]public void FinalPeriodThenCleanupThenCompletion()
  {var h=new Host();var order=new List<string>();var d=Definition(new[]{new Completion(c=>{Assert.That(h.Tags.HasTagExact(Active),Is.False);order.Add("complete");})},new Period(c=>{Assert.That(h.Tags.HasTagExact(Active),Is.True);order.Add("period");}));h.Effects.Apply(d,h.Application);h.Effects.Advance(5);Assert.That(order,Is.EqualTo(new[]{"period","period","period","complete"}));}
  [Test]public void ReentrantApplyPreservesIdentityAndStartsNewPhaseNextAdvance()
  {var h=new Host();int ticks=0;GameplayEffectHandle next=default;GameplaySourceId oldSource=default;var buff=new GameplayEffectDefinition(GameplayEffectDurationPolicy.Infinite,0,1,Array.Empty<GameplayEffectAttributeModifierDefinition>(),Array.Empty<GameplayTag>(),new[]{new Period(c=>{ticks++;Assert.That(c.Context,Is.SameAs(h.Application.Context));Assert.That(c.SetByCaller,Is.SameAs(h.Application.SetByCaller));Assert.That(c.InstigatorAttributes,Is.SameAs(h.Application.InstigatorAttributes));})});var d=Definition(new[]{new Completion(c=>{oldSource=c.SourceId;Assert.That(c.Application,Is.SameAs(h.Application));next=c.Apply(buff).ActiveHandle;})});var old=h.Effects.Apply(d,h.Application).ActiveHandle;h.Effects.Advance(100);Assert.That(ticks,Is.Zero);Assert.That(next,Is.Not.EqualTo(old));Assert.That(h.Effects.GetActiveEntriesSnapshot()[0].SourceId,Is.Not.EqualTo(oldSource));Assert.That(h.Effects.TryGetPeriodRemaining(next,out float remaining),Is.True);Assert.That(remaining,Is.EqualTo(1));h.Effects.Advance(.5f);Assert.That(ticks,Is.Zero);h.Effects.Advance(.5f);Assert.That(ticks,Is.EqualTo(1));}
  [Test]public void FinalPeriodThrowsCleansUpWithoutCompletion()
  {var h=new Host();int periods=0,completions=0;var handle=h.Effects.Apply(Definition(new[]{new Completion(c=>completions++)},new Period(c=>{if(++periods==3)throw new InvalidOperationException("period");})),h.Application).ActiveHandle;Assert.Throws<InvalidOperationException>(()=>h.Effects.Advance(3));Assert.That(h.Effects.IsActive(handle),Is.False);Assert.That(h.Tags.HasTagExact(Active),Is.False);Assert.That(h.Attributes.GetCurrentValue(Value),Is.EqualTo(10));Assert.That(completions,Is.Zero);}
  [Test]public void CompletionThrowsStopsLaterActionsAndDoesNotRollbackPriorApply()
  {var h=new Host();int later=0;var child=new GameplayEffectDefinition(GameplayEffectDurationPolicy.Infinite,0,Array.Empty<GameplayEffectAttributeModifierDefinition>());GameplayEffectHandle next=default;var handle=h.Effects.Apply(Definition(new[]{new Completion(c=>next=c.Apply(child).ActiveHandle),new Completion(c=>throw new InvalidOperationException("completion")),new Completion(c=>later++)}),h.Application).ActiveHandle;Assert.Throws<InvalidOperationException>(()=>h.Effects.Advance(3));Assert.That(h.Effects.IsActive(handle),Is.False);Assert.That(h.Effects.IsActive(next),Is.True);h.Effects.Advance(3);Assert.That(later,Is.Zero);}
  [Test]public void FinalPeriodExplicitlyRemovesEntryDoesNotComplete()
  {var h=new Host();int calls=0;GameplayEffectHandle handle=default;handle=h.Effects.Apply(Definition(new[]{new Completion(c=>calls++)},new Period(c=>h.Effects.Remove(handle))),h.Application).ActiveHandle;h.Effects.Advance(3);Assert.That(calls,Is.Zero);}
  [Test]public void CompletionDefinitionIsImmutableAndRequiresApplicationSnapshot()
  {var h=new Host();var input=new IGameplayEffectCompletionExecution[]{new Completion(c=>{})};var d=Definition(input);input[0]=null;Assert.That(d.CompletionExecutions[0],Is.Not.Null);Assert.Throws<InvalidOperationException>(()=>h.Effects.Apply(d));Assert.That(new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration,1,Array.Empty<GameplayEffectAttributeModifierDefinition>()).CompletionExecutions,Is.Empty);}
 }
}
