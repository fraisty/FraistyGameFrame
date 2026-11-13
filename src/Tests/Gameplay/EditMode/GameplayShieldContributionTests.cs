using System;
using System.Collections.Generic;
using FRFrame.Gameplay;
using FRFrame.Gameplay.Standard;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay
{
    public sealed class GameplayShieldContributionTests
    {
        /// <summary>Prevents expired/cancelled shields from subtracting later or unowned capacity;
        /// a failing hit must leave both actual balance and per-source remaining amounts intact.</summary>
        [Test]
        public void ConsumedShieldExpiryAndExactCleanupPreserveOtherCapacity()
        {
            using(var world=new GameplayStandardWorld())
            {
                var caster=world.CreateActor(default);var target=world.CreateActor(default);
                var hp=new GameplayAttributeId("Health");var max=new GameplayAttributeId("MaxHealth");var shield=new GameplayAttributeId("Shield");
                target.Attributes.Register(hp,100);target.Attributes.Register(max,100);target.Attributes.Register(shield,20);
                target.CombatAttributes=new GameplayCombatAttributeMap(hp,max,shield,default);
                var callerKey=new GameplayTag("Data.Test.Shield.Amount");
                var context=new GameplayContext(caster.Id,new GameplayTargetData(target.Id),abilityLevel:5,values:GameplayContextValues.Empty.WithValue(callerKey,50));
                new GameplayGrantTimedShieldAction(new GameplaySetByCallerFloatValue(callerKey),N(2)).Execute(context,default,caster);
                new GameplayGrantTimedShieldAction(N(60),N(4)).Execute(context,default,caster);
                var entries=target.Shields.Snapshot();Assert.That(entries.Count,Is.EqualTo(2));
                var application=new GameplayEffectApplicationContext(context,target,GameplaySetByCallerValues.Empty,caster.Attributes,caster.Id,instigatorTarget:caster);
                var absorption=new GameplayDamageStage(GameplayDamageStageKind.Shield);
                new GameplayDamageExecution(N(70),new[]{absorption}).Execute(application);
                Assert.That(entries[0].Remaining,Is.Zero);Assert.That(entries[1].Remaining,Is.EqualTo(40));
                Assert.That(target.Attributes.GetCurrentValue(shield),Is.EqualTo(60));
                var invalid=new GameplayDamageStage(GameplayDamageStageKind.Scale,GameplayFormula.Compile("Context.Missing"));
                Assert.Throws<System.Collections.Generic.KeyNotFoundException>(()=>new GameplayDamageExecution(N(10),new[]{absorption,invalid}).Execute(application));
                Assert.That(entries[1].Remaining,Is.EqualTo(40));Assert.That(target.Attributes.GetCurrentValue(shield),Is.EqualTo(60));
                world.Advance(2);Assert.That(entries[0].IsDisposed,Is.True);Assert.That(target.Attributes.GetCurrentValue(shield),Is.EqualTo(60));
                entries[1].Dispose();entries[1].Dispose();Assert.That(target.Attributes.GetCurrentValue(shield),Is.EqualTo(20));
                Assert.That(target.Shields.Snapshot().Count,Is.Zero);Assert.That(target.Effects.GetActiveEntriesSnapshot().Count,Is.Zero);
                var grant=caster.Abilities.Grant(new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>()));
                Assert.That(caster.Abilities.TryActivate(grant),Is.True);
                caster.Abilities.TryCreateExecutionContext(grant,new GameplayTargetData(target.Id),out var cast);
                new GameplayGrantTimedShieldAction(N(30),N(1),true).Execute(cast,default,caster);
                world.Advance(1);Assert.That(target.Attributes.GetCurrentValue(shield),Is.EqualTo(20));
                Assert.That(caster.Abilities.GetGrantedEntriesSnapshot()[0].OwnedLifetimesCount,Is.Zero,"Expiry must release the completed relation from the still-active cast.");
                new GameplayGrantTimedShieldAction(N(50),N(10),true).Execute(cast,default,caster);
                caster.Abilities.Cancel(grant);Assert.That(target.Attributes.GetCurrentValue(shield),Is.EqualTo(20));
                Assert.That(target.Shields.Snapshot().Count,Is.Zero);
            }
        }

        /// <summary>Terminal actions see the consumed shield gone and the original cast still active.
        /// Completion can end that cast before its configured expiry and leaves the next cast usable.</summary>
        [Test]
        public void ShieldRemovalCompletesOriginalCastAfterRetaliation()
        {
            using var world = new GameplayStandardWorld();
            var caster = world.CreateActor(default);
            var attacker = world.CreateActor(default);
            var hp = new GameplayAttributeId("Health");
            var max = new GameplayAttributeId("MaxHealth");
            var shield = new GameplayAttributeId("Shield");
            caster.Attributes.Register(hp,100);
            caster.Attributes.Register(max,100);
            caster.Attributes.Register(shield,0);
            caster.CombatAttributes = new GameplayCombatAttributeMap(hp,max,shield,default);
            var grant = caster.Abilities.Grant(new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),
                Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>()));
            var calls = new List<string>();
            var action = new GameplayGrantTimedShieldAction(N(20),N(5),true,
                onBroken: new[]{new Observe((context, _) =>
                {
                    Assert.That(context.TargetData.PrimaryTargetId, Is.EqualTo(attacker.Id));
                    Assert.That(caster.Shields.Snapshot(), Is.Empty);
                    Assert.That(caster.Abilities.IsActive(grant), Is.True);
                    calls.Add("broken");
                })},
                onRemoved: new IGameplaySkillAction[]{new Observe((context, _) =>
                {
                    Assert.That(context.TargetData.PrimaryTargetId, Is.EqualTo(caster.Id));
                    calls.Add("removed");
                }),new GameplayEndAbilityAction()});

            Assert.That(caster.Abilities.TryActivate(grant), Is.True);
            Assert.That(caster.Abilities.TryCreateExecutionContext(grant,new GameplayTargetData(caster.Id),out var cast), Is.True);
            Assert.That(action.Execute(cast,default,caster), Is.EqualTo(GameplaySkillActionResult.Succeeded));
            var hit = new GameplayContext(attacker.Id,new GameplayTargetData(caster.Id));
            var application = new GameplayEffectApplicationContext(hit,caster,GameplaySetByCallerValues.Empty,attacker.Attributes,attacker.Id);
            new GameplayDamageExecution(N(20),new[]{new GameplayDamageStage(GameplayDamageStageKind.Shield)}).Execute(application);
            Assert.That(calls, Is.EqualTo(new[]{"broken","removed"}));
            Assert.That(caster.Abilities.TryActivate(grant), Is.True);
            Assert.That(caster.Abilities.TryCreateExecutionContext(grant,new GameplayTargetData(caster.Id),out cast), Is.True);
            Assert.That(action.Execute(cast,default,caster), Is.EqualTo(GameplaySkillActionResult.Succeeded));
            caster.Abilities.Cancel(grant);
            Assert.That(calls, Is.EqualTo(new[]{"broken","removed"}));
            Assert.That(caster.Attributes.GetCurrentValue(shield), Is.Zero);
        }

        [TestCase(GameplayShieldReapplicationPolicy.RefreshSingleCapacityAndDuration, 20, 5, 5)]
        [TestCase(GameplayShieldReapplicationPolicy.AccumulateSingleCapacityAndExtendDuration, 32, 4, 6)]
        public void ReapplicationPreservesExactLeaseFifoAndOneTerminalCallback(
            GameplayShieldReapplicationPolicy policy, float capacity, float duration, float remaining)
        {
            using var world = new GameplayStandardWorld();
            var caster = world.CreateActor(default);
            var other = world.CreateActor(default);
            var target = world.CreateActor(default);
            var hp = new GameplayAttributeId("Health");
            var max = new GameplayAttributeId("MaxHealth");
            var shield = new GameplayAttributeId("Shield");
            target.Attributes.Register(hp, 100);
            target.Attributes.Register(max, 100);
            target.Attributes.Register(shield, 3);
            target.CombatAttributes = new GameplayCombatAttributeMap(hp, max, shield, default);
            var hit = new GameplayContext(other.Id, new GameplayTargetData(target.Id));
            var application = new GameplayEffectApplicationContext(hit, target, GameplaySetByCallerValues.Empty, other.Attributes, other.Id);
            int breaks = 0, expiries = 0, removals = 0;
            var action = new GameplayGrantTimedShieldAction(N(20), N(duration),
                onBroken: new[] { new Observe((ctx, _) =>
                {
                    Assert.That(ctx.TargetData.PrimaryTargetId, Is.EqualTo(other.Id));
                    breaks++;
                }) },
                onExpired: new[] { new Observe((ctx, _) =>
                {
                    Assert.That(ctx.TargetData.PrimaryTargetId, Is.EqualTo(target.Id));
                    expiries++;
                }) },
                onRemoved: new[] { new Observe((_, _) => removals++) },
                reapplicationPolicy: policy);
            GameplayContext CastContext(GameplayStandardActor actor) => new GameplayContext(actor.Id,
                new GameplayTargetData(target.Id), world.Sources.Allocate());
            var original = CastContext(caster);
            action.Execute(original, default, caster);
            var first = target.Shields.Snapshot()[0];
            new GameplayGrantTimedShieldAction(N(30), N(15)).Execute(CastContext(caster), default, caster);
            action.Execute(CastContext(other), default, other);
            world.Advance(2);
            target.Attributes.SetBaseValue(shield, 62); // External debit consumes 3 unowned and 8 from the first group.
            action.Execute(CastContext(caster), default, caster);
            var shields = target.Shields.Snapshot();
            Assert.That(shields.Count, Is.EqualTo(3));
            Assert.That(shields[0], Is.SameAs(first));
            Assert.That(shields[0].Context, Is.SameAs(original));
            Assert.That(shields[0].Remaining, Is.EqualTo(capacity));
            Assert.That(shields[1].Remaining, Is.EqualTo(30));
            Assert.That(shields[2].Context.InstigatorId, Is.EqualTo(other.Id));
            Assert.That(target.Effects.TryGetRemainingDuration(first.EffectHandle, out var time), Is.True);
            Assert.That(time, Is.EqualTo(remaining));
            Assert.That(target.Attributes.GetCurrentValue(shield), Is.EqualTo(capacity + 50));

            target.Attributes.SetBaseValue(shield, capacity + 53); // Restore 3 unowned capacity before a FIFO hit.
            var stages = new[] { new GameplayDamageStage(GameplayDamageStageKind.Shield) };
            var immune = new GameplayTag("State.Test.ShieldImmune");
            target.Tags.AddTag(immune, world.Sources.Allocate());
            new GameplayDamageExecution(N(capacity + 1), new[]{new GameplayDamageStage(GameplayDamageStageKind.Immunity), stages[0]},
                new GameplayTagQuery(new[]{immune}, Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>())).Execute(application);
            var miss = hit.WithValues(GameplayContextValues.Empty.WithValue(GameplayDamageExecution.MissKey, 1));
            new GameplayDamageExecution(N(capacity + 1), new[]{new GameplayDamageStage(GameplayDamageStageKind.Miss), stages[0]})
                .Execute(new GameplayEffectApplicationContext(miss, target, GameplaySetByCallerValues.Empty, other.Attributes, other.Id));
            Assert.That(first.Remaining, Is.EqualTo(capacity));
            new GameplayDamageExecution(N(capacity + 1), stages).Execute(application);
            Assert.That(breaks, Is.EqualTo(1));
            Assert.That(expiries, Is.Zero);
            Assert.That(removals, Is.EqualTo(1));
            Assert.That(shields[1].Remaining, Is.EqualTo(29));
            action.Execute(CastContext(caster), default, caster);
            var replacement = target.Shields.Snapshot()[2];
            Assert.That(replacement.EffectHandle, Is.Not.EqualTo(first.EffectHandle));
            target.Effects.Remove(shields[2].EffectHandle, GameplayEffectRemovalCause.Dispel);
            world.Advance(duration);
            Assert.That(breaks, Is.EqualTo(1));
            Assert.That(expiries, Is.EqualTo(1));
            Assert.That(removals, Is.EqualTo(3));
            Assert.That(target.Attributes.GetCurrentValue(shield), Is.EqualTo(32));
        }

        [Test]
        public void InvalidGroupedUpdatesDoNotCommitCapacityOrDeadline()
        {
            using var world = new GameplayStandardWorld();
            var actor = world.CreateActor(default);
            var hp = new GameplayAttributeId("Health");
            var max = new GameplayAttributeId("MaxHealth");
            var shield = new GameplayAttributeId("Shield");
            actor.Attributes.Register(hp, 100);
            actor.Attributes.Register(max, 100);
            var attribute = actor.Attributes.Register(shield, 0);
            actor.CombatAttributes = new GameplayCombatAttributeMap(hp, max, shield, default);
            var duration = new GameplayTag("Data.Test.Shield.Duration");
            var amount = new GameplayTag("Data.Test.Shield.Amount");
            GameplayContext Cast(float capacity, float time) => new GameplayContext(actor.Id,
                new GameplayTargetData(actor.Id), values: GameplayContextValues.Empty.WithValue(duration, time).WithValue(amount, capacity));
            var action = new GameplayGrantTimedShieldAction(new GameplayContextFloatValue(amount), new GameplayContextFloatValue(duration),
                reapplicationPolicy: GameplayShieldReapplicationPolicy.AccumulateSingleCapacityAndExtendDuration);
            action.Execute(Cast(20, 5), default, actor);
            world.Advance(1);
            var entry = actor.Shields.Snapshot()[0];
            Assert.That(action.Execute(Cast(0, 2), default, actor), Is.EqualTo(GameplaySkillActionResult.Rejected));
            Assert.Throws<ArgumentOutOfRangeException>(() => action.Execute(Cast(20, 0), default, actor));
            Assert.Throws<ArithmeticException>(() => action.Execute(Cast(20, float.MaxValue), default, actor));
            attribute.AddModifier(new GameplayAttributeModifier(GameplayModifierOperation.Multiply, 3, world.Sources.Allocate()));
            Assert.Throws<OverflowException>(() => action.Execute(Cast(float.MaxValue / 2, 5), default, actor));
            Assert.That(entry.Remaining, Is.EqualTo(20));
            Assert.That(attribute.BaseValue, Is.EqualTo(20));
            Assert.That(actor.Effects.TryGetRemainingDuration(entry.EffectHandle, out var time), Is.True);
            Assert.That(time, Is.EqualTo(4));
            actor.Effects.Remove(entry.EffectHandle);
            int publications = 0;
            actor.Effects.PresentationChanged += _ => publications++;
            var firstApply = new GameplayGrantTimedShieldAction(N(float.MaxValue / 2), N(5), presentation: new ShieldView(),
                reapplicationPolicy: GameplayShieldReapplicationPolicy.RefreshSingleCapacityAndDuration);
            Assert.Throws<OverflowException>(() => firstApply.Execute(Cast(1, 1), default, actor));
            Assert.That(publications, Is.Zero);
            Assert.That(actor.Shields.Snapshot(), Is.Empty);
            Assert.That(actor.Effects.GetActiveEntriesSnapshot(), Is.Empty);
        }

        [Test]
        public void RemovalCanReapplyAfterExpiryAndAbilityCancelKeepsWorldGroup()
        {
            using var world = new GameplayStandardWorld();
            var actor = world.CreateActor(default);
            var hp = new GameplayAttributeId("Health");
            var max = new GameplayAttributeId("MaxHealth");
            var shield = new GameplayAttributeId("Shield");
            actor.Attributes.Register(hp, 100);
            actor.Attributes.Register(max, 100);
            actor.Attributes.Register(shield, 0);
            actor.CombatAttributes = new GameplayCombatAttributeMap(hp, max, shield, default);
            var context = new GameplayContext(actor.Id, new GameplayTargetData(actor.Id));
            int expired = 0, removed = 0, broken = 0;
            GameplayGrantTimedShieldAction action = null;
            action = new GameplayGrantTimedShieldAction(N(20), N(1),
                onBroken: new[] { new Observe((_, _) => broken++) },
                onExpired: new[] { new Observe((_, _) => expired++) },
                onRemoved: new[] { new Observe((_, _) =>
                {
                    removed++;
                    if (removed != 1) return;
                    Assert.That(actor.Shields.Snapshot(), Is.Empty);
                    action.Execute(context, default, actor);
                }) },
                reapplicationPolicy: GameplayShieldReapplicationPolicy.RefreshSingleCapacityAndDuration);
            action.Execute(context, default, actor);
            var first = actor.Shields.Snapshot()[0].EffectHandle;
            world.Advance(1);
            var live = actor.Shields.Snapshot()[0].EffectHandle;
            Assert.That(live, Is.Not.EqualTo(first));
            Assert.That(actor.Effects.TryGetRemainingDuration(live, out var time), Is.True);
            Assert.That(time, Is.EqualTo(1));
            var grant = actor.Abilities.Grant(new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),
                Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>()));
            actor.Abilities.TryActivate(grant);
            actor.Abilities.TryCreateExecutionContext(grant, context.TargetData, out var cast);
            var owned = new GameplayGrantTimedShieldAction(N(20), N(5), true,
                reapplicationPolicy: GameplayShieldReapplicationPolicy.AccumulateSingleCapacityAndExtendDuration);
            owned.Execute(cast, default, actor);
            owned.Execute(cast, default, actor);
            Assert.That(actor.Shields.Snapshot().Count, Is.EqualTo(2));
            Assert.That(actor.Attributes.GetCurrentValue(shield), Is.EqualTo(60));
            actor.Abilities.Cancel(grant);
            Assert.That(actor.Attributes.GetCurrentValue(shield), Is.EqualTo(20));
            var hit = new GameplayEffectApplicationContext(context, actor, GameplaySetByCallerValues.Empty);
            new GameplayDamageExecution(N(150), Array.Empty<GameplayDamageStage>()).Execute(hit);
            Assert.That(actor.Shields.Snapshot(), Is.Empty);
            Assert.That(expired, Is.EqualTo(1));
            Assert.That(removed, Is.EqualTo(2));
            Assert.That(broken, Is.Zero);
        }

        private sealed class ShieldView : IGameplayEffectPresentationDefinition { }

        private sealed class Observe : IGameplaySkillAction
        {
            private readonly Action<GameplayContext, IGameplaySkillHost> _observe;
            internal Observe(Action<GameplayContext, IGameplaySkillHost> observe) => _observe = observe;
            public GameplaySkillActionResult Execute(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host)
            {
                _observe(context, host);
                return GameplaySkillActionResult.Succeeded;
            }
        }
        private static IGameplayFloatValue N(float value)=>new GameplayConstantFloatValue(value);
    }
}
