using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay
{
    /// <summary>按游戏自定义的有效属性键管理运行时属性，不静默创建或覆盖。</summary>
    public sealed class AttributeSet : IGameplayResourceStore
    {
        private readonly Dictionary<GameplayAttributeId, GameplayAttribute> _attributes =
            new Dictionary<GameplayAttributeId, GameplayAttribute>();

        /// <summary>重复注册抛出 ArgumentException，原属性及其修饰器保持不变。</summary>
        public GameplayAttribute Register(GameplayAttributeId id, float baseValue)
        {
            ValidateId(id);
            if (_attributes.ContainsKey(id))
                throw new ArgumentException("Attribute is already registered: " + id, nameof(id));
            var attribute = new GameplayAttribute(baseValue);
            _attributes.Add(id, attribute);
            return attribute;
        }

        public bool Contains(GameplayAttributeId id)
        {
            ValidateId(id);
            return _attributes.ContainsKey(id);
        }

        /// <summary>有效但未注册的键抛出 KeyNotFoundException；default 键抛出 ArgumentException。</summary>
        public GameplayAttribute GetAttribute(GameplayAttributeId id)
        {
            ValidateId(id);
            if (!_attributes.TryGetValue(id, out GameplayAttribute attribute))
                throw new KeyNotFoundException("Attribute is not registered: " + id);
            return attribute;
        }

        public float GetCurrentValue(GameplayAttributeId id) => GetAttribute(id).CurrentValue;

        /// <summary>Captures registered values and modifier sources without exposing the backing map.</summary>
        public IReadOnlyList<GameplayAttributeEntrySnapshot> GetEntriesSnapshot()
        {
            var ids = new List<GameplayAttributeId>(_attributes.Keys);
            ids.Sort((a, b) => string.CompareOrdinal(a.Value, b.Value));
            var entries = new List<GameplayAttributeEntrySnapshot>(ids.Count);
            foreach (GameplayAttributeId id in ids)
            {
                GameplayAttribute attribute = _attributes[id];
                entries.Add(new GameplayAttributeEntrySnapshot(id, attribute.BaseValue,
                    attribute.CurrentValue, attribute.GetModifiersSnapshot()));
            }
            return entries.AsReadOnly();
        }
        public void SetBaseValue(GameplayAttributeId id, float baseValue) => GetAttribute(id).SetBaseValue(baseValue);

        /// <summary>Resource balances use BaseValue; additive/multiplicative modifiers are not spendable currency.</summary>
        public bool TryGetBalance(GameplayAttributeId id, out float balance)
        {
            ValidateId(id); balance = 0;
            if (!_attributes.TryGetValue(id, out var attribute)) return false;
            balance = attribute.BaseValue; return true;
        }
        /// <summary>Validates the resulting aggregate for every attribute before mutating any resource.
        /// This synchronous container emits no callbacks between preflight and commit.</summary>
        public void SetBalancesAtomic(IReadOnlyDictionary<GameplayAttributeId, float> balances)
        {
            if (balances == null) throw new ArgumentNullException(nameof(balances));
            var updates = new List<KeyValuePair<GameplayAttribute, float>>();
            foreach (var pair in balances)
            {
                var attribute = GetAttribute(pair.Key); attribute.ValidateBaseValueChange(pair.Value);
                updates.Add(new KeyValuePair<GameplayAttribute, float>(attribute, pair.Value));
            }
            foreach (var pair in updates) pair.Key.SetBaseValue(pair.Value);
        }

        /// <summary>
        /// 遍历所有已注册属性，返回总移除数量。无效来源抛出 ArgumentException。
        /// 若清理会造成任一属性数值溢出，则不修改任何属性。本容器按单线程同步使用。
        /// </summary>
        public int RemoveModifiersFromSource(GameplaySourceId sourceId)
        {
            if (!sourceId.IsValid)
                throw new ArgumentException("Source ID must be valid.", nameof(sourceId));
            foreach (GameplayAttribute attribute in _attributes.Values)
                attribute.ValidateSourceRemoval(sourceId);
            int removed = 0;
            foreach (GameplayAttribute attribute in _attributes.Values)
                removed += attribute.RemoveModifiersFromSource(sourceId);
            return removed;
        }

        private static void ValidateId(GameplayAttributeId id)
        {
            if (!id.IsValid)
                throw new ArgumentException("Attribute ID must be valid.", nameof(id));
        }
    }
}
