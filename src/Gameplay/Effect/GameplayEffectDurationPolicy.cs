namespace FRFrame.Gameplay
{
    /// <summary>Duration/Infinite 拥有活动生命周期；Instant 仅同步执行一次。</summary>
    public enum GameplayEffectDurationPolicy
    {
        Duration,
        Infinite,
        Instant
    }
}
