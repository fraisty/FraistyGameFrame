namespace FRFrame.Gameplay
{
    /// <summary>Ordered synchronous continuation, called only after a Duration effect naturally expires and is cleaned up.</summary>
    public interface IGameplayEffectCompletionExecution
    {
        void Execute(GameplayEffectCompletionContext completion);
    }

    /// <summary>
    /// Immutable completed-instance view. Apply preserves the original application identity and target,
    /// but creates a new instance and period phase. New entries start advancing on the next host Advance.
    /// </summary>
    public sealed class GameplayEffectCompletionContext
    {
        private readonly GameplayEffectContainer _container;
        private bool _dispatching = true;
        public GameplayEffectDefinition Definition { get; }
        public GameplayEffectHandle Handle { get; }
        public GameplaySourceId SourceId { get; }
        public GameplayEffectApplicationContext Application { get; }

        internal GameplayEffectCompletionContext(GameplayEffectContainer container,
            GameplayEffectDefinition definition, GameplayEffectHandle handle, GameplaySourceId sourceId,
            GameplayEffectApplicationContext application)
        {
            _container = container;
            Definition = definition;
            Handle = handle;
            SourceId = sourceId;
            Application = application;
        }

        /// <summary>Applies to the same target using the completed application's Context, Instigator and SetByCaller snapshot.</summary>
        public GameplayEffectApplicationResult Apply(GameplayEffectDefinition next) => _container.Apply(next, Application);

        internal void Close() => _dispatching = false;
        internal void Run(GameplayEffectLifecycleActions actions)
        {
            if (_dispatching && Application != null)
                actions.Run(_container, GameplayEffectLifecycleKind.NaturalExpiry, Handle, SourceId,
                    Application, Application.StackCount, Definition.Stacking?.MaxStacks ?? 1, GameplayEffectRemovalCause.NaturalExpiry);
        }
    }
}
