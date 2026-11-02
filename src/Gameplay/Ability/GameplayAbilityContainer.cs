using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace FRFrame.Gameplay
{
    /// <summary>Stable first blocking condition for one granted ability.</summary>
    public enum GameplayAbilityBlockReason
    {
        None, InvalidHandle, AlreadyActive, MissingRequiredTag, BlockedByTag,
        BlockedByAbility, Cost, NoCharges, Cooldown, AlreadyPreparing
    }

    /// <summary>Read-only activation and timing snapshot. Charge values are zero for normal-cooldown abilities.</summary>
    public readonly struct GameplayAbilityStatus
    {
        public GameplayAbilityBlockReason BlockReason { get; }
        public GameplayAbilityPhase Phase { get; }
        public bool CanActivate => BlockReason == GameplayAbilityBlockReason.None;
        public int CurrentCharges { get; }
        public int MaxCharges { get; }
        public float RechargeRemaining { get; }
        public float RechargeDuration { get; }
        public float CooldownRemaining { get; }
        public float CooldownDuration { get; }

        public GameplayAbilityStatus(GameplayAbilityBlockReason blockReason, int currentCharges, int maxCharges,
            float rechargeRemaining, float rechargeDuration, float cooldownRemaining, float cooldownDuration,
            GameplayAbilityPhase phase = GameplayAbilityPhase.Inactive)
        {
            BlockReason = blockReason;
            Phase = phase;
            CurrentCharges = currentCharges;
            MaxCharges = maxCharges;
            RechargeRemaining = rechargeRemaining;
            RechargeDuration = rechargeDuration;
            CooldownRemaining = cooldownRemaining;
            CooldownDuration = cooldownDuration;
        }
    }

    /// <summary>
    /// 管理一个 Owner 已 Grant Ability 的显式激活与 Charge 生命周期；不读取 Unity 时间、不推进 Effect。
    /// 主路径绑定 IGameplayAbilityHost；调用方保证它的 Tags、Effects 与来源分配器同属一个运行域。
    /// </summary>
    public sealed class GameplayAbilityContainer
    {
        private readonly IGameplayAbilityHost _host;
        private readonly GameplayTagContainer _ownerTags;
        private readonly GameplayEffectContainer _ownerEffects;
        private readonly GameplaySourceIdAllocator _sourceIdAllocator;
        private readonly object _handleOwner = new object();
        private readonly Dictionary<GameplayAbilityHandle, Entry> _entries =
            new Dictionary<GameplayAbilityHandle, Entry>();
        private long _lastSequence;
        private bool _activationCommitInProgress;
        private readonly Dictionary<IDisposable, Entry> _lifetimeOwners = new Dictionary<IDisposable, Entry>(LifetimeComparer.Instance);
        private readonly HashSet<GameplayEffectHandle> _cleanupEffects = new HashSet<GameplayEffectHandle>();
        private readonly HashSet<GameplayEffectLayerHandle> _cleanupLayers = new HashSet<GameplayEffectLayerHandle>();
        /// <summary>Container-scoped synchronous notification after all cleanup attempts; it is not authored continuation.</summary>
        public event Action<GameplayAbilityTerminationObservation> Terminated;
        /// <summary>Latest terminated scope in this container, retained after grant removal.</summary>
        public GameplayAbilityTerminationObservation LastTermination { get; private set; }

        /// <summary>
        /// 绑定 Owner 的标签、Effect 与运行域来源分配器；验证 owner 身份和 AttributeSet。
        /// </summary>
        public GameplayAbilityContainer(IGameplayAbilityHost host,
            GameplaySourceIdAllocator sourceIdAllocator)
        {
            _host = host ?? throw new ArgumentNullException(nameof(host));
            if (!host.OwnerId.IsValid || host.TargetId != host.OwnerId)
                throw new ArgumentException("Ability host must have matching valid owner and target IDs.", nameof(host));
            _ownerTags = host.Tags ?? throw new ArgumentException("Host tags are required.", nameof(host));
            _ownerEffects = host.Effects ?? throw new ArgumentException("Host effects are required.", nameof(host));
            if (!ReferenceEquals(host.Attributes, _ownerEffects.TargetAttributes))
                throw new ArgumentException("Host attributes must belong to its Effect container.", nameof(host));
            _sourceIdAllocator = sourceIdAllocator ?? throw new ArgumentNullException(nameof(sourceIdAllocator));
        }

        /// <summary>Verifies a borrowed host/domain binding without exposing mutable container internals.</summary>
        public bool IsBoundTo(IGameplayAbilityHost host, GameplaySourceIdAllocator allocator) =>
            ReferenceEquals(_host, host) && ReferenceEquals(_sourceIdAllocator, allocator);

        /// <summary>
        /// 授予一份 Ability Definition 并返回本容器内唯一 Handle。
        /// 同一个 Definition 可以多次 Grant，每份授权拥有独立活动状态。
        /// </summary>
        public GameplayAbilityHandle Grant(GameplayAbilityDefinition definition, int level = 1)
        {
            if (definition == null)
                throw new ArgumentNullException(nameof(definition));
            if (level < 1 || level > definition.MaxLevel) throw new ArgumentOutOfRangeException(nameof(level));

            long sequence = checked(_lastSequence + 1);
            var handle = new GameplayAbilityHandle(_handleOwner, sequence);
            _entries.Add(handle, new Entry(definition, handle, level));
            _lastSequence = sequence;
            return handle;
        }

        /// <summary>Reads the current grant level without evaluating eligibility, costs or allocating
        /// a diagnostic snapshot. Foreign, removed and default handles return false and level zero.</summary>
        public bool TryGetLevel(GameplayAbilityHandle handle, out int level)
        {
            if (_entries.TryGetValue(handle, out Entry entry)) { level = entry.Level; return true; }
            level = 0;
            return false;
        }

        /// <summary>Changes an inactive grant within its definition's level range. Active/preparing grants
        /// reject upgrades so an in-flight execution cannot silently change level.</summary>
        public bool TrySetLevel(GameplayAbilityHandle handle, int level)
        {
            if (!_entries.TryGetValue(handle, out Entry entry) || entry.Phase != GameplayAbilityPhase.Inactive ||
                entry.Committing || entry.Activating || level < 1 || level > entry.Definition.MaxLevel) return false;
            entry.Level = level;
            return true;
        }

        /// <summary>Creates a detached cast/child context from an active grant. Level and activation source
        /// are captured once; later End/Upgrade does not rewrite this snapshot.</summary>
        public bool TryCreateExecutionContext(GameplayAbilityHandle handle, GameplayTargetData target,
            out GameplayContext context)
        {
            context = null;
            if (!_entries.TryGetValue(handle, out Entry entry) || !entry.IsActive) return false;
            context = new GameplayContext(_host.OwnerId, target, entry.ActivationSourceId,
                entry.ActiveContext?.Values, entry.Level);
            return true;
        }

        /// <summary>
        /// 移除一份已 Grant Ability，摘除 Grant 后完整清理 Preparing/Active 的 owned scope；
        /// 已开始的冷却 Effect 不会因此移除。无效、外部容器或已移除 Handle 返回 false。
        /// </summary>
        public bool Remove(GameplayAbilityHandle handle)
        {
            if (!_entries.TryGetValue(handle, out Entry entry))
                return false;

            if(entry.Activating&&!entry.IsActive)return false;

            _entries.Remove(handle); // Detach the grant before external cleanup can reenter.
            if (entry.Phase != GameplayAbilityPhase.Inactive)
                FinishActivation(entry, GameplayAbilityTerminationReason.Removed);
            return true;
        }

        /// <summary>
        /// 查询当前是否可以激活：Handle 必须仍被本容器拥有、当前非 Active、满足全部 RequiredTags，
        /// 且不命中 BlockedTag、Charge 耗尽或 Cooldown Effect 的 GrantedTag。标签使用层级查询。
        /// </summary>
        public bool CanActivate(GameplayAbilityHandle handle) => GetStatus(handle).CanActivate;

        /// <summary>
        /// 按 Active、required、blocked、active Ability relation、Cost、Charge、Cooldown 的固定优先级
        /// 返回首个阻止原因，同时保留此 Grant 的充能/冷却快照。
        /// </summary>
        public GameplayAbilityStatus GetStatus(GameplayAbilityHandle handle) => GetStatus(handle, false);

        /// <summary>Eligibility with this call's captured scalar inputs. Does not store the context or spend resources.</summary>
        public GameplayAbilityStatus GetStatus(GameplayAbilityHandle handle,GameplayContext context)
        {ValidateContext(context);return GetStatus(handle,false,context);}
        private GameplayAbilityStatus GetStatus(GameplayAbilityHandle handle, bool committing,GameplayContext context=null)
        {
            if (!_entries.TryGetValue(handle, out Entry entry))
                return new GameplayAbilityStatus(GameplayAbilityBlockReason.InvalidHandle, 0, 0, 0f, 0f, 0f, 0f);

            GameplayAbilityChargeDefinition charge = entry.Definition.Charge;
            GameplayEffectDefinition cooldown = entry.Definition.CooldownEffect;
            float cooldownRemaining = 0f;
            if (cooldown != null)
                _ownerEffects.TryGetRemainingDuration(entry.LastCooldownEffectHandle, out cooldownRemaining);
            GameplayAbilityBlockReason reason = GameplayAbilityBlockReason.None;
            if (_activationCommitInProgress || entry.Activating) reason = GameplayAbilityBlockReason.AlreadyPreparing;
            else if (entry.IsActive) reason = GameplayAbilityBlockReason.AlreadyActive;
            else if (!committing && entry.Phase == GameplayAbilityPhase.Preparing) reason = GameplayAbilityBlockReason.AlreadyPreparing;

            if (reason == GameplayAbilityBlockReason.None)
                foreach (GameplayTag requiredTag in entry.Definition.RequiredTags)
                    if (!_ownerTags.HasTag(requiredTag)) { reason = GameplayAbilityBlockReason.MissingRequiredTag; break; }

            if (reason == GameplayAbilityBlockReason.None)
                foreach (GameplayTag blockedTag in entry.Definition.BlockedTags)
                    if (_ownerTags.HasTag(blockedTag)) { reason = GameplayAbilityBlockReason.BlockedByTag; break; }

            if (reason == GameplayAbilityBlockReason.None)
                foreach (Entry active in OrderedEntries())
                    if (active.IsActive && MatchesAny(active.Definition.BlockAbilitiesWithTags,
                            entry.Definition.AbilityTags))
                    { reason = GameplayAbilityBlockReason.BlockedByAbility; break; }

            if (reason == GameplayAbilityBlockReason.None && entry.Definition.Cost != null &&
                !(entry.Definition.Cost is IGameplayAbilityContextCost contextCost ? contextCost.CanPay(_host,entry.Level,context??entry.ActiveContext) : entry.Definition.Cost is IGameplayAbilityLevelCost levelCost ?
                    levelCost.CanPay(_host, entry.Level) : entry.Definition.Cost.CanPay(_host)))
                reason = GameplayAbilityBlockReason.Cost;

            if (reason == GameplayAbilityBlockReason.None && charge != null && entry.CurrentCharges == 0)
                reason = GameplayAbilityBlockReason.NoCharges;
            if (reason == GameplayAbilityBlockReason.None && cooldown != null)
                foreach (GameplayTag cooldownTag in cooldown.GrantedTags)
                    if (_ownerTags.HasTag(cooldownTag)) { reason = GameplayAbilityBlockReason.Cooldown; break; }

            return new GameplayAbilityStatus(reason, entry.CurrentCharges, charge?.MaxCharges ?? 0,
                entry.RechargeRemaining, charge?.RechargeDuration ?? 0f,
                cooldownRemaining, cooldown?.DurationValue!=null?entry.LastCooldownDuration:cooldown?.Duration??0f, entry.Phase);
        }

        /// <summary>
        /// Advances only per-grant sequential recharge; the owner separately advances Effects.
        /// Accumulation stays double, while boundary comparisons use authored float time.
        /// End/Cancel do not refund charges, and spending another charge does not reset an ongoing recharge.
        /// </summary>
        public void Advance(float amount)
        {
            if (!float.IsFinite(amount) || amount < 0f) throw new ArgumentOutOfRangeException(nameof(amount));
            if(_activationCommitInProgress)throw new InvalidOperationException("Ability clock cannot advance during activation commit.");
            foreach (Entry entry in OrderedEntries())
            {
                if (!_entries.ContainsKey(entry.Handle)) continue;
                if (entry.Phase != GameplayAbilityPhase.Inactive && HasAnyOwnerTag(entry.Definition.OngoingBlockedTags))
                    FinishActivation(entry, GameplayAbilityTerminationReason.OngoingBlocked);
                if (!_entries.ContainsKey(entry.Handle)) continue;
                GameplayAbilityChargeDefinition charge = entry.Definition.Charge;
                if (charge == null || entry.CurrentCharges == charge.MaxCharges) continue;
                entry.RechargeElapsed += amount;
                while (entry.CurrentCharges < charge.MaxCharges &&
                    (float)entry.RechargeElapsed >= (float)entry.NextRechargeAt)
                {
                    entry.CurrentCharges++;
                    entry.NextRechargeAt += charge.RechargeDuration;
                }
                if (entry.CurrentCharges == charge.MaxCharges)
                {
                    // A full grant has no pending schedule; excess Advance must not carry into the next cast.
                    entry.RechargeElapsed = 0d;
                    entry.NextRechargeAt = 0d;
                }
            }
        }

        /// <summary>
        /// 门禁通过后暂存可撤销的 tags/cooldown/activation contributions，再原子支付并提交活动状态和 Charge。
        /// 支付拒绝或提交前异常不执行初始动作，静默撤销暂存贡献。成功提交后初始 Execution 恰好执行一次；
        /// 其异常传播但不退款、不撤销已发布结果，活动 grant 仍拥有清理责任，可正常 Cancel/Remove。
        /// </summary>
        public bool TryActivate(GameplayAbilityHandle handle) => TryActivate(handle, null);

        /// <summary>Attach this execution's context only when activation succeeds; End/Cancel/Remove clear it.</summary>
        public bool TryActivate(GameplayAbilityHandle handle, GameplayContext context) => TryActivateCore(handle, context, false);

        /// <summary>Reserves this grant without spending Cost/Charge or applying tags/effects. No cast timer is created.</summary>
        public bool TryBeginActivation(GameplayAbilityHandle handle, GameplayContext context = null)
        {
            if (!GetStatus(handle,false,context).CanActivate) return false;
            ValidateContext(context);
            Entry entry = _entries[handle];
            entry.ActiveContext = context?.WithAbilityLevel(entry.Level);
            entry.Phase = GameplayAbilityPhase.Preparing;
            return true;
        }
        /// <summary>Revalidates current gates and commits the saved Begin context exactly once. Failure terminates preparation.</summary>
        public bool TryCommitActivation(GameplayAbilityHandle handle)
        {
            if (!_entries.TryGetValue(handle, out Entry entry) || entry.Phase != GameplayAbilityPhase.Preparing || entry.Committing) return false;
            entry.Committing = true;
            try
            {
                bool success = TryActivateCore(handle, entry.ActiveContext, true);
                if (!success && entry.Phase == GameplayAbilityPhase.Preparing)
                    FinishActivation(entry, GameplayAbilityTerminationReason.CommitRejected);
                return success;
            }
            catch (Exception error)
            {
                if (entry.Phase == GameplayAbilityPhase.Preparing)
                {
                    try { FinishActivation(entry, GameplayAbilityTerminationReason.CommitRejected); }
                    catch (Exception cleanup) { throw new AggregateException(error, cleanup); }
                }
                throw;
            }
            finally { entry.Committing = false; }
        }
        private void ValidateContext(GameplayContext context)
        {
            if (context != null && context.InstigatorId != _host.OwnerId)
                throw new ArgumentException("Activation context instigator must be the Ability owner.", nameof(context));
        }
        private bool TryActivateCore(GameplayAbilityHandle handle, GameplayContext context, bool committing)
        {
            if (!GetStatus(handle, committing,context).CanActivate)
                return false;
            ValidateContext(context);

            Entry entry = _entries[handle];
            GameplaySourceId activationSourceId = _sourceIdAllocator.Allocate();
            GameplayEffectHandle cooldownEffectHandle = default;
            GameplayEffectApplicationContext cooldownApplication=null;
            float cooldownDuration=0;
            var activationEffectHandles = new List<GameplayEffectHandle>();
            entry.Activating = true;
            bool committed=false,attemptOpen=true;
            _activationCommitInProgress=true;_ownerEffects.BeginActivationAttempt();
            try
            {
                foreach (GameplayTag ownedTag in entry.Definition.ActivationOwnedTags)
                    _ownerTags.AddTag(ownedTag, activationSourceId);

                if (entry.Definition.CooldownEffect != null){
                    var cooldown=entry.Definition.CooldownEffect;
                    if(cooldown.DurationValue==null)cooldownEffectHandle=_ownerEffects.Apply(cooldown);
                    else{
                        var cooldownContext=new GameplayContext(_host.OwnerId,new GameplayTargetData(_host.OwnerId),activationSourceId,context?.Values,entry.Level);
                        cooldownApplication=new GameplayEffectApplicationContext(cooldownContext,_host,cooldownContext.Values.AsSetByCallerValues());
                        cooldownEffectHandle=_ownerEffects.Apply(cooldown,cooldownApplication).ActiveHandle;
                    }
                    if(!cooldownEffectHandle.IsValid){RollbackAttempt(activationSourceId,cooldownEffectHandle,activationEffectHandles);return false;}
                    _ownerEffects.TryGetAppliedDuration(cooldownEffectHandle,out cooldownDuration);
                }

                if (entry.Definition.ActivationEffects.Count > 0)
                {
                    var selfContext = new GameplayContext(_host.OwnerId,
                        new GameplayTargetData(_host.OwnerId), context?.EffectiveSourceId ?? _host.OwnerId,
                        context?.Values, entry.Level);
                    var application = new GameplayEffectApplicationContext(selfContext, _host,
                        // Explicit per-application snapshot; missing keys are not implicit zero.
                        selfContext.Values.AsSetByCallerValues());
                    foreach (GameplayEffectDefinition effect in entry.Definition.ActivationEffects)
                    {
                        GameplayEffectApplicationResult result = _ownerEffects.StageActivation(effect, application);
                        if (!result.Succeeded)
                        {
                            RollbackAttempt(activationSourceId, cooldownEffectHandle, activationEffectHandles);
                            return false;
                        }
                        activationEffectHandles.Add(result.ActiveHandle);
                    }
                }

                if (entry.Definition.Cost != null && !(entry.Definition.Cost is IGameplayAbilityContextCost contextCost ? contextCost.TryCommit(_host,entry.Level,context) : entry.Definition.Cost is IGameplayAbilityLevelCost levelCost ?
                    levelCost.TryCommit(_host, entry.Level) : entry.Definition.Cost.TryCommit(_host)))
                {
                    RollbackAttempt(activationSourceId, cooldownEffectHandle, activationEffectHandles);
                    return false;
                }

                entry.ActivationSourceId = activationSourceId;
                entry.LastCooldownEffectHandle = cooldownEffectHandle;
                entry.LastCooldownDuration=cooldownDuration;entry.LastCooldownApplication=cooldownApplication;
                entry.ActiveContext = context?.WithAbilityLevel(entry.Level);
                entry.OwnedEffectHandles.AddRange(activationEffectHandles);
                entry.Phase = GameplayAbilityPhase.Active;
                if (entry.Definition.Charge != null)
                {
                    if (entry.CurrentCharges == entry.Definition.Charge.MaxCharges)
                    {
                        entry.RechargeElapsed = 0d;
                        entry.NextRechargeAt = entry.Definition.Charge.RechargeDuration;
                    }
                    entry.CurrentCharges--;
                }
                committed=true;
                _activationCommitInProgress=false;_ownerEffects.EndActivationAttempt();attemptOpen=false;
                foreach (Entry other in OrderedEntries())
                    if (other != entry && other.Phase != GameplayAbilityPhase.Inactive &&
                        MatchesAny(entry.Definition.CancelAbilitiesWithTags, other.Definition.AbilityTags))
                        FinishActivation(other, GameplayAbilityTerminationReason.CancelledByAbility);
                foreach(var effect in activationEffectHandles)
                {
                    if(!entry.IsActive)break; // An initial action may terminate the successful grant.
                    _ownerEffects.CommitActivationInitial(effect);
                }
                return true;
            }
            catch
            {
                if (!committed)
                    RollbackAttempt(activationSourceId, cooldownEffectHandle, activationEffectHandles);
                throw;
            }
            finally { if(attemptOpen)_ownerEffects.EndActivationAttempt();_activationCommitInProgress=false;entry.Activating = false; }
        }

        private void RollbackAttempt(GameplaySourceId source, GameplayEffectHandle cooldown,
            IReadOnlyList<GameplayEffectHandle> effects)
        {
            for (int i = effects.Count - 1; i >= 0; i--)
                _ownerEffects.AbortActivation(effects[i]);
            if (cooldown.IsValid) _ownerEffects.AbortActivation(cooldown);
            _ownerTags.RemoveTagsFromSource(source);
        }

        /// <summary>
        /// Registers cleanup of a newly created, still active Effect in this owner's Effect container.
        /// Only an active grant and NewActiveEffect result are accepted; merged stack results never
        /// transfer ownership. Duplicate registration on the same grant succeeds without adding a relation.
        /// A handle already owned by another grant is rejected. End, Cancel and grant removal remove
        /// registered Effects; their clocks and state remain solely in EffectContainer.
        /// Normal cooldowns stay independent unless explicitly registered by the caller.
        /// Callers must register against the activation that created the result, before it ends.
        /// </summary>
        public bool TryOwnEffect(GameplayAbilityHandle ability, GameplayEffectApplicationResult application)
        {
            if (!_entries.TryGetValue(ability, out Entry entry) || !entry.IsActive ||
                application.Outcome != GameplayEffectApplicationOutcome.NewActiveEffect ||
                !_ownerEffects.IsActive(application.ActiveHandle) || _cleanupEffects.Contains(application.ActiveHandle))
                return false;

            foreach (Entry other in _entries.Values)
            {
                if (other != entry && other.OwnedEffectHandles.Contains(application.ActiveHandle)) return false;
                if (other.OwnedLayers.Exists(layer => layer.EffectHandle == application.ActiveHandle && _ownerEffects.IsLayerActive(layer))) return false;
            }
            foreach (var layer in _cleanupLayers) if (layer.EffectHandle == application.ActiveHandle) return false;

            // Passive event/completion cycles can replace an Effect many times within one activation.
            // Compact at successful registration, without subscribing to Effect removal events.
            entry.OwnedEffectHandles.RemoveAll(handle => !_ownerEffects.IsActive(handle));
            if (!entry.OwnedEffectHandles.Contains(application.ActiveHandle))
                entry.OwnedEffectHandles.Add(application.ActiveHandle);
            return true;
        }

        /// <summary>仅查询本容器内指定 Grant 当前是否处于 Active；无效或已移除 Handle 返回 false。</summary>
        public bool IsActive(GameplayAbilityHandle handle) =>
            _entries.TryGetValue(handle, out Entry entry) && entry.IsActive;

        /// <summary>Return the context of an active grant; a context-free activation returns false.</summary>
        public bool TryGetActiveContext(GameplayAbilityHandle handle, out GameplayContext context)
        {
            if (_entries.TryGetValue(handle, out Entry entry) && entry.IsActive && entry.ActiveContext != null)
            {
                context = entry.ActiveContext;
                return true;
            }
            context = null;
            return false;
        }

        /// <summary>
        /// 查询指定 Grant 最近一次成功激活时由它自身启动、且仍活动的冷却 Effect 剩余时长。
        /// End/Cancel 不影响查询；外部同标签 Effect 只会阻止激活，不属于该 Grant 的自身冷却。
        /// 无效、其他容器、已移除 Grant、无自身冷却或冷却已到期时返回 false，并将 remaining 置为 0。
        /// </summary>
        public bool TryGetCooldownRemaining(GameplayAbilityHandle handle, out float remaining)
        {
            if (_entries.TryGetValue(handle, out Entry entry))
                return _ownerEffects.TryGetRemainingDuration(entry.LastCooldownEffectHandle, out remaining);

            remaining = 0f;
            return false;
        }

        /// <summary>
        /// 正常结束一次 Active Ability，清理 ActivationOwnedTags、初始及动态登记的 Effect handles，回到 Inactive。
        /// 已开始的冷却继续由 EffectContainer 管理；非 Active 或外部 Handle 返回 false。
        /// </summary>
        public bool End(GameplayAbilityHandle handle) => Finish(handle, GameplayAbilityTerminationReason.Ended, false);

        /// <summary>Terminates Preparing or Active. Precommit cancellation has no resource payment to refund.</summary>
        public bool Cancel(GameplayAbilityHandle handle) => Finish(handle, GameplayAbilityTerminationReason.Cancelled, true);

        private bool Finish(GameplayAbilityHandle handle, GameplayAbilityTerminationReason reason, bool preparing)
        {
            if (!_entries.TryGetValue(handle, out Entry entry) || entry.Phase == GameplayAbilityPhase.Inactive ||
                (!preparing && !entry.IsActive)) return false;
            if(entry.Activating&&!entry.IsActive)return false;
            FinishActivation(entry, reason);
            return true;
        }
        private void FinishActivation(Entry entry, GameplayAbilityTerminationReason reason)
        {
            var observation = new GameplayAbilityTerminationObservation(entry.Definition, entry.Handle, entry.Phase, entry.ActiveContext, reason);
            var source = entry.ActivationSourceId;
            var effects = entry.OwnedEffectHandles.ToArray();
            var layers = entry.OwnedLayers.ToArray();
            var lifetimes = entry.OwnedLifetimes.ToArray();
            entry.LastTermination = observation;
            entry.Phase = GameplayAbilityPhase.Inactive;
            entry.ActivationSourceId = default;
            entry.ActiveContext = null;
            entry.OwnedEffectHandles.Clear(); entry.OwnedLayers.Clear(); entry.OwnedLifetimes.Clear();
            // Keep cleanup reservations until the old resource is actually released, even under reentrant activation.
            foreach (var effect in effects) _cleanupEffects.Add(effect);
            foreach (var layer in layers) _cleanupLayers.Add(layer);
            var errors = new List<Exception>();
            void Attempt(Action action) { try { action(); } catch (Exception error) { errors.Add(error); } }
            if (source.IsValid) Attempt(() => _ownerTags.RemoveTagsFromSource(source));
            foreach (var effect in effects)
            {
                Attempt(() => _ownerEffects.Remove(effect, GameplayEffectRemovalCause.OwnerCleanup));
                _cleanupEffects.Remove(effect);
            }
            foreach (var layer in layers)
            {
                Attempt(() => _ownerEffects.RemoveLayer(layer, GameplayEffectRemovalCause.OwnerCleanup));
                _cleanupLayers.Remove(layer);
            }
            foreach (var lifetime in lifetimes)
            {
                Attempt(() => { if (lifetime is IGameplayAbilityTerminationAwareLifetime aware) aware.Terminate(observation); else lifetime.Dispose(); });
                _lifetimeOwners.Remove(lifetime);
            }
            LastTermination = observation;
            if (Terminated != null)
                foreach (Action<GameplayAbilityTerminationObservation> observer in Terminated.GetInvocationList())
                    Attempt(() => observer(observation));
            if (errors.Count > 0) throw new AggregateException("Ability termination cleanup failed.", errors);
        }

        /// <summary>Uses the existing per-grant phase; IsActive remains committed-only.</summary>
        public bool IsPreparing(GameplayAbilityHandle handle) => _entries.TryGetValue(handle, out var entry) && entry.Phase == GameplayAbilityPhase.Preparing;
        /// <summary>True for either Preparing or committed Active; no polling is needed for owned cleanup.</summary>
        public bool IsEngaged(GameplayAbilityHandle handle) => _entries.TryGetValue(handle, out var entry) && entry.Phase != GameplayAbilityPhase.Inactive;

        /// <summary>Owns an idempotent disposable within Preparing/Active. Reference identity is exclusive across grants.</summary>
        public bool TryOwnLifetime(GameplayAbilityHandle ability, IDisposable lifetime)
        {
            if (lifetime == null || !_entries.TryGetValue(ability, out var entry) || entry.Phase == GameplayAbilityPhase.Inactive) return false;
            if (_lifetimeOwners.TryGetValue(lifetime, out var owner))
                return owner == entry && entry.OwnedLifetimes.Exists(item => ReferenceEquals(item, lifetime));
            _lifetimeOwners.Add(lifetime, entry); entry.OwnedLifetimes.Add(lifetime); return true;
        }
        /// <summary>Unlinks a naturally completed child without disposing it.</summary>
        public bool ReleaseOwnedLifetime(GameplayAbilityHandle ability, IDisposable lifetime)
        {
            if (lifetime == null || !_entries.TryGetValue(ability, out var entry) ||
                !_lifetimeOwners.TryGetValue(lifetime, out var owner) || owner != entry) return false;
            int index = entry.OwnedLifetimes.FindIndex(item => ReferenceEquals(item, lifetime));
            if (index < 0) return false;
            entry.OwnedLifetimes.RemoveAt(index); _lifetimeOwners.Remove(lifetime); return true;
        }
        /// <summary>Owns only this newly-created contribution, including the first layer of a merged entry.</summary>
        public bool TryOwnEffectLayer(GameplayAbilityHandle ability, GameplayEffectApplicationResult application)
        {
            var layer = application.LayerHandle;
            if (!_entries.TryGetValue(ability, out var entry) || !entry.IsActive || !layer.IsValid ||
                (application.Outcome != GameplayEffectApplicationOutcome.NewActiveEffect && application.Outcome != GameplayEffectApplicationOutcome.StackIncreased) ||
                !_ownerEffects.IsLayerActive(layer) || _cleanupEffects.Contains(layer.EffectHandle) || _cleanupLayers.Contains(layer)) return false;
            foreach (var other in _entries.Values)
            {
                if (other.OwnedEffectHandles.Contains(layer.EffectHandle)) return false;
                if (other != entry && other.OwnedLayers.Contains(layer)) return false;
            }
            entry.OwnedLayers.RemoveAll(item => !_ownerEffects.IsLayerActive(item));
            if (!entry.OwnedLayers.Contains(layer)) entry.OwnedLayers.Add(layer);
            return true;
        }

        /// <summary>Edits only this grant's own normal cooldown Effect; external same-tag blockers remain untouched.</summary>
        public bool TrySetCooldownRemaining(GameplayAbilityHandle ability, float remaining)
        {
            if (!float.IsFinite(remaining) || remaining < 0) return false;
            if (!_entries.TryGetValue(ability, out var entry) || entry.Definition.CooldownEffect == null) return false;
            if (_ownerEffects.IsActive(entry.LastCooldownEffectHandle))
                return _ownerEffects.TrySetSimpleDurationRemaining(entry.LastCooldownEffectHandle, remaining);
            if (remaining == 0) return true;
            var handle = _ownerEffects.ApplySimpleDuration(entry.Definition.CooldownEffect,entry.LastCooldownApplication,remaining);
            if (!handle.IsValid) return false;
            entry.LastCooldownEffectHandle = handle;
            return _ownerEffects.TrySetSimpleDurationRemaining(handle, remaining);
        }
        /// <summary>Adds/reduces only own normal cooldown from its current remaining; crossing zero clears it.</summary>
        public bool TryAdjustCooldownRemaining(GameplayAbilityHandle ability, float delta)
        {
            if (!float.IsFinite(delta)) return false;
            TryGetCooldownRemaining(ability, out float remaining);
            double value = Math.Max(0d, (double)remaining + delta);
            return value <= float.MaxValue && TrySetCooldownRemaining(ability, (float)value);
        }
        /// <summary>Restarts at the last successful cast's frozen authored duration. Constant CD
        /// can restart before the first cast; value-driven CD requires a captured successful cast.</summary>
        public bool TryResetCooldown(GameplayAbilityHandle ability) => _entries.TryGetValue(ability, out var entry) &&
            entry.Definition.CooldownEffect != null && (entry.Definition.CooldownEffect.DurationValue==null||entry.LastCooldownDuration>0) &&
            TrySetCooldownRemaining(ability,entry.Definition.CooldownEffect.DurationValue==null?entry.Definition.CooldownEffect.Duration:entry.LastCooldownDuration);
        /// <summary>Count edits preserve a running sequential recharge phase; reaching full clears it.</summary>
        public bool TrySetCharges(GameplayAbilityHandle ability, int charges)
        {
            if (!_entries.TryGetValue(ability, out var entry) || entry.Definition.Charge == null ||
                charges < 0 || charges > entry.Definition.Charge.MaxCharges) return false;
            var policy = entry.Definition.Charge;
            if (charges == policy.MaxCharges) { entry.RechargeElapsed = 0; entry.NextRechargeAt = 0; }
            else if (entry.CurrentCharges == policy.MaxCharges) { entry.RechargeElapsed = 0; entry.NextRechargeAt = policy.RechargeDuration; }
            entry.CurrentCharges = charges; return true;
        }
        /// <summary>Restores/consumes charges without resetting a running recharge phase. Out-of-range edits fail unchanged.</summary>
        public bool TryAdjustCharges(GameplayAbilityHandle ability, int delta)
        {
            if (!_entries.TryGetValue(ability, out var entry) || entry.Definition.Charge == null) return false;
            long value = (long)entry.CurrentCharges + delta;
            return value >= 0 && value <= entry.Definition.Charge.MaxCharges && TrySetCharges(ability, (int)value);
        }
        private sealed class LifetimeComparer : IEqualityComparer<IDisposable>
        {
            internal static readonly LifetimeComparer Instance = new LifetimeComparer();
            public bool Equals(IDisposable a, IDisposable b) => ReferenceEquals(a, b);
            public int GetHashCode(IDisposable value) => RuntimeHelpers.GetHashCode(value);
        }

        private List<Entry> OrderedEntries()
        {
            var ordered = new List<Entry>(_entries.Values);
            ordered.Sort((a, b) => a.Handle.Sequence.CompareTo(b.Handle.Sequence));
            return ordered;
        }

        /// <summary>Captures grants in Handle order using the authoritative, side-effect-free status query.</summary>
        public IReadOnlyList<GameplayGrantedAbilitySnapshot> GetGrantedEntriesSnapshot()
        {
            var ordered = OrderedEntries();
            var result = new List<GameplayGrantedAbilitySnapshot>(ordered.Count);
            foreach (Entry entry in ordered)
                result.Add(new GameplayGrantedAbilitySnapshot(entry.Handle, entry.Definition,
                    GetStatus(entry.Handle), entry.IsActive, entry.ActiveContext,
                    entry.OwnedEffectHandles.FindAll(_ownerEffects.IsActive).Count,
                    entry.OwnedLayers.FindAll(_ownerEffects.IsLayerActive).Count, entry.OwnedLifetimes.Count, entry.LastTermination, entry.Level));
            return result.AsReadOnly();
        }

        private bool HasAnyOwnerTag(IReadOnlyList<GameplayTag> tags)
        {
            foreach (GameplayTag tag in tags)
                if (_ownerTags.HasTag(tag)) return true;
            return false;
        }

        private static bool MatchesAny(IReadOnlyList<GameplayTag> relations,
            IReadOnlyList<GameplayTag> identities)
        {
            foreach (GameplayTag relation in relations)
                foreach (GameplayTag identity in identities)
                    if (identity == relation || identity.IsDescendantOf(relation)) return true;
            return false;
        }

        private sealed class Entry
        {
            internal readonly GameplayAbilityDefinition Definition;
            internal readonly GameplayAbilityHandle Handle;
            internal int Level;
            internal readonly List<GameplayEffectHandle> OwnedEffectHandles = new List<GameplayEffectHandle>();
            internal GameplaySourceId ActivationSourceId;
            internal GameplayEffectHandle LastCooldownEffectHandle;
            internal float LastCooldownDuration;
            internal GameplayEffectApplicationContext LastCooldownApplication;
            internal GameplayContext ActiveContext;
            internal GameplayAbilityPhase Phase;
            internal bool IsActive => Phase == GameplayAbilityPhase.Active;
            internal bool Committing;
            internal bool Activating;
            internal GameplayAbilityTerminationObservation LastTermination;
            internal readonly List<GameplayEffectLayerHandle> OwnedLayers = new List<GameplayEffectLayerHandle>();
            internal readonly List<IDisposable> OwnedLifetimes = new List<IDisposable>();
            internal int CurrentCharges;
            internal double RechargeElapsed;
            internal double NextRechargeAt;
            internal float RechargeRemaining => (float)Math.Max(0d, NextRechargeAt - RechargeElapsed);

            internal Entry(GameplayAbilityDefinition definition, GameplayAbilityHandle handle, int level)
            {
                Definition = definition;
                Handle = handle;
                Level = level;
                CurrentCharges = definition.Charge?.MaxCharges ?? 0;
            }
        }

    }
}
