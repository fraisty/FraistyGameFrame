using System;
using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Ability
{
    public sealed class GameplayAbilityCompletionTests
    {
        private static readonly GameplayTag Root = new GameplayTag("Ability.Test");
        private static readonly GameplayTag A = new GameplayTag("Ability.Test.A");
        private static readonly GameplayTag B = new GameplayTag("Ability.Test.B");
        private static readonly GameplayTag Other = new GameplayTag("Ability.Other");
        private static readonly GameplayTag Active = new GameplayTag("State.Test.Active");
        private static readonly GameplayTag Stun = new GameplayTag("State.Test.Stun");
        private static readonly GameplayTag Cooldown = new GameplayTag("Cooldown.Test.Ability");

        [Test]
        public void AbilityIdentityRelationsBlockAndCancelInGrantOrderWithoutRefund()
        {
            var setup = new Setup();
            var identity = new[] { A };
            var first = setup.Abilities.Grant(Definition(abilityTags: identity,
                block: new[] { B }, charge: new GameplayAbilityChargeDefinition(1, 5f)));
            identity[0] = Other;
            Assert.That(setup.Abilities.TryActivate(first), Is.True);
            var blocked = setup.Abilities.Grant(Definition(abilityTags: new[] { B }));
            var unrelated = setup.Abilities.Grant(Definition(abilityTags: new[] { Other }));
            Assert.That(setup.Abilities.GetStatus(blocked).BlockReason,
                Is.EqualTo(GameplayAbilityBlockReason.BlockedByAbility));
            Assert.That(setup.Abilities.CanActivate(unrelated), Is.True);
            var cancel = setup.Abilities.Grant(Definition(cancel: new[] { Root }));
            Assert.That(setup.Abilities.TryActivate(cancel), Is.True);
            Assert.That(setup.Abilities.IsActive(first), Is.False);
            Assert.That(setup.Abilities.GetStatus(first).CurrentCharges, Is.Zero);
            Assert.That(setup.Abilities.GetStatus(blocked).CanActivate, Is.True);
            Assert.That(setup.Host.Tags.HasTag(A), Is.False, "Ability identity is not an owner state tag.");
            Assert.That(setup.Abilities.GetStatus(first).BlockReason, Is.EqualTo(GameplayAbilityBlockReason.NoCharges));
        }

        [Test]
        public void OngoingStunCancelsAndCleansActivationTagsEffectsAndContext()
        {
            var setup = new Setup();
            var effect = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Infinite, 0f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), new[] { Active });
            var handle = setup.Abilities.Grant(Definition(owned: new[] { A }, ongoing: new[] { Stun },
                activationEffects: new[] { effect }));
            var context = new GameplayContext(setup.Host.OwnerId,
                new GameplayTargetData(new GameplaySourceId(900)));
            Assert.That(setup.Abilities.TryActivate(handle, context), Is.True);
            Assert.That(setup.Host.Tags.HasTag(A), Is.True);
            Assert.That(setup.Host.Tags.HasTag(Active), Is.True);
            Assert.That(setup.Abilities.TryGetActiveContext(handle, out _), Is.True);
            setup.Host.Tags.AddTag(Stun, setup.Ids.Allocate());
            setup.Abilities.Advance(0f);
            Assert.That(setup.Abilities.IsActive(handle), Is.False);
            Assert.That(setup.Host.Tags.HasTag(A), Is.False);
            Assert.That(setup.Host.Tags.HasTag(Active), Is.False);
            Assert.That(setup.Abilities.TryGetActiveContext(handle, out _), Is.False);
        }

        [Test]
        public void CostStatusCommitFailureAndChargePreserveTransaction()
        {
            var setup = new Setup();
            var cost = new ResourceCost(2);
            var effect = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Infinite, 0f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), new[] { Active });
            var handle = setup.Abilities.Grant(Definition(owned: new[] { A }, cost: cost,
                charge: new GameplayAbilityChargeDefinition(2, 3f), activationEffects: new[] { effect }));
            setup.Host.Resource = 1;
            Assert.That(setup.Abilities.GetStatus(handle).BlockReason, Is.EqualTo(GameplayAbilityBlockReason.Cost));
            Assert.That(setup.Abilities.TryActivate(handle), Is.False);
            setup.Host.Resource = 5;
            setup.Host.RejectPayment = true;
            Assert.That(setup.Abilities.TryActivate(handle), Is.False);
            Assert.That(setup.Host.Resource, Is.EqualTo(5));
            Assert.That(setup.Abilities.GetStatus(handle).CurrentCharges, Is.EqualTo(2));
            Assert.That(setup.Host.Tags.HasTag(A) || setup.Host.Tags.HasTag(Active), Is.False);
            setup.Host.RejectPayment = false;
            Assert.That(setup.Abilities.TryActivate(handle), Is.True);
            Assert.That(setup.Host.Resource, Is.EqualTo(3));
            Assert.That(setup.Abilities.GetStatus(handle).CurrentCharges, Is.EqualTo(1));
            Assert.That(setup.Abilities.End(handle), Is.True);
            Assert.That(setup.Abilities.GetStatus(handle).CurrentCharges, Is.EqualTo(1));
        }

        [Test]
        public void ActivationEffectRejectionRollsBackEarlierEffectsAndCooldown()
        {
            var setup = new Setup();
            var noStun = new GameplayTagQuery(Array.Empty<GameplayTag>(),
                Array.Empty<GameplayTag>(), new[] { Stun });
            var firstEffect = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Infinite, 0f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), new[] { Active });
            var rejectedEffect = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Infinite, 0f, 0f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), Array.Empty<GameplayTag>(),
                Array.Empty<IGameplayEffectExecution>(), null, noStun);
            var cooldown = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 5f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), new[] { Cooldown });
            var handle = setup.Abilities.Grant(Definition(owned: new[] { A }, cooldown: cooldown,
                activationEffects: new[] { firstEffect, rejectedEffect }));
            setup.Host.Tags.AddTag(Stun, setup.Ids.Allocate());
            Assert.That(setup.Abilities.TryActivate(handle), Is.False);
            Assert.That(setup.Abilities.IsActive(handle), Is.False);
            Assert.That(setup.Host.Tags.HasTag(A) || setup.Host.Tags.HasTag(Active) ||
                setup.Host.Tags.HasTag(Cooldown), Is.False);
            Assert.That(setup.Abilities.TryGetCooldownRemaining(handle, out _), Is.False);
        }

        [Test]
        public void FailedCostCommitRemovesCooldownAndActivationEffects()
        {
            var setup = new Setup();
            setup.Host.Resource = 5;
            setup.Host.RejectPayment = true;
            var cooldown = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 5f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), new[] { Cooldown });
            var activeEffect = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Infinite, 0f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), new[] { Active });
            var handle = setup.Abilities.Grant(Definition(owned: new[] { A }, cooldown: cooldown,
                cost: new ResourceCost(2), activationEffects: new[] { activeEffect }));
            Assert.That(setup.Abilities.TryActivate(handle), Is.False);
            Assert.That(setup.Host.Resource, Is.EqualTo(5));
            Assert.That(setup.Abilities.IsActive(handle), Is.False);
            Assert.That(setup.Abilities.TryGetCooldownRemaining(handle, out _), Is.False);
            Assert.That(setup.Host.Tags.HasTag(A) || setup.Host.Tags.HasTag(Active) ||
                setup.Host.Tags.HasTag(Cooldown), Is.False);
        }

        [Test]
        public void CooldownValidationAndSelfTargetPeriodicContext()
        {
            var setup = new Setup();
            var capture = new CaptureExecution();
            var periodic = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 4f, 1f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), Array.Empty<GameplayTag>(),
                new IGameplayEffectExecution[] { capture });
            var handle = setup.Abilities.Grant(Definition(activationEffects: new[] { periodic }));
            var source = setup.Ids.Allocate();
            var context = new GameplayContext(setup.Host.OwnerId,
                new GameplayTargetData(new GameplaySourceId(900)), source);
            Assert.That(setup.Abilities.TryActivate(handle, context), Is.True);
            setup.Host.Effects.Advance(1f);
            Assert.That(capture.Last.Context.TargetData.PrimaryTargetId, Is.EqualTo(setup.Host.OwnerId));
            Assert.That(capture.Last.Context.EffectiveSourceId, Is.EqualTo(source));
            Assert.That(setup.Abilities.Remove(handle), Is.True);
            var invalidCooldown = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration, 4f, 1f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), new[] { Cooldown },
                new IGameplayEffectExecution[] { capture });
            Assert.Throws<ArgumentException>(() => Definition(cooldown: invalidCooldown));
            Assert.Throws<ArgumentException>(() => Definition(activationEffects: new[] {
                new GameplayEffectDefinition(GameplayEffectDurationPolicy.Instant, 0f, 0f,
                    Array.Empty<GameplayEffectAttributeModifierDefinition>(), Array.Empty<GameplayTag>(),
                    new IGameplayEffectExecution[] { capture }) }));
        }

        [Test]
        public void CooldownRejectsNonSimpleEffectsAndContextMismatchFailsBeforeActivation()
        {
            var noStun = new GameplayTagQuery(Array.Empty<GameplayTag>(),
                Array.Empty<GameplayTag>(), new[] { Stun });
            var modifiers = new[] { new GameplayEffectAttributeModifierDefinition(
                new GameplayAttributeId("Test.Ability.Stat"), GameplayModifierOperation.Add, 1f) };
            Assert.Throws<ArgumentException>(() => Definition(cooldown: new GameplayEffectDefinition(
                GameplayEffectDurationPolicy.Infinite, 0f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), new[] { Cooldown })));
            Assert.Throws<ArgumentException>(() => Definition(cooldown: new GameplayEffectDefinition(
                GameplayEffectDurationPolicy.Duration, 5f, modifiers, new[] { Cooldown })));
            Assert.Throws<ArgumentException>(() => Definition(cooldown: new GameplayEffectDefinition(
                GameplayEffectDurationPolicy.Duration, 5f, 0f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), new[] { Cooldown },
                Array.Empty<IGameplayEffectExecution>(), null, noStun)));
            var stacking = new GameplayEffectStackingDefinition(new GameplayTag("Effect.Test.Cooldown"),
                GameplayEffectStackAggregation.AggregateByTarget, 2,
                GameplayEffectStackDurationRefreshPolicy.NeverRefresh,
                GameplayEffectStackPeriodResetPolicy.NeverReset,
                GameplayEffectStackOverflowPolicy.Reject);
            Assert.Throws<ArgumentException>(() => Definition(cooldown: new GameplayEffectDefinition(
                GameplayEffectDurationPolicy.Duration, 5f, 0f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), new[] { Cooldown },
                Array.Empty<IGameplayEffectExecution>(), stacking)));

            var setup = new Setup();
            var handle = setup.Abilities.Grant(Definition(cooldown: new GameplayEffectDefinition(
                GameplayEffectDurationPolicy.Duration, 5f,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(), new[] { Cooldown })));
            var wrong = new GameplayContext(setup.Ids.Allocate(),
                new GameplayTargetData(setup.Host.OwnerId));
            Assert.Throws<ArgumentException>(() => setup.Abilities.TryActivate(handle, wrong));
            Assert.That(setup.Abilities.IsActive(handle), Is.False);
            Assert.That(setup.Host.Tags.HasTag(Cooldown), Is.False);
        }

        /// <summary>Prevents irreversible initial execution on rejected/throwing payment and
        /// guards the distinct paid, exactly-once postcommit exception boundary.</summary>
        [TestCase(0)] [TestCase(1)] [TestCase(2)] [TestCase(3)]
        public void InitialExecutionStartsOnlyAfterAtomicPayment(int mode)
        {
            var setup=new Setup();setup.Host.Resource=20;
            var execution=new InitialExecution(setup,mode==3);
            var effect=new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration,3,1,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(),new[]{Active},new IGameplayEffectExecution[]{execution},null,
                periodicStart:GameplayEffectPeriodicStartPolicy.ExecuteOnApplication);
            var cooldown=new GameplayEffectDefinition(GameplayEffectDurationPolicy.Duration,5,Array.Empty<GameplayEffectAttributeModifierDefinition>(),new[]{Cooldown});
            bool useCharge=mode%2==0;
            GameplayAbilityHandle handle=default;
            var cost=new AtomicCost(mode,()=>{
                Assert.Throws<InvalidOperationException>(()=>setup.Abilities.Advance(.5f));
                Assert.Throws<InvalidOperationException>(()=>setup.Host.Effects.Advance(.5f));
                Assert.That(setup.Abilities.Remove(handle),Is.False,"Precommit removal cannot detach the in-flight grant.");
            });
            handle=setup.Abilities.Grant(Definition(owned:new[]{A},cooldown:useCharge?null:cooldown,cost:cost,
                charge:useCharge?new GameplayAbilityChargeDefinition(2,8):null,activationEffects:new[]{effect}));
            execution.Handle=handle;
            int removals=0;setup.Host.Effects.Removed+=notification=>removals++;
            if(mode==1||mode==3)Assert.Throws<InvalidOperationException>(()=>setup.Abilities.TryActivate(handle));
            else Assert.That(setup.Abilities.TryActivate(handle),Is.EqualTo(mode==2));
            bool paid=mode>=2;
            Assert.That(execution.Calls,Is.EqualTo(paid?1:0));
            Assert.That(setup.Host.Resource,Is.EqualTo(paid?5:20));
            Assert.That(setup.Abilities.IsActive(handle),Is.EqualTo(paid));
            if(useCharge)Assert.That(setup.Abilities.GetStatus(handle).CurrentCharges,Is.EqualTo(paid?1:2));
            Assert.That(setup.Host.Tags.HasTag(Cooldown),Is.EqualTo(paid&&!useCharge));
            Assert.That(removals,Is.Zero,"Unpublished failed contributions must not emit Removed events.");
            if(paid){Assert.That(execution.Reentered,Is.False);setup.Abilities.Cancel(handle);Assert.That(setup.Host.Tags.HasTag(Active),Is.False);}
            else Assert.That(setup.Host.Effects.GetActiveEntriesSnapshot().Count,Is.Zero);
        }
        private sealed class AtomicCost:IGameplayAbilityCost
        {
            private readonly int _mode;private readonly Action _before;
            internal AtomicCost(int mode,Action before){_mode=mode;_before=before;}
            public bool CanPay(IGameplayAbilityHost host)=>((Host)host).Resource>=20;
            public bool TryCommit(IGameplayAbilityHost host){_before();if(_mode==1)throw new InvalidOperationException("Payment rejected");if(_mode==0)return false;((Host)host).Resource-=5;return true;}
        }
        private sealed class InitialExecution:IGameplayEffectExecution
        {
            private readonly Setup _setup;private readonly bool _throws;
            internal int Calls;internal bool Reentered;internal GameplayAbilityHandle Handle;
            internal InitialExecution(Setup setup,bool throws){_setup=setup;_throws=throws;}
            public void Execute(GameplayEffectApplicationContext application){Calls++;Reentered=_setup.Abilities.TryActivate(Handle);_setup.Host.Resource-=10;if(_throws)throw new InvalidOperationException("Committed initial execution");}
        }
        private static GameplayAbilityDefinition Definition(GameplayTag[] abilityTags = null,
            GameplayTag[] owned = null, GameplayTag[] cancel = null, GameplayTag[] block = null,
            GameplayTag[] ongoing = null, GameplayAbilityChargeDefinition charge = null,
            GameplayEffectDefinition cooldown = null, IGameplayAbilityCost cost = null,
            GameplayEffectDefinition[] activationEffects = null) =>
            new GameplayAbilityDefinition(Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>(),
                owned ?? Array.Empty<GameplayTag>(), cooldown, charge, abilityTags: abilityTags,
                cancelAbilitiesWithTags: cancel, blockAbilitiesWithTags: block,
                ongoingBlockedTags: ongoing, cost: cost, activationEffects: activationEffects);

        private sealed class Setup
        {
            public readonly GameplaySourceIdAllocator Ids = new GameplaySourceIdAllocator();
            public readonly Host Host;
            public readonly GameplayAbilityContainer Abilities;
            public Setup()
            {
                Host = new Host(Ids.Allocate(), Ids);
                Abilities = new GameplayAbilityContainer(Host, Ids);
            }
        }

        private sealed class Host : IGameplayAbilityHost
        {
            public GameplaySourceId OwnerId { get; }
            public GameplaySourceId TargetId => OwnerId;
            public AttributeSet Attributes { get; } = new AttributeSet();
            public GameplayTagContainer Tags { get; } = new GameplayTagContainer();
            public GameplayEffectContainer Effects { get; }
            public int Resource;
            public bool RejectPayment;
            public Host(GameplaySourceId id, GameplaySourceIdAllocator ids)
            { OwnerId = id; Effects = new GameplayEffectContainer(Attributes, ids, Tags); }
        }

        private sealed class ResourceCost : IGameplayAbilityCost
        {
            private readonly int _amount;
            public ResourceCost(int amount) { _amount = amount; }
            public bool CanPay(IGameplayAbilityHost host) => ((Host)host).Resource >= _amount;
            public bool TryCommit(IGameplayAbilityHost host)
            {
                var typed = (Host)host;
                if (typed.RejectPayment || typed.Resource < _amount) return false;
                typed.Resource -= _amount;
                return true;
            }
        }

        private sealed class CaptureExecution : IGameplayEffectExecution
        {
            public GameplayEffectApplicationContext Last;
            public void Execute(GameplayEffectApplicationContext application) { Last = application; }
        }
    }
}
