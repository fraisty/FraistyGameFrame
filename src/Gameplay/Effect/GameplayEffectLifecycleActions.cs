using System.Collections.Generic;

namespace FRFrame.Gameplay
{
    public enum GameplayEffectLifecycleKind { Applied, StackChanged, EnterMax, Period, Overflow, NaturalExpiry }

    /// <summary>Evidence of a committed Effect event. Only the container can issue it; the
    /// synchronous authorization expires when its action list returns.</summary>
    public sealed class GameplayEffectLifecycleEvent
    {
        private readonly GameplayEffectContainer _container;
        public GameplayEffectLifecycleKind Kind { get; }
        public GameplayEffectHandle Handle { get; }
        public GameplaySourceId EffectSourceId { get; }
        public GameplayEffectApplicationContext Application { get; }
        public int PreviousStacks { get; }
        public int StackCount => Application.StackCount;
        public int MaxStacks { get; }
        public GameplayEffectRemovalCause? Cause { get; }
        public bool IsExecuting { get; private set; } = true;

        internal GameplayEffectLifecycleEvent(GameplayEffectContainer container, GameplayEffectLifecycleKind kind,
            GameplayEffectHandle handle, GameplaySourceId effectSourceId, GameplayEffectApplicationContext application,
            int previousStacks, int maxStacks, GameplayEffectRemovalCause? cause)
        {
            _container = container; Kind = kind; Handle = handle; EffectSourceId = effectSourceId;
            Application = application; PreviousStacks = previousStacks; MaxStacks = maxStacks; Cause = cause;
        }

        internal void Close() => IsExecuting = false;

        /// <summary>Applies a continuation to the same target with the committed source and value snapshot.</summary>
        public GameplayEffectApplicationResult ApplyToTarget(GameplayEffectDefinition definition) =>
            IsExecuting ? _container.Apply(definition, Application) : default;
    }

    /// <summary>The same ordinary Skill actions run in each of the Effect's synchronous lifecycle slots.</summary>
    public sealed class GameplayEffectLifecycleActions : IGameplayEffectOverflowExecution, IGameplayEffectCompletionExecution
    {
        private readonly IReadOnlyList<IGameplaySkillAction> _actions;
        public GameplayEffectLifecycleActions(IEnumerable<IGameplaySkillAction> actions) =>
            _actions = GameplaySkillBranchAction.Copy(actions);

        void IGameplayEffectOverflowExecution.Execute(GameplayEffectOverflowContext context) => context.Run(this);
        void IGameplayEffectCompletionExecution.Execute(GameplayEffectCompletionContext context) => context.Run(this);

        internal void Run(GameplayEffectContainer container, GameplayEffectLifecycleKind kind, GameplayEffectHandle handle,
            GameplaySourceId source, GameplayEffectApplicationContext application, int previousStacks, int maxStacks,
            GameplayEffectRemovalCause? cause = null)
        {
            // The target hosts the continuation even when the original caster has left. The
            // immutable Application still carries the caster's identity, attributes and values.
            if (_actions.Count == 0 || !(application?.Target is IGameplaySkillHost host)) return;
            var lifecycle = new GameplayEffectLifecycleEvent(container, kind, handle, source, application, previousStacks, maxStacks, cause);
            try { GameplaySkillActionRunner.Execute(_actions, application.Context, new GameplayEventData(lifecycle), host); }
            finally { lifecycle.Close(); }
        }
    }
}
