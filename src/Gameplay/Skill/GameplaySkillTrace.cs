using System;

namespace FRFrame.Gameplay
{
    public enum GameplaySkillTraceStage { Triggered, ConditionRejected, TargetRejected, ContextRejected, ActionStarted, ActionSucceeded, ActionRejected, ActionFailed }
    /// <summary>Optional authored location, with no Unity reference. The asset identity and
    /// managed-reference ID describe the original Source, never a second executable graph.</summary>
    public readonly struct GameplaySkillTraceSource
    {
        public string DefinitionId {get;}
        public long NodeId {get;}
        public string DocumentId {get;}
        public bool IsValid=>!string.IsNullOrEmpty(DefinitionId)&&NodeId>=0;
        public GameplaySkillTraceSource(string definitionId,long nodeId,string documentId=null){DefinitionId=definitionId;NodeId=nodeId;DocumentId=documentId;}
    }
    public interface IGameplaySkillTraceSourceProvider
    {bool TryGetActionSource(IGameplaySkillAction action,GameplayContext context,out GameplaySkillTraceSource source);}
    /// <summary>Detached observation of an existing synchronous rule/action. Context is immutable;
    /// ordering is supplied by the observer, with no new rule clock or execution authority.</summary>
    public readonly struct GameplaySkillTraceRecord
    {
        public GameplaySourceId OwnerId {get;}
        public GameplayContext Context {get;}
        public GameplayTag EventTag {get;}
        public GameplaySkillTraceStage Stage {get;}
        public string Operation {get;}
        public int ActionIndex {get;}
        public string Detail {get;}
        public GameplaySkillTraceSource Source {get;}
        public GameplaySkillTraceRecord(GameplaySourceId owner,GameplayContext context,GameplayTag eventTag,GameplaySkillTraceStage stage,string operation,int actionIndex=-1,string detail=null,GameplaySkillTraceSource source=default)
        {OwnerId=owner;Context=context;EventTag=eventTag;Stage=stage;Operation=operation;ActionIndex=actionIndex;Detail=detail;Source=source;}
    }
    /// <summary>Optional diagnostics only. Observer exceptions are isolated from gameplay;
    /// observers must never mutate/advance rules while recording a trace.</summary>
    public interface IGameplaySkillTraceSink {void RecordSkillTrace(GameplaySkillTraceRecord record);}
    internal static class GameplaySkillTrace
    {
        internal static void Write(IGameplaySkillHost host,GameplayContext context,GameplayEventData trigger,GameplaySkillTraceStage stage,string operation,int index=-1,string detail=null,IGameplaySkillAction action=null)
        {
            if(!(host is IGameplaySkillTraceSink sink))return;
            try{GameplaySkillTraceSource source=default;if(action!=null&&host is IGameplaySkillTraceSourceProvider provider)provider.TryGetActionSource(action,context,out source);
                sink.RecordSkillTrace(new GameplaySkillTraceRecord(host.OwnerId,context,trigger.EventTag,stage,operation,index,detail,source));}
            catch{ /* Diagnostics cannot change payment, ordering, cleanup or rule exceptions. */ }
        }
    }
}
