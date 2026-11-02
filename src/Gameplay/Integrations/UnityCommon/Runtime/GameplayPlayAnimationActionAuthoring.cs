using System;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Unity
{
    [Serializable,Preserve]
    public sealed class GameplayPlayAnimationActionAuthoring:GameplaySkillActionAuthoring
    {
        public GameplayAnimationAsset Animation;
        public bool Queue;
        public override void Validate(GameplayAuthoringValidationContext validation,string path)=>validation.Asset(Animation,path+".Animation");
        public override void ValidateReferences(GameplayAuthoringValidationContext validation,string path)=>validation.Asset(Animation,path+".Animation");
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context)=>new Action(context.ResolveDefinition(Animation, this, nameof(Animation), () => Animation.Build(context)),Queue);
        private sealed class Action:IGameplaySkillAction
        {
            private readonly GameplayAnimationDefinition _definition;
            private readonly bool _queue;
            internal Action(GameplayAnimationDefinition definition,bool queue){_definition=definition;_queue=queue;}
            public GameplaySkillActionResult Execute(GameplayContext context,GameplayEventData triggerEvent,IGameplaySkillHost host)
            {
                if(!(host is GameplayStandardActor actor))return GameplaySkillActionResult.Rejected;
                if(!(actor.Presentation is GameplayUnityAnimationController controller))
                {
                    actor.RecordSkillTrace(new GameplaySkillTraceRecord(actor.Id,context,triggerEvent.EventTag,
                        GameplaySkillTraceStage.ActionRejected,"AnimationRejected",detail:"PresentationControllerMissing"));
                    return GameplaySkillActionResult.Succeeded;
                }
                // Presentation priority is not a combat gate. Skill eligibility belongs to Ability tags/conditions.
                if(_queue)controller.TryQueue(_definition,context);else controller.TryPlay(_definition,context);return GameplaySkillActionResult.Succeeded;
            }
        }
    }
}
