using System;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Unity
{
    [Serializable,Preserve]
    public sealed class GameplayPlayPresentationCueActionAuthoring:GameplaySkillActionAuthoring
    {
        public GameplayPresentationCueAsset Cue;
        public override void Validate(GameplayAuthoringValidationContext validation,string path)=>validation.Asset(Cue,path+".Cue");
        public override void ValidateReferences(GameplayAuthoringValidationContext validation,string path)=>validation.Asset(Cue,path+".Cue");
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context)=>new Action(context.ResolveDefinition(Cue, this, nameof(Cue), () => Cue.Build(context)));
        private sealed class Action:IGameplaySkillAction
        {
            private readonly GameplayPresentationCueDefinition _definition;
            internal Action(GameplayPresentationCueDefinition definition)=>_definition=definition;
            public GameplaySkillActionResult Execute(GameplayContext context,GameplayEventData triggerEvent,IGameplaySkillHost host)=>
                host is GameplayStandardActor actor&&actor.Presentation is GameplayUnityAnimationController controller&&controller.TryPlayCue(_definition,context)
                    ?GameplaySkillActionResult.Succeeded:GameplaySkillActionResult.Rejected;
        }
    }
}
