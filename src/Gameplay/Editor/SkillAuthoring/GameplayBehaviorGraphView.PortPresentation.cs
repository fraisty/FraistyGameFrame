using System.Linq;
using System.Collections.Generic;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEditor.Experimental.GraphView;
using Node=UnityEditor.Experimental.GraphView.Node;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    public sealed partial class GameplayBehaviorGraphView
    {
        private static readonly Color CallbackColor=new Color(.91f,.66f,.29f);
        private static void StylePortBackground(Node card)
        {
            // GraphView gives the two sides different fills. Paint the full header once.
            card.outputContainer.parent.style.backgroundColor=GameplayEditorStyles.Surface;
            card.inputContainer.style.backgroundColor=Color.clear;
            card.outputContainer.style.backgroundColor=Color.clear;
        }

        private void StylePorts(Node card,bool alignSides)
        {
            StylePortBackground(card);
            // Give each port its own row. Keep the two groups compact and independently
            // measured so a single input does not reserve half of a multi-output card.
            var ports=card.inputContainer.parent;
            ports.style.flexDirection=alignSides?FlexDirection.Row:FlexDirection.Column;
            if(alignSides)ports.style.justifyContent=Justify.SpaceBetween;
            foreach(var side in new[]{card.inputContainer,card.outputContainer}){
                side.style.flexGrow=0;side.style.flexBasis=StyleKeyword.Auto;
                side.style.alignSelf=Align.Stretch;side.style.paddingTop=0;side.style.paddingBottom=0;
                side.style.flexDirection=FlexDirection.Column;
            }
            var cardPorts=card.Query<Port>().ToList();_portsByCard[card.viewDataKey]=cardPorts;
            foreach(var port in cardPorts){
                if((port.userData as Socket)?.Scheduled==true)continue;
                var socket=port.userData as Socket;var data=_document.AuthoringGraph.Nodes.Find(n=>n.Id==card.viewDataKey);var action=(data?.Payload as GameplayActionNodePayload)?.Action;
                if(socket?.Port=="Then"){
                    bool cue=action!=null&&GameplaySkillNodeDescriptors.Get(action.GetType()).IsPresentationCue;
                    bool starts=action is GameplayOwnedSequenceActionAuthoring||action is GameplaySkillPlaySequenceActionAuthoring||action is GameplaySpawnProjectileActionAuthoring||action is GameplaySpawnAreaActionAuthoring||action is GameplaySpawnSummonActionAuthoring;
                    port.portName=cue?"开始播放后继续":starts?"启动后继续":"继续主链";
                    port.tooltip=cue?"成功启动特效或音频后立即执行下游，不等待播放结束。":starts?"启动编排、弹体、区域或召唤后立即继续；不等待其结束。结束回调请使用对应生命周期出口。":"此行为及其分支完成后继续主链；保留它输出的实际上下文。";
                }
                port.style.minHeight=22;port.style.fontSize=12;port.style.flexShrink=0;
                var connector=port.Q("connector");if(connector!=null){connector.style.width=12;connector.style.height=12;connector.style.minWidth=12;connector.style.flexShrink=0;}
                if(port.portType==typeof(GameplayEffectDefinitionAsset)){
                    port.portColor=CallbackColor;
                    if(port.direction==Direction.Input){port.portName="效果输入";port.tooltip="仅提供此效果定义给到期/溢出回调；不执行此卡的执行入口或后续链。";}
                }
            }
            var contract=new Label{name="ContextContract"};contract.style.whiteSpace=WhiteSpace.Normal;contract.style.color=new Color(1,.75f,.38f);contract.style.fontSize=11;contract.style.marginLeft=10;contract.style.marginRight=10;contract.style.marginBottom=6;contract.style.display=DisplayStyle.None;card.extensionContainer.Add(contract);
        }
        private static void GroupSearchPorts(VisualElement outputs)
        {
            outputs.style.width = 132;
            var ports = outputs.Children().OfType<Port>().ToDictionary(p => ((Socket)p.userData).Port);
            outputs.Clear();
            AddSection("查询开始", "OnBegin");
            AddSection("单个目标", "EachTarget");
            AddSection("搜索结束", "Completed", "NoTargets");
            if (ports.TryGetValue("Then", out var continuation)) outputs.Add(continuation);

            void AddSection(string title, params string[] keys)
            {
                var heading = new Label(title) { pickingMode = PickingMode.Ignore };
                heading.style.fontSize = 10;
                heading.style.color = new Color(.72f, .79f, .84f);
                heading.style.unityTextAlign = TextAnchor.MiddleRight;
                heading.style.paddingRight = 13;
                heading.style.borderTopWidth = 1;
                heading.style.borderTopColor = new Color(.36f, .4f, .44f);
                outputs.Add(heading);
                foreach (var key in keys) outputs.Add(ports[key]);
            }
        }

        private void RefreshSearchPorts(string id, GameplaySearchTargetActionAuthoring search, bool refreshConnections)
        {
            if (_outputs.TryGetValue(Key(id, "Then"), out var next))
            {
                next.portName = search.HopIntervalSeconds > 0 ? "启动后继续" : "继续主链";
                next.tooltip = search.HopIntervalSeconds > 0
                    ? "首跳处理后沿原输入继续。最终结果与结束施放请接搜索完成。"
                    : "搜索与分支完成后继续，接收实际输出上下文。";
            }
            if (!refreshConnections) return;
            Set("OnBegin", "开始", "查询前调用一次，保留原始输入。");
            Set("EachTarget", "每个目标", "每次选到目标时调用，提供单个 Actor、采样地点及逐次重选倍率。");
            Set("Completed", "完成 · 结果集", "正常结束时调用一次，提供最终结果集。零结果时在无目标回调之后调用。");
            Set("NoTargets", "未找到目标", "整次查询零结果时先调用，提供查询中心。之后仍会正常调用完成。");

            void Set(string key, string label, string description)
            {
                if (!_outputs.TryGetValue(Key(id, key), out var port)) return;
                var graph = _document.AuthoringGraph;
                var edges = graph.Connections.Where(e => e.FromNodeId == id && e.OutputPortKey == key).OrderBy(e => e.Order).ToArray();
                var types = edges.Select(e => Record(e.ToNodeId)).Where(node => (node?.Payload as GameplayActionNodePayload)?.Action != null)
                    .Select(node =>
                    {
                        var action = ((GameplayActionNodePayload)node.Payload).Action;
                        return GameplaySkillFieldSchema.ValueNodeTypeName(GameplaySkillNodeDisplay.BoundValues(node, graph))
                            ?? GameplaySkillNodeDescriptors.Get(action.GetType()).DisplayName;
                    }).GroupBy(type => type);
                string summary = string.Join(" / ", types.Select(group => group.Key + " ×" + group.Count()));
                port.portName = label + " [" + edges.Length + "]";
                port.tooltip = description + (summary.Length == 0 ? "" : "\n直接连接  " + summary);
                foreach (var edge in port.connections)
                    edge.tooltip = "搜索目标 · " + label + " → " + edge.input.node.title + "\n" + description;
            }
        }
        private static void Heading(VisualElement side,string text,bool right)
        {
            var label=new Label(text);label.style.fontSize=10;label.style.color=new Color(.65f,.72f,.78f);label.style.unityTextAlign=right?TextAnchor.MiddleRight:TextAnchor.MiddleLeft;label.style.marginBottom=3;side.Insert(0,label);
        }
        private void RefreshContextContracts(bool force=false)
        {
            if(!_document)return;ContextAnalysisCount++;long sample=System.Diagnostics.Stopwatch.GetTimestamp();
            Dictionary<string,GameplayGraphContextPolicy.Requirement> contracts;
            using(new Unity.Profiling.ProfilerMarker("FRFrame.Workbench.Context").Auto())contracts=GameplayGraphContextPolicy.ConditionalInputs(_document.AuthoringGraph);
            ContextAnalysisMilliseconds+=(System.Diagnostics.Stopwatch.GetTimestamp()-sample)*1000d/System.Diagnostics.Stopwatch.Frequency;
            foreach(var pair in _nodes){
                contracts.TryGetValue(pair.Key,out var next);_contextContracts.TryGetValue(pair.Key,out var previous);if(!force&&next==previous)continue;
                var label=pair.Value.extensionContainer.Q<Label>("ContextContract");if(label==null)continue;
                bool conditional=contracts.TryGetValue(pair.Key,out var required);label.style.display=conditional?DisplayStyle.Flex:DisplayStyle.None;
                label.text=conditional?"条件输入 · 需调用者提供"+GameplayGraphContextPolicy.Describe(required)+"\n缺失时运行拒绝，可在 Trace 定位":"";
                foreach(var input in pair.Value.inputContainer.Children().OfType<Port>().Where(p=>p.portType==typeof(GameplaySkillActionAuthoring)))input.portColor=conditional?CallbackColor:new Color(.62f,.76f,.87f);
            }
            _contextContracts=contracts;
        }
        private void AddCallbackCreationMenu(ContextualMenuPopulateEvent evt)
        {
            if(!_document)return;
            foreach(var card in selection.OfType<Node>()){
                var node=_document.AuthoringGraph.Nodes.Find(n=>n.Id==card.viewDataKey);if(node==null)continue;
                foreach(var binding in node.Payload.Definitions){
                    var local=_document.AuthoringGraph.LocalDefinitions.Find(d=>d.Id==binding.LocalId);if(!(local?.Payload is GameplayEffectDefinitionData effect))continue;
                    string owner=local.Id;var position=CreationPosition;
                    if(effect.DurationPolicy==GameplayEffectDurationPolicy.Duration)Add("自然到期",GameplayGraphEffectCallbacks.Completion);
                    if(effect.Stacking!=null)Add(effect.Stacking.Overflow==GameplayEffectStackOverflowPolicy.ExecuteOverflow?"叠层溢出":"启用叠层溢出",GameplayGraphEffectCallbacks.Overflow);
                    void Add(string title,string port)=>evt.menu.AppendAction(title+" / 新建后续伤害（随所有者删除）",_=>Try(()=>{
                        string id=GameplaySkillAuthoringCommands.CreateOwnedEffectCallback(_document,owner,port,new GameplayEffectDefinitionData{Executions=new GameplayEffectExecutionAuthoring[]{new GameplayDamageExecutionAuthoring()}},position);
                        Selected?.Invoke(id);
                    }));
                }
            }
        }
    }
}
