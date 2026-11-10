using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using FRFrame.Gameplay.Extensions;
using FRFrame.Gameplay.Standard;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay
{
    /// <summary>Three guards for regression-prone exact geometry/order, pure hit settlement and
    /// cross-target scope/clock cleanup. The permanent Standard Lab supplies interactive coverage.</summary>
    public sealed class GameplayStandardLibraryTests
    {
        private static readonly GameplayAttributeId Health = new GameplayAttributeId("Health"), MaxHealth = new GameplayAttributeId("MaxHealth"),
            Shield = new GameplayAttributeId("Shield"), Defense = new GameplayAttributeId("Defense");
        [Test]
        public void GeometryAndFilteringUseStableUniqueOrderAndRealBoundaries()
        {
            using (var world = new GameplayStandardWorld())
            {
                var source = world.CreateActor(new GameplayPoint(0, 0), 1);
                var first = world.CreateActor(new GameplayPoint(2, 0), 2);
                var second = world.CreateActor(new GameplayPoint(2, 0), 2);
                var ally = world.CreateActor(new GameplayPoint(1, 0), 1);
                var geometry = new GameplaySearchGeometry(GameplaySearchShape.Sector, GameplaySpatialPlane.XY, source.Position, source.Facing, radius: 6, angle: 120);
                Assert.That(geometry.Contains(first.Id, new GameplayPoint(6, 0)), Is.True);
                Assert.That(geometry.Contains(first.Id, new GameplayPoint(3, 5.196152f)), Is.True);
                Assert.That(geometry.Contains(first.Id, new GameplayPoint(6.01f, 0)), Is.False);
                Assert.That(geometry.Contains(first.Id, new GameplayPoint(0, 2)), Is.False);
                var context = new GameplayContext(source.Id, new GameplayTargetData(source.Id));
                var request = new GameplaySpatialSearchRequest(context, geometry, 1, maxTargets: 2);
                Assert.That(request.Select(new IGameplaySpatialActor[] { second, second, ally, first, source }).Targets,
                    Is.EqualTo(new[] { first.Id, second.Id }));
                var cell = new GameplaySearchGeometry(GameplaySearchShape.GridCells, GameplaySpatialPlane.XZ, source.Position,
                    new GameplayDirection(1, 0), cells: new[] { new GameplayPoint(-.5f, 0, 2.5f) });
                Assert.That(cell.Contains(first.Id, new GameplayPoint(-.1f, 0, 2.1f)), Is.True);
                Assert.That(cell.Contains(first.Id, new GameplayPoint(.1f, 0, 2.1f)), Is.False);
                var anchor = new GameplayPoint(1, 0);
                var bound = world.CreateActor(anchor, 2, readPosition: () => anchor, writePosition: p => anchor = p);
                anchor = new GameplayPoint(4, 0);
                Assert.That(bound.Position.X, Is.EqualTo(4), "A physics/presentation binding cannot be a stale copied position.");
                bound.Position = new GameplayPoint(5, 0);
                Assert.That(anchor.X, Is.EqualTo(5), "Movement commits to the actual bound anchor.");
                var pointContext = new GameplayContext(source.Id, new GameplayTargetData(first.Id, new GameplayPoint(0,5)));
                var segment = new GameplaySearchTargetAction(GameplaySearchShape.Line, GameplaySpatialPlane.XY, GameplaySearchOrigin.Owner,
                    Number(1),Number(120),GameplayFormula.Compile("Context.UnusedLength"),Number(1),Number(1),
                    GameplayFactionFilter.Enemies,null,-1,32,GameplayTargetOrder.Nearest,false,null,null,null,
                    direction:GameplaySearchDirection.ToContextPoint,lengthFromTarget:true);
                Assert.That(segment.TryCreateRequest(pointContext,source,out var segmentRequest),Is.True);
                Assert.That(segmentRequest.Geometry.Length,Is.EqualTo(5));
                Assert.That(segmentRequest.Geometry.Contains(first.Id,new GameplayPoint(0,2)),Is.True);
                Assert.That(segmentRequest.Geometry.Contains(first.Id,new GameplayPoint(2,0)),Is.False);
                Assert.That(segment.TryCreateRequest(new GameplayContext(source.Id,new GameplayTargetData(first.Id,source.Position)),source,out _),Is.False,"Coincident endpoints cannot silently use facing.");
            }
        }
        [Test]
        public void OrderedPipelineCommitsShieldAndHpOnceAndPreservesFullAbsorption()
        {
            using (var world = new GameplayStandardWorld())
            {
                var source = world.CreateActor(default, 1); var target = world.CreateActor(new GameplayPoint(1, 0), 2);
                Configure(target); var values = GameplayContextValues.Empty.WithValue(GameplayDamageExecution.CriticalKey, 1);
                var context = new GameplayContext(source.Id, new GameplayTargetData(target.Id), values: values, abilityLevel: 5);
                var application = new GameplayEffectApplicationContext(context, target, GameplaySetByCallerValues.Empty, source.Attributes, source.Id, instigatorTarget: source);
                var attack=new GameplayAttributeId("Attack");source.Attributes.Register(attack,100);
                var key=new GameplayTag("Data.Guard.CapturedAttack");
                var captured=new GameplayCaptureValueAction(key,new GameplayAttributeFloatValue(GameplayValueAttributeSource.Instigator,attack)).ExecuteWithContext(context,default,source).Context;
                source.Attributes.SetBaseValue(attack,200);
                Assert.That(captured.Values.GetRequiredValue(key),Is.EqualTo(100));Assert.That(context.Values.TryGetValue(key,out _),Is.False,"Capturing cannot mutate the original context.");
                Assert.That(GameplayValueEvaluator.Evaluate(new GameplayAttributeFloatValue(GameplayValueAttributeSource.Instigator,attack),application),Is.EqualTo(200));
                var points=new[]{new System.Collections.Generic.KeyValuePair<float,float>(1,10),new System.Collections.Generic.KeyValuePair<float,float>(9,90)};
                var curve=new GameplayCurveFloatValue(new GameplayAbilityLevelFloatValue(),points);points[1]=new System.Collections.Generic.KeyValuePair<float,float>(9,900);
                Assert.That(GameplayValueEvaluator.Evaluate(curve,application),Is.EqualTo(50),"Built curve copies authored keys and interpolates the cast level.");
                Assert.That(GameplayValueEvaluator.Evaluate(curve,new GameplayEffectApplicationContext(context.WithAbilityLevel(10),target,GameplaySetByCallerValues.Empty)),Is.EqualTo(90));
                var shield = new GameplayDamageStage(GameplayDamageStageKind.Shield);
                var defense = new GameplayDamageStage(GameplayDamageStageKind.Defense);
                var crit = new GameplayDamageStage(GameplayDamageStageKind.Critical);
                var minimum = new GameplayDamageStage(GameplayDamageStageKind.Minimum);
                GameplayEventData committed = default;
                bool committedBeforeEvent = false;
                world.Events.Subscribe(GameplayCombatEvents.Damage,data=>{
                    committed=data;
                    committedBeforeEvent=target.Attributes.GetCurrentValue(Shield)==0 && target.Attributes.GetCurrentValue(Health)==940;
                });
                new GameplayDamageExecution(new GameplayContextFloatValue(key), new[] { shield, defense, crit, minimum }).Execute(
                    new GameplayEffectApplicationContext(captured,target,captured.Values.AsSetByCallerValues(),source.Attributes,source.Id,instigatorTarget:source));
                Assert.That(target.Attributes.GetCurrentValue(Health), Is.EqualTo(940));
                Assert.That(target.Attributes.GetCurrentValue(Shield), Is.Zero);
                Assert.That(target.LastCombatOutcome.Value.Damage.AbsorbedDamage, Is.EqualTo(40));
                Assert.That(target.LastCombatOutcome.Value.Damage.IsCritical, Is.True);
                Assert.That(committedBeforeEvent,Is.True,"Damage producers report only after atomic resource settlement.");
                Assert.That(committed.Context,Is.SameAs(captured));
                Assert.That(committed.Magnitude,Is.EqualTo(60));
                target.Attributes.SetBaseValue(Health, 1000); target.Attributes.SetBaseValue(Shield, 40);
                new GameplayDamageExecution(new GameplayConstantFloatValue(100), new[] { defense, shield, crit, minimum }).Execute(application);
                Assert.That(target.Attributes.GetCurrentValue(Health), Is.EqualTo(980));
                new GameplayHealExecution(new GameplayConstantFloatValue(100)).Execute(application);
                Assert.That(target.LastCombatOutcome.Value.Applied, Is.EqualTo(20));
                Assert.That(target.LastCombatOutcome.Value.Overflow, Is.EqualTo(80));
                target.Attributes.SetBaseValue(Shield, 200);
                new GameplayDamageExecution(new GameplayConstantFloatValue(100), new[] { shield, defense, crit, minimum }).Execute(application);
                Assert.That(target.Attributes.GetCurrentValue(Health), Is.EqualTo(1000));
                Assert.That(target.Attributes.GetCurrentValue(Shield), Is.EqualTo(100));
                Assert.That(target.LastCombatOutcome.Value.Applied, Is.Zero, "Minimum must not pierce a fully absorbing shield.");
                var invalid = new GameplayDamageStage(GameplayDamageStageKind.Minimum, GameplayFormula.Compile("1 / Context.Missing"));
                Assert.Throws<KeyNotFoundException>(() => new GameplayDamageExecution(new GameplayConstantFloatValue(100), new[] { shield, invalid }).Execute(application));
                Assert.That(target.Attributes.GetCurrentValue(Shield), Is.EqualTo(100), "Failing calculations cannot consume shield before HP commit.");
            }
        }
        [Test]
        public void CausalCombatQueueDeliversRetaliationAndChainsAndBoundsOnlyTheCurrentLoop()
        {
            using(var world=new GameplayStandardWorld())
            {
                var a=world.CreateActor(default,1);var b=world.CreateActor(default,2);var c=world.CreateActor(default,2);
                foreach(var actor in new[]{a,b,c}) {Configure(actor);actor.Attributes.SetBaseValue(Shield,0);actor.SkillTrace.Enabled=true;}
                var order=new List<string>();
                Action<GameplayStandardActor,GameplayStandardActor,bool> strike=(from,to,noRetaliate)=>{
                    var values=noRetaliate?GameplayContextValues.Empty.WithValue(GameplayCombatEvents.NoRetaliate,1):GameplayContextValues.Empty;
                    var context=new GameplayContext(from.Id,new GameplayTargetData(to.Id),values:values);
                    new GameplayDamageExecution(Number(10),Array.Empty<GameplayDamageStage>()).Execute(
                        new GameplayEffectApplicationContext(context,to,GameplaySetByCallerValues.Empty,from.Attributes,from.Id,instigatorTarget:from));
                };
                var hit=world.Events.Subscribe(GameplayCombatEvents.Hit,data=>{
                    order.Add(data.Context.TargetData.PrimaryTargetId==b.Id?"B":data.Context.TargetData.PrimaryTargetId==a.Id?"A":"C");
                    if(data.Context.Values.TryGetValue(GameplayCombatEvents.NoRetaliate,out float blocked)&&blocked>0)return;
                    if(data.Context.TargetData.PrimaryTargetId==b.Id){strike(b,a,true);strike(b,c,true);}
                });
                strike(a,b,false);
                Assert.That(order,Is.EqualTo(new[]{"B","A","C"}),"Nested legal outcomes drain FIFO, shared buses receive each once.");
                foreach(var actor in new[]{a,b,c})Assert.That(actor.Attributes.GetCurrentValue(Health),Is.EqualTo(990));
                world.Events.Unsubscribe(hit);order.Clear();
                int loopEvents=0;
                var loop=world.Events.Subscribe(GameplayCombatEvents.Hit,data=>{loopEvents++;strike(data.Context.TargetData.PrimaryTargetId==a.Id?a:b,data.Context.TargetData.PrimaryTargetId==a.Id?b:a,false);});
                strike(a,b,false);
                Assert.That(loopEvents,Is.EqualTo(GameplayStandardWorld.CombatChainDepthLimit+1));
                Assert.That(world.LastCombatDispatchDiagnostic,Does.Contain("CombatChainBudgetExceeded"));
                loopEvents=0;
                var limited=new GameplayContext(a.Id,new GameplayTargetData(b.Id),values:GameplayContextValues.Empty.WithValue(GameplayCombatEvents.MaxChainDepth,1));
                new GameplayDamageExecution(Number(10),Array.Empty<GameplayDamageStage>()).Execute(new GameplayEffectApplicationContext(limited,b,limited.Values.AsSetByCallerValues(),a.Attributes,a.Id,instigatorTarget:a));
                Assert.That(loopEvents,Is.EqualTo(2),"A root author may lower the chain limit without suppressing its legal first reaction.");
                Assert.That(world.LastCombatDispatchDiagnostic,Does.Contain("depth=2/1"));
                world.Events.Unsubscribe(loop);
                world.Events.Subscribe(GameplayCombatEvents.Hit,data=>order.Add("Independent"));
                strike(a,c,false);Assert.That(order,Is.EqualTo(new[]{"Independent"}),"Loop exhaustion cannot consume a later player's budget.");
            }
        }
        [Test]
        public void InstalledCastPreservesScopeAndForeignEffectCleanupWithoutPastDelta()
        {
            using (var world = new GameplayStandardWorld())
            {
                var source = world.CreateActor(default, 1); var target = world.CreateActor(new GameplayPoint(2, 0), 2);
                var tag = new GameplayTag("State.Standard.Test");
                var signal = new GameplayTag("Event.Standard.Test.Cast");
                var buff = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 2,
                    Array.Empty<GameplayEffectAttributeModifierDefinition>(), new[] { tag });
                var step = new GameplaySequenceSkillStepMarkerDefinition(8f / 30, Array.Empty<IGameplaySkillCondition>(),
                    new GameplaySkillSelfTarget(), new IGameplaySkillAction[] {
                        new GameplaySearchTargetAction(GameplaySearchShape.Sector, GameplaySpatialPlane.XY, GameplaySearchOrigin.Owner,
                            Number(6), Number(120), Number(6), Number(2), Number(1), GameplayFactionFilter.Enemies, null, -1, 32,
                            GameplayTargetOrder.Nearest, false, new[] { new GameplayApplyToTargetsAction(buff, true) }, null, null) });
                var sequence = new GameplaySequenceDefinition(5, new[] { step });
                var rules = new GameplaySkillDefinition(new GameplaySkillEventTriggerDefinition(signal), Array.Empty<IGameplaySkillCondition>(),
                    new GameplaySkillSelfTarget(), new[] { new GameplayOwnedSequenceAction(sequence) }, false);
                var ability = new GameplayAbilityDefinition(Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>(), maxLevel: 10);
                int observations = 0;
                GameplayContext observed = null;
                var followup = new GameplayTag("Event.Standard.Test.Followup");
                var additional = new GameplaySkillDefinition(new GameplaySkillEventTriggerDefinition(followup), Array.Empty<IGameplaySkillCondition>(),
                    new GameplaySkillSelfTarget(), new[] { new ObserveAction(c => { observations++; observed = c; }) }, false);
                var installed = source.InstallDocument(new GameplaySkillDocumentDefinition(2010, ability, rules, signal, additionalRules: new[] { additional }), 5);
                Assert.That(installed.TryCast(), Is.True);
                world.Events.Publish(new GameplayEventData(signal, new GameplayContext(source.Id, new GameplayTargetData(source.Id)), 1));
                Assert.That(source.Sequences.ActiveCount, Is.EqualTo(1), "An unrelated cast event from the same owner cannot replay this grant.");
                world.Events.Publish(new GameplayEventData(followup, new GameplayContext(target.Id, new GameplayTargetData(source.Id)), 7));
                Assert.That(observations, Is.EqualTo(1));
                Assert.That(observed.AbilityLevel, Is.EqualTo(5));
                Assert.That(observed.OriginSourceId, Is.EqualTo(installed.LastCastContext.OriginSourceId));
                world.Advance(8f / 30);
                var active = target.Effects.GetActiveEntriesSnapshot(); Assert.That(active.Count, Is.EqualTo(1));
                Assert.That(active[0].ApplicationContext.AbilityLevel, Is.EqualTo(5));
                Assert.That(active[0].ApplicationContext.OriginSourceId, Is.EqualTo(installed.LastCastContext.OriginSourceId));
                Assert.That(target.Effects.TryGetRemainingDuration(active[0].Handle, out float remaining), Is.True);
                Assert.That(remaining, Is.EqualTo(2), "New marker target effect cannot consume the marker's past step.");
                world.Advance(2);
                Assert.That(target.Tags.HasTag(tag), Is.False);
                Assert.That(source.Abilities.GetGrantedEntriesSnapshot()[0].OwnedLifetimesCount, Is.EqualTo(3), "Expired foreign lease relation must release; two listeners/Sequence remain.");
                Assert.That(installed.Cancel(), Is.True);
                Assert.That(source.Sequences.ActiveCount, Is.Zero);
                Assert.That(source.Abilities.GetGrantedEntriesSnapshot()[0].OwnedLifetimesCount, Is.Zero);
                world.Events.Publish(new GameplayEventData(followup, new GameplayContext(target.Id, new GameplayTargetData(source.Id)), 7));
                Assert.That(observations, Is.EqualTo(1), "Cancel must physically remove supplemental subscriptions.");
                var passiveActor=world.CreateActor(default);
                var inputKey=new GameplayTag("Data.Test.Passive.CD");
                var passiveCooldown=new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration,1,0,Array.Empty<GameplayEffectAttributeModifierDefinition>(),new[]{new GameplayTag("Cooldown.Test.Passive")},Array.Empty<IGameplayEffectExecution>(),null,durationValue:new GameplaySetByCallerFloatValue(inputKey));
                var passiveAbility=new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),passiveCooldown);
                var passive=passiveActor.InstallDocument(new GameplaySkillDocumentDefinition(2011,passiveAbility,rules,default,GameplayStandardActivationMode.Passive,
                    defaults:GameplayContextValues.Empty.WithValue(inputKey,3)));
                Assert.That(passive.Status.CooldownRemaining,Is.EqualTo(3),"Passive activation must capture the same formal defaults as active cost/timing.");
                passive.Dispose();
            }
        }
        /// <summary>Guards cross-actor reapplication against consuming pre-birth time, including
        /// independent layers, shared refresh/period reset and completion-born entries.</summary>
        [TestCase(false,false)][TestCase(true,false)][TestCase(false,true)][TestCase(true,true)]
        public void CrossActorEffectCallbacksShareBoundaryAndDoNotAgeNewbornEntries(bool reverse,bool independent)
        {
            using(var world=new GameplayStandardWorld()){
                var first=world.CreateActor(default);var second=world.CreateActor(default);
                var source=reverse?second:first;var target=reverse?first:second;
                var application=new GameplayEffectApplicationContext(new GameplayContext(source.Id,new GameplayTargetData(target.Id)),target,GameplaySetByCallerValues.Empty);
                var ticks=new List<int>();int completed=0;
                var stacking=new GameplayEffectStackingDefinition(new GameplayTag("Test.World.Stack"),GameplayEffectStackAggregation.AggregateByTarget,2,
                    independent?GameplayEffectStackDurationRefreshPolicy.NeverRefresh:GameplayEffectStackDurationRefreshPolicy.RefreshOnSuccessfulApplication,
                    GameplayEffectStackPeriodResetPolicy.ResetOnSuccessfulApplication,GameplayEffectStackOverflowPolicy.Reject,
                    independent?GameplayEffectStackLifetimePolicy.IndependentLayerLifetime:GameplayEffectStackLifetimePolicy.SharedEntryLifetime);
                var buff=new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration,.5f,.5f,Array.Empty<GameplayEffectAttributeModifierDefinition>(),Array.Empty<GameplayTag>(),
                    new[]{new CallbackExecution(a=>ticks.Add(a.StackCount))},stacking,completionExecutions:new[]{new CallbackCompletion(c=>completed++)});
                var handle=target.Effects.Apply(buff,application).ActiveHandle;
                GameplayEffectHandle newborn=default;
                var child=new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration,.1f,Array.Empty<GameplayEffectAttributeModifierDefinition>());
                var trigger=new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration,.25f,.25f,Array.Empty<GameplayEffectAttributeModifierDefinition>(),Array.Empty<GameplayTag>(),
                    new[]{new CallbackExecution(a=>{Assert.Throws<InvalidOperationException>(()=>source.Effects.Advance(0));target.Effects.Apply(buff,application);})},null,
                    completionExecutions:new[]{new CallbackCompletion(c=>newborn=target.Effects.Apply(child,application).ActiveHandle)});
                source.Effects.Apply(trigger,new GameplayEffectApplicationContext(new GameplayContext(source.Id,new GameplayTargetData(source.Id)),source,GameplaySetByCallerValues.Empty));
                world.Advance(.6f);
                Assert.That(target.Effects.TryGetRemainingDuration(handle,out float remaining),Is.True);
                Assert.That(remaining,Is.EqualTo(.15f).Within(.000001f),"Refresh must anchor to the .25 callback, not the target's old or end-of-step clock.");
                Assert.That(target.Effects.TryGetPeriodRemaining(handle,out float period),Is.True);Assert.That(period,Is.EqualTo(.15f).Within(.000001f));
                Assert.That(target.Effects.GetActiveEntriesSnapshot()[0].StackCount,Is.EqualTo(independent?1:2));
                Assert.That(target.Effects.TryGetRemainingDuration(newborn,out float age),Is.True);Assert.That(age,Is.EqualTo(.1f));
                Assert.That(ticks,Is.Empty);world.Advance(.15f);
                Assert.That(ticks,Is.EqualTo(new[]{independent?1:2}));Assert.That(completed,Is.EqualTo(1));
                Assert.That(target.Effects.GetActiveEntriesSnapshot(),Is.Empty);
            }
        }
        /// <summary>Prevents an initial postcommit exception from leaving an installed skill
        /// permanently active before Bind. Paid cooldown/resources remain committed; owned buffs clean up.</summary>
        [Test]
        public void InstalledSkillCancelsPaidActivationWhenInitialExecutionThrows()
        {
            using(var world=new GameplayStandardWorld()){
                var actor=world.CreateActor(default,1);var mana=new GameplayAttributeId("Mana");actor.Attributes.Register(mana,20);
                var active=new GameplayTag("State.Test.R2.Initial");var cooling=new GameplayTag("Cooldown.Test.R2.Initial");int calls=0;
                var initial=new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration,3,1,Array.Empty<GameplayEffectAttributeModifierDefinition>(),new[]{active},
                    new IGameplayEffectExecution[]{new GameplayAttributeDeltaExecution(mana,Number(-10)),new CallbackExecution(a=>{calls++;throw new InvalidOperationException("Committed initial failure");})},null,
                    periodicStart:GameplayEffectPeriodicStartPolicy.ExecuteOnApplication);
                var cooldown=new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration,5,Array.Empty<GameplayEffectAttributeModifierDefinition>(),new[]{cooling});
                var ability=new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),cooldown,
                    cost:new GameplayAttributeAbilityCost(new[]{new GameplayAttributeCostEntry(mana,Number(-5))}),activationEffects:new[]{initial});
                var signal=new GameplayTag("Event.Test.R2.Initial");
                var rule=new GameplaySkillDefinition(new GameplaySkillEventTriggerDefinition(signal),Array.Empty<IGameplaySkillCondition>(),new GameplaySkillSelfTarget(),new[]{(IGameplaySkillAction)new ObserveAction(c=>{})},false);
                var skill=actor.InstallDocument(new GameplaySkillDocumentDefinition(2299,ability,rule,signal));
                Assert.Throws<InvalidOperationException>(()=>skill.TryCast());
                Assert.That(actor.Abilities.IsActive(skill.Grant),Is.False);Assert.That(actor.Tags.HasTag(active),Is.False);
                Assert.That(actor.Tags.HasTag(cooling),Is.True);Assert.That(actor.Resources.TryGetBalance(mana,out float balance),Is.True);Assert.That(balance,Is.EqualTo(5));
                world.Advance(1);Assert.That(calls,Is.EqualTo(1));Assert.That(actor.Effects.GetActiveEntriesSnapshot().Count,Is.EqualTo(1),"Only the paid cooldown survives.");
            }
        }
        /// <summary>Guards the explicit Caller boundary, captured defaults/override/zero,
        /// and rollback of an earlier staged modifier when a required key is absent.</summary>
        [Test]
        public void ActivationCallerSnapshotPreservesValuesAndRejectsMissingKeyAtomically()
        {
            using(var world=new GameplayStandardWorld()){
                var actor=world.CreateActor(default,1);var mana=new GameplayAttributeId("Mana");var stat=new GameplayAttributeId("CallerStat");
                actor.Attributes.Register(mana,20);actor.Attributes.Register(stat,10);actor.Attributes.Register(Health,100);
                var key=new GameplayTag("Data.Test.Buff");var caller=new GameplaySetByCallerFloatValue(key);
                var effect=new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration,3,1,
                    new[]{new GameplayEffectAttributeModifierDefinition(stat,GameplayModifierOperation.Add,caller)},Array.Empty<GameplayTag>(),
                    new IGameplayEffectExecution[]{new GameplayAttributeDeltaExecution(Health,caller)},null,periodicStart:GameplayEffectPeriodicStartPolicy.ExecuteOnApplication);
                var signal=new GameplayTag("Event.Test.C1.Caller");var rules=new GameplaySkillDefinition(new GameplaySkillEventTriggerDefinition(signal),
                    Array.Empty<IGameplaySkillCondition>(),new GameplaySkillSelfTarget(),new[]{new ObserveAction(c=>{})},false);
                var cost=new GameplayAttributeAbilityCost(new[]{new GameplayAttributeCostEntry(mana,Number(-5))});
                var ability=new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),
                    charge:new GameplayAbilityChargeDefinition(2,8),cost:cost,activationEffects:new[]{effect});
                var defaults=GameplayContextValues.Empty.WithValue(key,3);
                var skill=actor.InstallDocument(new GameplaySkillDocumentDefinition(2292,ability,rules,signal,defaults:defaults));
                foreach(float value in new[]{3f,7f,0f}){
                    var input=value==3?null:GameplayContextValues.Empty.WithValue(key,value);
                    Assert.That(skill.TryCast(input:input),Is.True);
                    Assert.That(actor.Attributes.GetCurrentValue(stat),Is.EqualTo(10+value));
                    Assert.That(actor.Attributes.GetCurrentValue(Health),Is.EqualTo(100+value));
                    var application=actor.Effects.GetActiveEntriesSnapshot()[0].ApplicationContext;
                    Assert.That(application.Values.AsSetByCallerValues().GetRequiredValue(key),Is.EqualTo(value));
                    input=(input??defaults).WithValue(key,50);
                    Assert.That(actor.Attributes.GetCurrentValue(stat),Is.EqualTo(10+value),"New inputs cannot rewrite the captured active modifier.");
                    skill.Cancel();world.Advance(8);actor.Attributes.SetBaseValue(mana,20);actor.Attributes.SetBaseValue(Health,100);
                }
                skill.Dispose();
                var first=new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration,3,
                    new[]{new GameplayEffectAttributeModifierDefinition(stat,GameplayModifierOperation.Add,1)},Array.Empty<GameplayTag>());
                var missing=actor.InstallDocument(new GameplaySkillDocumentDefinition(2293,
                    new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),new[]{new GameplayTag("State.C1.Attempt")},
                        charge:new GameplayAbilityChargeDefinition(2,8),cost:cost,activationEffects:new[]{first,effect}),rules,signal));
                Assert.Throws<KeyNotFoundException>(()=>missing.TryCast());
                Assert.That(actor.Attributes.GetCurrentValue(stat),Is.EqualTo(10));Assert.That(actor.Attributes.GetCurrentValue(mana),Is.EqualTo(20));
                Assert.That(actor.Effects.GetActiveEntriesSnapshot(),Is.Empty);Assert.That(actor.Tags.HasTag(new GameplayTag("State.C1.Attempt")),Is.False);
                Assert.That(missing.Status.CurrentCharges,Is.EqualTo(2));Assert.That(missing.Status.Phase,Is.EqualTo(GameplayAbilityPhase.Inactive));
                missing.Dispose();
                var rejected=actor.InstallDocument(new GameplaySkillDocumentDefinition(2294,
                    new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),
                        cost:new RejectCommitCost(),activationEffects:new[]{effect}),rules,signal,defaults:defaults));
                Assert.That(rejected.TryCast(),Is.False);Assert.That(actor.Attributes.GetCurrentValue(Health),Is.EqualTo(100));
                Assert.That(actor.Attributes.GetCurrentValue(stat),Is.EqualTo(10));Assert.That(actor.Effects.GetActiveEntriesSnapshot(),Is.Empty);
            }
        }
        /// <summary>Prevents binding a paid but already-ended grant, including passive re-engagement;
        /// an actual Damage event synchronously terminates it before TryActivate returns.</summary>
        [TestCase(false)] [TestCase(true)]
        public void InstalledSkillAcceptsInitialTerminationWithoutBindingOrCastEvent(bool passive)
        {
            using(var world=new GameplayStandardWorld()){
                var actor=world.CreateActor(default,1);Configure(actor);actor.Attributes.SetBaseValue(Shield,0);
                var mana=new GameplayAttributeId("Mana");actor.Attributes.Register(mana,20);actor.SkillTrace.Enabled=true;
                var initial=new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration,3,1,Array.Empty<GameplayEffectAttributeModifierDefinition>(),Array.Empty<GameplayTag>(),
                    new IGameplayEffectExecution[]{new GameplayDamageExecution(Number(10),Array.Empty<GameplayDamageStage>())},null,
                    periodicStart:GameplayEffectPeriodicStartPolicy.ExecuteOnApplication);
                var signal=new GameplayTag("Event.Test.C1.End");int ruleCalls=0,casts=0;
                var rules=new GameplaySkillDefinition(new GameplaySkillEventTriggerDefinition(signal),Array.Empty<IGameplaySkillCondition>(),new GameplaySkillSelfTarget(),
                    new[]{new ObserveAction(c=>ruleCalls++)},false);
                var skill=actor.InstallDocument(new GameplaySkillDocumentDefinition(2295,
                    new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),
                        charge:new GameplayAbilityChargeDefinition(2,8),cost:new GameplayAttributeAbilityCost(new[]{new GameplayAttributeCostEntry(mana,Number(-5))}),activationEffects:new[]{initial}),
                    rules,signal,passive?GameplayStandardActivationMode.Passive:GameplayStandardActivationMode.Active));
                // InstallDocument auto-engages passive content before this test can attach its observer.
                if(passive){skill.Cancel();world.Advance(8);actor.Attributes.SetBaseValue(mana,20);actor.SkillTrace.Clear();}
                var damage=world.Events.Subscribe(GameplayCombatEvents.Damage,e=>{if(passive)skill.Cancel();else actor.Abilities.End(skill.Grant);});
                var cast=world.Events.Subscribe(signal,e=>casts++);
                for(int i=0;i<2;i++){
                    Assert.That(passive?skill.ActivatePassive():skill.TryCast(),Is.True);
                    Assert.That(skill.Status.Phase,Is.EqualTo(GameplayAbilityPhase.Inactive));Assert.That(skill.Status.CurrentCharges,Is.EqualTo(1));
                    Assert.That(actor.Attributes.GetCurrentValue(mana),Is.EqualTo(20-5*(i+1)));Assert.That(skill.LastCastContext,Is.Null);
                    Assert.That(actor.Effects.GetActiveEntriesSnapshot(),Is.Empty);Assert.That(actor.Abilities.GetGrantedEntriesSnapshot()[0].OwnedLifetimesCount,Is.Zero);
                    world.Events.Publish(new GameplayEventData(signal,new GameplayContext(actor.Id,new GameplayTargetData(actor.Id)),1));
                    Assert.That(ruleCalls,Is.Zero);world.Advance(8);
                }
                Assert.That(casts,Is.EqualTo(2),"Only the two explicitly published test signals, never a dead-grant CastEvent.");
                Assert.That(actor.SkillTrace.Snapshot()[0].Item2.Operation,Is.EqualTo("ActivationEndedBeforeBinding"));
                world.Events.Unsubscribe(damage);world.Events.Unsubscribe(cast);
                Assert.That(passive?skill.ActivatePassive():skill.TryCast(),Is.True,"Normal activation remains reusable after removing the terminating observer.");
                Assert.That(skill.Status.Phase,Is.EqualTo(GameplayAbilityPhase.Active));
                if(passive)world.Events.Publish(new GameplayEventData(signal,new GameplayContext(actor.Id,new GameplayTargetData(actor.Id)),1));
                Assert.That(ruleCalls,Is.EqualTo(1));skill.Cancel();
            }
        }
        // Reused step buffers must preserve birth boundaries and recover after an interrupted callback.
        [Test]
        public void WorldStepSnapshotExcludesNewbornAndRecoversAfterCallbackThrows()
        {
            using(var world=new GameplayStandardWorld()){
                var first=world.CreateActor(default,1);int firstTicks=0,newbornTicks=0;bool fail=true;
                first.Behaviour=new CallbackBehaviour(()=>{
                    firstTicks++;
                    if(!fail)return;fail=false;
                    var newborn=world.CreateActor(default,2);
                    newborn.Behaviour=new CallbackBehaviour(()=>newbornTicks++);
                    Assert.Throws<InvalidOperationException>(()=>world.Advance(0));
                    throw new InvalidOperationException("interrupted step");
                });
                Assert.Throws<InvalidOperationException>(()=>world.Advance(.1f));
                Assert.That(newbornTicks,Is.Zero,"A newborn cannot receive the delta before its creation.");
                world.Advance(.1f);
                Assert.That(firstTicks,Is.EqualTo(2));Assert.That(newbornTicks,Is.EqualTo(1),"The next step must not retain duplicate snapshot entries.");
            }
        }
        private sealed class CallbackBehaviour:IGameplayBehaviourDriver
        {
            private readonly Action _advance;internal CallbackBehaviour(Action advance)=>_advance=advance;
            public string CurrentState=>"Test";public bool TryChangeState(string state)=>false;
            public void Advance(float amount)=>_advance();public void Dispose(){}
        }
        private sealed class RejectCommitCost:IGameplayAbilityCost
        {public bool CanPay(IGameplayAbilityHost host)=>true;public bool TryCommit(IGameplayAbilityHost host)=>false;}
        private sealed class CallbackExecution:IGameplayEffectExecution
        {private readonly Action<GameplayEffectApplicationContext> _action;internal CallbackExecution(Action<GameplayEffectApplicationContext> action)=>_action=action;public void Execute(GameplayEffectApplicationContext context)=>_action(context);}
        private sealed class CallbackCompletion:IGameplayEffectCompletionExecution
        {private readonly Action<GameplayEffectCompletionContext> _action;internal CallbackCompletion(Action<GameplayEffectCompletionContext> action)=>_action=action;public void Execute(GameplayEffectCompletionContext context)=>_action(context);}
        [Test]
        public void DamageFollowupUsesEachCommittedHpLossWithoutReusingEarlierResults()
        {
            using var world = new GameplayStandardWorld();
            var source = world.CreateActor(default);
            var first = world.CreateActor(default);
            var second = world.CreateActor(default);
            Configure(source);
            Configure(first);
            Configure(second);
            source.Attributes.SetBaseValue(Health, 500);
            first.Attributes.SetBaseValue(Health, 20);
            first.Attributes.SetBaseValue(Shield, 0);
            var execution = new GameplayDamageExecution(Number(40),
                new[] { new GameplayDamageStage(GameplayDamageStageKind.Shield) },
                healInstigatorAmount: GameplayFormula.Compile("Context.Data.Standard.Combat.AppliedDamage * 0.35"));
            var heals = new List<GameplayCombatOutcome>();
            source.CombatChanged += heals.Add;
            var context = new GameplayContext(source.Id, new GameplayTargetData(first.Id), world.Sources.Allocate(),
                GameplayContextValues.Empty.WithValue(GameplayDamageExecution.AppliedDamageKey, 1000));

            void Hit(GameplayStandardActor target) => execution.Execute(new GameplayEffectApplicationContext(
                context.WithTarget(new GameplayTargetData(target.Id)), target, GameplaySetByCallerValues.Empty,
                source.Attributes, source.Id, instigatorTarget: source));

            Hit(first);
            Hit(second);
            Hit(first);
            Assert.That(heals.Count, Is.EqualTo(1));
            Assert.That(heals[0].Applied, Is.EqualTo(7).Within(.001));
            Assert.That(heals[0].Context.ActivationSourceId, Is.EqualTo(context.ActivationSourceId));
            Assert.That(source.Attributes.GetCurrentValue(Health), Is.EqualTo(507).Within(.001));
        }

        [Test]
        public void ShieldBreakConsumesTheLeaseAndUsesTheBreakerWithoutAnExpiryCallback()
        {
            using var world = new GameplayStandardWorld();
            var owner = world.CreateActor(default);
            var breaker = world.CreateActor(default);
            Configure(owner);
            Configure(breaker);
            owner.Attributes.SetBaseValue(Shield, 0);
            var broken = new List<GameplayContext>();
            var expired = new List<GameplayContext>();
            var grant = new GameplayGrantTimedShieldAction(Number(40), Number(1),
                onBroken: new[] { new ObserveAction(broken.Add) },
                onExpired: new[] { new ObserveAction(expired.Add) });
            var self = new GameplayContext(owner.Id, new GameplayTargetData(owner.Id));
            grant.Execute(self, default, owner);
            var hit = new GameplayContext(breaker.Id, new GameplayTargetData(owner.Id));
            var damage = new GameplayDamageExecution(Number(40), new[] { new GameplayDamageStage(GameplayDamageStageKind.Shield) });
            damage.Execute(new GameplayEffectApplicationContext(hit, owner, GameplaySetByCallerValues.Empty));
            Assert.That(owner.Shields.Snapshot(), Is.Empty);
            Assert.That(broken.Count, Is.EqualTo(1));
            Assert.That(broken[0].TargetData.PrimaryTargetId, Is.EqualTo(breaker.Id));
            world.Advance(2);
            Assert.That(expired, Is.Empty);

            grant.Execute(self, default, owner);
            world.Advance(1);
            Assert.That(expired.Count, Is.EqualTo(1));
            Assert.That(expired[0].TargetData.PrimaryTargetId, Is.EqualTo(owner.Id));
            grant.Execute(self, default, owner);
            owner.Effects.Remove(owner.Shields.Snapshot()[0].EffectHandle, GameplayEffectRemovalCause.Dispel);
            world.Advance(1);
            Assert.That(expired.Count, Is.EqualTo(1));
            Assert.That(broken.Count, Is.EqualTo(1));
        }

        [Test]
        public void PresentationOnlyShieldReleasesItsExactLeaseWhenSpent()
        {
            using var world = new GameplayStandardWorld();
            var target = world.CreateActor(default);
            var secondSource = world.CreateActor(default);
            Configure(target);
            target.Attributes.SetBaseValue(Shield, 0);
            var targetData = new GameplayTargetData(target.Id);
            var first = new GameplayContext(target.Id, targetData);
            var second = new GameplayContext(secondSource.Id, targetData);
            int expired = 0;
            new GameplayGrantTimedShieldAction(Number(80), Number(5), presentation: new ShieldPresentation())
                .Execute(first, default, target);
            new GameplayGrantTimedShieldAction(Number(40), Number(2),
                onExpired: new[] { new ObserveAction(_ => expired++) }).Execute(second, default, secondSource);
            var entries = target.Shields.Snapshot();
            var removals = new List<GameplayEffectRemovalObservation>();
            target.Effects.Removed += removals.Add;
            var stage = new[] { new GameplayDamageStage(GameplayDamageStageKind.Shield) };
            var hit = new GameplayEffectApplicationContext(second, target, GameplaySetByCallerValues.Empty);

            new GameplayDamageExecution(Number(20), stage).Execute(hit);
            Assert.That(entries[0].Remaining, Is.EqualTo(60));
            Assert.That(target.Effects.IsActive(entries[0].EffectHandle), Is.True);
            new GameplayDamageExecution(Number(60), stage).Execute(hit);

            Assert.That(target.Effects.IsActive(entries[0].EffectHandle), Is.False);
            Assert.That(removals, Has.Count.EqualTo(1));
            Assert.That(removals[0].Cause, Is.EqualTo(GameplayEffectRemovalCause.OwnerCleanup));
            Assert.That(target.Shields.Snapshot(), Is.EqualTo(new[] { entries[1] }));
            Assert.That(target.Effects.IsActive(entries[1].EffectHandle), Is.True);
            Assert.That(target.Attributes.GetCurrentValue(Shield), Is.EqualTo(40));
            Assert.That(expired, Is.Zero);
            world.Advance(2);
            Assert.That(expired, Is.EqualTo(1));
            Assert.That(target.Shields.Snapshot(), Is.Empty);
        }

        private sealed class ShieldPresentation : IGameplayEffectPresentationDefinition { }
        private static IGameplayFloatValue Number(float value) => new GameplayConstantFloatValue(value);
        private sealed class ObserveAction : IGameplaySkillAction
        {
            private readonly Action<GameplayContext> _observe;
            internal ObserveAction(Action<GameplayContext> observe) => _observe = observe;
            public GameplaySkillActionResult Execute(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host)
            { _observe(context); return GameplaySkillActionResult.Succeeded; }
        }
        private static void Configure(GameplayStandardActor target)
        {
            target.Attributes.Register(Health, 1000); target.Attributes.Register(MaxHealth, 1000);
            target.Attributes.Register(Shield, 40); target.Attributes.Register(Defense, 100);
            target.CombatAttributes = new GameplayCombatAttributeMap(Health, MaxHealth, Shield, Defense);
        }
    }
}
