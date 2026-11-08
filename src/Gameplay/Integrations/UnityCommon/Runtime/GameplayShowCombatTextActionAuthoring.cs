using System;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;
using UnityEngine.Scripting;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Overrides automatic feedback for a pending result on the same target and source.
    /// Connect after the combat action in its execution chain, before the next presentation step.</summary>
    [Serializable,Preserve]
    public sealed class GameplayShowCombatTextActionAuthoring:GameplaySkillActionAuthoring
    {
        public GameplayFloatingTextAsset Source;
        public override bool SupportsEffectLifecycle => true;
        public override void Validate(GameplayAuthoringValidationContext validation,string path)=>validation.Asset(Source,path+".Source");
        public override void ValidateReferences(GameplayAuthoringValidationContext validation,string path)=>validation.Asset(Source,path+".Source");
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context)=>new Action(context.ResolveDefinition(Source, this, nameof(Source), () => Source.Build(context)));
        private sealed class Action:IGameplaySkillAction
        {
            private readonly GameplayFloatingTextDefinition _definition;
            internal Action(GameplayFloatingTextDefinition definition)=>_definition=definition;
            public GameplaySkillActionResult Execute(GameplayContext context,GameplayEventData triggerEvent,IGameplaySkillHost host)
            {
                if (triggerEvent.EffectLifecycle is { } lifecycle)
                {
                    if (!lifecycle.IsExecuting) return GameplaySkillActionResult.Rejected;
                    if (host is GameplayStandardActor targetActor)
                    {
                        if (targetActor.Presentation is GameplayUnityAnimationController presenter)
                        {
                            try { if (presenter.TryShowAuthoredCombatText(_definition, context)) return GameplaySkillActionResult.Succeeded; }
                            catch (Exception error) { UnityEngine.Debug.LogException(error, presenter.Subject); }
                        }
                        targetActor.RecordSkillTrace(new GameplaySkillTraceRecord(targetActor.Id, context, default,
                            GameplaySkillTraceStage.ActionRejected, "EffectCombatText", detail: "目标缺少播放后端或本次生命周期没有可领取的战斗结果。"));
                    }
                    return GameplaySkillActionResult.Succeeded;
                }
                if(!(host is GameplayStandardActor actor)||!actor.World.TryGetActor(context.TargetData.PrimaryTargetId,out var target)||
                    !(target.Presentation is GameplayUnityAnimationController controller))return GameplaySkillActionResult.Rejected;
                return controller.TryShowAuthoredCombatText(_definition,context)?GameplaySkillActionResult.Succeeded:GameplaySkillActionResult.Rejected;
            }
        }
    }
}
