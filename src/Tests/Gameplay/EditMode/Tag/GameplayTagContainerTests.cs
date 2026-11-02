using FRFrame.Gameplay;
using NUnit.Framework;

namespace FRFrame.Tests.Gameplay.Tag
{
    public sealed class GameplayTagContainerTests
    {
        [Test]
        public void HierarchicalQueries_RespectSegmentBoundariesCaseAndStrictDescendants()
        {
            var allocator = new GameplaySourceIdAllocator();
            var container = new GameplayTagContainer();
            var stun = new GameplayTag("State.Control.Stun");
            var control = new GameplayTag("State.Control");

            Assert.That(stun.IsDescendantOf(control), Is.True);
            Assert.That(stun.IsDescendantOf(stun), Is.False);
            Assert.That(stun.IsDescendantOf(default(GameplayTag)), Is.False);
            Assert.That(new GameplayTag("State.Stunned").IsDescendantOf(new GameplayTag("State.Stun")), Is.False);

            Assert.That(container.AddTag(stun, allocator.Allocate()), Is.True);
            Assert.That(container.HasTag(new GameplayTag("State")), Is.True);
            Assert.That(container.HasTag(control), Is.True);
            Assert.That(container.HasTag(stun), Is.True);
            Assert.That(container.HasTagExact(stun), Is.True);
            Assert.That(container.HasTagExact(control), Is.False);
            Assert.That(container.HasTag(new GameplayTag("State.Control.Stunned")), Is.False);
            Assert.That(container.HasTag(new GameplayTag("state.control")), Is.False);

            var parentOnly = new GameplayTagContainer();
            var source = allocator.Allocate();
            parentOnly.AddTag(control, source);
            Assert.That(parentOnly.HasTag(stun), Is.False);
            parentOnly.AddTag(stun, source);
            Assert.That(parentOnly.RemoveTag(control, source), Is.True);
            Assert.That(parentOnly.HasTagExact(control), Is.False);
            Assert.That(parentOnly.HasTag(control), Is.True, "精确移除父贡献不能删除子贡献。");
        }

        [Test]
        public void SameExactTag_FromTwoSources_RemainsUntilBothContributionsAreRemoved()
        {
            var allocator = new GameplaySourceIdAllocator();
            var firstSource = allocator.Allocate();
            var secondSource = allocator.Allocate();
            var container = new GameplayTagContainer();
            var invincible = new GameplayTag("State.Invincible");

            Assert.That(container.AddTag(invincible, firstSource), Is.True);
            Assert.That(container.AddTag(invincible, secondSource), Is.True);
            Assert.That(container.RemoveTag(invincible, firstSource), Is.True);
            Assert.That(container.HasTagExact(invincible), Is.True);
            Assert.That(container.RemoveTag(invincible, secondSource), Is.True);
            Assert.That(container.HasTag(invincible), Is.False);
        }

        [Test]
        public void RemoveTagsFromSource_RemovesExactContributionsAndPreservesOtherSource()
        {
            var allocator = new GameplaySourceIdAllocator();
            var removedSource = allocator.Allocate();
            var retainedSource = allocator.Allocate();
            var container = new GameplayTagContainer();
            var stun = new GameplayTag("State.Control.Stun");
            var haste = new GameplayTag("State.Buff.Haste");
            var barrier = new GameplayTag("State.Defense.Barrier");

            container.AddTag(stun, removedSource);
            container.AddTag(haste, removedSource);
            container.AddTag(stun, retainedSource);
            container.AddTag(barrier, retainedSource);

            Assert.That(container.RemoveTagsFromSource(removedSource), Is.EqualTo(2));
            Assert.That(container.HasTagExact(stun), Is.True);
            Assert.That(container.HasTag(haste), Is.False);
            Assert.That(container.HasTag(new GameplayTag("State.Defense")), Is.True);
            Assert.That(container.RemoveTagsFromSource(removedSource), Is.Zero);
        }

        [Test]
        public void DuplicateContribution_IsIdempotentAndOneRemoveClearsIt()
        {
            var allocator = new GameplaySourceIdAllocator();
            var source = allocator.Allocate();
            var container = new GameplayTagContainer();
            var silenced = new GameplayTag("State.Control.Silenced");

            Assert.That(container.AddTag(silenced, source), Is.True);
            Assert.That(container.AddTag(silenced, source), Is.False);
            Assert.That(container.RemoveTag(silenced, source), Is.True);
            Assert.That(container.HasTagExact(silenced), Is.False);
            Assert.That(container.RemoveTag(silenced, source), Is.False);
        }
    }
}
