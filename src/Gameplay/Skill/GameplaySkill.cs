using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FRFrame.Gameplay
{
    /// <summary>技能执行所需的 owner 服务；游戏层可实现扩展接口提供专属动作，Core 不认识游戏对象。</summary>
    public interface IGameplaySkillHost : IGameplayEffectExecutionTarget
    {
        GameplaySourceId OwnerId { get; }
        GameplayEffectContainer Effects { get; }
    }

    /// <summary>仅包装现有 owner 规则容器，不拥有其生命周期。</summary>
    public sealed class GameplaySkillOwnerHost : IGameplaySkillHost
    {
        public GameplaySourceId OwnerId { get; }
        public GameplaySourceId TargetId => OwnerId;
        public GameplayTagContainer Tags { get; }
        public AttributeSet Attributes { get; }
        public GameplayEffectContainer Effects { get; }

        public GameplaySkillOwnerHost(GameplaySourceId ownerId, GameplayTagContainer tags,
            AttributeSet attributes, GameplayEffectContainer effects)
        {
            if (!ownerId.IsValid) throw new ArgumentException("Owner ID must be valid.", nameof(ownerId));
            OwnerId = ownerId;
            Tags = tags ?? throw new ArgumentNullException(nameof(tags));
            Attributes = attributes ?? throw new ArgumentNullException(nameof(attributes));
            Effects = effects ?? throw new ArgumentNullException(nameof(effects));
        }
    }

    /// <summary>不可变触发器定义；每次 Bind 为一个 owner 创建独立订阅及可变状态。</summary>
    public interface IGameplaySkillTriggerDefinition
    {
        IGameplaySkillTriggerBinding Bind(IGameplaySkillHost host, GameplayEventDispatcher events,
            Action<GameplayEventData> onTriggered);
    }

    /// <summary>一个 owner 的触发器运行态；Dispose 清理其事件订阅。</summary>
    public interface IGameplaySkillTriggerBinding : IDisposable { }

    /// <summary>仅由需要规则时间的触发器绑定实现；时间由宿主显式提供。</summary>
    public interface IGameplaySkillAdvanceableTriggerBinding
    {
        void Advance(float deltaTime);
    }

    /// <summary>09A 的简单事件触发器；具体标签是静态内容，订阅属于每个 Bind 产生的运行态。</summary>
    public sealed class GameplaySkillEventTriggerDefinition : IGameplaySkillTriggerDefinition
    {
        public GameplayTag EventTag { get; }

        public GameplaySkillEventTriggerDefinition(GameplayTag eventTag)
        {
            if (!eventTag.IsValid) throw new ArgumentException("Event tag must be valid.", nameof(eventTag));
            EventTag = eventTag;
        }

        public IGameplaySkillTriggerBinding Bind(IGameplaySkillHost host, GameplayEventDispatcher events,
            Action<GameplayEventData> onTriggered)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (events == null) throw new ArgumentNullException(nameof(events));
            if (onTriggered == null) throw new ArgumentNullException(nameof(onTriggered));
            return new EventBinding(events, events.Subscribe(EventTag, onTriggered));
        }

        private sealed class EventBinding : IGameplaySkillTriggerBinding
        {
            private readonly GameplayEventDispatcher _events;
            private readonly GameplayEventSubscriptionHandle _subscription;
            private bool _disposed;

            public EventBinding(GameplayEventDispatcher events, GameplayEventSubscriptionHandle subscription)
            {
                _events = events;
                _subscription = subscription;
            }

            public void Dispose()
            {
                if (_disposed) return;
                _disposed = true;
                _events.Unsubscribe(_subscription);
            }
        }
    }

    /// <summary>读取 owner 当前规则状态的技能条件；失败时不解析目标或执行动作。</summary>
    public interface IGameplaySkillCondition
    {
        bool Evaluate(IGameplaySkillHost host, GameplayEventData triggerEvent);
    }

    /// <summary>将一次技能的目标解析为已有 TargetData；null 表示本次没有有效目标。</summary>
    public interface IGameplaySkillTarget
    {
        GameplayTargetData Resolve(IGameplaySkillHost host, GameplayEventData triggerEvent);
    }

    /// <summary>动作结果；Rejected 停止本次技能后续动作与成功反馈。</summary>
    public enum GameplaySkillActionResult { Succeeded, Rejected }

    /// <summary>执行已解析的技能动作；09A 的 Effect Action 只作用于 Self。</summary>
    public interface IGameplaySkillAction
    {
        GameplaySkillActionResult Execute(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host);
    }

    /// <summary>游戏可将通用 ApplyEffect Action 路由到自己的 typed execution target。</summary>
    public interface IGameplaySkillEffectHost
    {
        GameplayEffectApplicationResult ApplyEffect(GameplayContext context,
            GameplayEffectDefinition effect, GameplaySetByCallerValues values);
    }

    /// <summary>只读技能配置。订阅、owner 和 once 状态属于各自的 GameplaySkillRuntime。</summary>
    public sealed class GameplaySkillDefinition
    {
        public IGameplaySkillTriggerDefinition Trigger { get; }
        public IReadOnlyList<IGameplaySkillCondition> Conditions { get; }
        public IGameplaySkillTarget Target { get; }
        public IReadOnlyList<IGameplaySkillAction> Actions { get; }
        public bool Once { get; }
        /// <summary>Opt-in for authored casts: retain the matching owner's event provenance/values.
        /// Legacy ordinary skills keep their established owner context when false.</summary>
        public bool PreserveTriggerContext { get; }

        public GameplaySkillDefinition(IGameplaySkillTriggerDefinition trigger, IEnumerable<IGameplaySkillCondition> conditions,
            IGameplaySkillTarget target, IEnumerable<IGameplaySkillAction> actions, bool once,
            bool preserveTriggerContext = false)
        {
            Trigger = trigger ?? throw new ArgumentNullException(nameof(trigger));
            if (conditions == null) throw new ArgumentNullException(nameof(conditions));
            if (actions == null) throw new ArgumentNullException(nameof(actions));
            Target = target ?? throw new ArgumentNullException(nameof(target));
            var conditionCopy = new List<IGameplaySkillCondition>(conditions);
            var actionCopy = new List<IGameplaySkillAction>(actions);
            if (conditionCopy.Contains(null)) throw new ArgumentException("Conditions cannot contain null.", nameof(conditions));
            if (actionCopy.Count == 0 || actionCopy.Contains(null))
                throw new ArgumentException("Actions must contain at least one non-null action.", nameof(actions));
            Conditions = new ReadOnlyCollection<IGameplaySkillCondition>(conditionCopy);
            Actions = new ReadOnlyCollection<IGameplaySkillAction>(actionCopy);
            Once = once;
            PreserveTriggerContext = preserveTriggerContext;
        }
    }

    /// <summary>一个 owner 的独立技能运行态；借用已有 EventDispatcher，释放时取消订阅。</summary>
    public sealed class GameplaySkillRuntime : IDisposable
    {
        private readonly GameplaySkillDefinition _definition;
        private readonly IGameplaySkillHost _host;
        private readonly Action<GameplayContext> _onExecuted;
        private readonly IGameplaySkillTriggerBinding _triggerBinding;
        private readonly Func<GameplayEventData, GameplayContext, GameplayContext> _executionContext;
        private bool _disposed;

        public bool HasTriggered => ActivationCount > 0;
        /// <summary>Shared immutable configuration; runtime counters remain on this instance.</summary>
        public GameplaySkillDefinition Definition => _definition;
        public bool IsDisposed => _disposed;
        /// <summary>Last successfully executed immutable context; null before the first activation.</summary>
        public GameplayContext LastExecutionContext { get; private set; }
        public int ActivationCount { get; private set; }
        /// <summary>本 owner 的独立触发器绑定，可供调试或类型化运行态检查。</summary>
        public IGameplaySkillTriggerBinding TriggerBinding => _triggerBinding;

        public GameplaySkillRuntime(GameplaySkillDefinition definition, IGameplaySkillHost host,
            GameplayEventDispatcher events, Action<GameplayContext> onExecuted = null,
            Func<GameplayEventData, GameplayContext, GameplayContext> executionContext = null)
        {
            _definition = definition ?? throw new ArgumentNullException(nameof(definition));
            _host = host ?? throw new ArgumentNullException(nameof(host));
            if (!_host.OwnerId.IsValid) throw new ArgumentException("Host owner ID must be valid.", nameof(host));
            if (events == null) throw new ArgumentNullException(nameof(events));
            _onExecuted = onExecuted;
            _executionContext = executionContext;
            _triggerBinding = _definition.Trigger.Bind(_host, events, OnTriggered)
                ?? throw new InvalidOperationException("Skill trigger did not return a binding.");
        }

        private void OnTriggered(GameplayEventData triggerEvent)
        {
            if (_disposed || (_definition.Once && HasTriggered)) return;
            GameplaySkillTrace.Write(_host,triggerEvent.Context,triggerEvent,GameplaySkillTraceStage.Triggered,"Rule");
            foreach (IGameplaySkillCondition condition in _definition.Conditions)
                if (!condition.Evaluate(_host, triggerEvent)){GameplaySkillTrace.Write(_host,triggerEvent.Context,triggerEvent,GameplaySkillTraceStage.ConditionRejected,condition.GetType().Name);return;}
            GameplayTargetData target = _definition.Target.Resolve(_host, triggerEvent);
            if (target == null){GameplaySkillTrace.Write(_host,triggerEvent.Context,triggerEvent,GameplaySkillTraceStage.TargetRejected,_definition.Target.GetType().Name);return;}
            var context = new GameplayContext(_host.OwnerId, target, _host.OwnerId,
                new GameplayContextValues(new[] { new KeyValuePair<GameplayTag, float>(
                    GameplayContextValues.TriggerMagnitudeKey, triggerEvent.Magnitude) }));
            // Ordinary skills retain their established owner/source and TriggerMagnitude context.
            // Only the owner's captured cast level is propagated; rich child provenance uses continuations.
            if (triggerEvent.Context != null && triggerEvent.Context.InstigatorId == _host.OwnerId)
                context = _definition.PreserveTriggerContext ? triggerEvent.Context.WithTarget(target)
                    .WithValues(triggerEvent.Context.Values.WithValue(GameplayContextValues.TriggerMagnitudeKey, triggerEvent.Magnitude)) :
                    context.WithAbilityLevel(triggerEvent.Context.AbilityLevel);
            // Optional per-installation cast scope adapter. Definitions remain immutable; null suppresses
            // execution for an unrelated event/inactive grant. Legacy callers do not install this adapter.
            if (_executionContext != null)
            {
                context = _executionContext(triggerEvent, context);
                if (context == null){GameplaySkillTrace.Write(_host,triggerEvent.Context,triggerEvent,GameplaySkillTraceStage.ContextRejected,"Installation scope");return;}
                if (context.InstigatorId != _host.OwnerId) throw new InvalidOperationException("Skill execution instigator must match its owner.");
            }
            var execution = GameplaySkillActionRunner.Execute(_definition.Actions, context, triggerEvent, _host);
            if (execution.Result == GameplaySkillActionResult.Rejected) return;
            ActivationCount++;
            LastExecutionContext = execution.Context;
            _onExecuted?.Invoke(execution.Context);
        }

        /// <summary>推进本 owner 的时间型触发器；事件型触发器无需逐步回调。</summary>
        public void Advance(float deltaTime)
        {
            if (!float.IsFinite(deltaTime) || deltaTime < 0f)
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            if (_disposed) return;
            if (_triggerBinding is IGameplaySkillAdvanceableTriggerBinding advanceable)
                advanceable.Advance(deltaTime);
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            _triggerBinding.Dispose();
        }
    }

    /// <summary>最小 required/blocked owner Tag 条件，使用现有层级 HasTag 语义。</summary>
    public sealed class GameplaySkillTagCondition : IGameplaySkillCondition
    {
        public IReadOnlyList<GameplayTag> Required { get; }
        public IReadOnlyList<GameplayTag> Blocked { get; }

        public GameplaySkillTagCondition(IEnumerable<GameplayTag> required, IEnumerable<GameplayTag> blocked)
        {
            if (required == null) throw new ArgumentNullException(nameof(required));
            if (blocked == null) throw new ArgumentNullException(nameof(blocked));
            var requiredCopy = new List<GameplayTag>(required);
            var blockedCopy = new List<GameplayTag>(blocked);
            if (requiredCopy.Exists(tag => !tag.IsValid) || blockedCopy.Exists(tag => !tag.IsValid))
                throw new ArgumentException("Condition tags must be valid.");
            Required = new ReadOnlyCollection<GameplayTag>(requiredCopy);
            Blocked = new ReadOnlyCollection<GameplayTag>(blockedCopy);
        }

        public bool Evaluate(IGameplaySkillHost host, GameplayEventData triggerEvent)
        {
            if (host == null) throw new ArgumentNullException(nameof(host));
            foreach (GameplayTag tag in Required)
                if (!host.Tags.HasTag(tag)) return false;
            foreach (GameplayTag tag in Blocked)
                if (host.Tags.HasTag(tag)) return false;
            return true;
        }
    }

    /// <summary>只解析技能 owner 本人，不查询场景或游戏单位。</summary>
    public sealed class GameplaySkillSelfTarget : IGameplaySkillTarget
    {
        public GameplayTargetData Resolve(IGameplaySkillHost host, GameplayEventData triggerEvent) =>
            host != null ? new GameplayTargetData(host.OwnerId) :
                throw new ArgumentNullException(nameof(host));
    }

    /// <summary>使用 owner 已有的 EffectContainer 对 Self 应用不可变 Effect。</summary>
    public sealed class GameplaySkillApplyEffectAction : IGameplaySkillAction
    {
        public GameplayEffectDefinition Effect { get; }

        public GameplaySkillApplyEffectAction(GameplayEffectDefinition effect) =>
            Effect = effect ?? throw new ArgumentNullException(nameof(effect));

        public GameplaySkillActionResult Execute(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (host == null) throw new ArgumentNullException(nameof(host));
            if (context.TargetData.PrimaryTargetId != host.OwnerId)
                throw new InvalidOperationException("ApplyEffect Action currently requires Self target.");
            GameplayEffectApplicationResult result = host is IGameplaySkillEffectHost routed
                ? routed.ApplyEffect(context, Effect, GameplaySetByCallerValues.Empty)
                : host.Effects.Apply(Effect, new GameplayEffectApplicationContext(
                    context, host, GameplaySetByCallerValues.Empty));
            return result.Succeeded ? GameplaySkillActionResult.Succeeded : GameplaySkillActionResult.Rejected;
        }
    }
}
