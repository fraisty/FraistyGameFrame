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
                if (!(host is GameplayStandardActor actor)) return GameplaySkillActionResult.Rejected;
                if (!MeetsPresentationConditions(actor,context)) return GameplaySkillActionResult.Succeeded;
                if (triggerEvent.EffectLifecycle is { } lifecycle)
                {
                    if (!lifecycle.IsExecuting) return GameplaySkillActionResult.Rejected;
                    if (!(actor.Presentation is GameplayUnityAnimationController presenter) || !presenter.TryQueueEffectCue(_definition, context, lifecycle))
                        actor.RecordSkillTrace(new GameplaySkillTraceRecord(actor.Id, context, default,
                            GameplaySkillTraceStage.ActionRejected, "EffectCue", detail: "目标缺少可用 Unity Cue 播放器，或 Cue 位置无法解析。"));
                    // Optional playback never prevents the next committed gameplay step.
                    return GameplaySkillActionResult.Succeeded;
                }
                var controller = actor.Presentation as GameplayUnityAnimationController;
                GameplayPoint? segment = null;
                bool callback = !_definition.OwnedByAbility && actor.TryReadPresentationCallback(context, out segment);
                string operation = callback ? "CallbackCue" : "PresentationCue";
                string failure;
                try
                {
                    if (controller == null) failure = "缺少可用 Unity Cue 播放器。";
                    else if (callback ? controller.TryPlayCallbackCue(_definition, context, segment) : controller.TryPlayCue(_definition, context))
                        return GameplaySkillActionResult.Succeeded;
                    else failure = "Cue 播放未完成，请检查位置、授权或播放服务。";
                }
                catch (Exception error)
                {
                    // This boundary applies only to playback of an already-built Cue.
                    failure = error.GetType().Name + ": " + error.Message;
                }
                if (actor.SkillTrace.Enabled)
                {
                    actor.TryGetActionSource(this, context, out var source);
                    actor.RecordSkillTrace(new GameplaySkillTraceRecord(actor.Id, context, triggerEvent.EventTag,
                        GameplaySkillTraceStage.ActionRejected, operation, detail: failure, source: source));
                }
                return GameplaySkillActionResult.Succeeded;
            }

            private bool MeetsPresentationConditions(GameplayStandardActor actor, GameplayContext context)
            {
                if (_definition.Outcome == GameplayPresentationCueAsset.OutcomeRequirement.Any && _definition.TargetRequirements.IsEmpty)
                    return true;
                if (!actor.World.TryGetActor(context.TargetData.PrimaryTargetId,out var target)) return false;
                if (!_definition.TargetRequirements.IsEmpty &&
                    (!target.IsTargetable || !_definition.TargetRequirements.Matches(target.Tags))) return false;
                if (_definition.Outcome == GameplayPresentationCueAsset.OutcomeRequirement.Any) return true;
                if (!(target.LastCombatOutcome is { } outcome) || outcome.Kind != GameplayCombatOutcomeKind.Damage ||
                    (!ReferenceEquals(outcome.ExecutionContext,context) && !ReferenceEquals(outcome.Context,context))) return false;
                return _definition.Outcome == GameplayPresentationCueAsset.OutcomeRequirement.HealthDamage
                    ? outcome.Applied > 0 : !outcome.Damage.IsImmune && !outcome.Damage.IsMiss;
            }
        }
    }
}
