using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Node=UnityEditor.Experimental.GraphView.Node;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    public sealed partial class GameplayBehaviorGraphView
    {
        private void AddSpatialLifecycleGuidance(Node card,GameplayAuthoringNode node)
        {
            if(!GameplaySkillAuthoringCommands.StartsSpatialLifetime(node))return;
            var graph=_document.AuthoringGraph;
            var active=Reachable(graph);
            bool pending=graph.Nodes.Any(n=>(n.Payload as GameplayActionNodePayload)?.Action is GameplayEndAbilityActionAuthoring&&!active.Contains(n.Id));
            var binding=node.Payload.Definitions.SingleOrDefault(b=>b.PropertyPath=="Projectile"||b.PropertyPath=="Area"||b.PropertyPath=="Summon");
            var resource=graph.LocalDefinitions.SingleOrDefault(d=>d.Id==binding?.LocalId)?.Payload;
            string ownership=binding==null?"尚未配置真实空间资源。":binding.External?"生命周期归属由共享资源配置；共享来源只读。":resource is GameplayProjectileDefinitionData projectile&&!projectile.CancelOnAbilityEnd?"当前弹体由世界托管；结束或取消技能不会清理弹体。":"当前对象随施放结束 / 取消清理。";
            var endIds=graph.Nodes.Where(n=>(n.Payload as GameplayActionNodePayload)?.Action is GameplayEndAbilityActionAuthoring).Select(n=>n.Id).ToHashSet();
            var current=graph.Connections.Where(e=>endIds.Contains(e.ToNodeId)&&(e.FromNodeId==node.Id&&e.OutputPortKey=="Then"||binding!=null&&e.FromNodeId==binding.LocalId)).Select(e=>e.OutputPortKey+" → "+GameplaySkillNodeDisplay.Name(graph.Nodes.Single(n=>n.Id==e.ToNodeId),graph)).ToArray();
            var label=new Label((pending?"结束策略待决 · 原结束节点保留，尚未接入执行流程\n":"")+"启动后继续立即返回，不等待对象结束。"+ownership+(current.Length>0?"\n直接结束关联："+string.Join("；",current):"")){
                name="SpatialLifecycleSummary",style={whiteSpace=WhiteSpace.Normal,fontSize=11,marginLeft=10,marginRight=10,color=new Color(1,.77f,.4f)}};
            card.extensionContainer.Add(label);
            var choose=new Button(()=>ShowSpatialEndChoices(node.Id)){text="选择 / 替换真实结束策略…"};
            choose.SetEnabled(!EditorApplication.isPlayingOrWillChangePlaymode&&!GameplaySkillAuthoringCommands.IsReadOnly(_document));card.extensionContainer.Add(choose);
        }

        private void ShowSpatialEndChoices(string nodeId)
        {
            GameplaySkillCreationMenu.Show(this,"持续行为：选择结束策略",menu=>{
                var graph=_document.AuthoringGraph;var node=graph.Nodes.Single(n=>n.Id==nodeId);
                var binding=node.Payload.Definitions.SingleOrDefault(b=>b.PropertyPath=="Projectile"||b.PropertyPath=="Area"||b.PropertyPath=="Summon");
                if(binding==null){menu.AddDisabledItem(new GUIContent("先配置真实弹体、区域或召唤资源"));return;}
                var local=graph.LocalDefinitions.SingleOrDefault(d=>d.Id==binding.LocalId);
                if(local==null){menu.AddDisabledItem(new GUIContent("共享来源只读：先明确转为本地配置"));return;}
                var ends=graph.Nodes.Where(n=>(n.Payload as GameplayActionNodePayload)?.Action is GameplayEndAbilityActionAuthoring).ToArray();
                if(ends.Length==0)menu.AddDisabledItem(new GUIContent("先从真实回调拖出创建「结束技能」"));
                foreach(var end in ends){
                    string endId=end.Id,title=end.Title+"（"+(System.Array.IndexOf(ends,end)+1)+"）";
                    foreach(var slot in GameplayGraphFields.ActionArrays(local.Payload)){
                        string port=slot.Path;
                        string caption=port=="OnEnd"?"自然结束后结束技能":port=="OnHit"?"首次命中即结束（清理余下飞行）":port=="OnArrive"?"到达终点后结束技能":port=="OnRange"?"达到射程后结束技能":null;
                        if(caption==null)continue;
                        string reason=GameplayGraphPortPolicy.Unavailable(graph,local.Id,port);
                        var content=new GUIContent("保持施放 / "+caption+" / "+title,reason);
                        if(reason!=null)menu.AddDisabledItem(content);
                        else menu.AddItem(content,false,()=>ReplaceSpatialEnd(nodeId,endId,port,false));
                    }
                    if(local.Payload is GameplayProjectileDefinitionData)menu.AddItem(new GUIContent("世界托管（结束或取消不清理弹体） / "+title),false,()=>ReplaceSpatialEnd(nodeId,endId,"Then",true));
                }
                menu.AddDisabledItem(new GUIContent("超时：从启动后继续创建真实时间编排，安排原结束节点"));
                if(local.Payload is GameplayProjectileDefinitionData)menu.AddDisabledItem(new GUIContent("弹体没有统一寿命结束回调；命中、到达与射程是不同事件"));
                else menu.AddDisabledItem(new GUIContent("区域 / 召唤暂不支持脱离施放的世界托管"));
            });
        }

        private void ReplaceSpatialEnd(string nodeId,string endId,string port,bool worldOwned)=>Try(()=>{
            string review=GameplaySkillAuthoringCommands.SpatialEndReview(_document.AuthoringGraph,nodeId);
            if(review!=null&&!EditorUtility.DisplayDialog("审核结束策略替换",review+"\n\n是否仅替换列出的直接结束关联？","替换直接结束关联","取消"))return;
            GameplaySkillAuthoringCommands.SetSpatialEnd(_document,nodeId,endId,port,worldOwned,review!=null);
        });
    }
}
