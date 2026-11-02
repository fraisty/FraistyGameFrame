using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay
{
    /// <summary>Opt-in side interface; implement alongside IGameplaySkillAction to carry immutable output to the next action.</summary>
    public interface IGameplaySkillContextAction
    {
        GameplaySkillActionExecution ExecuteWithContext(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host);
    }
    /// <summary>Opt-in condition view of the current action-chain context.</summary>
    public interface IGameplaySkillContextCondition
    {
        bool EvaluateContext(GameplayContext context, IGameplaySkillHost host, GameplayEventData triggerEvent);
    }
    /// <summary>Successful execution must supply a context; rejection stops the chain without rollback.</summary>
    public readonly struct GameplaySkillActionExecution
    {
        public GameplaySkillActionResult Result { get; }
        public GameplayContext Context { get; }
        public GameplaySkillActionExecution(GameplaySkillActionResult result, GameplayContext context)
        {
            if (!Enum.IsDefined(typeof(GameplaySkillActionResult), result)) throw new ArgumentOutOfRangeException(nameof(result));
            if (result == GameplaySkillActionResult.Succeeded && context == null) throw new ArgumentNullException(nameof(context));
            Result = result; Context = context;
        }
    }
    /// <summary>Composite actions propagate the caller's cancellation guard to every nested action.
    /// Used by first-party Branch and standard Search each/completed/no-target callbacks.</summary>
    public interface IGameplaySkillGuardedContextAction
    {
        GameplaySkillActionExecution ExecuteGuarded(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host, Func<bool> canContinue);
    }
    /// <summary>One ordered runner for Skill, SkillStep, nested branches and child continuation. Owns no time or lifetime.</summary>
    public static class GameplaySkillActionRunner
    {
        /// <summary>Legacy successes carry the same context. The optional guard stops pending actions after host cancellation.</summary>
        public static GameplaySkillActionExecution Execute(IReadOnlyList<IGameplaySkillAction> actions, GameplayContext context,
            GameplayEventData triggerEvent, IGameplaySkillHost host, Func<bool> canContinue = null)
        {
            if (actions == null) throw new ArgumentNullException(nameof(actions));
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (host == null) throw new ArgumentNullException(nameof(host));
            int index=0;
            foreach (var action in actions)
            {
                if (canContinue != null && !canContinue()) return new GameplaySkillActionExecution(GameplaySkillActionResult.Rejected, context);
                if (action == null) throw new ArgumentException("Actions cannot contain null.", nameof(actions));
                GameplaySkillActionExecution execution;
                GameplaySkillTrace.Write(host,context,triggerEvent,GameplaySkillTraceStage.ActionStarted,action?.GetType().Name,index,action:action);
                try{
                    if (action is IGameplaySkillGuardedContextAction guarded) execution = guarded.ExecuteGuarded(context, triggerEvent, host, canContinue);
                    else if (action is IGameplaySkillContextAction contextual) execution = contextual.ExecuteWithContext(context, triggerEvent, host);
                    else execution = new GameplaySkillActionExecution(action.Execute(context, triggerEvent, host), context);
                }catch(Exception error){GameplaySkillTrace.Write(host,context,triggerEvent,GameplaySkillTraceStage.ActionFailed,action?.GetType().Name,index,error.Message,action);throw;}
                if (execution.Result == GameplaySkillActionResult.Rejected){GameplaySkillTrace.Write(host,context,triggerEvent,GameplaySkillTraceStage.ActionRejected,action?.GetType().Name,index,action:action);return execution;}
                if (execution.Result != GameplaySkillActionResult.Succeeded || execution.Context == null)
                    throw new InvalidOperationException("Successful contextual action must return a non-null Context.");
                context = execution.Context;
                GameplaySkillTrace.Write(host,context,triggerEvent,GameplaySkillTraceStage.ActionSucceeded,action.GetType().Name,index++,action:action);
            }
            return new GameplaySkillActionExecution(GameplaySkillActionResult.Succeeded, context);
        }
        /// <summary>Context-aware conditions opt in; legacy conditions retain their host/event contract.</summary>
        public static bool Evaluate(IGameplaySkillCondition condition, GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host) =>
            condition is IGameplaySkillContextCondition contextual ? contextual.EvaluateContext(context, host, triggerEvent) : condition.Evaluate(host, triggerEvent);
    }
    /// <summary>Exactly one nested action list executes, then its context merges into the outer ordered list. No loops or waits.</summary>
    public sealed class GameplaySkillBranchAction : IGameplaySkillAction, IGameplaySkillContextAction, IGameplaySkillGuardedContextAction
    {
        public IGameplaySkillCondition Condition { get; }
        public IReadOnlyList<IGameplaySkillAction> TrueActions { get; }
        public IReadOnlyList<IGameplaySkillAction> FalseActions { get; }
        public GameplaySkillBranchAction(IGameplaySkillCondition condition, IEnumerable<IGameplaySkillAction> trueActions, IEnumerable<IGameplaySkillAction> falseActions)
        { Condition = condition ?? throw new ArgumentNullException(nameof(condition)); TrueActions = Copy(trueActions); FalseActions = Copy(falseActions); }
        internal static IReadOnlyList<IGameplaySkillAction> Copy(IEnumerable<IGameplaySkillAction> actions)
        {
            if (actions == null) throw new ArgumentNullException(nameof(actions));
            var copy = new List<IGameplaySkillAction>(actions);
            if (copy.Contains(null)) throw new ArgumentException("Actions cannot contain null.", nameof(actions));
            return copy.AsReadOnly();
        }
        public GameplaySkillActionResult Execute(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host) => ExecuteWithContext(context, triggerEvent, host).Result;
        public GameplaySkillActionExecution ExecuteWithContext(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host) => ExecuteGuarded(context, triggerEvent, host, null);
        GameplaySkillActionExecution IGameplaySkillGuardedContextAction.ExecuteGuarded(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host, Func<bool> canContinue) => ExecuteGuarded(context, triggerEvent, host, canContinue);
        private GameplaySkillActionExecution ExecuteGuarded(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host, Func<bool> canContinue) =>
            GameplaySkillActionRunner.Execute(GameplaySkillActionRunner.Evaluate(Condition, context, triggerEvent, host) ? TrueActions : FalseActions, context, triggerEvent, host, canContinue);
    }
    /// <summary>Scalar comparisons for context values; a missing key never matches, including NotEqual.</summary>
    public enum GameplaySkillContextComparison { Equal, NotEqual, Less, LessOrEqual, Greater, GreaterOrEqual }
    /// <summary>Reads the current branch input scalar; legacy direct evaluation requires an explicit event Context envelope.</summary>
    public sealed class GameplaySkillContextValueCondition : IGameplaySkillCondition, IGameplaySkillContextCondition
    {
        public GameplayTag Key { get; }
        public GameplaySkillContextComparison Comparison { get; }
        public float Constant { get; }
        public GameplaySkillContextValueCondition(GameplayTag key, GameplaySkillContextComparison comparison, float constant)
        {
            if (!key.IsValid || !float.IsFinite(constant)) throw new ArgumentException("Valid key and finite constant required.");
            if (!Enum.IsDefined(typeof(GameplaySkillContextComparison), comparison)) throw new ArgumentOutOfRangeException(nameof(comparison));
            Key = key; Comparison = comparison; Constant = constant;
        }
        public bool Evaluate(IGameplaySkillHost host, GameplayEventData triggerEvent) => triggerEvent.HasContext && EvaluateContext(triggerEvent.Context, host, triggerEvent);
        public bool EvaluateContext(GameplayContext context, IGameplaySkillHost host, GameplayEventData triggerEvent)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!context.Values.TryGetValue(Key, out float value)) return false;
            switch (Comparison)
            {
                case GameplaySkillContextComparison.Equal: return value == Constant;
                case GameplaySkillContextComparison.NotEqual: return value != Constant;
                case GameplaySkillContextComparison.Less: return value < Constant;
                case GameplaySkillContextComparison.LessOrEqual: return value <= Constant;
                case GameplaySkillContextComparison.Greater: return value > Constant;
                default: return value >= Constant;
            }
        }
    }
    /// <summary>Writes one typed finite scalar to a derived immutable context, for subsequent value/branch consumers.</summary>
    public sealed class GameplaySkillSetContextValueAction : IGameplaySkillAction, IGameplaySkillContextAction
    {
        public GameplayTag Key { get; }
        public float Value { get; }
        public GameplaySkillSetContextValueAction(GameplayTag key, float value)
        { if (!key.IsValid || !float.IsFinite(value)) throw new ArgumentException("Valid key and finite value required."); Key = key; Value = value; }
        public GameplaySkillActionResult Execute(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host) => ExecuteWithContext(context, triggerEvent, host).Result;
        public GameplaySkillActionExecution ExecuteWithContext(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host) =>
            new GameplaySkillActionExecution(GameplaySkillActionResult.Succeeded, context.WithValues(context.Values.WithValue(Key, Value)));
    }
    /// <summary>Immutable callback action list for game-owned child hit/arrive/enter/exit. No trigger, timer or mutable owner.</summary>
    public sealed class GameplayContinuationDefinition
    {
        public IReadOnlyList<IGameplaySkillAction> Actions { get; }
        public GameplayContinuationDefinition(IEnumerable<IGameplaySkillAction> actions) { Actions = GameplaySkillBranchAction.Copy(actions); }
        /// <summary>Executes with the explicit callback context. An optional child-disposed guard stops pending actions without polling Ability.</summary>
        public GameplaySkillActionExecution Execute(GameplayContext context, GameplayEventData triggerEvent, IGameplaySkillHost host, Func<bool> canContinue = null) =>
            GameplaySkillActionRunner.Execute(Actions, context, triggerEvent, host, canContinue);
    }
}
