using System;
using System.Linq;
using FRFrame.Gameplay.Standard;

namespace FRFrame.Gameplay.Authoring.Graph
{
    /// <summary>Compiler-only gate for optional caller input. It runs immediately before the
    /// actual consumer, so an unchosen branch is never rejected and sequence ownership guards,
    /// context outputs and source tracing retain their existing runtime behavior.</summary>
    internal sealed class GameplayGraphInputGuard : IGameplaySkillAction, IGameplaySkillContextAction, IGameplaySkillGuardedContextAction
    {
        private readonly IGameplaySkillAction _action;
        private readonly GameplayGraphContextPolicy.Requirement _required;
        private readonly GameplaySkillTraceSource _source;
        internal GameplayGraphInputGuard(IGameplaySkillAction action,GameplayGraphContextPolicy.Requirement required,GameplaySkillTraceSource source)
        {_action=action;_required=required;_source=source;}
        public GameplaySkillActionResult Execute(GameplayContext context,GameplayEventData trigger,IGameplaySkillHost host)=>ExecuteGuarded(context,trigger,host,null).Result;
        public GameplaySkillActionExecution ExecuteWithContext(GameplayContext context,GameplayEventData trigger,IGameplaySkillHost host)=>ExecuteGuarded(context,trigger,host,null);
        public GameplaySkillActionExecution ExecuteGuarded(GameplayContext context,GameplayEventData trigger,IGameplaySkillHost host,Func<bool> canContinue)
        {
            if(canContinue!=null&&!canContinue())return new GameplaySkillActionExecution(GameplaySkillActionResult.Rejected,context);
            var data=context.TargetData;var missing=GameplayGraphContextPolicy.Requirement.None;
            Func<GameplaySourceId,bool> live=id=>id.IsValid&&(!(host is GameplayStandardActor actor)||actor.World.TryGetActor(id,out _));
            if((_required&GameplayGraphContextPolicy.Requirement.Actor)!=0&&!live(data.PrimaryTargetId)&&!data.Targets.Any(live))missing|=GameplayGraphContextPolicy.Requirement.Actor;
            if((_required&GameplayGraphContextPolicy.Requirement.PrimaryActor)!=0&&!live(data.PrimaryTargetId))missing|=GameplayGraphContextPolicy.Requirement.PrimaryActor;
            if((_required&GameplayGraphContextPolicy.Requirement.Point)!=0&&!data.HasPoint)missing|=GameplayGraphContextPolicy.Requirement.Point;
            if(missing!=GameplayGraphContextPolicy.Requirement.None){
                if(host is IGameplaySkillTraceSink sink)try{sink.RecordSkillTrace(new GameplaySkillTraceRecord(host.OwnerId,context,trigger.EventTag,GameplaySkillTraceStage.ContextRejected,"调用者输入",detail:"缺少"+GameplayGraphContextPolicy.Describe(missing),source:_source));}catch{ /* Observation cannot alter execution. */ }
                return new GameplaySkillActionExecution(GameplaySkillActionResult.Rejected,context);
            }
            if(_action is IGameplaySkillGuardedContextAction guarded)return guarded.ExecuteGuarded(context,trigger,host,canContinue);
            if(_action is IGameplaySkillContextAction contextual)return contextual.ExecuteWithContext(context,trigger,host);
            return new GameplaySkillActionExecution(_action.Execute(context,trigger,host),context);
        }
    }
}
