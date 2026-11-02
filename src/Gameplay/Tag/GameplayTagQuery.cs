using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FRFrame.Gameplay
{
    /// <summary>不可变目标标签查询；All 全满足、非空 Any 至少一项、None 全不满足，均使用 HasTag 层级语义。</summary>
    public sealed class GameplayTagQuery
    {
        public static GameplayTagQuery Empty { get; } = new GameplayTagQuery(
            Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>(), Array.Empty<GameplayTag>());

        public IReadOnlyList<GameplayTag> All { get; }
        public IReadOnlyList<GameplayTag> Any { get; }
        public IReadOnlyList<GameplayTag> None { get; }
        public bool IsEmpty => All.Count == 0 && Any.Count == 0 && None.Count == 0;

        public GameplayTagQuery(IEnumerable<GameplayTag> all,
            IEnumerable<GameplayTag> any, IEnumerable<GameplayTag> none)
        {
            All = Copy(all, nameof(all));
            Any = Copy(any, nameof(any));
            None = Copy(none, nameof(none));
        }

        public bool Matches(GameplayTagContainer tags)
        {
            if (tags == null) throw new ArgumentNullException(nameof(tags));
            foreach (GameplayTag tag in All)
                if (!tags.HasTag(tag)) return false;
            if (Any.Count > 0)
            {
                bool matched = false;
                foreach (GameplayTag tag in Any)
                    if (tags.HasTag(tag)) { matched = true; break; }
                if (!matched) return false;
            }
            foreach (GameplayTag tag in None)
                if (tags.HasTag(tag)) return false;
            return true;
        }

        private static IReadOnlyList<GameplayTag> Copy(IEnumerable<GameplayTag> tags, string name)
        {
            if (tags == null) throw new ArgumentNullException(name);
            var copy = new List<GameplayTag>();
            foreach (GameplayTag tag in tags)
            {
                if (!tag.IsValid) throw new ArgumentException("Query tags must be valid.", name);
                copy.Add(tag);
            }
            return new ReadOnlyCollection<GameplayTag>(copy);
        }
    }
}
