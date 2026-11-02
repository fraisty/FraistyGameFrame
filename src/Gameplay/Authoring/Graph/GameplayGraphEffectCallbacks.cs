using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace FRFrame.Gameplay.Authoring.Graph
{
    /// <summary>Effect-to-Effect callbacks are typed references to the actual completion/overflow
    /// executors, never SkillAction arrays. Stable callback IDs survive inspector array reorder.
    /// A receiver's Effect input consumes its definition only, not its Execute/Then action chain.</summary>
    public static class GameplayGraphEffectCallbacks
    {
        public const string Completion="AddCompletion",Overflow="AddOverflow",Input="Effect";
        public sealed class Slot {public string Key,Path;public bool IsCompletion;}
        public static IEnumerable<Slot> Slots(object value)
        {
            if(!(value is GameplayEffectDefinitionData effect))yield break;
            for(int i=0;i<effect.CompletionExecutions.Length;i++)if(effect.CompletionExecutions[i] is GameplayApplyEffectOnCompletionAuthoring completion)
                yield return new Slot{Key="Completion/"+completion.AuthoringId,Path="CompletionExecutions.Array.data["+i+"].Effect",IsCompletion=true};
            if(effect.Stacking!=null)for(int i=0;i<effect.Stacking.OverflowExecutions.Length;i++)if(effect.Stacking.OverflowExecutions[i] is GameplayApplyEffectOnOverflowAuthoring overflow)
                yield return new Slot{Key="Overflow/"+overflow.AuthoringId,Path="Stacking.OverflowExecutions.Array.data["+i+"].Effect"};
        }
        public static string[] Ports(object value){if(!(value is GameplayEffectDefinitionData effect))return Array.Empty<string>();var ports=new List<string>();if(effect.DurationPolicy==GameplayEffectDurationPolicy.Duration)ports.Add(Completion);if(effect.Stacking?.Overflow==GameplayEffectStackOverflowPolicy.ExecuteOverflow)ports.Add(Overflow);return ports.Concat(Slots(value).Select(s=>s.Key)).ToArray();}
        public static bool IsPort(object value,string key)=>Ports(value).Contains(key);
        public static GameplayDefinitionBinding EffectBinding(GameplayAuthoringNode node)=>
            (node?.Payload as GameplayActionNodePayload)?.Action is GameplayApplyToTargetsActionAuthoring||(node?.Payload as GameplayActionNodePayload)?.Action is GameplaySkillApplyEffectActionAuthoring?
                node.Payload.Definitions.SingleOrDefault(b=>b.PropertyPath=="Effect"):null;
        public static string Add(GameplayEffectDefinitionData effect,bool completion)
        {
            if(completion){var item=new GameplayApplyEffectOnCompletionAuthoring();effect.CompletionExecutions=effect.CompletionExecutions.Concat(new[]{item}).ToArray();return "Completion/"+item.AuthoringId;}
            if(effect.Stacking==null)throw new InvalidOperationException("请先配置叠层及 ExecuteOverflow。");
            var overflow=new GameplayApplyEffectOnOverflowAuthoring();effect.Stacking.OverflowExecutions=effect.Stacking.OverflowExecutions.Concat(new[]{overflow}).ToArray();return "Overflow/"+overflow.AuthoringId;
        }
        public static void Remove(GameplayEffectDefinitionData effect,string key)
        {
            if(key.StartsWith("Completion/",StringComparison.Ordinal))effect.CompletionExecutions=effect.CompletionExecutions.Where(e=>"Completion/"+e.AuthoringId!=key).ToArray();
            else if(effect.Stacking!=null){effect.Stacking.OverflowExecutions=effect.Stacking.OverflowExecutions.Where(e=>"Overflow/"+e.AuthoringId!=key).ToArray();if(effect.Stacking.OverflowExecutions.Length==0)effect.Stacking.Overflow=GameplayEffectStackOverflowPolicy.Reject;}
        }
        /// <summary>Lossless format upgrade: replace old callback configuration bindings with
        /// explicit typed edges. No executor, ownership flag, effect value or shared identity changes.</summary>
        public static bool ExposeBindings(GameplayAuthoringGraph graph)
        {
            bool changed=false;
            foreach(var local in graph.LocalDefinitions.ToArray())foreach(var slot in Slots(local.Payload).ToArray()){
                var binding=local.Definitions.SingleOrDefault(b=>b.PropertyPath==slot.Path);if(binding==null)continue;
                var target=graph.Nodes.FirstOrDefault(n=>{var b=EffectBinding(n);return b!=null&&b.LocalId==binding.LocalId&&b.External==binding.External;});
                if(target==null){var source=graph.Nodes.FirstOrDefault(n=>n.Payload.Definitions.Any(b=>b.LocalId==local.Id));
                    var resource=graph.LocalDefinitions.Find(d=>d.Id==binding.LocalId);
                    target=new GameplayAuthoringNode{Title=resource?.Name??binding.External?.name??"后续效果",Position=(source?.Position??Vector2.zero)+new Vector2(350,180),
                        SourceDefinitionId=resource?.SourceDefinitionId,Payload=new GameplayActionNodePayload{Action=new GameplayApplyToTargetsActionAuthoring()}};
                    target.Payload.Definitions.Add(new GameplayDefinitionBinding{PropertyPath="Effect",LocalId=binding.LocalId,External=binding.External});graph.Nodes.Add(target);
                }
                local.Definitions.Remove(binding);graph.Connections.Add(new GameplayAuthoringConnection{FromNodeId=local.Id,OutputPortKey=slot.Key,ToNodeId=target.Id,InputPortKey=Input});changed=true;
            }return changed;
        }
    }
}
