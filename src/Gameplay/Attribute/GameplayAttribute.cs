using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay
{
    /// <summary>
    /// 纯规则属性：(BaseValue + Sum(Add)) * Product(Multiply)。无游戏规则 Clamp、事件或时间驱动。
    /// 变更立即重算；非法输入或聚合溢出抛出异常，并保持此属性变更前的状态。
    /// </summary>
    public sealed class GameplayAttribute
    {
        private readonly object _handleOwner = new object();
        private List<Entry> _modifiers = new List<Entry>();
        private long _lastSequence;

        public float BaseValue { get; private set; }
        public float CurrentValue { get; private set; }

        /// <summary>Returns a detached copy in the attribute's canonical aggregation order.</summary>
        public IReadOnlyList<GameplayAttributeModifier> GetModifiersSnapshot()
        {
            var copy = new List<GameplayAttributeModifier>(_modifiers.Count);
            foreach (Entry entry in _modifiers) copy.Add(entry.Modifier);
            copy.Sort((left, right) =>
            {
                int operation = left.Operation.CompareTo(right.Operation);
                if (operation != 0) return operation;
                int magnitude = left.Magnitude.CompareTo(right.Magnitude);
                return magnitude != 0 ? magnitude : left.SourceId.Value.CompareTo(right.SourceId.Value);
            });
            return copy.AsReadOnly();
        }

        public GameplayAttribute(float baseValue)
        {
            ValidateBaseValue(baseValue);
            BaseValue = baseValue;
            CurrentValue = baseValue;
        }

        public void SetBaseValue(float baseValue)
        {
            ValidateBaseValue(baseValue);
            float current = Calculate(baseValue, _modifiers);
            BaseValue = baseValue;
            CurrentValue = current;
        }

        // Atomic multi-resource payment validates every resulting aggregate before changing any balance.
        internal void ValidateBaseValueChange(float baseValue)
        {
            ValidateBaseValue(baseValue);
            Calculate(baseValue, _modifiers);
        }

        public GameplayModifierHandle AddModifier(GameplayAttributeModifier modifier)
        {
            GameplayAttributeModifier.Validate(modifier.Operation, modifier.Magnitude, modifier.SourceId);
            long sequence = checked(_lastSequence + 1);
            var handle = new GameplayModifierHandle(_handleOwner, sequence);
            var candidate = new List<Entry>(_modifiers) { new Entry(handle, modifier) };
            candidate.Sort(CompareEntries);
            float current = Calculate(BaseValue, candidate);
            _modifiers = candidate;
            _lastSequence = sequence;
            CurrentValue = current;
            return handle;
        }

        /// <summary>无效、属于其他属性或已移除的 Handle 返回 false，且不修改任何数值。</summary>
        public bool RemoveModifier(GameplayModifierHandle handle)
        {
            int index = _modifiers.FindIndex(entry => entry.Handle == handle);
            if (index < 0) return false;
            var candidate = new List<Entry>(_modifiers);
            candidate.RemoveAt(index);
            float current = Calculate(BaseValue, candidate);
            _modifiers = candidate;
            CurrentValue = current;
            return true;
        }

        /// <summary>移除指定有效来源的全部修饰，返回移除数量；来源不存在时为 0。</summary>
        public int RemoveModifiersFromSource(GameplaySourceId sourceId)
        {
            if (!sourceId.IsValid)
                throw new ArgumentException("Source ID must be valid.", nameof(sourceId));
            var candidate = new List<Entry>(_modifiers);
            int removed = candidate.RemoveAll(entry => entry.Modifier.SourceId == sourceId);
            if (removed == 0) return 0;
            float current = Calculate(BaseValue, candidate);
            _modifiers = candidate;
            CurrentValue = current;
            return removed;
        }

        // AttributeSet 批量清理先验证所有属性，再执行移除，避免溢出造成部分清理。
        // Effect layer cleanup has the same atomic preflight, but selects exact handles rather than a shared source.
        internal void ValidateModifierRemoval(IReadOnlyCollection<GameplayModifierHandle> handles)
        { Calculate(BaseValue, _modifiers.FindAll(entry => !ContainsHandle(handles, entry.Handle))); }

        internal void RemoveModifiers(IReadOnlyCollection<GameplayModifierHandle> handles)
        {
            var candidate = _modifiers.FindAll(entry => !ContainsHandle(handles, entry.Handle));
            float current = Calculate(BaseValue, candidate);
            _modifiers = candidate;
            CurrentValue = current;
        }

        private static bool ContainsHandle(IReadOnlyCollection<GameplayModifierHandle> handles, GameplayModifierHandle handle)
        { foreach (var item in handles) if (item == handle) return true; return false; }

        // 本模块同步使用且不触发回调，两遍之间不会被内部规则改写。
        internal void ValidateSourceRemoval(GameplaySourceId sourceId)
        {
            var candidate = _modifiers.FindAll(entry => entry.Modifier.SourceId != sourceId);
            Calculate(BaseValue, candidate);
        }

        private static void ValidateBaseValue(float value)
        {
            if (float.IsNaN(value) || float.IsInfinity(value))
                throw new ArgumentOutOfRangeException(nameof(value), "Base value must be finite.");
        }

        private static int CompareEntries(Entry left, Entry right)
        {
            int operation = left.Modifier.Operation.CompareTo(right.Modifier.Operation);
            return operation != 0 ? operation : left.Modifier.Magnitude.CompareTo(right.Modifier.Magnitude);
        }

        private static float Calculate(float baseValue, List<Entry> modifiers)
        {
            // 按 Operation/Magnitude 的规范顺序计算，避免浮点加乘顺序随添加顺序变化。
            // double 仅用于中间聚合，公开值仍为 float；不承诺无限精度或跨平台确定性。
            double additive = 0d;
            double multiplier = 1d;
            foreach (Entry entry in modifiers)
            {
                GameplayAttributeModifier modifier = entry.Modifier;
                if (modifier.Operation == GameplayModifierOperation.Add)
                    additive += modifier.Magnitude;
                else
                    multiplier *= modifier.Magnitude;
            }

            double result = (baseValue + additive) * multiplier;
            if (double.IsNaN(result) || double.IsInfinity(result) || result > float.MaxValue || result < -float.MaxValue)
                throw new OverflowException("Aggregated attribute value must be representable as a finite float.");
            return (float)result;
        }

        private readonly struct Entry
        {
            internal readonly GameplayModifierHandle Handle;
            internal readonly GameplayAttributeModifier Modifier;

            internal Entry(GameplayModifierHandle handle, GameplayAttributeModifier modifier)
            {
                Handle = handle;
                Modifier = modifier;
            }
        }
    }
}
