using System;

namespace FRFrame.Gameplay.Extensions
{
    /// <summary>Existing container operations; Begin/Commit separate preparation from irreversible activation.</summary>
    public enum GameplayAbilityCommandKind { Activate, Begin, Commit, Cancel, End }
    /// <summary>Controller request snapshot, not an activation decision, queue, retry or cooldown authority.</summary>
    public readonly struct GameplayAbilityCommand
    {
        public GameplayAbilityCommandKind Kind { get; }
        public GameplayAbilityHandle Handle { get; }
        public GameplayContext Context { get; }
        public GameplayAbilityCommand(GameplayAbilityCommandKind kind, GameplayAbilityHandle handle, GameplayContext context = null)
        {
            if (!Enum.IsDefined(typeof(GameplayAbilityCommandKind), kind)) throw new ArgumentOutOfRangeException(nameof(kind));
            Kind = kind; Handle = handle; Context = context;
        }
    }
    /// <summary>Synchronous forwarding only. The container rechecks all rules; Commit uses its stored preparing context.</summary>
    public static class GameplayAbilityCommandExecutor
    {
        public static bool Execute(GameplayAbilityContainer container, GameplayAbilityCommand command)
        {
            if (container == null) throw new ArgumentNullException(nameof(container));
            switch (command.Kind)
            {
                case GameplayAbilityCommandKind.Activate: return container.TryActivate(command.Handle, command.Context);
                case GameplayAbilityCommandKind.Begin: return container.TryBeginActivation(command.Handle, command.Context);
                case GameplayAbilityCommandKind.Commit: return container.TryCommitActivation(command.Handle);
                case GameplayAbilityCommandKind.Cancel: return container.Cancel(command.Handle);
                case GameplayAbilityCommandKind.End: return container.End(command.Handle);
                default: throw new ArgumentOutOfRangeException(nameof(command));
            }
        }
    }
}
