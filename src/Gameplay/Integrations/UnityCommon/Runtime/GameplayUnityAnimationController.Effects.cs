using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    public sealed partial class GameplayUnityAnimationController
    {
        private readonly Queue<(GameplayEffectHandle Handle, GameplayEffectDefinition Definition, int Stacks)> _effectChanges = new();
        private readonly Dictionary<GameplayEffectHandle, EffectViewLease> _effectViews = new();
        private readonly Dictionary<GameplayEffectDefinition, EffectViewLease> _effectBadges = new();
        private bool _statusVisible;

        private void ObserveEffectState(GameplayEffectPresentationState state)
        {
            if (ReferenceEquals(Actor.Presentation, this)) _effectChanges.Enqueue((state.Handle, state.Definition, state.StackCount));
        }

        private void ObserveEffectRemoval(GameplayEffectRemovalObservation removal)
        {
            if (removal.Definition.Presentation != null && ReferenceEquals(Actor.Presentation, this))
                _effectChanges.Enqueue((removal.Handle, removal.Definition, 0));
        }

        /// <summary>Releases status views before temporarily replacing the actor's model driver.
        /// On the next advance as active driver, current statuses are restored without replaying their events.</summary>
        public void ReleaseEffectStatusViews()
        {
            ReleaseStatusViews();
            _effectChanges.Clear();
            _statusVisible = false;
        }

        private void AdvanceEffectPresentation(float amount)
        {
            bool visible = Actor.IsTargetable;
            if (_statusVisible != visible)
            {
                _statusVisible = visible;
                _effectChanges.Clear();
                if (visible)
                {
                    foreach (var entry in Actor.Effects.GetActiveEntriesSnapshot())
                        if (entry.Definition.Presentation is GameplayUnityEffectPresentation presentation)
                            CreateEffectView(entry.Handle, entry.Definition, presentation).SetStacks(entry.StackCount, pulse: false);
                }
                else ReleaseStatusViews();
            }
            while (_effectChanges.TryDequeue(out var change))
            {
                if (change.Stacks == 0)
                {
                    if (_effectViews.Remove(change.Handle, out var removed)) removed.Release();
                    continue;
                }
                if (!visible || change.Definition.Presentation is not GameplayUnityEffectPresentation presentation ||
                    !Actor.Effects.IsActive(change.Handle)) continue;
                if (!_effectViews.TryGetValue(change.Handle, out var lease))
                    lease = CreateEffectView(change.Handle, change.Definition, presentation);
                lease.SetStacks(change.Stacks);
            }
            PlayCommittedEffectCues();

            if (_effectViews.Count == 0) return;
            _effectBadges.Clear();
            foreach (var lease in _effectViews.Values)
                if (!_effectBadges.TryGetValue(lease.Definition, out var current) || lease.Stacks > current.Stacks)
                    _effectBadges[lease.Definition] = lease;
            var point = Actor.Position;
            foreach (var lease in _effectViews.Values)
            {
                lease.View.transform.position = new Vector3(point.X, point.Y, point.Z);
                lease.View.Advance(amount, ReferenceEquals(_effectBadges[lease.Definition], lease));
            }
        }

        private EffectViewLease CreateEffectView(GameplayEffectHandle handle, GameplayEffectDefinition definition, GameplayUnityEffectPresentation presentation)
        {
            var lease = new EffectViewLease(definition, presentation.StatusPrefab, SpatialViewPool);
            ApplyViewRenderLayer(lease.View.gameObject);
            _effectViews.Add(handle, lease);
            return lease;
        }

        private void DisposeEffectPresentation()
        {
            Actor.Effects.PresentationChanged -= ObserveEffectState;
            Actor.Effects.Removed -= ObserveEffectRemoval;
            ReleaseEffectStatusViews();
            _committedEffectCues.Clear();
        }

        private void ReleaseStatusViews()
        {
            foreach (var lease in _effectViews.Values) lease.Release();
            _effectViews.Clear();
            _effectBadges.Clear();
        }

        private sealed class EffectViewLease
        {
            private readonly GameObjectPoolModule _pool;
            private readonly ResourceKey _key;
            private readonly string _name;
            internal readonly GameplayEffectDefinition Definition;
            internal readonly GameplayEffectStatusView View;
            internal int Stacks { get; private set; }

            internal EffectViewLease(GameplayEffectDefinition definition, GameObject prefab, GameObjectPoolModule pool)
            {
                Definition = definition;
                _pool = pool;
                _name = prefab.name;
                _key = new ResourceKey("Gameplay.DirectPrefab", prefab.GetInstanceID().ToString(CultureInfo.InvariantCulture));
                var instance = _pool.GetOrCreate(_key, _name, prefab);
                instance.transform.SetParent(null, false);
                View = instance.GetComponent<GameplayEffectStatusView>();
                View.Begin();
            }

            internal void SetStacks(int count, bool pulse = true)
            {
                int cap = Definition.Stacking?.MaxStacks ?? 1;
                bool full = cap > 1 && count == cap;
                View.SetStacks(count, full, pulse && full && Stacks < cap);
                Stacks = count;
            }

            internal void Release()
            {
                if (View != null) _pool.Release(_key, _name, View.gameObject);
            }
        }
    }
}
