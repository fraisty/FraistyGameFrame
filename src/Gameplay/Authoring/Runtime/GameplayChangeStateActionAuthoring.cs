using System;
using FRFrame.Gameplay.Standard;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Host-registered state alias; no reflection registration or second FSM is created.</summary>
    [Serializable,Preserve]
    public sealed class GameplayChangeStateActionAuthoring : GameplaySkillActionAuthoring
    {
        public string State="Idle";
        public override void Validate(GameplayAuthoringValidationContext validation,string path)
        { if(string.IsNullOrWhiteSpace(State))validation.Error(path+".State","State alias is required."); }
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context)=>new GameplayChangeStateAction(State);
    }
}
