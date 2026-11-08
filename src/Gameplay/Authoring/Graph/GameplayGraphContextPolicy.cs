using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Standard;

namespace FRFrame.Gameplay.Authoring.Graph
{
    /// <summary>Proven values and optional caller values are different facts. Optional inputs
    /// allow authoring, with a visible contract and a compiled per-consumer runtime gate.</summary>
    public static class GameplayGraphContextPolicy
    {
        [Flags] public enum Requirement { None=0, Actor=1, Point=2, PrimaryActor=4 }
        public readonly struct Inputs
        {
            public readonly bool Actor,Point,OptionalActor,OptionalPoint;
            public readonly bool TriggerActor,TriggerPoint,OptionalTriggerActor,OptionalTriggerPoint;
            public readonly string Note;
            public Inputs(bool actor,bool point,string note,bool optionalActor=false,bool optionalPoint=false)
            {Actor=TriggerActor=actor;Point=TriggerPoint=point;OptionalActor=OptionalTriggerActor=optionalActor;OptionalPoint=OptionalTriggerPoint=optionalPoint;Note=note;}
            private Inputs(bool actor,bool point,string note,bool optionalActor,bool optionalPoint,Inputs trigger)
            {Actor=actor;Point=point;Note=note;OptionalActor=optionalActor;OptionalPoint=optionalPoint;TriggerActor=trigger.TriggerActor;TriggerPoint=trigger.TriggerPoint;OptionalTriggerActor=trigger.OptionalTriggerActor;OptionalTriggerPoint=trigger.OptionalTriggerPoint;}
            public Inputs With(bool actor,bool point,string note,bool optionalActor=false,bool optionalPoint=false)=>new Inputs(actor,point,note,optionalActor,optionalPoint,this);
        }
        private static Inputs Caller()=>new Inputs(false,false,"调用者输入是可选 Actor / Point",true,true);
        private static Inputs Target(GameplaySkillTargetAuthoring target,Inputs input,bool timeline=false)=>
            target is GameplaySkillSelfTargetAuthoring?input.With(true,false,"显式自身"):
            target is GameplaySkillPointTargetAuthoring?input.With(false,true,"显式地点"):
            timeline?input.With(input.TriggerActor,input.TriggerPoint,"时间事件读取原 TriggerEvent.Context",input.OptionalTriggerActor,input.OptionalTriggerPoint):input;
        public static HashSet<string> Reachable(GameplayAuthoringGraph graph)
        {
            var result=new HashSet<string>();var visits=new HashSet<string>();Action<string,bool> visit=null;visit=(id,effectOnly)=>{
                if(string.IsNullOrEmpty(id)||!visits.Add(id+":"+effectOnly))return;result.Add(id);
                var node=graph.Nodes.Find(n=>n.Id==id);var definition=graph.LocalDefinitions.Find(d=>d.Id==id);
                foreach(var b in node?.Payload.Definitions??definition?.Definitions??new List<GameplayDefinitionBinding>())visit(b.LocalId,false);
                if(effectOnly)return;
                var timeline=graph.Timelines.Find(t=>t.Id==id);if(timeline!=null)foreach(var e in timeline.Events)visit(e.EntryNodeId,false);
                foreach(var e in graph.Connections.Where(e=>e.FromNodeId==id))visit(e.ToNodeId,e.InputPortKey==GameplayGraphEffectCallbacks.Input);
            };visit(graph.AbilityNodeId,false);visit(graph.PrimaryRuleNodeId,false);foreach(var id in graph.AdditionalRuleNodeIds)visit(id,false);return result;
        }
        public static Inputs Output(object payload,string port,Inputs input)
        {
            if (payload is GameplayEffectDefinitionData && GameplayGraphEffectCallbacks.IsActionPort(port))
                return new Inputs(true, input.Point, "Effect 生命周期使用该组的实际目标。", optionalPoint: !input.Point);
            if(payload is GameplayEffectDefinitionData&&GameplayGraphEffectCallbacks.IsPort(payload,port))return input.With(true,input.Point,"Effect 续接到同一有效目标",false,input.OptionalPoint);
            if(payload is GameplaySearchTargetActionAuthoring){
                if(port=="EachTarget")return input.With(true,true,"搜索的有效逐目标 Actor/Point");
                if(port=="NoTargets"||port=="Completed"||port=="Then")return input.With(false,true,"搜索完成/无目标不保证 Actor；先以地点重新搜索");
            }
            if(payload is GameplayProjectileDefinitionData)return input.With(port=="OnHit",true,"弹体仅 OnHit 保证 Actor");
            if (payload is GameplayGrantTimedShieldActionAuthoring && port == "OnBroken")
                return input.With(true, true, "护盾耗尽后提供破盾者与地点");
            if (payload is GameplayGrantTimedShieldActionAuthoring && port == "OnExpired")
                return input.With(true, input.Point, "自然到期后提供原护盾目标");
            if(payload is GameplayAreaDefinitionData){
                if(port=="OnEnd")return input;
                return input.With(port=="OnEnter"||port=="OnStay"||port=="OnTick",true,"区域退出不保证 Actor");
            }
            if(payload is GameplayMoveTargetsActionAuthoring&&(port=="OnArrive"||port=="OnCancelled"))return input.With(port=="OnArrive",true,"移动中断不保证 Actor");
            if(payload is GameplaySummonDefinitionData)return input.With(port=="OnSpawn",true,"召唤结束不保证 Actor");
            return input;
        }
        private static object Payload(GameplayAuthoringGraph graph,string id)
        {var node=graph.Nodes.Find(n=>n.Id==id);return (node?.Payload as GameplayActionNodePayload)?.Action??(object)graph.LocalDefinitions.Find(d=>d.Id==id)?.Payload;}
        private static Requirement Required(object value)
        {
            var result=Requirement.None;
            if(value is GameplayApplyToTargetsActionAuthoring||value is GameplaySkillApplyEffectActionAuthoring||value is GameplayMoveTargetsActionAuthoring||value is GameplayDispelEffectsActionAuthoring||value is GameplayCaptureValueActionAuthoring||value is GameplayCaptureCombatRollActionAuthoring||value is GameplayGrantTimedShieldActionAuthoring||value is GameplayRotateToPointActionAuthoring rotate&&!rotate.RotateOwner)result|=Requirement.Actor;
            if(value is GameplaySearchTargetActionAuthoring search){
                if(search.Origin==GameplaySearchOrigin.PrimaryTarget||search.Directional&&search.Direction==GameplaySearchDirection.ToPrimaryTarget)result|=Requirement.PrimaryActor;
                if(search.Origin==GameplaySearchOrigin.ContextPoint||search.Directional&&search.Direction==GameplaySearchDirection.ToContextPoint)result|=Requirement.Point;
            }
            if(value is GameplayAreaDefinitionData area){
                if(area.Origin==GameplaySearchOrigin.PrimaryTarget)result|=Requirement.PrimaryActor;
                if(area.Origin==GameplaySearchOrigin.ContextPoint)result|=Requirement.Point;
            }return result;
        }
        private static Requirement RequiredNode(GameplayAuthoringGraph graph,GameplayAuthoringNode node)
        {
            var result=Required((node?.Payload as GameplayActionNodePayload)?.Action);if(node==null)return result;
            foreach(var binding in node.Payload.Definitions){
                object value=graph.LocalDefinitions.Find(d=>d.Id==binding.LocalId)?.Payload;
                if(value==null&&binding.External is GameplayAreaDefinitionAsset)value=binding.External.CaptureGraphValues();
                result|=Required(value);
            }return result;
        }
        private static Requirement Missing(Requirement required,Inputs input)
        {if(input.Actor)required&=~(Requirement.Actor|Requirement.PrimaryActor);if(input.Point)required&=~Requirement.Point;return required;}
        private static Requirement Optional(Inputs input)=>(input.OptionalActor?Requirement.Actor|Requirement.PrimaryActor:Requirement.None)|(input.OptionalPoint?Requirement.Point:Requirement.None);
        public static string Describe(Requirement required)=>((required&(Requirement.Actor|Requirement.PrimaryActor))!=0?"有效目标 Actor":"")+((required&(Requirement.Actor|Requirement.PrimaryActor))!=0&&(required&Requirement.Point)!=0?" + ":"")+((required&Requirement.Point)!=0?"地点 Point":"");
        private static string Impossible(Requirement required,Inputs input)
        {var missing=Missing(required,input)&~Optional(input);return missing==Requirement.None?null:"此行为需要"+Describe(missing)+"。"+input.Note;}
        private static Dictionary<string,List<Inputs>> Analyze(GameplayAuthoringGraph graph)
        {
            var inputs=new Dictionary<string,List<Inputs>>();var seen=new HashSet<string>();Action<string,Inputs,bool> visit=null;visit=(id,input,effectOnly)=>{
                if(string.IsNullOrEmpty(id)||!seen.Add(id+":"+input.Actor+":"+input.Point+":"+input.OptionalActor+":"+input.OptionalPoint+":"+input.TriggerActor+":"+input.TriggerPoint+":"+input.OptionalTriggerActor+":"+input.OptionalTriggerPoint+":"+effectOnly))return;
                if(!effectOnly){if(!inputs.TryGetValue(id,out var list))inputs[id]=list=new List<Inputs>();list.Add(input);}
                var node=graph.Nodes.Find(n=>n.Id==id);var local=graph.LocalDefinitions.Find(d=>d.Id==id);
                foreach(var b in node?.Payload.Definitions??local?.Definitions??new List<GameplayDefinitionBinding>())visit(b.LocalId,input,false);
                if(effectOnly)return;
                var timeline=graph.Timelines.Find(t=>t.Id==id);if(timeline!=null)foreach(var e in timeline.Events)visit(e.EntryNodeId,Target(e.Target,input,true),false);
                foreach(var edge in graph.Connections.Where(e=>e.FromNodeId==id))visit(edge.ToNodeId,Output(Payload(graph,id),edge.OutputPortKey,input),edge.InputPortKey==GameplayGraphEffectCallbacks.Input);
            };
            foreach(var node in graph.Nodes.Where(n=>n.Payload is GameplayRuleNodePayload))visit(node.Id,Target(((GameplayRuleNodePayload)node.Payload).Target,Caller()),false);
            return inputs;
        }
        /// <summary>Conditional requirements are not compile-time guarantees. The compiler gates
        /// each consumer at its actual execution point, including branch/Timeline context.</summary>
        public static Dictionary<string,Requirement> ConditionalInputs(GameplayAuthoringGraph graph)
        {
            var result=new Dictionary<string,Requirement>();
            foreach(var pair in Analyze(graph)){
                var node=graph.Nodes.Find(n=>n.Id==pair.Key);if(!(node?.Payload is GameplayActionNodePayload))continue;
                var required=RequiredNode(graph,node);var conditional=Requirement.None;
                foreach(var input in pair.Value)conditional|=Missing(required,input)&Optional(input);
                if(conditional!=Requirement.None)result[pair.Key]=conditional;
            }return result;
        }
        private static HashSet<string> LifecycleActions(GameplayAuthoringGraph graph)
        {
            var reached = new HashSet<string>();
            var localEffects = graph.LocalDefinitions.Where(d => d.Payload is GameplayEffectDefinitionData).Select(d => d.Id).ToHashSet();
            var roots = graph.Connections.Where(e => e.InputPortKey == "Execute" && localEffects.Contains(e.FromNodeId) &&
                GameplayGraphEffectCallbacks.IsActionPort(e.OutputPortKey)).ToArray();
            if (roots.Length == 0) return reached;
            var outgoing = graph.Connections.Where(e => e.InputPortKey == "Execute").ToLookup(e => e.FromNodeId);
            void Visit(string id)
            {
                if (!reached.Add(id)) return;
                foreach (var edge in outgoing[id]) Visit(edge.ToNodeId);
            }
            foreach (var edge in roots) Visit(edge.ToNodeId);
            return reached;
        }

