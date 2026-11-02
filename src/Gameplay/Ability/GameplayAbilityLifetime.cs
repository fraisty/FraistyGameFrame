namespace FRFrame.Gameplay
{
    /// <summary>Optional exact termination cause for an owned lifetime. Called once instead of
    /// IDisposable.Dispose after the cast is detached. Spatial callbacks distinguish normal End
    /// from cancellation; implementations must release their resource even when an observer throws.</summary>
    public interface IGameplayAbilityTerminationAwareLifetime : System.IDisposable
    {
        void Terminate(GameplayAbilityTerminationObservation observation);
    }
    /// <summary>Preparing reserves only this grant; resources and activation tags are committed only in Active.</summary>
    public enum GameplayAbilityPhase { Inactive, Preparing, Active }
    /// <summary>Why a Preparing or Active scope terminated. Observers run once after cleanup; they do not drive authored continuation.</summary>
    public enum GameplayAbilityTerminationReason { Ended, Cancelled, Removed, OngoingBlocked, CancelledByAbility, CommitRejected }
    /// <summary>Detached scope observation after authoritative detachment and all cleanup attempts.</summary>
    public sealed class GameplayAbilityTerminationObservation
    {
        public GameplayAbilityDefinition Definition { get; }
        public GameplayAbilityHandle Handle { get; }
        public GameplayAbilityPhase Phase { get; }
        public GameplayContext Context { get; }
        public GameplayAbilityTerminationReason Reason { get; }
        internal GameplayAbilityTerminationObservation(GameplayAbilityDefinition definition, GameplayAbilityHandle handle,
            GameplayAbilityPhase phase, GameplayContext context, GameplayAbilityTerminationReason reason)
        { Definition = definition; Handle = handle; Phase = phase; Context = context; Reason = reason; }
    }
}
