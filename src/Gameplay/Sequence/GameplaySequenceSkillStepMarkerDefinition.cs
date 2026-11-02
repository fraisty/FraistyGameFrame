using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;

namespace FRFrame.Gameplay
{
    /// <summary>
    /// Resolves a Skill step at marker time using the Sequence host and original trigger event.
    /// Conditions and target read current host state; actions receive a new target snapshot with
    /// the activation identity and values. Rejection stops only this step, without rollback.
    /// </summary>
    public sealed class GameplaySequenceSkillStepMarkerDefinition : GameplaySequenceMarkerDefinition
    {
        public IReadOnlyList<IGameplaySkillCondition> Conditions { get; }
        public IGameplaySkillTarget Target { get; }
        public IReadOnlyList<IGameplaySkillAction> Actions { get; }

        public GameplaySequenceSkillStepMarkerDefinition(float time,
            IEnumerable<IGameplaySkillCondition> conditions, IGameplaySkillTarget target,
            IEnumerable<IGameplaySkillAction> actions) : base(time)
        {
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
        }
    }
}
