using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay;
using FRFrame.Gameplay.Extensions;
using FRFrame.Gameplay.Standard;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay
{
    public sealed class GameplayStandardSpatialTests
    {
        [Test]
        public void SweptProjectileKeepsCastProvenanceAndDoesNotConsumeItsBirthStep()
        {
            using(var world=new GameplayStandardWorld())
            {
                var caster=world.CreateActor(default,1);world.CreateActor(new GameplayPoint(3,0),2);world.CreateActor(new GameplayPoint(4,0),2);
                var hits=new List<GameplayContext>();int cancels=0;
                var projectile=new GameplayProjectileDefinition(new GameplayProjectileMotionDefinition(GameplayProjectileMotionKind.Straight),N(8),N(2),N(.2f),N(10),N(1),maxHits:2,
                    onHit:new[]{(IGameplaySkillAction)new Observe(c=>hits.Add(c))},onCancelled:new[]{(IGameplaySkillAction)new Observe(c=>cancels++)});
                var marker=new GameplaySequenceSkillStepMarkerDefinition(.5f,Array.Empty<IGameplaySkillCondition>(),new GameplaySkillSelfTarget(),new[]{(IGameplaySkillAction)new GameplaySpawnProjectileAction(projectile)});
                var skill=Install(caster,new GameplayOwnedSequenceAction(new GameplaySequenceDefinition(3,new[]{marker})));
                Assert.That(skill.TryCast(),Is.True);
                world.Advance(.5f);var operation=(GameplayProjectile)world.GetSpatialSnapshot()[0];
                Assert.That(operation.Position.X,Is.Zero,"A marker-born projectile cannot consume the past half-second.");
                world.Advance(.5f);
                Assert.That(hits.Count,Is.EqualTo(2),"One swept step crosses both targets without duplicate collider/target hits.");
                foreach(var hit in hits){Assert.That(hit.AbilityLevel,Is.EqualTo(5));Assert.That(hit.OriginSourceId,Is.EqualTo(skill.LastCastContext.OriginSourceId));Assert.That(hit.EffectiveSourceId,Is.EqualTo(operation.Id));}
                Assert.That(operation.EndReason,Is.EqualTo(GameplaySpatialEndReason.HitLimit));
                world.Advance(.1f);Assert.That(hits.Count,Is.EqualTo(2));
                var ended=world.SpawnProjectile(caster,skill.LastCastContext,projectile);
                caster.Abilities.End(skill.Grant);Assert.That(ended.EndReason,Is.EqualTo(GameplaySpatialEndReason.Completed));Assert.That(cancels,Is.Zero,"Normal End must not execute OnCancelled.");
                Assert.That(skill.TryCast(),Is.True);world.SpawnProjectile(caster,skill.LastCastContext,projectile);skill.Cancel();
                Assert.That(cancels,Is.EqualTo(1));Assert.That(world.GetSpatialSnapshot().Count,Is.Zero);
            }
        }
        [Test]
        public void OverlappingAuraAndInterruptedMovementReleaseOnlyTheirOwnResources()
        {
            using(var world=new GameplayStandardWorld())
            {
                var a=world.CreateActor(default,1);var b=world.CreateActor(default,1);
                GameplayPoint position=new GameplayPoint(1,0);GameplayDirection facing=new GameplayDirection(1,0);
                var target=world.CreateActor(position,2,readPosition:()=>position,writePosition:p=>position=p,readFacing:()=>facing,writeFacing:d=>facing=d);
                var first=Install(a,new Observe(c=>{}));var second=Install(b,new Observe(c=>{}));first.TryCast();second.TryCast();
                var tag=new GameplayTag("State.Test.Spatial.Aura");
                var aura=new GameplayEffectDefinition(GameplayEffectDurationPolicy.Infinite,0,Array.Empty<GameplayEffectAttributeModifierDefinition>(),new[]{tag});
                var region=new GameplayAreaDefinition(GameplaySearchShape.Circle,GameplaySpatialPlane.XY,GameplaySearchOrigin.Owner,N(2),N(120),N(4),N(2),N(1),N(3),N(1),aura:new[]{aura});
                var areaA=world.SpawnArea(a,first.LastCastContext,region);var areaB=world.SpawnArea(b,second.LastCastContext,region);
                Assert.That(target.Effects.GetActiveEntriesSnapshot().Count,Is.EqualTo(2));areaA.Dispose();
                Assert.That(target.Tags.HasTag(tag),Is.True);Assert.That(target.Effects.GetActiveEntriesSnapshot().Count,Is.EqualTo(1));
                position=new GameplayPoint(4,0);world.Advance(.5f);Assert.That(target.Tags.HasTag(tag),Is.False);
                position=new GameplayPoint(1,0);world.Advance(.5f);Assert.That(areaB.InsideCount,Is.EqualTo(1));
                second.Cancel();Assert.That(target.Effects.GetActiveEntriesSnapshot().Count,Is.Zero);
                int interrupted=0,arrived=0;
                var low=world.StartMovement(a,new GameplayDisplacementRequest(first.LastCastContext.WithTarget(new GameplayTargetData(target.Id)),target.Id,GameplayDisplacementMode.Directional,2,2,new GameplayDirection(1,0)),
                    onCancelled:new[]{(IGameplaySkillAction)new Observe(c=>interrupted++)});
                var high=world.StartMovement(a,new GameplayDisplacementRequest(first.LastCastContext.WithTarget(new GameplayTargetData(target.Id)),target.Id,GameplayDisplacementMode.Directional,2,2,new GameplayDirection(0,1)),11,
                    onArrive:new[]{(IGameplaySkillAction)new Observe(c=>arrived++)});
                Assert.That(low.EndReason,Is.EqualTo(GameplaySpatialEndReason.Interrupted));Assert.That(interrupted,Is.EqualTo(1));
                world.Advance(1);Assert.That(position.X,Is.EqualTo(1));Assert.That(position.Y,Is.EqualTo(2));Assert.That(facing.Y,Is.EqualTo(1));
                Assert.That(high.EndReason,Is.EqualTo(GameplaySpatialEndReason.Arrived));Assert.That(arrived,Is.EqualTo(1));
                first.Cancel();Assert.That(world.GetSpatialSnapshot().Count,Is.Zero);
            }
        }
        [Test]
        public void SummonedCastsKeepBirthOriginButOwnDistinctActivationAndCleanup()
        {
            using(var world=new GameplayStandardWorld())
            {
                var owner=world.CreateActor(default,1);var parent=Install(owner,new Observe(c=>{}));Assert.That(parent.TryCast(),Is.True);
                var health=new GameplayAttributeId("Health");var max=new GameplayAttributeId("MaxHealth");
                var profile=new GameplayActorProfile(new[]{new KeyValuePair<GameplayAttributeId,float>(health,100),new KeyValuePair<GameplayAttributeId,float>(max,100)},new GameplayCombatAttributeMap(health,max,default,default));
                int executions=0,ends=0;
                var signal=new GameplayTag("Event.Test.Summon.Child");
                var child=new GameplaySkillDocumentDefinition(2,new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),maxLevel:10),
                    new GameplaySkillDefinition(new GameplaySkillEventTriggerDefinition(signal),Array.Empty<IGameplaySkillCondition>(),new GameplaySkillSelfTarget(),new[]{(IGameplaySkillAction)new Observe(c=>executions++)},false),signal);
                var definition=new GameplaySummonDefinition(profile,N(2),new[]{child},onEnd:new[]{(IGameplaySkillAction)new Observe(c=>ends++)});
                var summon=world.SpawnSummon(owner,parent.LastCastContext,definition);var npc=summon.SummonedActor;var first=npc.InstalledSkills[0];
                var second=npc.InstallDocument(new GameplaySkillDocumentDefinition(3,child.Ability,child.Rules,signal),5);
                Assert.That(first.TryCast(),Is.True);Assert.That(second.TryCast(),Is.True);Assert.That(executions,Is.EqualTo(2),"Two active grants sharing a birth root must not execute each other's primary rule.");
                Assert.That(first.LastCastContext.OriginSourceId,Is.EqualTo(parent.LastCastContext.OriginSourceId));
                Assert.That(first.LastCastContext.AbilityLevel,Is.EqualTo(5));Assert.That(first.LastCastContext.ActivationSourceId,Is.Not.EqualTo(second.LastCastContext.ActivationSourceId));
                var projectile=world.SpawnProjectile(npc,first.LastCastContext,new GameplayProjectileDefinition(new GameplayProjectileMotionDefinition(GameplayProjectileMotionKind.Straight),N(1),N(10),N(.1f),N(20),N(0)));
                Assert.That(GameplayStandardActivation.TryFindGrant(npc,projectile.Context,out var found),Is.True);Assert.That(found,Is.EqualTo(first.Grant));
                first.Cancel();Assert.That(projectile.IsDisposed,Is.True);Assert.That(second.Status.Phase,Is.EqualTo(GameplayAbilityPhase.Active));
                parent.Cancel();Assert.That(npc.IsDisposed,Is.True);Assert.That(world.Actors.Count,Is.EqualTo(1));Assert.That(ends,Is.EqualTo(1));
                summon.Dispose();Assert.That(ends,Is.EqualTo(1));
                parent.TryCast();var expires=world.SpawnSummon(owner,parent.LastCastContext,definition);world.Advance(2);
                Assert.That(expires.EndReason,Is.EqualTo(GameplaySpatialEndReason.Completed));Assert.That(expires.SummonedActor.IsDisposed,Is.True);Assert.That(world.GetSpatialSnapshot().Count,Is.Zero);
            }
        }
        private static IGameplayFloatValue N(float number)=>new GameplayConstantFloatValue(number);
        [Test]
        public void StraightDestinationRangeAndZeroTimeAreaHaveDistinctBoundaries()
        {
            using(var world=new GameplayStandardWorld())
            {
                var actor=world.CreateActor(default,1);var target=world.CreateActor(new GameplayPoint(2,0),2);
                var skill=Install(actor,new Observe(c=>{}));skill.TryCast();
                // A point without a primary target captures the formal endpoint.
                var context=skill.LastCastContext.WithTarget(new GameplayTargetData(default,new GameplayPoint(3,0)));
                int arrive=0,range=0,hits=0;
                Func<GameplayStraightDestinationMode,float,GameplayProjectileDefinition> shot=(mode,maxRange)=>new GameplayProjectileDefinition(
                    new GameplayProjectileMotionDefinition(GameplayProjectileMotionKind.Straight,straightDestination:mode),N(6),N(5),N(.1f),N(maxRange),N(0),maxHits:10,
                    onHit:new[]{(IGameplaySkillAction)new Observe(c=>hits++)},onArrive:new[]{(IGameplaySkillAction)new Observe(c=>arrive++)},onRange:new[]{(IGameplaySkillAction)new Observe(c=>range++)});
                var point=world.SpawnProjectile(actor,context,shot(GameplayStraightDestinationMode.CapturedDestination,10));world.Advance(.75f);
                Assert.That(point.Position.X,Is.EqualTo(3));Assert.That(point.EndReason,Is.EqualTo(GameplaySpatialEndReason.Arrived));
                Assert.That(arrive,Is.EqualTo(1));Assert.That(hits,Is.EqualTo(1));world.Advance(0);Assert.That(arrive,Is.EqualTo(1));
                var direction=world.SpawnProjectile(actor,context,shot(GameplayStraightDestinationMode.Direction,2));world.Advance(.75f);
                Assert.That(direction.EndReason,Is.EqualTo(GameplaySpatialEndReason.Range));Assert.That(direction.Position.X,Is.EqualTo(2));Assert.That(range,Is.EqualTo(1));Assert.That(arrive,Is.EqualTo(1));
                var clipped=world.SpawnProjectile(actor,context,shot(GameplayStraightDestinationMode.CapturedDestination,2));world.Advance(.75f);
                Assert.That(clipped.EndReason,Is.EqualTo(GameplaySpatialEndReason.Range),"A sampler reaching the endpoint cannot override a shorter range clip.");
                int enter=0,exit=0,stay=0,tick=0;
                var area=world.SpawnArea(actor,skill.LastCastContext,new GameplayAreaDefinition(GameplaySearchShape.Circle,GameplaySpatialPlane.XY,GameplaySearchOrigin.Owner,N(3),N(120),N(1),N(1),N(1),N(2),N(.1f),tickOnEnter:false,
                    onEnter:new[]{(IGameplaySkillAction)new Observe(c=>enter++)},onExit:new[]{(IGameplaySkillAction)new Observe(c=>exit++)},
                    onStay:new[]{(IGameplaySkillAction)new Observe(c=>stay++)},onTick:new[]{(IGameplaySkillAction)new Observe(c=>tick++)}));
                world.Advance(0);world.Advance(0);Assert.That(enter,Is.EqualTo(1));Assert.That(stay,Is.Zero);Assert.That(tick,Is.Zero);
                world.Advance(.1f);Assert.That(stay,Is.EqualTo(1));Assert.That(tick,Is.EqualTo(1));
                target.Position=new GameplayPoint(6,0);world.Advance(0);world.Advance(0);Assert.That(exit,Is.EqualTo(1));
                target.Position=new GameplayPoint(2,0);world.Advance(0);world.Advance(0);Assert.That(enter,Is.EqualTo(2));Assert.That(stay,Is.EqualTo(1));
                world.Advance(.2f);Assert.That(tick,Is.EqualTo(3));area.Dispose();Assert.That(exit,Is.EqualTo(2));
            }
        }
        [TestCase(true)]
        [TestCase(false)]
        public void AreaOwnershipKeepsExactAuraAndCallbackProvenance(bool cancelCast)
        {
            using var world = new GameplayStandardWorld();
            var actor = world.CreateActor(default, 1);
            var target = world.CreateActor(new GameplayPoint(1, 0), 2);
            var tag = new GameplayTag("State.Test.Area");
            var aura = new GameplayEffectDefinition(GameplayEffectDurationPolicy.Infinite,0,
                Array.Empty<GameplayEffectAttributeModifierDefinition>(),new[]{tag});
            var skill = Install(actor, new Observe(_=>{}));
            Assert.That(skill.TryCast(), Is.True);
            var cast = skill.LastCastContext;
            var ticks = new List<GameplayContext>();
            var exits = new List<GameplayContext>();
            var ends = new List<GameplayContext>();
            var managed = new GameplayAreaDefinition(GameplaySearchShape.Circle,GameplaySpatialPlane.XY,GameplaySearchOrigin.Owner,
                N(3),N(120),N(1),N(1),N(1),N(3),N(.5f),tickOnEnter:false,aura:new[]{aura},
                onTick:new[]{new Observe(ticks.Add)},onExit:new[]{new Observe(exits.Add)},onEnd:new[]{new Observe(ends.Add)},
                cancelOnAbilityEnd:false);
            var legacy = new GameplayAreaDefinition(GameplaySearchShape.Circle,GameplaySpatialPlane.XY,GameplaySearchOrigin.Owner,
                N(3),N(120),N(1),N(1),N(1),N(3),N(.5f),tickOnEnter:false,aura:new[]{aura});
            var area = world.SpawnArea(actor,cast,managed);
            var old = world.SpawnArea(actor,cast,legacy);
            if (cancelCast) skill.Cancel();
            else actor.Abilities.End(skill.Grant);
            Assert.That(old.EndReason, Is.EqualTo(cancelCast ? GameplaySpatialEndReason.Cancelled : GameplaySpatialEndReason.Completed));
            Assert.That(area.IsDisposed, Is.False);
            Assert.That(target.Effects.GetActiveEntriesSnapshot().Count, Is.EqualTo(1));
            Assert.That(GameplayStandardActivation.TryFindGrant(actor,cast,out _), Is.False);
            Assert.That(new GameplaySpawnAreaAction(managed).Execute(cast,default,actor), Is.EqualTo(GameplaySkillActionResult.Rejected));

            world.Advance(1);
            Assert.That(skill.TryCast(), Is.True);
            var second = world.SpawnArea(actor,skill.LastCastContext,managed);
            world.Advance(2);
            Assert.That(area.EndReason, Is.EqualTo(GameplaySpatialEndReason.Completed));
            Assert.That(second.IsDisposed, Is.False);
            Assert.That(target.Effects.GetActiveEntriesSnapshot().Count, Is.EqualTo(1));
            Assert.That(target.Tags.HasTag(tag), Is.True);
            Assert.That(ticks.FindAll(ctx=>ctx.EffectiveSourceId==area.Id).Count, Is.EqualTo(6));
            foreach (var ctx in ticks.FindAll(ctx=>ctx.EffectiveSourceId==area.Id))
                Assert.That(ctx.ActivationSourceId, Is.EqualTo(cast.ActivationSourceId));
            Assert.That(exits[0].TargetData.PrimaryTargetId, Is.EqualTo(target.Id));
            Assert.That(ends[0].EffectiveSourceId, Is.EqualTo(area.Id));
            world.Advance(1);
            Assert.That(exits.Count, Is.EqualTo(2));
            Assert.That(ends.Count, Is.EqualTo(2));
            Assert.That(target.Tags.HasTag(tag), Is.False);
            Assert.That(target.Effects.GetActiveEntriesSnapshot(), Is.Empty);
        }

        [TestCase(GameplaySpatialPlane.XY, 30, false)]
        [TestCase(GameplaySpatialPlane.XZ, 60, false)]
        [TestCase(GameplaySpatialPlane.XY, 30, true)]
        public void HitWindowSweepsRelativeHurtboxMotionWithoutDoubleHits(GameplaySpatialPlane plane, int hz, bool teleport)
        {
            using var world = new GameplayStandardWorld();
            var owner = world.CreateActor(default, 1);
            GameplayPoint Along(float y) => plane == GameplaySpatialPlane.XY ? new GameplayPoint(.45f, y) : new GameplayPoint(.45f, 0, y);
            var target = world.CreateActor(Along(-4), 2);
            target.Hurtbox = new GameplayHurtbox(.25f);
            var hits = new List<GameplayContext>();
            var definition = new GameplayHitWindowDefinition(GameplaySearchShape.Circle, plane, default, .08f,
                radius:.25f, onHit:new[] { new Observe(hits.Add) });
            var skill = Install(owner, new GameplayOpenHitWindowAction(definition));
            Assert.That(skill.TryCast(), Is.True);
            var window = (GameplayHitWindowOperation)world.GetSpatialSnapshot()[0];
            Assert.That(hits, Is.Empty);
            if (teleport) target.TeleportTo(Along(4));
            else target.Position = Along(4);
            world.Advance(1f / hz);
            Assert.That(hits.Count, Is.EqualTo(teleport ? 0 : 1));
            target.Position = Along(0);
            world.Advance(1f / hz);
            Assert.That(hits.Count, Is.EqualTo(1));
            Assert.That(hits[0].TargetData.PrimaryTargetId, Is.EqualTo(target.Id));
            Assert.That(hits[0].EffectiveSourceId, Is.EqualTo(window.Id));
            Assert.That(hits[0].ActivationSourceId, Is.EqualTo(skill.LastCastContext.ActivationSourceId));
            world.Advance(.1f);
            Assert.That(window.EndReason, Is.EqualTo(GameplaySpatialEndReason.Completed));
        }

        [Test]
        public void HitWindowsStartAtTheirMarkerAndCancelWithTheCast()
        {
            using var world = new GameplayStandardWorld();
            var owner = world.CreateActor(default, 1);
            var enemy = world.CreateActor(new GameplayPoint(1, 0), 2);
            world.CreateActor(new GameplayPoint(1, 0), 2);
            var hits = new List<GameplayContext>(); var interrupted = new List<GameplayContext>();
            var definition = new GameplayHitWindowDefinition(GameplaySearchShape.Sector, GameplaySpatialPlane.XY, default,
                .08f, onHit:new[] { new Observe(hits.Add) }, onCancelled:new[] { new Observe(interrupted.Add) });
            var actions = new[] { new GameplayOpenHitWindowAction(definition) };
            var start = new GameplaySequenceSkillStepMarkerDefinition(.06f, Array.Empty<IGameplaySkillCondition>(), new GameplaySkillSelfTarget(), actions);
            var second = new GameplaySequenceSkillStepMarkerDefinition(.31f, Array.Empty<IGameplaySkillCondition>(), new GameplaySkillSelfTarget(), actions);
            var skill = Install(owner, new GameplayOwnedSequenceAction(new GameplaySequenceDefinition(1, new[] { start, second })));
            skill.TryCast();
            world.Advance(.06f);
            var first = (GameplayHitWindowOperation)world.GetSpatialSnapshot()[0];
            Assert.That(first.Age, Is.Zero);
            Assert.That(hits[0].TargetData.PrimaryTargetId, Is.EqualTo(enemy.Id));
            world.Advance(.25f);
            Assert.That(first.EndReason, Is.EqualTo(GameplaySpatialEndReason.Completed));
            Assert.That(hits.Count, Is.EqualTo(2));
            Assert.That(hits[1].EffectiveSourceId, Is.Not.EqualTo(first.Id));
            skill.Cancel();
            Assert.That(interrupted.Count, Is.EqualTo(1));
            Assert.That(world.GetSpatialSnapshot(), Is.Empty);
        }

        [TestCase(90, false, 1)]
        [TestCase(140, false, 1)]
        [TestCase(140, true, 0)]
        [TestCase(180, false, 1)]
        public void HitSectorSweepsRotationUnlessThePoseWasCut(float degrees, bool cut, int expectedHits)
        {
            using var world = new GameplayStandardWorld();
            var owner = world.CreateActor(default, 1);
            float radians = degrees * MathF.PI / 180;
            world.CreateActor(new GameplayPoint(MathF.Cos(radians / 2), MathF.Sin(radians / 2)), 2);
            int hits = 0;
            var definition = new GameplayHitWindowDefinition(GameplaySearchShape.Sector, GameplaySpatialPlane.XY,
                default, .08f, angle:10, onHit:new[] { new Observe(_ => hits++) });
            Install(owner, new GameplayOpenHitWindowAction(definition)).TryCast();
            owner.Facing = new GameplayDirection(degrees == 180 ? -1 : MathF.Cos(radians), degrees == 180 ? 0 : MathF.Sin(radians));
            if(cut) owner.MarkHitPoseDiscontinuous();
            world.Advance(.03f);
            Assert.That(hits, Is.EqualTo(expectedHits));
        }

        [Test]
        public void HitWindowRehitsAtTheSameContactTimesAndStopsInsideTheStep()
        {
            string[] Run(float step, bool transit = false, bool cancel = false)
            {
                using var world = new GameplayStandardWorld();
                var owner = world.CreateActor(default, 1);
                owner.SkillTrace.Enabled = true;
                var chosen = world.CreateActor(new GameplayPoint(transit ? -4 : 1, 0), 2);
                world.CreateActor(new GameplayPoint(transit ? 5 : 1, 0), 2);
                GameplayInstalledSkill skill = null;
                int hits = 0;
                var definition = new GameplayHitWindowDefinition(GameplaySearchShape.Circle, GameplaySpatialPlane.XY,
                    default, .08f, maxHitsPerTarget:5, rehitInterval:.015f, onHit:new[] { new Observe(ctx =>
                    {
                        Assert.That(ctx.TargetData.PrimaryTargetId, Is.EqualTo(chosen.Id));
                        if (++hits == 2 && cancel) skill.Cancel();
                    }) });
                skill = Install(owner, new GameplayOpenHitWindowAction(definition));
                Assert.That(skill.TryCast(), Is.True);
                var window = (GameplayHitWindowOperation)world.GetSpatialSnapshot()[0];
                int steps = (int)Math.Round(.08 / step);
                for (int i = 1; i <= steps; i++)
                {
                    if (transit) chosen.Position = new GameplayPoint(-4 + 8 * i / (float)steps, 0);
                    world.Advance(step);
                }
                Assert.That(window.EndReason, Is.EqualTo(cancel ? GameplaySpatialEndReason.Cancelled : GameplaySpatialEndReason.Completed));
                return owner.SkillTrace.Snapshot().Select(entry => entry.record)
                    .Where(record => record.Operation == "HitWindow.Contact").Select(record => record.Detail.Split(',')[0]).ToArray();
            }

            string At(float t) => $"t={t:0.000}";
            var times = new[] { At(0), At(.015f), At(.03f), At(.045f), At(.06f) };
            Assert.That(Run(.04f), Is.EqualTo(times));
            Assert.That(Run(.01f), Is.EqualTo(times));
            var transit = new[] { At(.02f), At(.035f), At(.05f) };
            Assert.That(Run(.08f, transit:true), Is.EqualTo(transit));
            Assert.That(Run(.01f, transit:true), Is.EqualTo(transit));
            Assert.That(Run(.08f, cancel:true), Is.EqualTo(times.Take(2)));
        }

        [TestCase(.08f)]
        [TestCase(.01f)]
        public void HitWindowStopsRehittingWhenSecondContactMovesTheHurtbox(float step)
        {
            using var world = new GameplayStandardWorld();
            var owner = world.CreateActor(default, 1);
            var enemy = world.CreateActor(new GameplayPoint(1, 0), 2);
            int hits = 0;
            var definition = new GameplayHitWindowDefinition(GameplaySearchShape.Circle, GameplaySpatialPlane.XY,
                default, .08f, maxHitsPerTarget:5, rehitInterval:.015f, onHit:new[] { new Observe(_ =>
                {
                    if (++hits == 2) enemy.Hurtbox = new GameplayHurtbox(0, new GameplayPoint(10, 0));
                }) });
            Assert.That(Install(owner, new GameplayOpenHitWindowAction(definition)).TryCast(), Is.True);
            var window = (GameplayHitWindowOperation)world.GetSpatialSnapshot()[0];
            for (int i = 0; i < Math.Round(.08 / step); i++) world.Advance(step);
            Assert.That(hits, Is.EqualTo(2));
            Assert.That(window.EndReason, Is.EqualTo(GameplaySpatialEndReason.Completed));
        }

        [TestCase(true)]
        [TestCase(false)]
        public void HitWindowInvalidatesPendingContactsAfterEventSubscriberMovesActors(bool turnOwner)
        {
            using var world = new GameplayStandardWorld();
            var owner = world.CreateActor(default, 1);
            var first = world.CreateActor(new GameplayPoint(1, 0), 2);
            var waiting = world.CreateActor(new GameplayPoint(4, 0), 2);
            var hits = new List<GameplaySourceId>();
            owner.Events.Subscribe(new GameplayTag("Event.Standard.HitWindow.Hit"), data =>
            {
                hits.Add(data.TargetId);
                if (hits.Count != 2) return;
                if (turnOwner) owner.Facing = new GameplayDirection(-1, 0);
                else waiting.Position = new GameplayPoint(-4, 0);
            });
            var definition = new GameplayHitWindowDefinition(GameplaySearchShape.Sector, GameplaySpatialPlane.XY,
                default, .16f, angle:10, maxDistinctTargets:2, maxHitsPerTarget:5, rehitInterval:.015f);
            Assert.That(Install(owner, new GameplayOpenHitWindowAction(definition)).TryCast(), Is.True);
            waiting.Position = new GameplayPoint(1.25f, 0);
            world.Advance(.08f);
            world.Advance(.08f);
            Assert.That(hits, Is.EqualTo(Enumerable.Repeat(first.Id, turnOwner ? 2 : 5)));
        }

        [Test]
        public void HitBoxAndCanonicalGeometryUseRollWithUnchangedForward()
        {
            using var world = new GameplayStandardWorld();
            var owner = world.CreateActor(default, 1);
            var target = world.CreateActor(new GameplayPoint(1, .35f, 0), 2);
            var provider = new HitPoseSource
            { Pose = new GameplayHitPose(default, new GameplayDirection(1, 0), new GameplayDirection(0, 1)) };
            owner.HitAnchors = provider;
            var hits = new List<GameplayContext>();
            var definition = new GameplayHitWindowDefinition(GameplaySearchShape.Box, GameplaySpatialPlane.ThreeDimensional,
                new GameplayHitAnchor(GameplayHitAnchorKind.TransformPath, "Blade"), .08f, width:1, height:.2f,
                onHit:new[] { new Observe(hits.Add) });
            Install(owner, new GameplayOpenHitWindowAction(definition)).TryCast();
            var transverse = new GameplayPoint(1, 0, .35f);
            var unrolled = definition.Geometry(provider.Pose);
            Assert.That(unrolled.Contains(target.Id, target.Position), Is.False);
            Assert.That(unrolled.Contains(target.Id, transverse), Is.True);
            Assert.That(hits, Is.Empty);
            provider.Pose = new GameplayHitPose(default, provider.Pose.Forward, new GameplayDirection(0, 0, 1));
            var rolled = definition.Geometry(provider.Pose);
            Assert.That(rolled.Contains(target.Id, target.Position), Is.True);
            Assert.That(rolled.Contains(target.Id, transverse), Is.False);
            world.Advance(.04f);
            Assert.That(hits.Select(hit => hit.TargetData.PrimaryTargetId), Is.EqualTo(new[] { target.Id }));
        }

        private sealed class HitPoseSource : IGameplayHitAnchorProvider
        {
            public GameplayHitPose Pose { get; set; }
            public bool TrySample(GameplayHitAnchor anchor, double worldTime, out GameplayHitPose pose)
            { pose = Pose; return true; }
        }

        private static GameplayInstalledSkill Install(GameplayStandardActor actor,IGameplaySkillAction action)
        {
            var signal=new GameplayTag("Event.Test.Spatial.Cast");
            return actor.InstallDocument(new GameplaySkillDocumentDefinition(1,new GameplayAbilityDefinition(Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),Array.Empty<GameplayTag>(),maxLevel:10),
                new GameplaySkillDefinition(new GameplaySkillEventTriggerDefinition(signal),Array.Empty<IGameplaySkillCondition>(),new GameplaySkillSelfTarget(),new[]{action},false),signal),5);
        }
        private sealed class Observe:IGameplaySkillAction
        {
            private readonly Action<GameplayContext> _callback;
            internal Observe(Action<GameplayContext> callback)=>_callback=callback;
            public GameplaySkillActionResult Execute(GameplayContext context,GameplayEventData trigger,IGameplaySkillHost host){_callback(context);return GameplaySkillActionResult.Succeeded;}
        }
    }
}
