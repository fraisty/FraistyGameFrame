using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay.Extensions
{
    /// <summary>Shared child callback plumbing. Owns no motion, timer or Ability polling; can be owned by TryOwnLifetime.</summary>
    public sealed class GameplayChildRuleBinding : IDisposable
    {
        public GameplayContext BaseContext { get; }
        private readonly IGameplaySkillHost _host;
        public bool IsDisposed { get; private set; }
        public GameplayChildRuleBinding(GameplayContext original, GameplaySourceId childSource, IGameplaySkillHost host)
        {
            if (original == null) throw new ArgumentNullException(nameof(original));
            if (!childSource.IsValid) throw new ArgumentException("Child source must be valid.", nameof(childSource));
            _host = host ?? throw new ArgumentNullException(nameof(host));
            BaseContext = original.WithSource(childSource);
        }
        /// <summary>
        /// Overlays hit-local scalars on the original immutable Values; unspecified keys are retained and matching keys overwritten.
        /// Null/empty overrides retain the original snapshot. Overrides are applied in enumeration order through WithValue
        /// (valid tags and finite values required); BaseContext never changes. Target is captured per callback.
        /// </summary>
        public GameplaySkillActionExecution Signal(GameplayTag eventTag, GameplayTargetData target,
            GameplayContinuationDefinition continuation, IEnumerable<KeyValuePair<GameplayTag, float>> valueOverrides = null, float magnitude = 0)
        {
            if (IsDisposed) return new GameplaySkillActionExecution(GameplaySkillActionResult.Rejected, BaseContext);
            if (continuation == null) throw new ArgumentNullException(nameof(continuation));
            var merged = BaseContext.Values;
            if (valueOverrides != null)
                foreach (var pair in valueOverrides) merged = merged.WithValue(pair.Key, pair.Value);
            var context = BaseContext.WithTarget(target).WithValues(merged);
            var trigger = new GameplayEventData(eventTag, context, magnitude);
            return continuation.Execute(context, trigger, _host, () => !IsDisposed);
        }
        public void Dispose() { IsDisposed = true; }
    }
}
