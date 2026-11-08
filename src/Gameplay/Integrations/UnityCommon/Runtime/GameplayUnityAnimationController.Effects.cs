using System.Collections.Generic;
using System.Globalization;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    public sealed partial class GameplayUnityAnimationController
    {
        private readonly Queue<(GameplayEffectHandle Handle, GameplayEffectDefinition Definition, int Stacks)> _effectChanges = new();
        private readonly Queue<(GameplayEffectCue Cue, Vector3 Position)> _effectCues = new();
        private readonly Dictionary<GameplayEffectHandle, EffectViewLease> _effectViews = new();
        private readonly Dictionary<GameplayEffectDefinition, EffectViewLease> _effectBadges = new();

        private void ObserveEffectState(GameplayEffectPresentationState state) =>
            _effectChanges.Enqueue((state.Handle, state.Definition, state.StackCount));

        private void ObserveEffectRemoval(GameplayEffectRemovalObservation removal)
        {
            if (removal.Definition.Presentation != null) _effectChanges.Enqueue((removal.Handle, removal.Definition, 0));
        }

        internal void QueueEffectCue(GameplayEffectCue cue)
        {
            var point = Actor.Position;
            _effectCues.Enqueue((cue, new Vector3(point.X, point.Y, point.Z)));
        }

        private void AdvanceEffectPresentation(float amount)
        {
            while (_effectChanges.TryDequeue(out var change))
            {
                if (change.Stacks == 0)
                {
                    if (_effectViews.Remove(change.Handle, out var removed)) removed.Release();
                    continue;
                }
                if (change.Definition.Presentation is not GameplayUnityEffectPresentation presentation || !Actor.Effects.IsActive(change.Handle)) continue;
                if (!_effectViews.TryGetValue(change.Handle, out var lease))
                {
                    lease = new EffectViewLease(change.Definition, presentation.StatusPrefab, SpatialViewPool);
                    if (lease.View != null) ApplyViewRenderLayer(lease.View.gameObject);
                    _effectViews.Add(change.Handle, lease);
                    if (Actor.IsTargetable && presentation.FirstApplication != null) QueueEffectCue(presentation.FirstApplication);
                }
                lease.SetStacks(change.Stacks);
            }

            while (_effectCues.TryDequeue(out var playback))
            {
                var cue = playback.Cue;
                if (cue.Vfx != null) PlayCueAt(cue.Vfx, playback.Position, null, cue.Duration);
                if (cue.Audio != null) PlayCueAt(cue.Audio, playback.Position, null, cue.Duration);
            }

            if (_effectViews.Count == 0) return;
            _effectBadges.Clear();
            foreach (var lease in _effectViews.Values)
                if (!_effectBadges.TryGetValue(lease.Definition, out var current) || lease.Stacks > current.Stacks)
                    _effectBadges[lease.Definition] = lease;
            var point = Actor.Position;
            foreach (var lease in _effectViews.Values)
            {
                if (lease.View == null) continue;
                lease.View.transform.position = new Vector3(point.X, point.Y, point.Z);
                if (lease.View.gameObject.activeSelf != Actor.IsTargetable) lease.View.gameObject.SetActive(Actor.IsTargetable);
                if (Actor.IsTargetable) lease.View.Advance(amount, ReferenceEquals(_effectBadges[lease.Definition], lease));
            }
        }

        private void DisposeEffectPresentation()
        {
            Actor.Effects.PresentationChanged -= ObserveEffectState;
            Actor.Effects.Removed -= ObserveEffectRemoval;
            foreach (var lease in _effectViews.Values) lease.Release();
            _effectViews.Clear();
            _effectBadges.Clear();
            _effectChanges.Clear();
            _effectCues.Clear();
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
                if (prefab == null) return;
                _pool = pool;
                _name = prefab.name;
                _key = new ResourceKey("Gameplay.DirectPrefab", prefab.GetInstanceID().ToString(CultureInfo.InvariantCulture));
                var instance = _pool.GetOrCreate(_key, _name, prefab);
                instance.transform.SetParent(null, false);
                View = instance.GetComponent<GameplayEffectStatusView>();
                View.Begin();
            }

            internal void SetStacks(int count)
            {
                int cap = Definition.Stacking?.MaxStacks ?? 1;
                bool full = cap > 1 && count == cap;
                View?.SetStacks(count, full, full && Stacks < cap);
                Stacks = count;
            }

            internal void Release()
            {
                if (View != null) _pool.Release(_key, _name, View.gameObject);
            }
        }
    }
}
