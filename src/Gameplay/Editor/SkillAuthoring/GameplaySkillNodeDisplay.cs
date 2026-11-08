using System;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using FRFrame.Gameplay.Editor.SkillAuthoring;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Business metadata over the same payload used by the compiler. No asset search,
    /// factory execution or mutable runtime state is needed to draw a card.</summary>
    internal static class GameplaySkillNodeDisplay
    {
        internal static string Name(GameplayAuthoringNode node,GameplayAuthoringGraph graph)
        {
            var action=(node.Payload as GameplayActionNodePayload)?.Action;
            var descriptor=action==null?null:GameplaySkillNodeDescriptors.Get(action.GetType());
            if(descriptor?.IsPresentationCue==true){
                string type=GameplaySkillFieldSchema.ValueNodeTypeName(BoundValues(node,graph))??descriptor.DisplayName;
                return string.IsNullOrEmpty(node.Title)||node.Title==type||node.Title==descriptor.DisplayName?type:type+" · "+node.Title;
            }
            if(!(action is GameplayApplyToTargetsActionAuthoring)&&!(action is GameplaySkillApplyEffectActionAuthoring))return GameplaySkillFieldSchema.NodeName(node);
            var binding=node.Payload.Definitions.FirstOrDefault(b=>b.PropertyPath=="Effect");
            var effect=graph.LocalDefinitions.Find(d=>d.Id==binding?.LocalId)?.Payload as GameplayEffectDefinitionData;
            if(effect==null&&binding?.External is GameplayEffectDefinitionAsset shared)effect=(GameplayEffectDefinitionData)shared.CaptureGraphValues();
            if(effect==null)return GameplaySkillFieldSchema.NodeName(node);
            string name=effect.DurationPolicy!=GameplayEffectDurationPolicy.Instant?"持续 Buff":effect.Executions.Length==1&&effect.Modifiers.Length==0?
                effect.Executions[0] is GameplayDamageExecutionAuthoring?"造成伤害":effect.Executions[0] is GameplayHealExecutionAuthoring?"治疗":effect.Executions[0] is GameplayShieldExecutionAuthoring?"增加护盾":"施加效果":"复合效果";
            return string.IsNullOrEmpty(node.Title)||node.Title==name||node.Title==GameplaySkillNodeDescriptors.Get(action.GetType()).DisplayName?name:name+" · "+node.Title;
        }
        internal static GameplayNodeVisualKind Kind(GameplayAuthoringNode node)
        {
            if(node?.Payload is GameplayAbilityNodePayload)return GameplayNodeVisualKind.Ability;
            if(node?.Payload is GameplayRuleNodePayload)return GameplayNodeVisualKind.Condition;
            var action=(node?.Payload as GameplayActionNodePayload)?.Action;
            if(action==null)return GameplayNodeVisualKind.Neutral;
            return GameplaySkillNodeDescriptors.Get(action.GetType()).VisualKind??GameplayNodeVisuals.Classify(action);
        }
        internal static string Value(GameplayFloatValueAuthoring value)
        {
            if(value is GameplayConstantFloatValueAuthoring constant)return constant.Value.ToString("0.###");
            if(value is GameplayFormulaFloatValueAuthoring formula)return formula.Expression;
            if(value is GameplayBinaryFloatValueAuthoring binary)return binary.Operation+"("+Value(binary.Left)+", "+Value(binary.Right)+")";
            if(value is GameplayAttributeFloatValueAuthoring attribute)return attribute.Source+"."+attribute.Attribute.Value;
            if(value is GameplayResourceFloatValueAuthoring resource)return resource.Source+"Resource."+resource.Resource.Value;
            if(value is GameplayContextFloatValueAuthoring context)return "Context."+context.Key.Name;
            if(value is GameplaySetByCallerFloatValueAuthoring caller)return "Caller."+caller.Key.Name;
            if(value is GameplayAbilityLevelFloatValueAuthoring)return "Level";
            if(value is GameplayStackCountFloatValueAuthoring)return "StackCount";
            if(value is GameplayLevelTableFloatValueAuthoring table)return "等级表 ["+string.Join(", ",table.Values.Take(4).Select(v=>v.ToString("0.###")))+"] · "+table.Values.Length+" 项";
            if(value is GameplayCurveFloatValueAuthoring curve)return "曲线("+Value(curve.Input)+") ["+string.Join(", ",curve.Keys.Take(4).Select(p=>p.Input.ToString("0.###")+":"+p.Output.ToString("0.###")))+"] · "+curve.Keys.Length+" 个点";
            return value==null?"未配置":GameplaySkillNodeDescriptors.Get(value.GetType()).DisplayName;
        }
        internal static string Cost(GameplayAbilityCostAuthoring cost)
        {
            if(cost==null)return "消耗 · 无";
            if(cost is GameplayAttributeAbilityCostAuthoring resources){
                var entries=resources.Resources??Array.Empty<GameplayAttributeAbilityCostAuthoring.Entry>();
                string text=string.Join(" / ",entries.Take(2).Select(e=>e==null?"待配置":e.Delta is GameplayConstantFloatValueAuthoring constant?(constant.Value<0?"消耗 ":"获得 ")+e.Attribute.Value+" "+Math.Abs(constant.Value).ToString("0.###"):e.Attribute.Value+"变化 "+Value(e.Delta)));
                return text+(entries.Length>2?" · 另"+(entries.Length-2)+"项":"");
            }return "消耗 · "+GameplaySkillNodeDescriptors.Get(cost.GetType()).DisplayName;
        }
        internal static string Target(GameplaySkillTargetAuthoring target)=>target is GameplaySkillSelfTargetAuthoring?"自身":target is GameplaySkillPointTargetAuthoring?"指定地点":target is GameplaySkillContextTargetAuthoring?"调用者目标 / 地点":target==null?"待配置":GameplaySkillNodeDescriptors.Get(target.GetType()).DisplayName;
        // Card fields own their values. This supplementary text contains only semantics which
        // are absent from those fields; full summaries remain useful in tooltips/diagnostics.
        internal static string CardSummary(GameplayAuthoringNode node,GameplayAuthoringGraph graph,bool hasFields)
        {
            if(node.Payload is GameplayAbilityNodePayload)return null;
            var action=(node.Payload as GameplayActionNodePayload)?.Action;
            if(action is GameplayOwnedSequenceActionAuthoring||action is GameplaySkillPlaySequenceActionAuthoring)return null;
            if(action is GameplayApplyToTargetsActionAuthoring||action is GameplaySkillApplyEffectActionAuthoring){
                var binding=node.Payload.Definitions.FirstOrDefault(b=>b.PropertyPath=="Effect");
                var effect=graph.LocalDefinitions.Find(d=>d.Id==binding?.LocalId)?.Payload as GameplayEffectDefinitionData;
                if(effect==null)return null;
                string state=effect.DurationPolicy==GameplayEffectDurationPolicy.Instant?"瞬时结算":effect.DurationPolicy==GameplayEffectDurationPolicy.Infinite?"无限持续":"持续效果";
                int count=effect.Executions.Length+effect.Modifiers.Length;
                string presentation=effect.Presentation==null?null:GameplaySkillFieldSchema.ValueSummary(effect.Presentation);
                var overflow=effect.Stacking?.OverflowExecutions.Select(GameplaySkillFieldSchema.ValueSummary).Where(s=>!string.IsNullOrEmpty(s)).ToArray();
                return state+(count>1?" · 复合 "+count+" 项":"")+(!hasFields&&effect.Stacking!=null?" · 叠层上限 "+effect.Stacking.MaxStacks:"")+
                    (effect.GrantedTags.Length>0?"\n授予状态 · "+string.Join(" / ",effect.GrantedTags.Take(3).Select(t=>t.Name)):"")+
                    (presentation==null?"":"\n效果表现 · "+presentation)+
                    (overflow?.Length>0?"\n"+string.Join("\n",overflow):"");
            }
            return hasFields?null:Summary(node,graph);
        }
        internal static string SharedSummary(GameplayDefinitionAsset source)
        {
            object value=source.CaptureGraphValues();
            if(value is GameplayEffectDefinitionData effect)return Effect(effect,source.name)+" · 共享来源";
            if(value is GameplayAbilityDefinitionData ability)return source.name+" · "+Cost(ability.Cost)+(ability.Charge==null?"":" · 充能 "+ability.Charge.MaxCharges+" / "+ability.Charge.RechargeDuration+"s");
            return source.name+(GameplaySkillFieldSchema.ValueSummary(value) is string text?" · "+text:"")+" · 共享来源";
        }
        internal static string Summary(GameplayAuthoringNode node,GameplayAuthoringGraph graph)
        {
            if(node.Payload is GameplayAbilityNodePayload)return "技能施放 · 消耗 / 冷却 / 充能";
            if(node.Payload is GameplayRuleNodePayload rule)return (rule.Trigger is GameplaySkillEventTriggerAuthoring trigger?"事件 · "+trigger.EventTag.Name:"触发条件 · "+(rule.Trigger==null?"待配置":GameplaySkillNodeDescriptors.Get(rule.Trigger.GetType()).DisplayName))+"\n目标 · "+Target(rule.Target)+(rule.Conditions?.Length>0?" · 条件"+rule.Conditions.Length+"项":"");
            var action=(node.Payload as GameplayActionNodePayload)?.Action;
            if(action is GameplayChangeStateActionAuthoring state)return "切换行为状态 · "+state.State;
            if(action is GameplayCooldownActionAuthoring cooldown)return "技能 "+(cooldown.SkillId==0?"当前施放":cooldown.SkillId.ToString())+" · "+cooldown.Operation+(cooldown.Operation==Standard.GameplayCooldownOperation.Restart?"":" · "+Value(cooldown.Value));
            if(action is GameplayCaptureCombatRollActionAuthoring roll)return "暴击 "+Value(roll.CriticalChance)+" · 未命中 "+Value(roll.MissChance)+(roll.PreserveExisting?" · 保留已有采样":"");
            if(action is GameplaySkillBranchActionAuthoring branch)return "判断 · "+(branch.Condition==null?"尚未配置":GameplaySkillNodeDescriptors.Get(branch.Condition.GetType()).DisplayName)+"\n满足 / 否则使用真实分支，条件细节在右侧配置";
            if(action is GameplaySkillSetContextValueActionAuthoring set)return set.Key.Name+" = "+set.Value;
            if(action is GameplayCaptureValueActionAuthoring capture)return "捕获 "+capture.Key.Name+" · "+Value(capture.Value);
            if(action is GameplayMoveTargetsActionAuthoring move)return move.Mode+" · 距离 "+Value(move.Distance)+" · 速度 "+Value(move.Speed);
            if(action is GameplayGrantTimedShieldActionAuthoring shield)return "护盾 "+Value(shield.Amount)+" · "+Value(shield.Duration)+"s";
            if(action is GameplayRotateToPointActionAuthoring rotate)return rotate.RotateOwner?"自身朝向当前地点":"逐目标朝向当前地点";
            if(action is GameplayDispelEffectsActionAuthoring dispel)return "驱散分类 · "+string.Join(" / ",(dispel.EffectTags?.All??Array.Empty<GameplayTagReference>()).Concat(dispel.EffectTags?.Any??Array.Empty<GameplayTagReference>()).Select(t=>t.Name));
            if(action is GameplaySearchTargetActionAuthoring search)return search.Shape+" · "+search.Plane+" · "+search.Faction+"\n半径 "+Value(search.Radius)+" · 目标上限 "+search.MaxTargets;
            if(action is GameplayEndAbilityActionAuthoring end)return end.Cancel?"取消当前施放并释放所属内容":"结束当前施放并释放所属内容";
            if(action is GameplayOwnedSequenceActionAuthoring||action is GameplaySkillPlaySequenceActionAuthoring){
                var timeline=graph.Timelines.Find(t=>node.Payload.Definitions.Any(b=>b.LocalId==t.Id));
                return timeline==null?"尚未绑定编排":timeline.DurationSeconds.ToString("0.###")+" 秒 · "+timeline.Events.Count+" 个时间事件";
            }
            var summaries=node.Payload.Definitions.Select(binding=>{
                var definition=graph.LocalDefinitions.Find(d=>d.Id==binding.LocalId);object values=definition?.Payload;
                if(values is GameplayEffectDefinitionData effect)return Effect(effect,definition.Name);
                if(binding.External is GameplayEffectDefinitionAsset shared)return Effect((GameplayEffectDefinitionData)shared.CaptureGraphValues(),shared.name)+" · 共享";
                if(values is GameplayProjectileDefinitionData projectile)return projectile.Motion+" · "+projectile.Plane+"\n速度 "+Value(projectile.Speed)+" · 存续 "+Value(projectile.Duration)+"s · 命中上限 "+projectile.MaxHits;
                if(values is GameplayAreaDefinitionData area)return area.Shape+" · "+area.Plane+"\n存续 "+Value(area.Duration)+"s · 周期 "+Value(area.TickInterval)+"s";
                return values==null?(binding.External?binding.External.name+" · 共享":null):GameplaySkillFieldSchema.ValueSummary(values)??definition.Name;
            }).Where(s=>!string.IsNullOrEmpty(s)).ToArray();
            return summaries.Length==0?GameplaySkillNodeDescriptors.Get(action?.GetType()).Summary:string.Join("\n",summaries);
        }
        internal static string EventLabel(GameplayAuthoringNode node,GameplayAuthoringGraph graph)
        {
            if(node==null)return "缺失入口";
            var action=(node.Payload as GameplayActionNodePayload)?.Action;
            var descriptor=action==null?null:GameplaySkillNodeDescriptors.Get(action.GetType());
            if(descriptor?.IsPresentationCue==true){
                string summary=GameplaySkillFieldSchema.ValueSummary(BoundValues(node,graph));
                return Name(node,graph)+(summary==null?"":"\n"+summary);
            }
            if(descriptor?.IsAnimation==true){
                var value=BoundValues(node,graph);
                string summary=value==null?null:GameplaySkillFieldSchema.ValueSummary(value);return Name(node,graph)+(summary==null?"":" · "+summary)+"\n"+string.Join(" / ",node.Payload.Definitions.Select(b=>b.External?"共享 · "+b.External.name:"本地 · "+graph.LocalDefinitions.Find(d=>d.Id==b.LocalId)?.Name));
            }return Name(node,graph);
        }
        internal static object BoundValues(GameplayAuthoringNode node,GameplayAuthoringGraph graph)=>node.Payload.Definitions.Select(b=>graph.LocalDefinitions.Find(d=>d.Id==b.LocalId)?.Payload??(b.External?b.External.CaptureGraphValues():null)).FirstOrDefault(v=>v!=null);
        private static string Effect(GameplayEffectDefinitionData effect,string name)
        {
            string duration=effect.DurationPolicy==GameplayEffectDurationPolicy.Instant?"瞬时":effect.DurationPolicy==GameplayEffectDurationPolicy.Infinite?"无限持续":"持续 "+(effect.DurationValue==null?effect.Duration.ToString("0.###"):Value(effect.DurationValue))+"s";
            string execution=string.Join(" / ",effect.Executions.Select(e=>e is GameplayDamageExecutionAuthoring damage?"伤害 "+Value(damage.Amount):e is GameplayHealExecutionAuthoring heal?"治疗 "+Value(heal.Amount):e is GameplayShieldExecutionAuthoring shield?"护盾 "+Value(shield.Amount):e==null?"缺少结算":GameplaySkillNodeDescriptors.Get(e.GetType()).DisplayName));
            string modifiers=string.Join(" / ",effect.Modifiers.Take(2).Select(m=>m.Attribute.Value+" "+m.Operation+" "+Value(m.Magnitude)));
            string tags=string.Join(" / ",effect.GrantedTags.Take(3).Select(t=>t.Name));
            int count=effect.Executions.Length+effect.Modifiers.Length;
            return name+" · "+duration+(count>1?" · 复合 "+count+" 项":"")+(effect.PeriodValue!=null||effect.Period>0?" · 每 "+(effect.PeriodValue==null?effect.Period.ToString("0.###"):Value(effect.PeriodValue))+"s":"")+"\n"+execution+
                (modifiers.Length>0?"\n属性 · "+modifiers+(effect.Modifiers.Length>2?" 等 "+effect.Modifiers.Length+" 项":""):"")+
                (tags.Length>0?"\n状态 · "+tags+(effect.GrantedTags.Length>3?" 等 "+effect.GrantedTags.Length+" 项":""):"")+
                (effect.Stacking!=null?"\n叠层上限 "+effect.Stacking.MaxStacks+" · "+effect.Stacking.Overflow:"");
        }
    }
    public interface IGameplaySkillValueDescription
    {
        string Summary(object values);
        /// <summary>Optional creation-menu name when one payload type has multiple authoring presets.</summary>
        string NodeTypeName(object values)=>null;
    }
}
