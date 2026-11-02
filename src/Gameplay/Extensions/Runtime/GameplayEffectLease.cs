using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay.Extensions
{
    /// <summary>Exclusive cleanup of a captured contribution; does not scan definitions/tags or advance the container.</summary>
    public sealed class GameplayEffectLease : IDisposable
    {
        private readonly GameplayEffectContainer _container;
        public GameplayEffectHandle Handle { get; }
        public GameplayEffectLayerHandle Layer { get; }
        public bool IsDisposed { get; private set; }
        /// <summary>Current exact contribution liveness; a surviving shared entry does not keep an expired layer alive.</summary>
        public bool IsActive => !IsDisposed && (Layer.IsValid ? _container.IsLayerActive(Layer) : _container.IsActive(Handle));
        private GameplayEffectLease(GameplayEffectContainer container, GameplayEffectHandle handle, GameplayEffectLayerHandle layer)
        { _container = container; Handle = handle; Layer = layer; }
        /// <summary>
        /// Captures only NewActiveEffect/StackIncreased results from this container. Prefers the first/new exact layer.
        /// Refresh, overflow, rejection and instant results confer no cleanup ownership. A removed entry returns false;
        /// a layer removed reentrantly while its shared entry survives can be captured, but cleanup is a harmless no-op.
        /// The caller must assign each contribution to only one owner and not also register whole-entry Ability ownership.
        /// </summary>
        public static bool TryCapture(GameplayEffectContainer container, GameplayEffectApplicationResult application, out GameplayEffectLease lease)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            lease = null;
            if ((application.Outcome != GameplayEffectApplicationOutcome.NewActiveEffect && application.Outcome != GameplayEffectApplicationOutcome.StackIncreased) ||
                !container.IsActive(application.ActiveHandle)) return false;
            if (application.Outcome == GameplayEffectApplicationOutcome.StackIncreased && !application.LayerHandle.IsValid) return false;
            lease = new GameplayEffectLease(container, application.ActiveHandle, application.LayerHandle);
            return true;
        }
        /// <summary>Explicit whole-entry lease for an exclusively owned handle (e.g. legacy unstacked Apply). Never use on a shared stack entry.</summary>
        public static GameplayEffectLease OwnEntry(GameplayEffectContainer container, GameplayEffectHandle handle)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            if (!handle.IsValid || !container.IsActive(handle)) throw new ArgumentException("Handle must be active in this container.", nameof(handle));
            return new GameplayEffectLease(container, handle, default);
        }
        public void Dispose()
        {
            if (IsDisposed) return;
            IsDisposed = true; // Mark first: removal observers may reenter cleanup.
            if (Layer.IsValid) _container.RemoveLayer(Layer, GameplayEffectRemovalCause.OwnerCleanup);
            else _container.Remove(Handle, GameplayEffectRemovalCause.OwnerCleanup);
        }
    }
    /// <summary>One lease per target for one source. World membership remains with the game; Clear is reusable.</summary>
    public sealed class GameplayEffectLeaseSet
    {
        private readonly Dictionary<GameplaySourceId, GameplayEffectLease> _leases = new Dictionary<GameplaySourceId, GameplayEffectLease>();
        public int Count => _leases.Count;
        public bool Contains(GameplaySourceId target) => _leases.ContainsKey(target);
        public void AddOrReplace(GameplaySourceId target, GameplayEffectLease lease)
        {
            if (!target.IsValid) throw new ArgumentException("Target must be valid.", nameof(target));
            if (lease == null || lease.IsDisposed) throw new ArgumentException("Lease must be live.", nameof(lease));
            _leases.TryGetValue(target, out var old);
            if (ReferenceEquals(old, lease)) return;
            _leases[target] = lease;
            old?.Dispose();
        }
        public bool Remove(GameplaySourceId target)
        {
            if (!_leases.TryGetValue(target, out var lease)) return false;
            _leases.Remove(target);
            lease.Dispose();
            return true;
        }
        public void Clear()
        {
            var owned = new List<GameplayEffectLease>(_leases.Values);
            _leases.Clear();
            List<Exception> errors = null;
            foreach (var lease in owned)
                try { lease.Dispose(); } catch (Exception ex) { (errors ?? (errors = new List<Exception>())).Add(ex); }
            if (errors != null) throw new AggregateException(errors);
        }
    }
}
