using System;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Explicit presentation action after a committed hit on the same target/source/root.
    /// Leave automatic FloatingText null when using this action to avoid showing a hit twice.</summary>
    [Serializable,Preserve]
    public sealed class GameplayShowCombatTextActionAuthoring:GameplaySkillActionAuthoring
    {
        public GameplayFloatingTextAsset Source;
        public override void Validate(GameplayAuthoringValidationContext validation,string path)=>validation.Asset(Source,path+".Source");
        public override void ValidateReferences(GameplayAuthoringValidationContext validation,string path)=>validation.Asset(Source,path+".Source");
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context)=>new Action(context.ResolveDefinition(Source, this, nameof(Source), () => Source.Build(context)));
        private sealed class Action:IGameplaySkillAction
        {
            private readonly GameplayFloatingTextDefinition _definition;
            internal Action(GameplayFloatingTextDefinition definition)=>_definition=definition;
            public GameplaySkillActionResult Execute(GameplayContext context,GameplayEventData triggerEvent,IGameplaySkillHost host)
            {
                if(!(host is GameplayStandardActor actor)||!actor.World.TryGetActor(context.TargetData.PrimaryTargetId,out var target)||
                    !(target.Presentation is GameplayUnityAnimationController controller)||!target.LastCombatOutcome.HasValue)return GameplaySkillActionResult.Rejected;
                var outcome=target.LastCombatOutcome.Value;
                if(outcome.Context.EffectiveSourceId!=context.EffectiveSourceId||outcome.Context.OriginSourceId!=context.OriginSourceId)return GameplaySkillActionResult.Rejected;
                return controller.ShowFloatingText(_definition,outcome)?GameplaySkillActionResult.Succeeded:GameplaySkillActionResult.Rejected;
            }
        }
    }
}
