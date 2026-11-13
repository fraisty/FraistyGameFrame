using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    public sealed partial class GameplaySkillWorkbench
    {
        private sealed class SkillExample
        {
            internal GameplaySkillDocumentAsset Source;
            internal string Category,Host,Status,Lesson,Tags,Mechanisms,Keywords;
            internal int Complexity;
        }
        private VisualElement _examplePanel;
        private string _exampleSearch="";
        private Vector2 _exampleScroll;
        private List<SkillExample> _examples;
        private readonly HashSet<int> _exampleLessons=new HashSet<int>();
        private void ShowExamples()
        {
            _examplePanel?.RemoveFromHierarchy();
            _examples=Documents().Select(doc=>{
                var graph=doc.AuthoringGraph;
                var types=graph.Nodes.Select(n=>(n.Payload as GameplayActionNodePayload)?.Action?.GetType().Name??n.Payload.GetType().Name).ToArray();
                bool Has(string word)=>types.Any(t=>t.IndexOf(word,StringComparison.OrdinalIgnoreCase)>=0);
                // Index every concrete authored role, not only the first display category.
                // Descriptor vocabulary is shared with the real creation menus, including
                // plugin actions/conditions/values and callbacks inside local resources.
                var values=graph.Nodes.SelectMany(n=>GameplayGraphFields.ObjectPaths(n.Payload).Keys)
                    .Concat(graph.LocalDefinitions.SelectMany(d=>GameplayGraphFields.ObjectPaths(d.Payload).Keys)).ToArray();
                var descriptions=values.Where(v=>v is GameplaySkillActionAuthoring||v is GameplaySkillTriggerAuthoring||v is GameplaySkillConditionAuthoring||v is GameplayEffectExecutionAuthoring||v is GameplayEffectCompletionAuthoring||v is GameplayEffectOverflowAuthoring||v is GameplayFloatValueAuthoring)
                    .Select(v=>GameplaySkillNodeDescriptors.Get(v.GetType())).ToArray();
                var mechanisms=descriptions.Select(d=>d.CategoryPath+" · "+d.DisplayName).ToList();
                if(graph.Timelines.Count>0)mechanisms.Add("时间编排 Timeline 多时点与复用");
                if(values.OfType<GameplayEffectDefinitionData>().Any(e=>e.Period>0||e.PeriodValue!=null))mechanisms.Add("周期效果 Periodic");
                if(values.OfType<GameplayEffectDefinitionData>().Any(e=>e.CompletionExecutions.Length>0)||graph.LocalDefinitions.Any(d=>GameplayGraphFields.ActionArrays(d.Payload).Any(s=>s.Path=="OnEnd")))mechanisms.Add("生命周期 自然结束 到期回调");
                if(values.OfType<GameplayEffectDefinitionData>().Any(e=>e.Stacking!=null))mechanisms.Add("效果与叠层 Stack 溢出");
                if(values.OfType<GameplaySearchTargetActionAuthoring>().Any(search=>search.RepeatFromResult))mechanisms.Add("连锁 · 最近未访问目标 · 每跳倍率");
                if(values.OfType<GameplayDamageExecutionAuthoring>().Any(damage=>damage.HealInstigatorAmount!=null))mechanisms.Add("吸血 · 实际净 HP 伤害 · 治疗施法者");
                var shields = values.OfType<GameplayGrantTimedShieldActionAuthoring>().ToArray();
                foreach(var shield in shields)
                    if(graph.Connections.Any(e=>e.OutputPortKey=="OnBroken"||e.OutputPortKey=="OnExpired"))
                        mechanisms.Add("盾破反击 · 自然到期 · "+GameplaySkillBusinessLabels.Display(shield.ReapplicationPolicy.ToString()));
                if(values.OfType<GameplaySpawnProjectileActionAuthoring>().Any(projectile=>projectile.LaunchFromTriggerPoint))mechanisms.Add("分裂弹 · 从命中点发射子弹");
                if(doc.Mode==FRFrame.Gameplay.Standard.GameplayStandardActivationMode.Passive||graph.AdditionalRuleNodeIds.Count>0)mechanisms.Add("被动与条件 事件监听");
                string mechanismIndex=string.Join(" · ",mechanisms.Distinct());
                string category=Has("Animation")?"主动画与时间":Has("SpawnArea")?"周期领域":Has("SpawnSummon")?"召唤单位":Has("SpawnProjectile")?"弹体与命中":Has("Branch")||graph.AdditionalRuleNodeIds.Count>0?"被动与条件":graph.Timelines.Any(t=>t.Events.Count>3)?"多时点与复用":graph.LocalDefinitions.Any(d=>d.Payload is GameplayEffectDefinitionData e&&e.Stacking!=null)?"效果与叠层":"即时效果与控制";
                bool privateHost=types.Any(t=>t.StartsWith("Conformance",StringComparison.Ordinal)||t.StartsWith("ExtensionLab",StringComparison.Ordinal));
                string host=graph.Scope=="XH"?"XHRehostParity":graph.Scope=="AzurLane"?"AzurLane 游戏安装链（工作台未注册）":"FRFrameGameplaySkillLab / 对应 "+graph.Scope+" Host";
                string route=category=="主动画与时间"?"时间编排 → 设置主动画 → 选择实际后端、骨骼/Clip/Skin → 从第8帧创建搜索 → 逐目标连接伤害 → 30帧安排结束。主动画偏移与逻辑总长独立。":
                    category=="周期领域"?"施放执行 → 生成持续区域 → 配置Duration/TickInterval与阵营 → 从区域每周期端口连接治疗或伤害；需要退出清理时用Aura/Exit，不用持续停留代替周期。":
                    category=="弹体与命中"?"施放执行 → 发射弹体 → 配置运动、速度、存续与命中上限 → 从实际OnHit端口接即时效果；继续主链表示发射后立即继续，不表示等命中。":
                    category=="召唤单位"?"施放执行 → 召唤正式单位 → 高级配置选择独立单位定义、其自身技能与归属 → 配置召回时限。召唤单位以自己的技能执行，不复用施法者身份。":
                    category=="被动与条件"?"创建附加触发规则 → 选择真实事件与目标 → 连接条件分支 → 满足/否则接实际行为 → 启用为正式规则。目标输入不能保证Actor时先搜索；从Context诊断定位修复。":
                    category=="多时点与复用"?"创建时间编排 → 此帧创建实际行为 → 在其它帧选择“安排已有行为”。复用保留一个Action，多条发生独立时间/Order；若段间参数不同，复制行为再独立安排。删除时间发生不删除行为。":
                    category=="效果与叠层"?"施放/搜索逐目标 → 持续Buff → 设置时长/周期/主要Execution与Stacking → 真自然到期或溢出端口接后续效果。取消归属通过OwnedByAbility明确，禁止伪造OnTick输出。":
                    "新建空白或即时起点 → 从施放执行创建搜索 → 逐目标接造成伤害/治疗 → 搜索继续主链接结束。常量在卡上编辑，Formula/Caller在高级编辑并保留类型；高级可设置复杂Modifier与Tag。";
                if(doc.SkillId==4130)route+=" 本例旧资产为12组Search→Heal，0.7至6.2秒每0.5秒调用；可在新副本复用同一Search→Heal多次发生，或明确改成Area Tick。两者首Tick和边界不能冒称逐时等价。";
                if(doc.SkillId==7005)route+=" 本例半径6、4秒、0.5秒周期、进入即Tick；完整驻留为9次伤害，OnEnd结束施放。取消即清理区域。";
                if(doc.SkillId==7006)route+=" 本例4/12/20帧复用同一Search→Heal50，自身也是友方；一次施放总治疗150。";
                if(doc.SkillId==7008)route+=" 本例4秒/0.5秒周期、最多3层；层数不自动倍乘伤害。第四次应用触发溢出伤害，自然到期治疗50；OwnedByAbility=false，因此瞬时施放结束后DoT继续，驱散不等于自然完成。";
                if(doc.SkillId==7009)route+=" 本例Mode=Passive，Subject=OwnerTarget，监听真实受击Hit；Owner当前Health<500才治疗当前目标50。试装面板发布者→敌人的测试事件不会冒充Owner受击；可让敌人安装7004面向Owner攻击验证。";
                if(doc.SkillId==7010)route+=" 本例共享SpatialActorProfile与独立7004技能；AutoCast开启、存续4秒、OnEnd结束施放。召唤技能以新单位自己的属性和身份执行。";
                bool u7=AssetDatabase.GetAssetPath(doc).Contains("/FRFrameSkillLab/U7Examples/");
                if(u7){
                    host="FRFrameGameplaySkillLab / 共用练习场";
                    route+=" 进入共用练习场，或用 Scene / 调试 → 安装当前正式技能。";
                    route+=shields.Length>0?" 三种定时护盾的策略配置和同场对照见 U7-HR-P5 第 4 节。":
                        doc.SkillId==4201?" 剧毒爆发从零步骤见 U7 Task 第 10 节。":
                        " 连锁闪电从零创作、其余案例复制修改和正式表现资源定位见 U7-HR-P3 第 6 节。目录外副本需勾选「共用练习场试装」。";
                }
                var ability=graph.Nodes.Find(n=>n.Id==graph.AbilityNodeId)?.Payload as GameplayAbilityNodePayload;var settings=ability?.Settings as GameplayAbilityDefinitionData;
                string tags=(settings?.Cost!=null?"消耗 ":"")+(settings?.Charge!=null?"充能 ":"")+(ability?.Definitions.Any(b=>b.PropertyPath=="CooldownEffect")==true?"冷却 ":"")+(graph.Nodes.Any(n=>n.Payload.Definitions.Any(b=>b.External))?"共享SO ":"");
                return new SkillExample{Source=doc,Category=category,Host=host,Tags=tags,Mechanisms=mechanismIndex,Keywords=string.Join(" ",descriptions.Select(d=>d.Keywords)),Complexity=graph.Nodes.Count>15?3:graph.Nodes.Count>7?2:1,Status=u7?"U7 标准范例 · Lab 可运行":privateHost?"特定样例Host；非通用能力":AssetDatabase.GetAssetPath(doc).Contains("/AuthoringExamples/H1/")?(doc.SkillId==7003?"已由 GUI 复制改造并在 Lab 验证":doc.SkillId==7002||doc.SkillId>=7004&&doc.SkillId<=7010?"已由 GUI 从零重建并在 Lab 运行验证":"已由 GUI 创作；运行验收待完成"):"既有正式Runtime内容；GUI复刻待验证",Lesson=route};
            }).OrderBy(e=>e.Category,StringComparer.Ordinal).ThenBy(e=>e.Source.SkillId).ToList();
            _examplePanel=new VisualElement{name="SkillExampleLibrary",style={position=Position.Absolute,left=12,right=12,top=38,bottom=12,backgroundColor=new Color(.14f,.16f,.19f)}};
            _examplePanel.Add(new IMGUIContainer(DrawExamples){style={flexGrow=1}});rootVisualElement.Add(_examplePanel);_examplePanel.BringToFront();
        }
        private void DrawExamples()
        {
            GUILayout.BeginHorizontal();GUILayout.Label("技能范例 · 真实正式源与从零路线",EditorStyles.boldLabel);GUILayout.FlexibleSpace();if(GUILayout.Button("返回工作台",GUILayout.Width(110)))_examplePanel.RemoveFromHierarchy();GUILayout.EndHorizontal();
            _exampleSearch=EditorGUILayout.TextField("机制组合 / 名称 / Scope / ID",_exampleSearch);
            EditorGUILayout.HelpBox("只读参考与新技能副本使用同一个工作台。状态不把Builder生成、Build成功或历史运行记录冒充本轮GUI验收。外部共享SO在副本中仍显式共享，只读定位。",MessageType.Info);
            _exampleScroll=EditorGUILayout.BeginScrollView(_exampleScroll);
            var terms=_exampleSearch.Split(new[]{' ','\t','\u3000'},StringSplitOptions.RemoveEmptyEntries);
            foreach(var example in _examples.Where(e=>terms.All(term=>(e.Category+e.Source.DisplayName+e.Source.AuthoringGraph.Scope+e.Source.SkillId+e.Tags+e.Mechanisms+e.Keywords).IndexOf(term,StringComparison.OrdinalIgnoreCase)>=0))){
                GUILayout.BeginVertical(EditorStyles.helpBox);
                GUILayout.Label(example.Category+" · "+example.Source.DisplayName+" #"+example.Source.SkillId+" · 难度 "+example.Complexity,EditorStyles.boldLabel);
                GUILayout.Label("Scope: "+example.Source.AuthoringGraph.Scope+" | Host: "+example.Host,EditorStyles.wordWrappedMiniLabel);
                GUILayout.Label(example.Status+" · "+example.Tags,EditorStyles.wordWrappedMiniLabel);
                GUILayout.Label("机制索引："+example.Mechanisms,EditorStyles.wordWrappedMiniLabel);
                GUILayout.Label(AssetDatabase.GetAssetPath(example.Source),EditorStyles.wordWrappedMiniLabel);
                bool expanded=_exampleLessons.Contains(example.Source.SkillId);
                bool show=EditorGUILayout.Foldout(expanded,"从零教程 / 本例真实时间与参数",true);
                if(show!=expanded){if(show)_exampleLessons.Add(example.Source.SkillId);else _exampleLessons.Remove(example.Source.SkillId);}
                if(show){
                    GUILayout.Label(example.Lesson,EditorStyles.wordWrappedLabel);
                    GUILayout.Label("本例可能同时含多种机制；以下列出实际节点和回调，分类路线不是逐节点复刻已验收承诺。",EditorStyles.wordWrappedMiniLabel);
                    var graph=example.Source.AuthoringGraph;
                    foreach(var timeline in graph.Timelines){
                        GUILayout.Label(timeline.Name+" · 总长 "+timeline.DurationSeconds.ToString("0.###")+"s · "+timeline.FramesPerSecond+"fps",EditorStyles.boldLabel);
                        foreach(var entry in timeline.Events.OrderBy(e=>e.TimeSeconds).ThenBy(e=>e.Order)){
                            var action=graph.Nodes.Find(n=>n.Id==entry.EntryNodeId);
                            GUILayout.Label((entry.TimeSeconds*timeline.FramesPerSecond).ToString("0.#")+"f / "+entry.TimeSeconds.ToString("0.###")+"s · Order "+entry.Order+" → "+GameplaySkillFieldSchema.NodeName(action)+" ["+entry.EntryNodeId.Substring(0,5)+"]",EditorStyles.wordWrappedMiniLabel);
                        }
                    }
                    foreach(var node in graph.Nodes.Where(n=>n.Payload is GameplayActionNodePayload)){
                        string summary=GameplaySkillNodeDisplay.CardSummary(node,graph,false);
                        GUILayout.Label(GameplaySkillFieldSchema.NodeName(node)+"："+summary,EditorStyles.wordWrappedMiniLabel);
                        foreach(var edge in graph.Connections.Where(e=>e.FromNodeId==node.Id))GUILayout.Label("  "+edge.OutputPortKey+" → "+GameplaySkillFieldSchema.NodeName(graph.Nodes.Find(n=>n.Id==edge.ToNodeId)),EditorStyles.wordWrappedMiniLabel);
                    }
                    GUILayout.Label("运行：打开 "+example.Host+"，进入Play，再从工作台 Scene / 调试试装到当前角色；不自动重载或保存Scene。预期：观察真实数值/状态与Trace；取消后检查归属清理，暂停后时序冻结。共享来源只读定位；副本需要独立值时，先明确换为局部定义再改参数。",EditorStyles.wordWrappedMiniLabel);
                }
                GUILayout.BeginHorizontal();
                if(GUILayout.Button("打开只读参考")){
                    SelectDocument(example.Source);_referenceView=true;GameplaySkillAuthoringCommands.ReadOnlyDocument=example.Source;Canvas.SetBusinessReading(true);Canvas.Load(example.Source);_examplePanel.RemoveFromHierarchy();_feedback="只读范例：可浏览与试装；请以此创建新技能后编辑。";
                }
                if(GUILayout.Button("以此创建新正式技能"))GameplaySkillWizard.Open(this,example.Source);
                if(GUILayout.Button("现有 Host 试装入口")){
                    SelectDocument(example.Source);_referenceView=true;GameplaySkillAuthoringCommands.ReadOnlyDocument=example.Source;_detailTab=2;_examplePanel.RemoveFromHierarchy();_details.MarkDirtyRepaint();
                }
                GUILayout.EndHorizontal();GUILayout.EndVertical();
            }
            EditorGUILayout.EndScrollView();
        }
    }
}
