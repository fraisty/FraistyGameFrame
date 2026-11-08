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
        public override bool SupportsEffectLifecycle => true;
        public override void Validate(GameplayAuthoringValidationContext validation,string path)=>validation.Asset(Cue,path+".Cue");
        public override void ValidateReferences(GameplayAuthoringValidationContext validation,string path)=>validation.Asset(Cue,path+".Cue");
        public override IGameplaySkillAction Build(GameplayAuthoringBuildContext context)=>new Action(context.ResolveDefinition(Cue, this, nameof(Cue), () => Cue.Build(context)));
        private sealed class Action:IGameplaySkillAction
        {
            private readonly GameplayPresentationCueDefinition _definition;
            internal Action(GameplayPresentationCueDefinition definition)=>_definition=definition;
            public GameplaySkillActionResult Execute(GameplayContext context,GameplayEventData triggerEvent,IGameplaySkillHost host)
            {
                if (triggerEvent.EffectLifecycle is { } lifecycle)
                {
                    if (!lifecycle.IsExecuting) return GameplaySkillActionResult.Rejected;
                    if (host is GameplayStandardActor target &&
                        (!(target.Presentation is GameplayUnityAnimationController presenter) || !presenter.TryQueueEffectCue(_definition, context, lifecycle)))
                        target.RecordSkillTrace(new GameplaySkillTraceRecord(target.Id, context, default,
                            GameplaySkillTraceStage.ActionRejected, "EffectCue", detail: "目标缺少可用 Unity Cue 播放器，或 Cue 位置无法解析。"));
                    // Optional playback never prevents the next committed gameplay step.
                    return GameplaySkillActionResult.Succeeded;
                }
                return host is GameplayStandardActor actor && actor.Presentation is GameplayUnityAnimationController controller &&
                    controller.TryPlayCue(_definition,context) ? GameplaySkillActionResult.Succeeded : GameplaySkillActionResult.Rejected;
            }
        }
    }
}
