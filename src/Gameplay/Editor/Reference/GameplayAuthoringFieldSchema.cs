using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using FRFrame.Gameplay.Standard;
using UnityEditor;
using UnityEngine;
using Object=UnityEngine.Object;

namespace FRFrame.Gameplay.Editor
{
    /// <summary>One business field description for Project and Workbench. Descriptors contain
    /// paths/conditions only; all values and Undo belong to the real SerializedObject.</summary>
    public static class GameplayAuthoringFieldSchema
    {
        private static readonly HashSet<string> OpenInactive=new HashSet<string>();
        private static readonly IGameplayBusinessInspectorProvider[] Providers=TypeCache.GetTypesDerivedFrom<IGameplayBusinessInspectorProvider>()
            .Where(t=>!t.IsAbstract&&t.GetConstructor(Type.EmptyTypes)!=null).OrderBy(t=>t.FullName)
            .Select(t=>(IGameplayBusinessInspectorProvider)Activator.CreateInstance(t)).ToArray();
        public static object Value(Object source,string path="")
        {
            if(string.IsNullOrEmpty(path))return source;
            using(var so=new SerializedObject(source)){var p=so.FindProperty(path);return p?.propertyType==SerializedPropertyType.ManagedReference?p.managedReferenceValue:p?.propertyType==SerializedPropertyType.Generic&&!p.isArray?p.boxedValue:null;}
        }
        /// <summary>Read-only card text from the same live source as the two Inspector hosts.
        /// Cards do not maintain a third editable field list.</summary>
        public static string Summary(GameplayAuthoringLocation node)
        {
            var value=Value(node.Source,node.Path);
            if(value is GameplaySearchTargetActionAuthoring search)
                return search.Shape+" · "+search.Plane+" · "+search.Faction+"\n半径 "+Number(search.Radius)+" · 目标上限 "+search.MaxTargets+" · 每目标 "+(search.EachTarget?.Length??0)+" 项";
            if(value is GameplayEffectDefinitionAsset effect)
                return effect.DurationPolicy==GameplayEffectDurationPolicy.Instant&&effect.Executions?.Length==1?
                    effect.Executions[0] is GameplayDamageExecutionAuthoring hit?"伤害 · "+hit.DamageType+" / "+Number(hit.Amount):
                    effect.Executions[0] is GameplayHealExecutionAuthoring healing?"治疗 · "+Number(healing.Amount):
                    effect.Executions[0] is GameplayShieldExecutionAuthoring ward?"护盾 · "+Number(ward.Amount):"瞬时效果 · "+effect.name:
                    effect.DurationPolicy+(effect.DurationPolicy==GameplayEffectDurationPolicy.Duration?" · 持续 "+effect.Duration.ToString("0.###")+"s":"")+(effect.DurationPolicy!=GameplayEffectDurationPolicy.Instant?" · 周期 "+effect.Period.ToString("0.###")+"s":"")+"\n执行 "+(effect.Executions?.Length??0)+" 项"+(effect.DurationPolicy==GameplayEffectDurationPolicy.Duration?" · 自然完成 "+(effect.CompletionExecutions?.Length??0)+" 项":"");
            if(value is GameplayProjectileDefinitionAsset projectile)
                return projectile.Motion+(projectile.Motion==GameplayProjectileMotionKind.Straight?" / "+projectile.StraightDestination:"")+" · "+projectile.Plane+"\n速度 "+Number(projectile.Speed)+" · 射程 "+Number(projectile.MaxRange)+" · 命中上限 "+projectile.MaxHits;
            if(value is GameplayAreaDefinitionAsset area)
                return area.Shape+" · "+area.Plane+" · 持续 "+Number(area.Duration)+"s\n周期 "+Number(area.TickInterval)+"s · Aura "+(area.Aura?.Length??0)+" 项";
            if(value is GameplayDamageExecutionAuthoring damage)return damage.DamageType+" · "+Number(damage.Amount)+"\n结算阶段 "+(damage.Stages?.Length??0)+" 项";
            if(value is GameplayHealExecutionAuthoring heal)return "治疗 · "+Number(heal.Amount);
            if(value is GameplayShieldExecutionAuthoring shield)return "护盾 · "+Number(shield.Amount);
            if(value is GameplayApplyToTargetsActionAuthoring targets)return targets.Effect==null?"未连接效果":"对已解析目标应用效果\n"+targets.Effect.name;
            if(value is GameplayEndAbilityActionAuthoring end)return end.Cancel?"取消当前施法并清理来源":"结束当前施法并清理来源";
            if(value is GameplaySkillActionAuthoring)return GameplaySkillNodeDescriptors.Get(value.GetType()).DisplayName;
            return string.Join("\n",node.Summary.Split('\n').Take(2));
        }
        private static string Number(GameplayFloatValueAuthoring value)=>value is GameplayConstantFloatValueAuthoring constant?constant.Value.ToString("0.###"):
            value is GameplayFormulaFloatValueAuthoring formula?formula.Expression:value==null?"缺失":"动态数值";
        /// <summary>Provider groups plus every remaining direct serialized field. New fields cannot
        /// disappear merely because a compact business provider predates them.</summary>
        public static GameplayBusinessInspectorGroup[] Describe(GameplayAuthoringLocation node,string scope)
        {
            var provider=Providers.FirstOrDefault(p=>(p.AuthoringScope=="Common"||p.AuthoringScope==scope)&&p.Supports(node));
            var groups=provider?.Groups(node).ToList()??new List<GameplayBusinessInspectorGroup>();
            var covered=new HashSet<string>(groups.Where(g=>g.Source==node.Source).SelectMany(g=>g.Paths));
            var remaining=new List<string>();
            using(var so=new SerializedObject(node.Source)){
                var root=node.Path.Length==0?null:so.FindProperty(node.Path);var cursor=root?.Copy()??so.GetIterator();
                var end=root?.GetEndProperty();bool enter=true;
                while(cursor.NextVisible(enter)){
                    enter=false;if(end!=null&&SerializedProperty.EqualContents(cursor,end))break;
                    if(cursor.name=="m_Script"||cursor.name=="CompositionLayout"||covered.Contains(cursor.propertyPath))continue;
                    remaining.Add(cursor.propertyPath);
                }
            }
            foreach(var group in remaining.GroupBy(p=>GameplayAuthoringInspector.HasBusinessLabel(p.Split('.').Last())?GameplayAuthoringInspector.Group(p.Split('.').Last()):"高级 / 扩展字段"))
                groups.Add(new GameplayBusinessInspectorGroup(node.Source,group.Key,"正式来源字段；未显示的条件字段保留原值。",group.ToArray()){Advanced=group.Key=="高级 / 扩展字段"});
            return groups.ToArray();
        }
        public static bool Visible(object owner,string field)
        {
            if(owner is GameplaySequenceCueMarkerAuthoring cue){if(field=="Duration")return cue.HasDuration;if(field=="Magnitude")return cue.HasMagnitude;}
            if(owner is GameplayMoveTargetsActionAuthoring move){if(field=="Blocked")return move.UseControlGate;}
            if(owner is GameplaySearchTargetActionAuthoring search){
                bool length=search.Shape==GameplaySearchShape.Box||search.Shape==GameplaySearchShape.Line||search.Shape==GameplaySearchShape.Ray||search.Shape==GameplaySearchShape.Capsule;
                if(field=="Direction"||field=="DirectionAngle")return length||search.Shape==GameplaySearchShape.Sector;
                if(field=="LengthFromTarget")return length;
                if(field=="Radius")return search.Shape==GameplaySearchShape.Circle||search.Shape==GameplaySearchShape.Sector||search.Shape==GameplaySearchShape.Capsule;
                if(field=="Angle")return search.Shape==GameplaySearchShape.Sector;
                if(field=="Length")return length&&!search.LengthFromTarget;
                if(field=="Width")return search.Shape==GameplaySearchShape.Box||search.Shape==GameplaySearchShape.Line||search.Shape==GameplaySearchShape.Ray;
                if(field=="Cells"||field=="CellSize")return search.Shape==GameplaySearchShape.GridCells;
            }
            if(owner is GameplayEffectDefinitionAsset effect){
                if(field=="Duration"||field=="DurationValue")return effect.DurationPolicy==GameplayEffectDurationPolicy.Duration;
                if(field=="PeriodicStart")return effect.DurationPolicy!=GameplayEffectDurationPolicy.Instant&&(effect.Period>0||effect.PeriodValue!=null);
                if(field=="Period"||field=="PeriodValue"||field=="Stacking"||field=="Modifiers"||field=="GrantedTags"||field=="OngoingRequirements")return effect.DurationPolicy!=GameplayEffectDurationPolicy.Instant;
                if(field=="CompletionExecutions")return effect.DurationPolicy==GameplayEffectDurationPolicy.Duration;
            }
            if(owner is GameplayProjectileDefinitionAsset projectile){
                if(field=="StraightDestination")return projectile.Motion==GameplayProjectileMotionKind.Straight;
                // Arc bends perpendicular to the rotated launch direction; the other curves
                // sample their captured points/control points or world-up height instead.
                if(field=="DirectionAngle")return projectile.Motion==GameplayProjectileMotionKind.Straight||projectile.Motion==GameplayProjectileMotionKind.Arc;
                if(field=="CurveHeight")return projectile.Motion==GameplayProjectileMotionKind.Arc||projectile.Motion==GameplayProjectileMotionKind.Parabolic;
                if(field=="Control1Offset"||field=="Control2Offset")return projectile.Motion==GameplayProjectileMotionKind.Bezier;
            }
            if(owner is GameplayAreaDefinitionAsset area){
                if(field=="Radius")return area.Shape==GameplayAreaAuthoringShape.Circle||area.Shape==GameplayAreaAuthoringShape.Sector;
                if(field=="Angle")return area.Shape==GameplayAreaAuthoringShape.Sector;
                if(field=="Length"||field=="Width")return area.Shape==GameplayAreaAuthoringShape.Box;
                if(field=="Cells"||field=="CellSize")return area.Shape==GameplayAreaAuthoringShape.Cells;
            }
            return true;
        }
        /// <summary>Static impossibility disables a port. Environment-dependent callbacks remain
        /// connectable with a Host/context explanation, never a promise of unconditional firing.</summary>
        public static string Unavailable(object owner,string field)
        {
            if(owner is GameplayProjectileDefinitionAsset projectile&&field=="OnArrive"&&projectile.Motion==GameplayProjectileMotionKind.Straight&&projectile.StraightDestination==GameplayStraightDestinationMode.Direction)
                return "Direction 没有自然到达；改用 CapturedDestination 并提供点/目标，或连接 OnRange。原回调数据保留。";
            if(field.StartsWith("On",StringComparison.Ordinal)&&owner is GameplayAreaDefinitionAsset area&&area.TickInterval is GameplayConstantFloatValueAuthoring interval&&interval.Value<=0)
                return "Area TickInterval 必须大于 0；当前配置无法创建运行实例。";
            if(owner is GameplayEffectDefinitionAsset effect&&field=="CompletionExecutions"&&effect.DurationPolicy!=GameplayEffectDurationPolicy.Duration)
                return "仅 Duration 自然到期触发；Instant、Infinite、Cancel、Dispel 不触发。";
            if(owner is GameplayEffectStackingAuthoring stack&&field=="OverflowExecutions"&&stack.Overflow!=GameplayEffectStackOverflowPolicy.ExecuteOverflow)
                return "仅 ExecuteOverflow 消费此独立 Overflow 数组；改变策略不会删除旧配置。";
            return null;
        }
        public static string FieldUnavailable(Object source,string path)
        {
            int dot=path.LastIndexOf('.');return Unavailable(Value(source,dot<0?"":path.Substring(0,dot)),dot<0?path:path.Substring(dot+1));
        }
        public static string CallbackNote(object owner,string field)
        {
            if(!field.StartsWith("On",StringComparison.Ordinal))return null;
            if(owner is GameplayProjectileDefinitionAsset)return "需要 StandardActor、有效 cast grant 和空间 Host；保留 Target / Point / Source / Level。"+
                (field=="OnCancelled"?"仅 Cancelled / TargetLost，Duration 自然 Completed 不触发。":field=="OnArrive"?"CapturedDestination 需点或目标；HitLimit / Range / Duration 可先终止。":field=="OnRange"?"达到 MaxRange，与 Duration 自然完成不同。":"逐段 Swept Query；MaxHits 可令弹体提前结束。");
            if(owner is GameplayAreaDefinitionAsset)return "需要有效 Actor / grant / 空间 Host；Physics2D 仅 XY。"+
                (field=="OnExit"?"先释放本区域 Aura lease，再执行 OnExit；无需伪造 RemoveEffect 连线。":field=="OnTick"?"正 TickInterval；TickOnEnter 可在入场当刻触发。":field=="OnStay"?"仅 activeDelta > 0 时逐步执行。":field=="OnEnd"?"结束时释放全部 Inside/Aura，再运行 OnEnd。":"进入区域时派发，Aura 必须是持久 Effect。");
            return null;
        }
        public static void Draw(GameplayAuthoringLocation node,string scope)
        {
            if(node.Path.Length==0&&GameplayAnimationSourceInspectors.For(node.Source)!=null){using(var source=new SerializedObject(node.Source))GameplayAnimationSourceInspectors.DrawShared(source);return;}
            var nodeValue=Value(node.Source,node.Path);
            var inactive=new List<(Object source,string path)>();
            using(GameplayAuthoringInspector.UseScope(scope))foreach(var group in Describe(node,scope)){
                if(group.Advanced){string advanced=node.Handle.Key+".extension";bool open=EditorGUILayout.Foldout(OpenInactive.Contains(advanced),group.Title,true);if(open)OpenInactive.Add(advanced);else OpenInactive.Remove(advanced);if(!open)continue;}
                GUILayout.Space(5);GUILayout.Label(group.Title,EditorStyles.boldLabel);
                if(!string.IsNullOrEmpty(group.Note))EditorGUILayout.LabelField(group.Note,EditorStyles.wordWrappedMiniLabel);
                using(var so=new SerializedObject(group.Source)){
                    foreach(string path in group.Paths){
                        // A provider can serve both logical and Unity-prefab subclasses. Only
                        // fields actually present on this source belong to its Inspector.
                        var p=so.FindProperty(path);if(p==null)continue;
                        int dot=path.LastIndexOf('.');var owner=Value(group.Source,dot<0?"":path.Substring(0,dot));
                        if(!Visible(owner,p.name)){inactive.Add((group.Source,path));continue;}
                        if(owner is GameplayAbilityDefinitionAsset&&p.name=="Charge"){
                            bool enabled=p.managedReferenceValue!=null;
                            bool next=EditorGUILayout.Toggle("使用充能（与普通冷却互斥）",enabled);
                            if(next!=enabled){p.managedReferenceValue=next?new GameplayAbilityChargeAuthoring():null;if(next)so.FindProperty("CooldownEffect").objectReferenceValue=null;}
                            if(!next)continue;
                        }
                        string disabled=Unavailable(owner,p.name),note=CallbackNote(owner,p.name);
                        using(new EditorGUI.DisabledScope(disabled!=null||group.ReadOnly.Contains(path))){
                            if(owner is GameplayEndAbilityActionAuthoring&&p.name=="Cancel")
                                p.boolValue=EditorGUILayout.Popup("结束类型",p.boolValue?1:0,new[]{"正常结束","取消结束"})==1;
                            else if(group.Choices.TryGetValue(path,out var choices))p.intValue=EditorGUILayout.IntPopup(GameplayAuthoringInspector.Label(p),p.intValue,choices.Select(c=>new GUIContent(c)).ToArray(),Enumerable.Range(0,choices.Length).ToArray());
                            else GameplayAuthoringInspector.Field(p,group.Labels.TryGetValue(path,out var label)?new GUIContent(label):GameplayAuthoringInspector.Label(p));
                        }
                        if(disabled!=null)EditorGUILayout.HelpBox(disabled,MessageType.Warning);
                        else if(note!=null)EditorGUILayout.LabelField(note,EditorStyles.wordWrappedMiniLabel);
                    }
                    if(so.ApplyModifiedProperties())foreach(var path in group.Paths)GameplayAuthoringSourceChanges.Notify(group.Source,path);
                }
            }
            if(inactive.Count>0){
                string key=node.Handle.Key;bool open=EditorGUILayout.Foldout(OpenInactive.Contains(key),"未启用配置 · "+inactive.Count+" 项（保留原值）",true);
                if(open)OpenInactive.Add(key);else OpenInactive.Remove(key);
                if(open)foreach(var item in inactive)using(var so=new SerializedObject(item.source)){
                    GameplayAuthoringInspector.Field(so.FindProperty(item.path));
                    if(so.ApplyModifiedProperties())GameplayAuthoringSourceChanges.Notify(item.source,item.path);
                }
            }
        }
    }
}
