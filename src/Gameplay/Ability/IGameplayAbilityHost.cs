namespace FRFrame.Gameplay
{
    /// <summary>显式 Ability 的 owner 规则视图；游戏可另外实现自己的 typed resource capability。</summary>
    public interface IGameplayAbilityHost : IGameplayEffectExecutionTarget
    {
        GameplaySourceId OwnerId { get; }
        GameplayEffectContainer Effects { get; }
    }

    /// <summary>不可变 Cost 策略；CanPay 无副作用，TryCommit 的 false 或异常必须不留下部分支付。</summary>
    public interface IGameplayAbilityCost
    {
        bool CanPay(IGameplayAbilityHost host);
        bool TryCommit(IGameplayAbilityHost host);
    }

    /// <summary>Optional captured-input payment. Caller keys refer to the same immutable scalar
    /// snapshot as Context keys for Ability costs; target attribute reads remain owner-relative.</summary>
    public interface IGameplayAbilityContextCost : IGameplayAbilityLevelCost
    {
        bool CanPay(IGameplayAbilityHost host,int level,GameplayContext context);
        bool TryCommit(IGameplayAbilityHost host,int level,GameplayContext context);
    }
    /// <summary>Optional first-party level-aware payment. Legacy costs keep their existing host-only contract.
    /// Both queries and commit receive the authoritative grant level; implementations remain stateless.</summary>
    public interface IGameplayAbilityLevelCost : IGameplayAbilityCost
    {
        bool CanPay(IGameplayAbilityHost host, int level);
        bool TryCommit(IGameplayAbilityHost host, int level);
    }
}
