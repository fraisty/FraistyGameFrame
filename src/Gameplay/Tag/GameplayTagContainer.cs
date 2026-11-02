using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay
{
    /// <summary>
    /// 按 exact 标签保存来源集合，单线程同步使用。父标签仅在层级查询中成立，不物理添加。
    /// 同来源对同 exact 标签只有一份贡献，source + tag 已足以精确撤销，因此不需要 Tag Handle。
    /// 所有入口拒绝无效标签或无效来源，抛出 ArgumentException。
    /// </summary>
    public sealed class GameplayTagContainer
    {
        private readonly Dictionary<GameplayTag, HashSet<GameplaySourceId>> _contributions =
            new Dictionary<GameplayTag, HashSet<GameplaySourceId>>();

        /// <summary>Captures exact tags and all sources in deterministic order.</summary>
        public IReadOnlyList<GameplayTagContributionSnapshot> GetContributionsSnapshot()
        {
            var tags = new List<GameplayTag>(_contributions.Keys);
            tags.Sort((a, b) => string.CompareOrdinal(a.Name, b.Name));
            var result = new List<GameplayTagContributionSnapshot>(tags.Count);
            foreach (GameplayTag tag in tags)
            {
                var sources = new List<GameplaySourceId>(_contributions[tag]);
                sources.Sort((a, b) => a.Value.CompareTo(b.Value));
                result.Add(new GameplayTagContributionSnapshot(tag, sources));
            }
            return result.AsReadOnly();
        }

        /// <summary>添加一份 exact 贡献；新贡献返回 true，同来源重复添加返回 false。</summary>
        public bool AddTag(GameplayTag tag, GameplaySourceId sourceId)
        {
            ValidateTag(tag);
            ValidateSource(sourceId);
            if (!_contributions.TryGetValue(tag, out HashSet<GameplaySourceId> sources))
            {
                sources = new HashSet<GameplaySourceId>();
                _contributions.Add(tag, sources);
            }
            return sources.Add(sourceId);
        }

        /// <summary>只撤销指定来源的 exact 贡献；不存在返回 false，不影响其他来源或子标签。</summary>
        public bool RemoveTag(GameplayTag tag, GameplaySourceId sourceId)
        {
            ValidateTag(tag);
            ValidateSource(sourceId);
            if (!_contributions.TryGetValue(tag, out HashSet<GameplaySourceId> sources) || !sources.Remove(sourceId))
                return false;
            if (sources.Count == 0) _contributions.Remove(tag);
            return true;
        }

        /// <summary>
        /// 撤销一个来源的所有 exact 贡献，返回撤销的贡献数（不是最终消失的标签数）。
        /// 其他来源仍贡献的标签继续存在；来源不存在返回0。
        /// </summary>
        public int RemoveTagsFromSource(GameplaySourceId sourceId)
        {
            ValidateSource(sourceId);
            int removed = 0;
            var emptyTags = new List<GameplayTag>();
            foreach (var pair in _contributions)
            {
                if (!pair.Value.Remove(sourceId)) continue;
                removed++;
                if (pair.Value.Count == 0) emptyTags.Add(pair.Key);
            }
            foreach (GameplayTag tag in emptyTags) _contributions.Remove(tag);
            return removed;
        }

        /// <summary>层级查询：拥有自身或任一后代则为 true；只有父标签不能满足子标签查询。</summary>
        public bool HasTag(GameplayTag tag)
        {
            ValidateTag(tag);
            foreach (GameplayTag owned in _contributions.Keys)
                if (owned == tag || owned.IsDescendantOf(tag)) return true;
            return false;
        }

        /// <summary>精确查询：必须有来源明确贡献该完整名称，子标签不算。</summary>
        public bool HasTagExact(GameplayTag tag)
        {
            ValidateTag(tag);
            return _contributions.ContainsKey(tag);
        }

        private static void ValidateTag(GameplayTag tag)
        {
            if (!tag.IsValid) throw new ArgumentException("Tag must be valid.", nameof(tag));
        }

        private static void ValidateSource(GameplaySourceId sourceId)
        {
            if (!sourceId.IsValid) throw new ArgumentException("Source ID must be valid.", nameof(sourceId));
        }
    }
}
