using System;
using FRFrame.Gameplay.Standard;
using UnityEngine;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Separate timed consumable shield. Duration uses the existing target Effect clock.</summary>
    [Serializable,Preserve]
    public sealed class GameplayGrantTimedShieldActionAuthoring : GameplaySkillActionAuthoring
    {
        [SerializeReference] public GameplayFloatValueAuthoring Amount=new GameplayConstantFloatValueAuthoring{Value=100};
        [SerializeReference] public GameplayFloatValueAuthoring Duration=new GameplayConstantFloatValueAuthoring{Value=5};
        public bool OwnedByAbility;
        [SerializeReference] public GameplayEffectPresentationAuthoring Presentation;
        [SerializeReference] public GameplaySkillActionAuthoring[] OnAbsorbed = Array.Empty<GameplaySkillActionAuthoring>();
        [SerializeReference] public GameplaySkillActionAuthoring[] OnBroken = Array.Empty<GameplaySkillActionAuthoring>();
        [SerializeReference] public GameplaySkillActionAuthoring[] OnExpired = Array.Empty<GameplaySkillActionAuthoring>();
        /// <summary>Runs with the original target after break or expiry actions, or on direct removal.
        /// An ability-owned shield skips it if the owning activation has already ended.</summary>
        [SerializeReference] public GameplaySkillActionAuthoring[] OnRemoved = Array.Empty<GameplaySkillActionAuthoring>();
        public override void Validate(GameplayAuthoringValidationContext validation,string path)
        {
            if(Amount==null)validation.Error(path+".Amount","Shield amount required.");else Amount.Validate(validation,path+".Amount");
            if(Duration==null)validation.Error(path+".Duration","Shield duration required.");else Duration.Validate(validation,path+".Duration");
            GameplaySpatialAuthoringUtility.Validate(OnBroken, validation, path + ".OnBroken");
            GameplaySpatialAuthoringUtility.Validate(OnExpired, validation, path + ".OnExpired");
            GameplaySpatialAuthoringUtility.Validate(OnRemoved, validation, path + ".OnRemoved");
            GameplaySpatialAuthoringUtility.Validate(OnAbsorbed, validation, path + ".OnAbsorbed");
            Presentation?.Validate(validation, path + ".Presentation");
        }
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context)=>new GameplayGrantTimedShieldAction(
            Amount.Build(), Duration.Build(), OwnedByAbility, GameplaySpatialAuthoringUtility.Build(OnBroken, context),
            GameplaySpatialAuthoringUtility.Build(OnExpired, context), Presentation?.Build(),
            GameplaySpatialAuthoringUtility.Build(OnAbsorbed, context),
            GameplaySpatialAuthoringUtility.Build(OnRemoved, context));
    }
}
