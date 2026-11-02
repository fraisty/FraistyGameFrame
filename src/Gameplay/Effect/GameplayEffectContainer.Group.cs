using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay
{
    public sealed partial class GameplayEffectContainer
    {
        private bool _groupAdvancing;
        private bool _singleAdvancing;
        private int _activationAttempts;
        internal void BeginActivationAttempt()=>_activationAttempts++;
        internal void EndActivationAttempt()=>_activationAttempts--;
        /// <summary>Advances a host's distinct Effect containers once on the same supplied step.
        /// Existing entries meet at chronological boundaries before callbacks, so cross-target
        /// layers/refreshes use their actual birth boundary. New entries start next step, matching
        /// single-container snapshot semantics. The supplied order breaks simultaneous ties.
        /// Containers still own all handles, elapsed values and cleanup; no second clock is stored.</summary>
        public static void AdvanceTogether(IReadOnlyList<GameplayEffectContainer> containers, float amount)
        {
            if (containers == null) throw new ArgumentNullException(nameof(containers));
            if (!float.IsFinite(amount) || amount < 0) throw new ArgumentOutOfRangeException(nameof(amount));
            var unique = new HashSet<GameplayEffectContainer>();
            var owners = new List<GameplayEffectContainer>();
            var entries = new List<GroupEntry>();
            foreach (var container in containers)
            {
                if (container == null || !unique.Add(container)) throw new ArgumentException("Supply distinct, non-null Effect containers.", nameof(containers));
                if (container._groupAdvancing || container._singleAdvancing || container._activationAttempts>0) throw new InvalidOperationException("Grouped Effect Advance cannot be nested or run during activation commit.");
                owners.Add(container);
                foreach (var entry in container.OrderedEntries()) entries.Add(new GroupEntry(container, entry, amount));
            }
            foreach (var container in owners) container._groupAdvancing = true;
            try
            {
                double offset = 0;
                while (true)
                {
                    GroupEntry selected = null; double nextOffset = double.PositiveInfinity, boundary = 0;
                    bool removedMismatch = false;
                    foreach (var item in entries)
                    {
                        if (!item.Active) continue;
                        if (!item.Owner.MeetsOngoing(item.Value))
                        { item.Owner.RemoveEntry(item.Value.Handle, GameplayEffectRemovalCause.OngoingRequirementMismatch); removedMismatch = true; break; }
                        double next = Math.Min(item.Value.Period > 0 ? item.Value.NextPeriodAt : double.PositiveInfinity,
                            item.Value.Finite ? item.Value.NextExpiry : double.PositiveInfinity);
                        if (double.IsPositiveInfinity(next) || (float)next > (float)item.End) continue;
                        double candidate = Math.Min(amount, Math.Max(offset, next - item.Start));
                        if (candidate < nextOffset) { selected = item; nextOffset = candidate; boundary = next; }
                    }
                    if (removedMismatch) continue; // Removal observers may change another entry's schedule.
                    if (selected == null) { Align(entries, amount); break; }
                    offset = nextOffset; Align(entries, offset);
                    if (!selected.Active) continue;
                    if (!selected.Owner.MeetsOngoing(selected.Value))
                    { selected.Owner.RemoveEntry(selected.Value.Handle, GameplayEffectRemovalCause.OngoingRequirementMismatch); continue; }
                    selected.Owner.AdvanceBoundary(selected.Value, boundary);
                }
            }
            finally { foreach (var container in owners) container._groupAdvancing = false; }
        }
        private static void Align(List<GroupEntry> entries, double offset)
        { foreach (var entry in entries) if (entry.Active) entry.Value.Elapsed = Math.Min(entry.Start + offset, entry.End); }
        private sealed class GroupEntry
        {
            internal readonly GameplayEffectContainer Owner;
            internal readonly Entry Value;
            internal readonly double Start, End;
            internal bool Active => Owner._entries.ContainsKey(Value.Handle);
            internal GroupEntry(GameplayEffectContainer owner, Entry value, float amount)
            { Owner = owner; Value = value; Start = value.Elapsed; End = Start + amount; }
        }
    }
}
