using System;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Natural-expiry reapplication owned by an active grant. Zero skill ID requires the
    /// original activation; an explicit ID resolves that installed skill's current activation.
    /// The completed application still preserves its source/value snapshot. An inactive skill
    /// never receives a Buff; a passive re-engaged during suppression owns its resumed Buff.</summary>
    public sealed class GameplayApplyOwnedEffectOnCompletionExecution:IGameplayEffectCompletionExecution
    {
        private readonly GameplayEffectDefinition _effect;
        private readonly int _ownerSkillId;
        public GameplayApplyOwnedEffectOnCompletionExecution(GameplayEffectDefinition effect,int ownerSkillId=0)
        {if(ownerSkillId<0)throw new ArgumentOutOfRangeException(nameof(ownerSkillId));_effect=effect??throw new ArgumentNullException(nameof(effect));_ownerSkillId=ownerSkillId;}
        public void Execute(GameplayEffectCompletionContext completion)
        {
            if(!(completion.Application.Target is GameplayStandardActor actor))return;
            GameplayAbilityHandle grant=default;
            if(_ownerSkillId>0){foreach(var skill in actor.InstalledSkills)if(!skill.IsDisposed&&skill.Definition.SkillId==_ownerSkillId&&actor.Abilities.IsActive(skill.Grant)){grant=skill.Grant;break;}if(!grant.IsValid)return;}
            else if(!GameplayStandardActivation.TryFindGrant(actor,completion.Application.Context,out grant))return;
            var result=completion.Apply(_effect);
            if(result.Outcome==GameplayEffectApplicationOutcome.NewActiveEffect&&!actor.Abilities.TryOwnEffect(grant,result)){
                actor.Effects.Remove(result.ActiveHandle);throw new InvalidOperationException("Completion effect ownership was rejected.");
            }
        }
    }
}
