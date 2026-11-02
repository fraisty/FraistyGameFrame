using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>First-party business groups over the actual serialized source; no parameter mirror.</summary>
    public sealed class GameplayStandardBusinessInspector : IGameplayBusinessInspectorProvider
    {
        public string AuthoringScope=>"Common";
        private static object Value(GameplayAuthoringLocation node)
        {
            if(node.Path.Length==0)return node.Source;
            using(var so=new SerializedObject(node.Source)){
                var property=so.FindProperty(node.Path);
                return property?.propertyType==SerializedPropertyType.ManagedReference?property.managedReferenceValue:null;
            }
        }
        public bool Supports(GameplayAuthoringLocation node)
        {
            var value=Value(node);
            if((value is GameplayAbilityDefinitionAsset || value is GameplayAbilityDefinitionData)||value is GameplaySkillDefinitionAsset||value is GameplaySequenceDefinitionAsset||(value is GameplayEffectDefinitionAsset || value is GameplayEffectDefinitionData)||
                value is GameplaySequenceMarkerAuthoring||value is GameplaySkillConditionAuthoring||value is GameplaySkillTriggerAuthoring||value is GameplaySkillTargetAuthoring||
                value is GameplayFloatValueAuthoring||value is GameplayAbilityCostAuthoring||value is GameplayEffectCompletionAuthoring||value is GameplayEffectOverflowAuthoring||
                value?.GetType().Name=="GameplayPresentationCueAsset"||value?.GetType().Name=="GameplayFloatingTextAsset"||value?.GetType().Name=="GameplayPresentationCueData"||value?.GetType().Name=="GameplayFloatingTextData")return true;
            return value is GameplayDispelEffectsActionAuthoring || value is GameplaySearchTargetActionAuthoring ||
                value is GameplayDamageExecutionAuthoring || value is GameplayHealExecutionAuthoring || value is GameplayShieldExecutionAuthoring ||
                value is GameplayApplyToTargetsActionAuthoring || value is GameplayOwnedSequenceActionAuthoring || value is GameplayEndAbilityActionAuthoring ||
                value is GameplaySkillDocumentAsset || (value is GameplayProjectileDefinitionAsset || value is GameplayProjectileDefinitionData) || (value is GameplayAreaDefinitionAsset || value is GameplayAreaDefinitionData) || (value is GameplaySummonDefinitionAsset || value is GameplaySummonDefinitionData) || value is GameplaySpawnSummonActionAuthoring ||
                value is GameplaySpawnProjectileActionAuthoring || value is GameplaySpawnAreaActionAuthoring || value is GameplayMoveTargetsActionAuthoring || value is GameplayRotateToPointActionAuthoring || value is GameplayChangeStateActionAuthoring || value is GameplayGrantTimedShieldActionAuthoring || value is GameplayCaptureCombatRollActionAuthoring || value is GameplayCaptureValueActionAuthoring || value is GameplayCooldownActionAuthoring || value is GameplayActorProfileAsset;
        }
        public IEnumerable<GameplayBusinessInspectorGroup> Groups(GameplayAuthoringLocation node)
        {
            string prefix=node.Path.Length==0?"":node.Path+".";
            foreach(var group in Describe(Value(node)))yield return new GameplayBusinessInspectorGroup(node.Source,group.Title,group.Note,group.Paths.Select(p=>prefix+p).ToArray()){Advanced=group.Advanced};
        }
        /// <summary>Shared business sections for both persisted V1 library resources and embedded
        /// V2 values. Metadata contains relative field names only, with no graph or SO proxy.</summary>
        public static IEnumerable<GameplayBusinessInspectorGroup> Describe(object value)
        {
            if((value is GameplayAbilityDefinitionAsset || value is GameplayAbilityDefinitionData)){
                yield return Group("释放门禁 / 生命周期标签","标签来自正式能力定义；Formation/舰种等游戏规则不进入通用能力。","AbilityTags","RequiredTags","BlockedTags","ActivationOwnedTags","CancelAbilitiesWithTags","BlockAbilitiesWithTags","OngoingBlockedTags");
                yield return Group("费用 / 冷却 / 充能","资源变化负数为扣除；先检查释放前最低余额。普通冷却与充能互斥。","Cost","CooldownEffect","Charge");
                yield return Group("激活效果","持续效果归本次授权所有。","ActivationEffects","MaxLevel");
            }
            else if(value is GameplaySkillDefinitionAsset || value is FRFrame.Gameplay.Authoring.Graph.GameplayRuleNodePayload){
                yield return Group("触发 / 生命周期","被动事件与主动施放复用正式规则；不是第二份Graph执行状态。","Trigger","Once","PreserveTriggerContext");
                yield return Group("条件 / 明确目标","保留Context可能只有地点；需要逐目标效果时先显式选取或搜索Actor。","Conditions","Target");
                yield return Group("动作顺序","按正式数组顺序执行；右键时间事件可调整同帧顺序。","Actions");
            }
            else if(value is GameplaySequenceDefinitionAsset){
                yield return Group("正式编排时长","以秒保存，帧只是编辑器刻度。End与总时长独立，不随动画自动改动。","Duration");
                // The Timeline already selects each real marker/action. Recursively drawing
                // the complete expanded array on every card drag repaints offscreen fields.
                var markers=Group("完整事件数组 / 高级编辑","通常从轨道选择单个事件或动作；展开后编辑同一份正式数组。","Markers");
                markers.Advanced=true;yield return markers;
            }
            else if(value is GameplaySequenceMarkerAuthoring){
                yield return Group("事件时刻 / 目标 / 条件","Time以秒保存；更改数组顺序不会替换事件身份。","Time","Target","Conditions");
                yield return Group("业务动作 / 表现","所有动作消费正式Source；Cue只有配置真实Duration才显示持续区间。","Actions","Effect","CueTag","HasMagnitude","Magnitude","HasDuration","Duration");
            }
            else if((value is GameplayEffectDefinitionAsset || value is GameplayEffectDefinitionData)){
                yield return Group("持续 / 周期（秒）","Period=0不执行周期；动态数值沿用正式求值器，非启用字段保留旧值。","DurationPolicy","Duration","DurationValue","Period","PeriodValue","PeriodicStart");
                yield return Group("标签 / 应用与持续门禁","EffectTags用于驱散分类，GrantedTags在存续期间贡献标签。","EffectTags","GrantedTags","ApplicationRequirements","OngoingRequirements");
                yield return Group("属性修改 / 结算","所有Execution均可配置，按正式数组顺序执行。","Modifiers","Executions");
                yield return Group("叠层 / 溢出 / 自然完成","Overflow与Completion是不同的typed数组；取消/驱散不运行自然完成。","Stacking","CompletionExecutions");
            }
            else if(value is GameplaySkillConditionAuthoring||value is GameplaySkillTriggerAuthoring||value is GameplaySkillTargetAuthoring||value is GameplayFloatValueAuthoring||value is GameplayAbilityCostAuthoring||value is GameplayEffectCompletionAuthoring||value is GameplayEffectOverflowAuthoring)
                yield return Group(value is GameplayFloatValueAuthoring?"数值来源 / 求值":value is GameplayAbilityCostAuthoring?"资源消耗 / 释放前检查":value is GameplaySkillConditionAuthoring?"条件判定":value is GameplaySkillTargetAuthoring?"明确目标 / 地点":"事件 / 后续回调","直接编辑真实嵌入配置；类型替换会提示字段丢失，可Undo恢复。",Fields(value));
            else if(value?.GetType().Name=="GameplayPresentationCueAsset"||value?.GetType().Name=="GameplayFloatingTextAsset")
                yield return Group("表现资源 / 位置 / 反馈","由已安装表现后端消费；不推进Gameplay时间。",Fields(value));
            else if(value is GameplayDispelEffectsActionAuthoring)yield return Group("驱散效果分类","作用于明确选择的目标；匹配 EffectTags，移除原因是 Dispel，不触发自然到期动作。至少选择一个 All/Any 分类。","EffectTags");
            else if(value is GameplayCooldownActionAuthoring cooldown)yield return Group("冷却 / 充能命令","SkillId=0仅当前cast；正数选择本角色已安装技能。重置使用上次捕获时长，减少冷却使用负数，恢复充能使用正整数。",cooldown.Operation==Standard.GameplayCooldownOperation.Restart?new[]{"SkillId","Operation"}:new[]{"SkillId","Operation","Value"});
            else if(value is GameplayActorProfileAsset)yield return Group("战前角色配置","初始属性与技能等级解析到独立运行容器；共享配置不保存战斗状态。","Combat","Attributes","StartingSkills");
            else if(value is GameplaySearchTargetActionAuthoring search)
            {
                yield return Group("搜索形状 / 坐标","所有后端复用同一几何；按 Actor 中心判断。","Shape","Plane","Origin");
                bool directional=search.Shape==Standard.GameplaySearchShape.Sector || search.Shape==Standard.GameplaySearchShape.Box || search.Shape==Standard.GameplaySearchShape.Line || search.Shape==Standard.GameplaySearchShape.Ray || search.Shape==Standard.GameplaySearchShape.Capsule;
                if(directional)yield return Group("方向 / 两点线段","朝向、目标方向、地点方向或绝对角度；正角度在XY从+X转向+Y，在XZ/3D从+X转向+Z。缺目标或重合端点拒绝，不改成角色朝向。",search.Shape==Standard.GameplaySearchShape.Sector?new[]{"Direction","DirectionAngle"}:new[]{"Direction","DirectionAngle","LengthFromTarget"});
                var dimensions=new List<string>();
                if(search.Shape==FRFrame.Gameplay.Standard.GameplaySearchShape.Circle || search.Shape==FRFrame.Gameplay.Standard.GameplaySearchShape.Sector || search.Shape==FRFrame.Gameplay.Standard.GameplaySearchShape.Capsule)dimensions.Add("Radius");
                if(search.Shape==FRFrame.Gameplay.Standard.GameplaySearchShape.Sector)dimensions.Add("Angle");
                if(!search.LengthFromTarget && (search.Shape==FRFrame.Gameplay.Standard.GameplaySearchShape.Box || search.Shape==FRFrame.Gameplay.Standard.GameplaySearchShape.Line || search.Shape==FRFrame.Gameplay.Standard.GameplaySearchShape.Ray || search.Shape==FRFrame.Gameplay.Standard.GameplaySearchShape.Capsule))dimensions.Add("Length");
                if(search.Shape==FRFrame.Gameplay.Standard.GameplaySearchShape.Box || search.Shape==FRFrame.Gameplay.Standard.GameplaySearchShape.Line || search.Shape==FRFrame.Gameplay.Standard.GameplaySearchShape.Ray)dimensions.Add("Width");
                if(search.Shape==FRFrame.Gameplay.Standard.GameplaySearchShape.GridCells)dimensions.AddRange(new[]{"Cells","CellSize"});
                dimensions.Add("Height");
                yield return Group("正式范围数值","可用常量、等级表、公式；形状切换保留原有配置。",dimensions.ToArray());
                yield return Group("筛选 / 稳定结果","先过滤和去重，再排序与限制数量；空结果不会退回 Self。","Faction","Tags","LayerMask","MaxTargets","Order","IncludeSelf");
                yield return Group("正式续接","每个目标只有一个 Target；搜索完成收到完整结果，无目标分支收到空列表。","EachTarget","Completed","NoTargets");
            }
            else if(value is GameplayDamageExecutionAuthoring)
            {
                yield return Group("伤害数值 / 类型","Physical读Defense，Magical读MagicDefense；True只跳过防御阶段。所有计算成功后一次提交HP/Shield。","Amount","DamageType");
                yield return Group("结算阶段（列表顺序）","拖动重排；Critical/Miss读取捕获输入，可先接「捕获暴击/未命中」动作。","Stages","Immunity");
            }
            else if(value is GameplayHealExecutionAuthoring || value is GameplayShieldExecutionAuthoring)
                yield return Group("结算数值",value is GameplayHealExecutionAuthoring?"治疗受实际 MaxHealth 限制，结果保留溢出。":"瞬时增加实际 Shield；本动作不提供按来源的时限护盾。","Amount");
            else if(value is GameplayApplyToTargetsActionAuthoring)
                yield return Group("目标效果 / 来源清理","对当前 Target 列表生效。OwnedByAbility 绑定本次 grant，取消或结束只移除该来源贡献。","Effect","OwnedByAbility");
            else if(value is GameplayOwnedSequenceActionAuthoring)
                yield return Group("播放正式编排","只启动 Core Sequence；Ability scope 取消清理，不创建额外时钟。","Sequence");
            else if(value is GameplayEndAbilityActionAuthoring)
                yield return Group("结束当前来源","按捕获的激活来源查找本次 grant。End/Cancel 结束 Ability；后继是否继续由宿主守卫决定，普通 Rule 可继续清理，延时 OwnedSequence 会取消自身。","Cancel");
            else if((value is GameplayProjectileDefinitionAsset || value is GameplayProjectileDefinitionData))
            {
                yield return Group("弹体轨迹 / 时间 / 数值","直线方向模式保持旧射程弹；固定终点捕获释放点/目标位置，不追踪。射程/时长先耗尽分别 Range/Completed；命中上限优先。弧线/抛物线/Bezier使用时长与逐段扫掠。","Motion","StraightDestination","Plane","Speed","Duration","HitRadius","MaxRange","CurveHeight","Control1Offset","Control2Offset","DirectionAngle","LaunchOffset","DestinationOffset","CancelOnAbilityEnd");
                yield return Group("命中筛选 / 来源","MaxHits 是本弹体最多命中不同目标的总数；取消当前 cast 精确清理。","Faction","Tags","LayerMask","MaxHits");
                yield return Group("实际回调","使用既有规则动作；可以施效果、生成区域或执行后续编排。","OnHit","OnArrive","OnRange","OnCancelled");
                yield return Group("可选场景资源","场景 Source 直接绑定实际 Prefab；逻辑 Source 无 Unity 表现依赖。","Prefab");
            }
            else if((value is GameplayAreaDefinitionAsset || value is GameplayAreaDefinitionData))
            {
                yield return Group("持续区域 / 坐标","与 Search 共用几何与后端；Cells 为世界坐标。","Shape","Plane","Origin","OriginOffset","Radius","Angle","Length","Width","Height","Cells","CellSize","CancelOnAbilityEnd");
                yield return Group("存续 / 陷阱 / 筛选 / 光环","布防前不查询；触发上限0不限。Aura离开/到期/取消只移除本区域来源。","Duration","ArmingDelay","TriggerLimit","TickInterval","TickOnEnter","FollowOwner","Faction","Tags","LayerMask","Aura");
                yield return Group("进入 / 停留 / 周期 / 离开 / 结束","Stay 每步执行，Tick 按间隔执行；所有回调保留 cast Level/Root。","OnEnter","OnStay","OnTick","OnExit","OnEnd","Prefab");
            }
            else if(value is GameplaySpawnProjectileActionAuthoring)yield return Group("发射正式弹体","直接引用唯一弹体 Source；Logic/Physics2D/3D复用同一规则。","Projectile");
            else if(value is GameplaySpawnAreaActionAuthoring)yield return Group("生成正式区域","位置来自 Source Origin；OnHit/Arrive 的 ContextPoint 可成为区域中心。","Area");
            else if((value is GameplaySummonDefinitionAsset || value is GameplaySummonDefinitionData))
            {
                yield return Group("召唤单位 / 技能 / 生命周期","新单位独立持有容器；技能继承出生来源与等级，死亡/到期/取消释放单位和池化表现。","Profile","Skills","AutoCast","Duration","Prefab","FloatingText");
                yield return Group("出生 / 结束续接","回调目标为召唤单位，保留父技能上下文。","OnSpawn","OnEnd");
            }
            else if(value is GameplaySpawnSummonActionAuthoring)yield return Group("召唤正式单位","出生位置使用当前 ContextPoint；生命周期归本次cast所有。","Summon");
            else if(value is GameplayMoveTargetsActionAuthoring)
                yield return Group("位移 / 控制 / 到达","写回实际位置与朝向；等/高优先级替换，低优先级忽略。控制门禁为可选标签查询。","Mode","Distance","Speed","Priority","UseControlGate","Blocked","CancelOnAbilityEnd","FollowSource","MinimumSeparation","Duration","OnArrive","OnCancelled");
            else if(value is GameplayRotateToPointActionAuthoring)yield return Group("转向实际目标点","修改实际 Actor Facing；场景绑定映射到真实朝向。","RotateOwner");
            else if(value is GameplayChangeStateActionAuthoring)yield return Group("现有行为状态","使用Host显式注册的状态别名；转移约束由原Core/Fsm决定。","State");
            else if(value is GameplayGrantTimedShieldActionAuthoring)yield return Group("可消耗时限护盾","FIFO先消耗贡献盾，再消耗无主容量。现有Effect时钟到期只扣本份剩余值；OwnedByAbility另绑定当前cast。","Amount","Duration","OwnedByAbility");
            else if(value is GameplayCaptureCombatRollActionAuthoring)yield return Group("本次命中随机输入","概率0..1；使用World注入的随机源。保留已有输入时不重新抽样，可放进逐目标回调。","CriticalChance","MissChance","PreserveExisting");
            else if(value is GameplayCaptureValueActionAuthoring)yield return Group("捕获数值快照","在此动作/目标边界求值一次，写入Key。后续Context/Caller读取捕获值；属性节点仍读实时值。","Key","Value");
            else if(value is GameplaySkillDocumentAsset)
                yield return Group("正式技能入口","被动事件主体：OwnerInstigator=本角色发起，OwnerTarget=本角色被选为目标。学习前置只作用于战前学习/升级。","SkillId","DisplayName","Icon","Mode","Subject","RunRulesOnPassiveActivation","Ability","Rules","AdditionalRules","CastEvent","Defaults","LearningRequirements");
        }
        private static readonly Dictionary<System.Type,string[]> FieldCache=new Dictionary<System.Type,string[]>();
        private static string[] Fields(object value){var type=value.GetType();if(!FieldCache.TryGetValue(type,out var fields))FieldCache[type]=fields=type.GetFields(System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.Public).Where(f=>!f.IsNotSerialized&&GameplayAuthoringInspector.HasBusinessLabel(f.Name)).Select(f=>f.Name).ToArray();return fields;}
        private static GameplayBusinessInspectorGroup Group(string title,string note,params string[] fields)
        { return new GameplayBusinessInspectorGroup(null,title,note,fields); }
    }
}