        public static string LifecycleActionReason(GameplaySkillActionAuthoring action) => action?.SupportsEffectLifecycle == true ? null :
            "此行为依赖施放授权，不能连接 Effect 生命周期。请选择声光、战斗飘字、条件、非施放托管效果或驱散。";

        private static string Reason(GameplayAuthoringGraph graph,Dictionary<string,List<Inputs>> inputs,HashSet<string> lifecycle,string owner,string port,string target,string inputPort,bool scheduled)
        {
            if(inputPort!="Execute")return null;var node=graph.Nodes.Find(n=>n.Id==target);if(!(node?.Payload is GameplayActionNodePayload action))return null;
            var ownerPayload=Payload(graph,owner);
            if (!scheduled && (lifecycle.Contains(owner) || ownerPayload is GameplayEffectDefinitionData && GameplayGraphEffectCallbacks.IsActionPort(port)))
            {
                string blocked = LifecycleActionReason(action.Action);
                if (blocked != null) return blocked;
            }
            GameplayScheduledEntry entry=null;if(scheduled){var timeline=graph.Timelines.FirstOrDefault(t=>t.Events.Any(e=>e.Id==owner));if(timeline==null)return "时间事件已改变，请重新拖线。";entry=timeline.Events.Single(e=>e.Id==owner);owner=timeline.Id;}
            if(!inputs.TryGetValue(owner,out var paths))paths=new List<Inputs>{Caller()};
            foreach(var input in paths){var actual=entry!=null?Target(entry.Target,input,true):Output(Payload(graph,owner),port,input);string reason=Impossible(RequiredNode(graph,node),actual);if(reason!=null)return reason;}return null;
        }
        public static string ConnectionReason(GameplayAuthoringGraph graph,string owner,string port,string target,string inputPort="Execute")=>Reason(graph,Analyze(graph),LifecycleActions(graph),owner,port,target,inputPort,false);
        public static Func<string,string,string,string,bool,string> ConnectionChecker(GameplayAuthoringGraph graph)
        {var inputs=Analyze(graph);var lifecycle=LifecycleActions(graph);return (owner,port,target,input,scheduled)=>Reason(graph,inputs,lifecycle,owner,port,target,input,scheduled);}
        public static string ScheduledReason(GameplayAuthoringGraph graph,string eventId,string target)=>Reason(graph,Analyze(graph),LifecycleActions(graph),eventId,"Schedule",target,"Execute",true);
        public static void Validate(GameplayAuthoringGraph graph,GameplayAuthoringValidationContext validation,string path)
        {
            var inputs=Analyze(graph);
            var lifecycle=LifecycleActions(graph);
            foreach(var pair in inputs){var node=graph.Nodes.Find(n=>n.Id==pair.Key);var local=graph.LocalDefinitions.Find(d=>d.Id==pair.Key);
                foreach(var input in pair.Value){string reason=Impossible(node!=null?RequiredNode(graph,node):Required(local?.Payload),input);if(reason!=null)validation.Error(path+"/"+pair.Key,reason);}
            }
            foreach(var edge in graph.Connections){string reason=Reason(graph,inputs,lifecycle,edge.FromNodeId,edge.OutputPortKey,edge.ToNodeId,edge.InputPortKey,false);if(reason!=null)validation.Error(path+"/"+edge.Id,reason);}
            foreach(var pair in ConditionalInputs(graph))validation.Warning(path+"/"+pair.Key,"条件输入：需调用者提供"+Describe(pair.Value)+"；缺失时运行拒绝并在 Trace 定位，不保证此行为必然执行。");
        }
    }
}
