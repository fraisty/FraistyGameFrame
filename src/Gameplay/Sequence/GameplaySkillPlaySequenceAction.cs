using System;

namespace FRFrame.Gameplay
{
    /// <summary>Optional Skill host capability for scheduling marker timing on its owner.</summary>
    public interface IGameplaySequenceHost
    {
        GameplaySequenceContainer Sequences { get; }
    }

    /// <summary>A Skill action that starts a Sequence with the activation Context and actual trigger event.</summary>
    public sealed class GameplaySkillPlaySequenceAction : IGameplaySkillAction
    {
        public GameplaySequenceDefinition Sequence { get; }
        public GameplaySkillPlaySequenceAction(GameplaySequenceDefinition sequence) =>
            Sequence = sequence ?? throw new ArgumentNullException(nameof(sequence));

        public GameplaySkillActionResult Execute(GameplayContext context, GameplayEventData triggerEvent,
            IGameplaySkillHost host)
        {
            if (context == null) throw new ArgumentNullException(nameof(context));
            if (!(host is IGameplaySequenceHost sequenceHost))
                throw new InvalidOperationException("PlaySequence requires IGameplaySequenceHost.");
            sequenceHost.Sequences.StartSkill(Sequence, context, triggerEvent, host);
            return GameplaySkillActionResult.Succeeded;
        }
    }
}
