namespace FRFrame.Gameplay
{
    public enum GameplaySequenceTerminationReason { Completed, Cancelled, Cleared }
    /// <summary>Read-only lifecycle notification, not an authored rule continuation or another clock.</summary>
    public readonly struct GameplaySequenceTermination
    {
        public GameplaySequenceHandle Handle { get; }
        public GameplayContext Context { get; }
        public GameplaySequenceTerminationReason Reason { get; }
        public GameplaySequenceTermination(GameplaySequenceHandle handle, GameplayContext context, GameplaySequenceTerminationReason reason)
        { Handle = handle; Context = context; Reason = reason; }
    }
}
