using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor
{
    /// <summary>Shared labels/grouping used by standalone inspectors and the Workbench, with unchanged serialized names.</summary>
    public static class GameplayAuthoringInspector
    {
        internal static string CurrentScope { get; private set; }
        private static string _fieldContext="";
        private static string _focusRequest;
        public static string FocusedFieldControl {get;private set;}
        public static Rect FocusedFieldRect {get;private set;}
        public static Rect FocusedFieldScreenRect {get;private set;}
        /// <summary>Names the actual serialized input controls so editor navigation and profiler
        /// probes can focus a real field without changing authoring data.</summary>
        public static IDisposable UseFieldContext(string context)=>new FieldContext(context);
        private sealed class FieldContext:IDisposable
        {
            private readonly string _previous=_fieldContext;
            internal FieldContext(string context){_fieldContext=context??"";}
            public void Dispose(){_fieldContext=_previous;}
        }
        public static void RequestFieldFocus(UnityEngine.Object source,string path,string context="Inspector")
        {_focusRequest=ControlName(source,path,context);}
        private static string ControlName(UnityEngine.Object source,string path,string context)=>"Gameplay.Field."+context+"."+source.GetInstanceID()+":"+path;
        /// <summary>Fallback scope for unclassified scratch sources while drawing a workspace. Explicit source scopes still take precedence.</summary>
        public static System.IDisposable UseScope(string scope)=>new ScopeLease(scope);
        private sealed class ScopeLease : System.IDisposable
        {
            private readonly string _previous=CurrentScope;
            internal ScopeLease(string scope){CurrentScope=scope;}
            public void Dispose(){CurrentScope=_previous;}
        }
        public const string Help = "技能创作以主技能内嵌行为图为唯一来源；Graph、Timeline、Inspector 编辑同一份数据。独立共享资源仍使用真实资产引用。";
        private static readonly Dictionary<string,string> Labels = new Dictionary<string,string> {
            {"State","状态名称"},{"Animation","动画来源"},{"Queue","排队播放"},{"Priority","优先级"},{"Channel","通道"},{"RestorePrevious","结束后恢复"},
            {"Direction","搜索方向"},{"DirectionAngle","方向角度 / 偏移（度）"},{"LengthFromTarget","长度使用两点距离"},
            {"definitionId","定义 ID"},{"schemaVersion","数据版本"},{"displayName","显示名称"},{"DisplayName","显示名称"},{"SkillId","技能编号"},{"AuthoringScope","所属游戏 / Scope"},{"Category","分类"},{"LearningHint","学习说明"},{"SampleScene","样例场景"},{"SamplePage","样例页"},
            {"Rules","正式规则"},{"AdditionalRules","附加规则"},{"RuntimeDocument","正式技能定义"},{"AuthorProfile","预览角色"},{"CastEvent","施放事件（安装器发出）"},
            {"Ability","释放 / Ability"},{"PrimaryRule","主触发规则"},{"ScopedRules","激活期间规则"},{"ExtensionRoots","世界行为 / 补充配置"},
            {"RequiredTags","所需标签"},{"BlockedTags","阻止释放标签"},{"ActivationOwnedTags","激活拥有标签（Activation Owned Tags）"},{"AbilityTags","技能分类标签"},{"CancelAbilitiesWithTags","取消其它技能标签（Cancel Abilities With Tags）"},{"BlockAbilitiesWithTags","阻止其它技能标签"},{"OngoingBlockedTags","持续阻止标签"},
            {"Cost","消耗（Cost）"},{"CooldownEffect","冷却效果"},{"Charge","充能（Charge）"},{"ActivationEffects","激活施加效果"},{"Once","只触发一次"},{"Trigger","触发"},{"Conditions","条件"},{"Target","目标"},{"Actions","动作"},
            {"DurationPolicy","持续策略"},{"Duration","持续时间（Duration）"},{"Period","周期（Period）"},{"DurationValue","动态持续时间"},{"PeriodValue","动态周期"},{"PeriodicStart","首次周期时机"},{"EffectTags","效果分类标签"},{"GrantedTags","授予标签"},{"Modifiers","属性修改（Modifiers）"},{"Magnitude","数值大小"},{"All","全部具备"},{"Any","任一具备"},{"None","全部不具备"},{"StackKey","叠层键"},{"Aggregation","来源聚合"},{"DurationRefresh","持续刷新策略"},{"PeriodReset","周期重置策略"},{"Lifetime","层生命周期"},{"OverflowExecutions","溢出执行"},{"Executions","执行（Executions）"},{"Stacking","叠层 / 溢出（Stack / Overflow）"},{"ApplicationRequirements","应用条件"},{"OngoingRequirements","持续条件"},{"CompletionExecutions","自然完成后续（Completion）"},
            {"Markers","时间轴 / 标记"},{"Time","执行时刻（秒）"},{"HasMagnitude","携带数值"},{"HasDuration","携带时长"},{"CueTag","表现事件"},{"Name","名称"},{"AnimationName","动画名称"},{"AnimationFrames","动画帧数"},{"AnimationLoop","循环动画"},{"ImpactFrame","命中帧"},{"EndFrame","结束帧"},{"ParticlePrefab","特效资源"},{"ParticleStartFrame","特效开始帧"},{"ParticleEndFrame","特效结束帧"},{"LaunchBinding","来源发射挂点"},{"TargetBinding","来源目标挂点"},{"TargetMode","来源目标模式"},{"FlyOver","来源越过命中"},{"CancelOnAbilityEnd","来源结束时取消"},{"Piercing","来源穿透"},{"TargetPositionSource","来源瞄准位置"},{"PositionSource","来源位置类型"},{"StartFrame","来源起始帧"},{"DestroyWithNode","来源随节点销毁"},{"BaseDamage","固定伤害"},{"ParticleOffset","粒子偏移"},{"ParticleScale","粒子缩放"},{"AttachParticle","附着挂点"},{"LoopParticle","保留对象（Loop来源）"},{"DestroyParticleWithNode","来源随节点销毁"},{"Scale","缩放"},{"Offset","偏移"},{"Binding","挂点"},{"Prefab","预制体"},{"Attach","附着"},{"Loop","保留对象"},{"Speed","速度"},{"Distance","距离"},{"MinDistance","停止距离"},{"Pull","拉向施法者"},{"MultiplyByStack","乘以叠层数"},{"CollisionRadius","碰撞半径"},{"MaxDistance","最大飞行距离"},{"OffsetAngle","角度偏移"},{"OwnerEffect","拥有该表现的Effect"},{"OwnedByAbility","随技能结束清理"},{"CueName","触发表现事件"},{"FloatColor","飘字颜色"},{"FloatFontSize","字号"},{"FloatOffset","位置偏移"},{"FloatDirection","屏幕运动方向"},{"FloatContextKey","结果对象 Key"},{"FloatPositionSource","位置来源（0施法者 / 1主目标 / 2父输入）"},{"FloatDuration","飘字时长"},{"FloatBinding","飘字挂点"},{"ParticleBinding","特效挂点"},{"Icon","图标"},{"Effect","效果资产"},{"Sequence","时间编排资产"},{"Radius","半径"},{"Angle","角度"},{"Width","宽度"},{"Length","长度"},{"Damage","伤害"},{"Hops","跳跃次数"},{"Open","开启窗口"},{"Explosion","爆炸后续规则"},{"Label","说明"},{"Marker","标记序号"},{"Amount","增量"},{"Heal","治疗"},{"MaxStacks","最大叠层"},{"Overflow","溢出动作"},{"Mode","模式"},{"OverflowActions","溢出动作"},{"Modifier","属性增量"},{"Value","数值"},{"Input","输入"},{"Output","输出"},{"Keys","曲线控制点"},{"Operation","运算"},{"Attribute","属性"},{"EventTag","事件标签"},{"Interval","间隔"},{"RequiredCount","事件次数"},{"TrueActions","满足时动作"},{"FalseActions","不满足时动作"},{"Condition","分支条件"},{"Key","数据键"},{"Comparison","比较"},{"MaxCharges","最大充能"},{"RechargeDuration","充能恢复时长"}
        };
        static GameplayAuthoringInspector(){
            Labels["definitionId"]="定义标识";
            Labels["RecycleWhenParticlesFinish"]="播完回收";
            Labels["ActivationOwnedTags"]="激活期间拥有的标签";
            Labels["CancelAbilitiesWithTags"]="取消具有这些标签的技能";
            Labels["Cost"]="资源消耗";Labels["Charge"]="充能";
            Labels["Modifiers"]="属性修改";Labels["Executions"]="效果执行";
            Labels["Stacking"]="叠层 / 溢出";Labels["CompletionExecutions"]="自然完成后续";
            Labels["schemaVersion"]="结构版本";
            Labels["Shape"]="范围形状";
            Labels["Plane"]="坐标平面";
            Labels["Origin"]="中心来源";
            Labels["OriginOffset"]="中心偏移";
            Labels["Faction"]="阵营筛选";
            Labels["Tags"]="标签筛选";
            Labels["LayerMask"]="碰撞层";
            Labels["MaxTargets"]="目标上限";
            Labels["Order"]="排序规则";
            Labels["IncludeSelf"]="包含自身";
            Labels["EachTarget"]="逐个目标";
            Labels["Completed"]="搜索完成";
            Labels["NoTargets"]="无目标";
            Labels["Height"]="高度";
            Labels["Cells"]="格子坐标";
            Labels["CellSize"]="格子尺寸";
            Labels["Motion"]="运动轨迹";
            Labels["StraightDestination"]="直线终点模式";
            Labels["LaunchOffset"]="发射偏移";
            Labels["DestinationOffset"]="终点偏移";
            Labels["DirectionAngle"]="方向角度";
            Labels["HitRadius"]="命中半径";
            Labels["MaxRange"]="最大射程";
            Labels["CurveHeight"]="曲线高度";
            Labels["Control1Offset"]="第一控制点偏移";
            Labels["Control2Offset"]="第二控制点偏移";
            Labels["MaxHits"]="最大命中数";
            Labels["Projectile"]="弹体配置";
            Labels["Area"]="区域配置";
            Labels["Summon"]="召唤配置";
            Labels["OnHit"]="命中回调";
            Labels["OnArrive"]="到达回调";
            Labels["OnRange"]="射程回调";
            Labels["OnCancelled"]="取消回调";
            Labels["OnEnter"]="进入回调";
            Labels["OnStay"]="停留回调";
            Labels["OnTick"]="周期回调";
            Labels["OnExit"]="离开回调";
            Labels["OnEnd"]="结束回调";
            Labels["OnSpawn"]="生成回调";
            Labels["TickInterval"]="周期间隔（必须为正）";
            Labels["TickOnEnter"]="进入时结算周期";
            Labels["FollowOwner"]="跟随拥有者";
            Labels["ArmingDelay"]="布防延迟";
            Labels["TriggerLimit"]="触发上限（0 不限）";
            Labels["Aura"]="区域光环效果";
            Labels["DamageType"]="伤害类型";
            Labels["Stages"]="结算阶段";
            Labels["Immunity"]="免疫筛选";
            Labels["HealInstigatorAmount"]="伤害提交后治疗施法者";
            Labels["ExcludePrimaryTarget"]="排除输入的主要目标";
            Labels["RepeatFromResult"]="从每次结果继续搜索";
            Labels["RepeatMultiplier"]="每跳倍率";
            Labels["LaunchFromTriggerPoint"]="从触发事件地点发射";
            Labels["OnlyHitPrimaryTarget"]="仅命中主要目标";
            Labels["EndOnOwnerDeath"]="施法者死亡时结束";
            Labels["Expression"]="数值表达式";
            Labels["Source"]="数值来源";
            Labels["Constant"]="固定比较值";
            Labels["AttributeSource"]="属性来源";
            Labels["SourceAttribute"]="来源属性";
            Labels["SetByCaller"]="调用方数值";
            Labels["Capture"]="捕获策略";
            Labels["Snapshot"]="快照策略";
            Labels["Priority"]="优先级";
            Labels["Defense"]="物理防御属性";Labels["MagicDefense"]="魔法防御属性";Labels["Shield"]="护盾属性";
            Labels["FailureReason"]="条件未满足说明";Labels["Minimum"]="基础最低值";Labels["PerLevel"]="每等级增量";Labels["Skill"]="正式技能";
            Labels["Channel"]="播放通道";
            Labels["RestorePrevious"]="恢复之前状态";
            Labels["Queue"]="排队播放";
            Labels["Animation"]="动画素材";
            Labels["State"]="行为状态";
            Labels["Skills"]="技能列表";
            Labels["Profile"]="角色配置";
            Labels["AutoCast"]="自动施放";
            Labels["FloatingText"]="飘字配置";
            Labels["SkillId"]="技能 ID";
            Labels["DisplayName"]="显示名称";
            Labels["Rules"]="事件规则";
            Labels["AdditionalRules"]="补充规则";
            Labels["CastEvent"]="施放事件";
            Labels["Subject"]="事件主体筛选";
            Labels["Defaults"]="默认上下文";
            Labels["LearningRequirements"]="学习前置";
            Labels["PreserveTriggerContext"]="保留触发上下文";
            Labels["RunRulesOnPassiveActivation"]="被动激活时执行规则";
            Labels["UseControlGate"]="使用控制门禁";
            Labels["Blocked"]="阻止标签";
            Labels["RotateOwner"]="转向拥有者";
            Labels["PreserveExisting"]="保留已捕获值";
            Labels["CriticalChance"]="暴击概率";
            Labels["MissChance"]="未命中概率";
            Labels["OperationSource"]="运算来源";
            Labels["Left"]="左值";
            Labels["Right"]="右值";
            Labels["Event"]="事件";
            Labels["Result"]="结果";
            Labels["Context"]="上下文";
            Labels["TargetSource"]="目标来源";
            Labels["Level"]="等级";
            Labels["LevelValues"]="等级数值";
            Labels["Curve"]="曲线";
            Labels["Fallback"]="缺失时默认值";
            Labels["Min"]="最小值";
            Labels["Max"]="最大值";
            Labels["Clamp"]="限制范围";
            Labels["Cooldown"]="冷却";
            Labels["Refresh"]="刷新策略";
            Labels["TargetCount"]="目标数量";
            Labels["Health"]="生命值";
            Labels["CurrentHealth"]="当前生命";
            Labels["MaxHealth"]="最大生命";
            Labels["MaxLevel"]="最高技能等级";
            Labels["Cancel"]="以取消方式结束";
            Labels["Percent"]="百分比";
            Labels["Resources"]="资源条目";
            Labels["MinimumBalance"]="释放前最低余额";
            Labels["Delta"]="资源变化（负数扣除）";
            Labels["Duration"]="持续时间（秒）";Labels["Period"]="周期间隔（秒，0不周期）";Labels["TickInterval"]="区域周期间隔（秒，必须为正）";
            Labels["Acceleration"]="加速度";Labels["Attributes"]="初始属性";Labels["Combat"]="战斗属性映射";Labels["StartingSkills"]="初始技能";
            Labels["AudioLoop"]="循环音效";Labels["Backend"]="播放后端";Labels["BlendDuration"]="混合时长（秒）";Labels["Clip"]="动画或音频片段";
            Labels["CriticalColor"]="暴击颜色";Labels["CriticalPrefix"]="暴击前缀";Labels["Cue"]="表现资源";Labels["DamageColor"]="伤害颜色";Labels["DamagePrefix"]="伤害前缀";
            Labels["FadeStartRatio"]="开始淡出的进度（0～1）";Labels["FixedPoint"]="固定位置";Labels["Follow"]="跟随位置";Labels["FollowSource"]="持续跟随来源";
            Labels["Presentation"]="状态外观";Labels["StatusPrefab"]="持续状态预制体";
            Labels["OnApplied"]="首次成功应用";Labels["OnStackChanged"]="层数变化";Labels["OnEnterMax"]="进入满层";Labels["OnPeriod"]="每次周期";
            Labels["ImmuneText"]="免疫文字";Labels["Layer"]="Animator层";Labels["Material"]="材质";Labels["MinimumSeparation"]="最小间距";
            Labels["MirrorFacing"]="随朝向镜像";Labels["MissColor"]="未命中颜色";Labels["MissText"]="未命中文字";Labels["ModelPrefab"]="Cubism模型预制体";
            Labels["NumberFormat"]="数字格式";Labels["OwnerSkillId"]="拥有者技能编号（0使用当前）";Labels["Pitch"]="音调";Labels["Point"]="地点来源或坐标";
            Labels["PreviewInitialAnimation"]="预览初始动画";Labels["PreviewPrefab"]="预览载体预制体";Labels["RequestedHealing"]="显示请求治疗量";
            Labels["Required"]="所需标签条件";Labels["Resource"]="资源属性";Labels["RiseSpeed"]="上升速度";Labels["ShieldPrefix"]="护盾前缀";
            Labels["SignalTag"]="定时信号标签";Labels["Skeleton"]="Spine骨骼资源";Labels["SkinNames"]="皮肤组合";Labels["StateName"]="Animator状态";
            Labels["SupportColor"]="治疗与护盾颜色";Labels["SuppressEmptyHealing"]="隐藏零治疗";Labels["TrackIndex"]="Spine轨道编号";
            Labels["Values"]="等级数值表";Labels["Variants"]="动画变体";Labels["VelocitySpread"]="速度随机范围";Labels["Volume"]="音量（0～1）";
            Labels["WeightedVariants"]="启用权重变体";Labels["MinimumLevel"]="最低技能等级";Labels["Weight"]="选择权重";Labels["Kind"]="表现类型";
            Labels["SourceId"]="来源标识";Labels["Instigator"]="发起者";Labels["X"]="X坐标";Labels["Y"]="Y坐标";Labels["Z"]="Z坐标";
        }
        public static bool HasBusinessLabel(string field)=>Labels.ContainsKey(field);
        public static GUIContent Label(SerializedProperty property){
            if(Labels.TryGetValue(property.name,out var label))return new GUIContent(label);
            if(property.name.StartsWith("data[",StringComparison.Ordinal)&&int.TryParse(property.name.Substring(5,property.name.Length-6),out int index))return new GUIContent("配置 "+(index+1));
            return new GUIContent(property.displayName,"扩展字段；内部路径可在高级来源中查看。");
        }
        public static void Field(SerializedProperty property)
        {if(property!=null)Field(property,Label(property));}
        public static void Field(SerializedProperty property,GUIContent caption)
        {
            if(property==null)return;
            string control=ControlName(property.serializedObject.targetObject,property.propertyPath,_fieldContext);
            GUI.SetNextControlName(control);
            using(new Unity.Profiling.ProfilerMarker("FRFrame.Gameplay.Editor.AuthoringField").Auto()){
                if(OwnedArray(property))DrawProperty(EditorGUILayout.GetControlRect(false,PropertyHeight(property)),property,caption);
                else EditorGUILayout.PropertyField(property,caption,true);
            }
            if(_focusRequest==control&&Event.current.type==EventType.Repaint){EditorGUI.FocusTextInControl(control);_focusRequest=null;}
            if(_fieldContext=="Inspector")FocusedFieldControl=GUI.GetNameOfFocusedControl();
            if(FocusedFieldControl==control&&Event.current.type==EventType.Repaint){FocusedFieldRect=GUILayoutUtility.GetLastRect();FocusedFieldScreenRect=new Rect(GUIUtility.GUIToScreenPoint(FocusedFieldRect.position),FocusedFieldRect.size);}
        }
        // Native ReorderableList minus/size controls bypass subasset ownership. Keep advanced
        // nested action arrays editable, but route removal through the same audited command.
        private static bool OwnedArray(SerializedProperty property)
        {
            if(!property.isArray||property.propertyType==SerializedPropertyType.String||property.arraySize==0)return false;
            var first=property.GetArrayElementAtIndex(0);
            if(first.propertyType!=SerializedPropertyType.ManagedReference)return false;
            string type=first.managedReferenceFieldTypename;
            return type.EndsWith("GameplaySkillActionAuthoring",StringComparison.Ordinal)||
                type.EndsWith("GameplaySequenceMarkerAuthoring",StringComparison.Ordinal)||
                type.EndsWith("GameplayEffectExecutionAuthoring",StringComparison.Ordinal)||
                type.EndsWith("GameplayEffectCompletionAuthoring",StringComparison.Ordinal)||
                type.EndsWith("GameplayEffectOverflowAuthoring",StringComparison.Ordinal);
        }
        internal static float PropertyHeight(SerializedProperty property)
        {
            if(!OwnedArray(property))return EditorGUI.GetPropertyHeight(property,true);
            float height=EditorGUIUtility.singleLineHeight;
            if(property.isExpanded)for(int i=0;i<property.arraySize;i++)height+=EditorGUI.GetPropertyHeight(property.GetArrayElementAtIndex(i),true)+EditorGUIUtility.singleLineHeight+6;
            return height;
        }
        internal static void DrawProperty(Rect rect,SerializedProperty property,GUIContent caption)
        {
            string unavailable=GameplayAuthoringFieldSchema.FieldUnavailable(property.serializedObject.targetObject,property.propertyPath);
            if(unavailable!=null)caption=new GUIContent(caption.text,caption.tooltip+"\n"+unavailable);
            using(new EditorGUI.DisabledScope(unavailable!=null))DrawPropertyCore(rect,property,caption);
        }
        private static void DrawPropertyCore(Rect rect,SerializedProperty property,GUIContent caption)
        {
            if(property.name=="Cancel"&&property.propertyType==SerializedPropertyType.Boolean){
                int dot=property.propertyPath.LastIndexOf('.');
                var parent=dot<0?null:property.serializedObject.FindProperty(property.propertyPath.Substring(0,dot));
                if(parent?.propertyType==SerializedPropertyType.ManagedReference&&parent.managedReferenceValue is GameplayEndAbilityActionAuthoring){
                    property.boolValue=EditorGUI.Popup(rect,"结束类型",property.boolValue?1:0,new[]{"正常结束","取消结束"})==1;return;
                }
            }
            if(!OwnedArray(property)){EditorGUI.PropertyField(rect,property,caption,true);return;}
            float line=EditorGUIUtility.singleLineHeight;
            property.isExpanded=EditorGUI.Foldout(new Rect(rect.x,rect.y,rect.width-30,line),property.isExpanded,new GUIContent(caption.text+" · "+property.arraySize,caption.tooltip),true);
            string appendReason=GameplayActionOrderValidation.PlacementReason(property.serializedObject.targetObject,property.propertyPath,property.arraySize,null);
            using(new EditorGUI.DisabledScope(appendReason!=null))if(GUI.Button(new Rect(rect.xMax-26,rect.y,26,line),new GUIContent("+",appendReason))){
                int at=property.arraySize;property.InsertArrayElementAtIndex(at);property.GetArrayElementAtIndex(at).managedReferenceValue=null;
                property.serializedObject.ApplyModifiedProperties();GameplayAuthoringSourceChanges.Notify(property.serializedObject.targetObject);
                return;
            }
            if(!property.isExpanded)return;
            float y=rect.y+line+4;
            for(int i=0;i<property.arraySize;i++){
                var item=property.GetArrayElementAtIndex(i);float height=EditorGUI.GetPropertyHeight(item,true);
                // Reserve a small Inspector toolbar, not a permanent right gutter through every
                // nested field. Otherwise completion Effect references collapse to a few pixels.
                EditorGUI.LabelField(new Rect(rect.x+12,y,Mathf.Max(0,rect.width-94),line),"顺序 / 删除",EditorStyles.miniLabel);
                string up=i==0?null:GameplayActionOrderValidation.PlacementReason(property.serializedObject.targetObject,property.propertyPath,i-1,item.managedReferenceValue,i);
                string down=i+1==property.arraySize?null:GameplayActionOrderValidation.PlacementReason(property.serializedObject.targetObject,property.propertyPath,i+2,item.managedReferenceValue,i);
                using(new EditorGUI.DisabledScope(i==0||up!=null))if(GUI.Button(new Rect(rect.xMax-78,y,24,line),new GUIContent("↑",up))){property.MoveArrayElement(i,i-1);return;}
                using(new EditorGUI.DisabledScope(i+1==property.arraySize||down!=null))if(GUI.Button(new Rect(rect.xMax-52,y,24,line),new GUIContent("↓",down))){property.MoveArrayElement(i,i+1);return;}
                using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))if(GUI.Button(new Rect(rect.xMax-26,y,26,line),new GUIContent("×","删除动作 / 事件并检查独占素材…"))){
                    var source=property.serializedObject.targetObject;var handle=GameplaySourceHandle.Capture(source,item.propertyPath);
                    property.serializedObject.ApplyModifiedProperties();
                    EditorApplication.delayCall+=()=>{
                        if(!source)return;
                        try{
                            GameplaySharedAuthoringCommands.Remove(source,handle.ResolvePath());
                        }
                        catch(Exception e){EditorUtility.DisplayDialog("不能删除此配置",e.Message,"了解");}
                    };
                    return;
                }
                EditorGUI.PropertyField(new Rect(rect.x+12,y+line+2,rect.width-12,height),item,new GUIContent("配置 "+(i+1)),true);
                y+=height+line+6;
            }
        }
        public static void Draw(SerializedObject serialized,bool showHelp=true)
        {
            serialized.UpdateIfRequiredOrScript();
            if(serialized.targetObject is GameplayDefinitionAsset history && history.MigrationHistoryOnly){DrawMigrationHistory(serialized);return;}
            GameplayAuthoringFieldSchema.Draw(new GameplayAuthoringLocation{Source=serialized.targetObject,Path=""},GameplayAuthoringTypeCatalog.Scope(serialized.targetObject));
        }
        public static void DrawMigrationHistory(SerializedObject serialized)
        {
            EditorGUILayout.HelpBox("只读迁移来源 / 历史。正式技能请在 正式主技能中编辑。",MessageType.Info);
            using(new EditorGUI.DisabledScope(true)){
                var iterator=serialized.GetIterator();bool enter=true;
                while(iterator.NextVisible(enter)){enter=false;if(iterator.name!="m_Script"&&iterator.name!="migrationHistoryOnly")EditorGUILayout.PropertyField(iterator,true);}
            }
        }
        internal static string Group(string field)
        {
            switch(field) {
                case "RequiredTags": case "BlockedTags": return "释放门禁";
                case "ActivationOwnedTags": case "AbilityTags": case "CancelAbilitiesWithTags": case "BlockAbilitiesWithTags": case "OngoingBlockedTags": return "生命周期标签";
                case "Cost": return "消耗";
                case "CooldownEffect": case "Charge": return "冷却 / 充能";
                case "ActivationEffects": return "激活效果";
                case "DurationPolicy": case "Duration": case "Period": case "DurationValue": case "PeriodValue": case "PeriodicStart": return "生命周期 / 持续与周期";
                case "GrantedTags": case "EffectTags": return "分类与标签";
                case "Modifiers": case "Executions": return "属性修改 / 执行";
                case "Stacking": return "叠层 / 溢出";
                case "ApplicationRequirements": case "OngoingRequirements": return "应用 / 持续条件";
                case "CompletionExecutions": return "自然完成后续";
                case "Trigger": return "触发"; case "Conditions": return "条件"; case "Target": return "目标"; case "Actions": return "动作 / 后续动作";
                case "Condition": case "TrueActions": case "FalseActions": return "条件 / 分支";
                case "Markers": return "时间轴 / 标记参数";
                case "Motion": case "StraightDestination": case "Direction": case "DirectionAngle": case "CurveHeight": case "Control1Offset": case "Control2Offset": return "运动轨迹 / 朝向";
                case "Shape": case "Plane": case "Origin": case "OriginOffset": case "Radius": case "Length": case "Width": case "Height": case "Cells": case "CellSize": return "空间范围 / 坐标";
                case "LaunchOffset": case "DestinationOffset": case "MaxRange": case "HitRadius": case "MaxHits": return "发射 / 命中参数";
                case "OnHit": case "OnArrive": case "OnRange": case "OnCancelled": case "OnEnter": case "OnStay": case "OnTick": case "OnExit": case "OnEnd": case "OnSpawn": return "回调 / 上下文";
                case "CancelOnAbilityEnd": case "FollowSource": case "MinimumSeparation": case "UseControlGate": case "Blocked": return "来源清理 / 控制";
                case "Skills": case "Profile": case "AutoCast": case "Prefab": case "FloatingText": return "宿主 / 资源";
                case "Cue": case "Kind": case "Clip": case "AudioLoop": case "Volume": case "Pitch": case "Binding": case "Attach": case "Follow": case "MirrorFacing": case "Offset": case "Scale": return "表现 / 位置 / 音效";
                default: return "基本信息";
            }
        }
    }
    public abstract class GameplayDefinitionInspector : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            if(target is GameplayDefinitionAsset history && history.MigrationHistoryOnly){GameplayAuthoringInspector.DrawMigrationHistory(serializedObject);return;}
            if(target is GameplayDefinitionAsset orderRoot){
                var sources=GameplayAuthoringDependencyGraph.BuildDefinition(orderRoot).Nodes.Select(n=>(UnityEngine.Object)n.Asset);
                foreach(var issue in GameplayActionOrderValidation.Scan(sources)){
                    EditorGUILayout.HelpBox(issue.Message,issue.Severity==GameplayAuthoringIssueSeverity.Error?MessageType.Error:MessageType.Warning);
                    if(GUILayout.Button("定位结束后动作 · "+issue.Source.name+" / "+issue.Path)){
                        SkillAuthoring.GameplaySkillWorkbench.OpenSource(issue.Source as GameplayDefinitionAsset,issue.Path);
                    }
                }
            }
            GameplayAuthoringInspector.Draw(serializedObject);
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target,out string guid,out long id);
            EditorGUILayout.LabelField("正式来源 · "+AssetDatabase.GetAssetPath(target)+" / 子对象 #"+id,EditorStyles.wordWrappedMiniLabel);
            if(target is GameplayDefinitionAsset&&GUILayout.Button("在同一技能工作台定位")){
                SkillAuthoring.GameplaySkillWorkbench.OpenSource(target as GameplayDefinitionAsset);
            }
            if (target is GameplayDefinitionAsset definition && GUILayout.Button("校验配置"))
            {
                var validation = new GameplayAuthoringValidationContext(); validation.Asset(definition, definition.name);
                if (!validation.HasErrors) Debug.Log("配置校验通过", target);
                foreach (var issue in validation.Issues) Debug.LogWarning(issue.ToString(), target);
            }
        }
    }
    /// <summary>Fallback for concrete integration sources; keeps SDK-specific controls and real sub-object identity visible.</summary>
    [CustomEditor(typeof(GameplayDefinitionAsset),true)]
    public sealed class GameplayResourceInspector : GameplayDefinitionInspector
    {
        public override void OnInspectorGUI()
        {
            if(target is GameplayDefinitionAsset history && history.MigrationHistoryOnly){GameplayAuthoringInspector.DrawMigrationHistory(serializedObject);return;}
            var provider=GameplayAnimationSourceInspectors.For(target);
            if(provider==null){base.OnInspectorGUI();return;}
            AssetDatabase.TryGetGUIDAndLocalFileIdentifier(target,out string guid,out long fileId);
            GUILayout.Label("动画素材 · 子对象 #"+fileId,EditorStyles.boldLabel);
            string path=AssetDatabase.GetAssetPath(target);
            GameplayAnimationSourceInspectors.DrawShared(serializedObject);
            if(target is GameplayDefinitionAsset asset){var validation=new GameplayAuthoringValidationContext();validation.Asset(asset,"素材");foreach(var issue in validation.Issues)EditorGUILayout.HelpBox(issue.Message,MessageType.Warning);}
        }
    }
    [CustomEditor(typeof(GameplaySkillDefinitionAsset))] public sealed class GameplayRuleInspector : GameplayDefinitionInspector { }
    [CustomEditor(typeof(GameplayEffectDefinitionAsset))] public sealed class GameplayEffectInspector : GameplayDefinitionInspector { }
    [CustomEditor(typeof(GameplaySequenceDefinitionAsset))] public sealed class GameplaySequenceInspector : GameplayDefinitionInspector { }
    [CustomEditor(typeof(GameplayContinuationDefinitionAsset))] public sealed class GameplayContinuationInspector : GameplayDefinitionInspector { }
    [CustomEditor(typeof(GameplaySkillDocumentAsset))] public sealed class GameplayFormalSkillInspector : GameplayDefinitionInspector
    {
        private string _node;
        public override void OnInspectorGUI()
        {
            var document=(GameplaySkillDocumentAsset)target;
            if(GUILayout.Button("打开 技能工作台"))SkillAuthoring.GameplaySkillWorkbench.Open(document,_node);
            if(document.AuthoringGraph==null){EditorGUILayout.HelpBox("此数据格式不能直接构建，请从正式技能入口打开。",MessageType.Warning);return;}
            var nodes=document.AuthoringGraph.Nodes;var names=nodes.Select(SkillAuthoring.GameplaySkillFieldSchema.NodeName).ToArray();
            int at=nodes.FindIndex(n=>n.Id==_node);if(at<0)at=nodes.FindIndex(n=>n.Id==document.AuthoringGraph.AbilityNodeId);
            if(names.Length>0){at=EditorGUILayout.Popup("编辑行为",Mathf.Max(0,at),names);_node=nodes[at].Id;}
            SkillAuthoring.GameplaySkillFieldSchema.Draw(document,_node);
            SkillAuthoring.GameplaySkillWorkbench.DrawTimelineFields(document,_node);
        }
    }
}
