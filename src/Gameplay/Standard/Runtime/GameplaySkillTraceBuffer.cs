using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Bounded per-actor diagnostic history. Disabled by default; does not tick rules
    /// or keep mutable runtime references. Snapshot survives subsequent recording/cleanup.</summary>
    public sealed class GameplaySkillTraceBuffer
    {
        private readonly Queue<(long order,GameplaySkillTraceRecord record)> _entries=new Queue<(long,GameplaySkillTraceRecord)>();
        private long _order;
        public int Capacity {get;}
        public bool Enabled {get;set;}
        public GameplaySkillTraceBuffer(int capacity=128){if(capacity<=0)throw new ArgumentOutOfRangeException(nameof(capacity));Capacity=capacity;}
        internal void Record(GameplaySkillTraceRecord record)
        {if(!Enabled)return;while(_entries.Count>=Capacity)_entries.Dequeue();_entries.Enqueue((++_order,record));}
        public IReadOnlyList<(long order,GameplaySkillTraceRecord record)> Snapshot()=>Array.AsReadOnly(_entries.ToArray());
        public void Clear()=>_entries.Clear();
    }
}
