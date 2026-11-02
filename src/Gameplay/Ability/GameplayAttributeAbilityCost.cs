using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay
{
    /// <summary>One signed resource change: negative spends BaseValue, positive grants BaseValue.
    /// Modifiers are not spendable balances. Formula values read a self application at the grant level
    /// with captured Context/Caller inputs supplied by the activation request.</summary>
    public readonly struct GameplayAttributeCostEntry
    {
        public GameplayAttributeId Attribute { get; }
        public IGameplayFloatValue Delta { get; }
        /// <summary>Pre-change balance gate, including positive exchanges. Actual XH signed
        /// Mana +10 requires an existing balance of 10; this is not a second cost execution.</summary>
        public float MinimumBalance {get;}
        public GameplayAttributeCostEntry(GameplayAttributeId attribute, IGameplayFloatValue delta,float minimumBalance=0)
        {
            if (!attribute.IsValid) throw new ArgumentException("Resource attribute is invalid.", nameof(attribute));
            Attribute = attribute;
            Delta = delta ?? throw new ArgumentNullException(nameof(delta));
            if(!float.IsFinite(minimumBalance)||minimumBalance<0)throw new ArgumentOutOfRangeException(nameof(minimumBalance));MinimumBalance=minimumBalance;
        }
    }

    /// <summary>First-party multi-resource payment. Repeated resource entries are aggregated; all amounts
    /// and balances are checked before mutation. The resource store commits the complete batch atomically.
    /// Single-threaded like AttributeSet; no Effect clock or game-specific host is required.</summary>
    public sealed class GameplayAttributeAbilityCost : IGameplayAbilityContextCost
    {
        private readonly GameplayAttributeCostEntry[] _entries;
        public GameplayAttributeAbilityCost(IEnumerable<GameplayAttributeCostEntry> entries)
        {
            if (entries == null) throw new ArgumentNullException(nameof(entries));
            var copy = new List<GameplayAttributeCostEntry>();
            foreach (var entry in entries)
            {
                if (!entry.Attribute.IsValid || entry.Delta == null) throw new ArgumentException("Invalid payment entry.", nameof(entries));
                copy.Add(entry);
            }
            _entries = copy.ToArray();
        }
        public bool CanPay(IGameplayAbilityHost host) => CanPay(host, 1);
        public bool TryCommit(IGameplayAbilityHost host) => TryCommit(host, 1);
        public bool CanPay(IGameplayAbilityHost host, int level) => CanPay(host,level,null);
        public bool TryCommit(IGameplayAbilityHost host, int level)=>TryCommit(host,level,null);
        public bool CanPay(IGameplayAbilityHost host,int level,GameplayContext context)
        {try{return Prepare(host,level,context,out _);}catch(KeyNotFoundException){return false;}}
        public bool TryCommit(IGameplayAbilityHost host, int level,GameplayContext context)
        {
            Dictionary<GameplayAttributeId,float> updates;
            try{if (!Prepare(host, level,context,out updates)) return false;}catch(KeyNotFoundException){return false;}
            Store(host).SetBalancesAtomic(updates); return true;
        }
        private static IGameplayResourceStore Store(IGameplayAbilityHost host) =>
            host is IGameplayResourceTarget target ? target.Resources : host.Attributes;
        private bool Prepare(IGameplayAbilityHost host, int level,GameplayContext context,out Dictionary<GameplayAttributeId, float> updates)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if(context!=null&&context.InstigatorId!=host.OwnerId)throw new ArgumentException("Cost context owner mismatch.",nameof(context));
            var self=(context??new GameplayContext(host.OwnerId,new GameplayTargetData(host.OwnerId))).WithAbilityLevel(level).WithTarget(new GameplayTargetData(host.OwnerId));
            var application = new GameplayEffectApplicationContext(self,host,self.Values.AsCostCallerValues());
            var deltas = new Dictionary<GameplayAttributeId, float>();
            var store = Store(host) ?? throw new InvalidOperationException("Resource store is unavailable.");
            foreach (var entry in _entries)
            {
                if (!store.TryGetBalance(entry.Attribute, out float balance)||balance<entry.MinimumBalance) { updates = null; return false; }
                deltas.TryGetValue(entry.Attribute, out float previous);
                float sum = previous + GameplayValueEvaluator.Evaluate(entry.Delta, application);
                if (!float.IsFinite(sum)) throw new ArithmeticException("Resource payment delta must be finite.");
                deltas[entry.Attribute] = sum;
            }
            updates = new Dictionary<GameplayAttributeId, float>(deltas.Count);
            foreach (var pair in deltas)
            {
                if (!store.TryGetBalance(pair.Key, out float before)) { updates = null; return false; }
                float after = before + pair.Value;
                if (!float.IsFinite(after)) throw new ArithmeticException("Resource payment balance must be finite.");
                if (pair.Value < 0 && after < 0) { updates = null; return false; }
                if (store is AttributeSet attributes) attributes.GetAttribute(pair.Key).ValidateBaseValueChange(after);
                updates.Add(pair.Key, after);
            }
            return true;
        }
    }
}
