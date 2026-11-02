namespace FRFrame.Gameplay
{
    /// <summary>Identity of one active Sequence entry; a handle cannot control another container.</summary>
    public readonly struct GameplaySequenceHandle
    {
        internal readonly object Owner;
        internal readonly long Id;
        internal GameplaySequenceHandle(object owner, long id) { Owner = owner; Id = id; }
        public bool IsValid => Owner != null && Id > 0;
        /// <summary>Container-local sequence for read-only diagnostics.</summary>
        public long LocalSequence => Id;
    }
}
