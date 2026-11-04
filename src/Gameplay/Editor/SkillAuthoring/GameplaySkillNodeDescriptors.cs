using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using UnityEditor;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Editor-only metadata for a SerializeReference skill node; it never participates in Build or Execute.</summary>
    public sealed class GameplaySkillNodeEditorDescriptor
    {
        public string DisplayName { get; }
        public string CategoryPath { get; }
        public string Summary { get; }
        public string AuthoringScope { get; }
        public string Keywords { get; }
        public bool IsSample { get; }
        public GameplayNodeVisualKind? VisualKind { get; }
        public bool IsAnimation { get; }
        /// <summary>Starts a short-lived presentation without waiting for it to finish.</summary>
        public bool IsPresentationCue { get; }

        public GameplaySkillNodeEditorDescriptor(string displayName, string categoryPath, string summary,
            string authoringScope = "FRFrame", string keywords = "", bool isSample = false, GameplayNodeVisualKind? visualKind = null, bool isAnimation = false, bool isPresentationCue = false)
        {
            DisplayName = displayName;
            CategoryPath = categoryPath;
            Summary = summary;
            AuthoringScope = authoringScope; Keywords = keywords; IsSample = isSample;
            VisualKind = visualKind; IsAnimation = isAnimation;
            IsPresentationCue = isPresentationCue;
        }
    }

    /// <summary>Game Editor assemblies can describe their own nodes without a FRFrame-to-game reference.</summary>
    public interface IGameplaySkillNodeEditorDescriptorProvider
    {
        bool Supports(Type nodeType);
        GameplaySkillNodeEditorDescriptor Describe(Type nodeType, SerializedProperty property);
    }

    /// <summary>Stable TypeCache discovery; duplicate claims are reported and resolved by provider type name.</summary>
    public static class GameplaySkillNodeDescriptors
    {
        private static readonly Dictionary<Type,GameplaySkillNodeEditorDescriptor> TypeDescriptions=new Dictionary<Type,GameplaySkillNodeEditorDescriptor>();
        /// <summary>Business creation categories shared by canvas, port and field pickers.</summary>
        public static string Category(Type type)
        {
            if(type==typeof(GameplayOwnedSequenceActionAuthoring))return "时间/时间编排";
            if(type==typeof(GameplaySearchTargetActionAuthoring))return "目标与空间/搜索";
            if(type==typeof(GameplaySpawnProjectileActionAuthoring))return "目标与空间/弹体";
            if(type==typeof(GameplaySpawnAreaActionAuthoring))return "目标与空间/持续区域";
            if(type==typeof(GameplaySpawnSummonActionAuthoring))return "目标与空间/召唤";
            if(type==typeof(GameplayMoveTargetsActionAuthoring)||type==typeof(GameplayRotateToPointActionAuthoring))return "状态与位移/移动朝向";
            if(type==typeof(GameplayChangeStateActionAuthoring))return "状态与位移/行为状态";
            if(type==typeof(GameplayEndAbilityActionAuthoring))return "控制/结束与取消";
            if(type==typeof(GameplayCooldownActionAuthoring))return "控制/运行时冷却与回充";
            if(type==typeof(GameplayCaptureValueActionAuthoring)||type==typeof(GameplaySkillSetContextValueActionAuthoring)||type==typeof(GameplayCaptureCombatRollActionAuthoring))return "数值与上下文/捕获与赋值";
            if(type==typeof(GameplaySkillBranchActionAuthoring))return "条件与触发/条件分支";
            if(type==typeof(GameplayApplyToTargetsActionAuthoring)||type==typeof(GameplayGrantTimedShieldActionAuthoring)||type==typeof(GameplayDispelEffectsActionAuthoring))return "效果/护盾与驱散";
            if(typeof(GameplayAbilityCostAuthoring).IsAssignableFrom(type)||type==typeof(GameplayAbilityChargeAuthoring))return "技能配置/消耗与冷却策略";
            if(typeof(GameplaySkillConditionAuthoring).IsAssignableFrom(type))return "条件与触发/条件";
            if(typeof(GameplayEffectExecutionAuthoring).IsAssignableFrom(type))return "效果/瞬时";
            if(type==typeof(GameplayRotateToPointActionAuthoring))return "任务 / 高级显式控制（可选）";
            var descriptor=Get(type);
            if(descriptor.IsAnimation)return "时间与表现/独立动画控制";
            if(descriptor.VisualKind==GameplayNodeVisualKind.Presentation)return "时间与表现/特效音效与飘字";
            if(typeof(GameplaySkillActionAuthoring).IsAssignableFrom(type))return descriptor.AuthoringScope=="FRFrame"?"控制/其它标准行为":descriptor.AuthoringScope+"/扩展行为";
            if(typeof(GameplaySkillTriggerAuthoring).IsAssignableFrom(type))return "条件与触发/触发事件";
            return "效果 / 高级配置";
        }
        private static readonly Type[] ProviderTypes = TypeCache.GetTypesDerivedFrom<IGameplaySkillNodeEditorDescriptorProvider>()
            .Where(t => !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null)
            .OrderBy(t => t.AssemblyQualifiedName, StringComparer.Ordinal).ToArray();

        public static GameplaySkillNodeEditorDescriptor Get(Type type, SerializedProperty property = null)
        {
            if (type == null) return new GameplaySkillNodeEditorDescriptor("缺失引用", "无效", "请检查节点引用");
            if(property==null&&TypeDescriptions.TryGetValue(type,out var cached))return cached;
            foreach (Type providerType in ProviderTypes)
            {
                try
                {
                    var provider = (IGameplaySkillNodeEditorDescriptorProvider)Activator.CreateInstance(providerType);
                    if (!provider.Supports(type)) continue;
                    var result=provider.Describe(type, property) ?? Fallback(type);if(property==null)TypeDescriptions[type]=result;return result;
                }
                catch (Exception ex) { UnityEngine.Debug.LogWarning("Skill node descriptor " + providerType.FullName + ": " + ex.Message); }
            }
            return Fallback(type);
        }

        public static IReadOnlyList<string> DuplicateClaims(Type type)
        {
            var claims = new List<string>();
            foreach (Type providerType in ProviderTypes)
                try
                {
                    if (((IGameplaySkillNodeEditorDescriptorProvider)Activator.CreateInstance(providerType)).Supports(type))
                        claims.Add(providerType.FullName);
                }
                catch (Exception ex) { claims.Add(providerType.FullName + " failed: " + ex.Message); }
            return claims.Count > 1 ? new[] { type.FullName + ": descriptor providers: " + string.Join(", ", claims) } :
                Array.Empty<string>();
        }

        /// <summary>Check every concrete projected role, including plugin Trigger, Condition and Target nodes.</summary>
        public static IReadOnlyList<string> DuplicateClaims(GameplaySkillDefinitionAsset skill)
        {
            if (skill == null) return Array.Empty<string>();
            using var so = new SerializedObject(skill);
            return GameplayAuthoringProperties.Walk(so).Where(p=>p.propertyType==SerializedPropertyType.ManagedReference).Select(p=>p.managedReferenceValue?.GetType())
                .Where(type => type != null).Distinct().SelectMany(DuplicateClaims).ToArray();
        }

        private static GameplaySkillNodeEditorDescriptor Fallback(Type type) =>
            new GameplaySkillNodeEditorDescriptor(ObjectNames.NicifyVariableName(type.Name.Replace("Authoring", "")),
                "其它 / 技术类型 / " + type.Assembly.GetName().Name, "缺少编辑器说明；请为此类型提供 descriptor。", "Other", type.FullName, true);
    }

    /// <summary>First-party descriptions stay in FRFrame; game nodes are supplied by their Editor assembly.</summary>
    public sealed class FRFrameSkillNodeDescriptorProvider : IGameplaySkillNodeEditorDescriptorProvider
    {
        public bool Supports(Type type) => type != null && type.Assembly == typeof(GameplaySkillDefinitionAsset).Assembly;

        public GameplaySkillNodeEditorDescriptor Describe(Type type, SerializedProperty property)
        {
            string name = type.Name == "GameplaySkillEventTriggerAuthoring" ? "事件触发" :
                type.Name == "GameplaySkillOwnerEventCountTriggerAuthoring" ? "事件计数触发" :
                type.Name == "GameplaySkillIntervalTriggerAuthoring" ? "定时触发" :
                type.Name == "GameplaySkillTagConditionAuthoring" ? "标签条件" :
                type.Name == "GameplaySkillAttributeConditionAuthoring" ? "属性条件" :
                type.Name == "GameplaySkillSelfTargetAuthoring" ? "自身目标" :
                type.Name == "GameplaySkillPointTargetAuthoring" ? "固定地点目标" :
                type.Name == "GameplaySkillContextTargetAuthoring" ? "保留触发目标 / 地点" :
                type.Name == "GameplaySkillApplyEffectActionAuthoring" ? "施加效果" :
                type.Name == "GameplaySkillPlaySequenceActionAuthoring" ? "播放序列" :
                type.Name == "GameplaySkillBranchActionAuthoring" ? "条件分支" :
                type.Name == "GameplaySkillContextValueConditionAuthoring" ? "上下文数值条件" :
                type.Name == "GameplaySkillSetContextValueActionAuthoring" ? "写入上下文数值" :
                type.Name == "GameplayCaptureValueActionAuthoring" ? "捕获数值快照" :
                type.Name == "GameplayDispelEffectsActionAuthoring" ? "驱散效果" :
                type.Name == "GameplaySequenceSkillStepMarkerAuthoring" ? "规则步骤" :
                type.Name == "GameplaySequenceCueMarkerAuthoring" ? "表现事件" :
                type.Name == "GameplaySequenceApplyEffectMarkerAuthoring" ? "施加效果标记" :
                type.Name == "GameplayConstantFloatValueAuthoring" ? "固定数值" :
                type.Name == "GameplaySetByCallerFloatValueAuthoring" ? "调用方数值" :
                type.Name == "GameplayContextFloatValueAuthoring" ? "上下文数值" :
                type.Name == "GameplayResourceFloatValueAuthoring" ? "实际资源余额" :
                type.Name == "GameplayLearningScalarRequirementAuthoring" ? "学习 / 玩家数值前置" :
                type.Name == "GameplayLearningSkillRequirementAuthoring" ? "学习 / 已学技能前置" :
                type.Name == "GameplayCooldownActionAuthoring" ? "调整冷却 / 恢复充能" :
                type.Name == "GameplayAttributeFloatValueAuthoring" ? "属性数值" :
                type.Name == "GameplayStackCountFloatValueAuthoring" ? "叠层数值" :
                type.Name == "GameplayBinaryFloatValueAuthoring" ? "数值运算" :
                type.Name == "GameplayAbilityLevelFloatValueAuthoring" ? "技能等级" :
                type.Name == "GameplayLevelTableFloatValueAuthoring" ? "逐级数值表" :
                type.Name == "GameplayCurveFloatValueAuthoring" ? "数值曲线（线性）" :
                type.Name == "GameplayFormulaFloatValueAuthoring" ? "数值公式" :
                type.Name == "GameplayAttributeAbilityCostAuthoring" ? "属性资源消耗" :
                type.Name == "GameplayAttributeDeltaExecutionAuthoring" ? "修改属性数值" :
                type.Name == "GameplaySearchTargetActionAuthoring" ? "搜索目标" :
                type.Name == "GameplayApplyToTargetsActionAuthoring" ? "对目标施加效果" :
                type.Name == "GameplayOwnedSequenceActionAuthoring" ? "播放时间编排" :
                type.Name == "GameplayEndAbilityActionAuthoring" ? "结束技能" :
                type.Name == "GameplaySpawnProjectileActionAuthoring" ? "发射弹体" :
                type.Name == "GameplaySpawnAreaActionAuthoring" ? "生成持续区域" :
                type.Name == "GameplaySpawnSummonActionAuthoring" ? "召唤正式单位" :
                type.Name == "GameplayMoveTargetsActionAuthoring" ? "位移 / 推拉" :
                type.Name == "GameplayRotateToPointActionAuthoring" ? "转向目标点" :
                type.Name == "GameplayChangeStateActionAuthoring" ? "切换行为状态" :
                type.Name == "GameplayGrantTimedShieldActionAuthoring" ? "给予时限护盾" :
                type.Name == "GameplayCaptureCombatRollActionAuthoring" ? "捕获暴击 / 未命中" :
                type.Name == "GameplayDamageExecutionAuthoring" ? "造成伤害" :
                type.Name == "GameplayHealExecutionAuthoring" ? "治疗" :
                type.Name == "GameplayShieldExecutionAuthoring" ? "增加护盾" :
                type.Name == "GameplayApplyEffectOnCompletionAuthoring" ? "自然完成施加效果" :
                type.Name == "GameplayApplyEffectOnOverflowAuthoring" ? "溢出施加效果" :
                type.Name == "GameplayRemoveCurrentOnOverflowAuthoring" ? "溢出移除当前效果" :
                type.Name == "GameplayEffectStackingAuthoring" ? "叠层策略" :
                type.Name == "GameplayTagQueryAuthoring" ? "标签查询" :
                type.Name == "GameplayAbilityChargeAuthoring" ? "充能策略" :
                "其它配置";
            string summary = type.Name.Contains("ApplyEffect") ? "向当前目标施加引用的正式效果配置。" :
                type == typeof(GameplaySkillPlaySequenceActionAuthoring) ? "启动正式时间编排；规则时钟由 Sequence 管理。" :
                type == typeof(GameplaySkillBranchActionAuthoring) ? "判断条件，只执行满足或否则的一支，然后继续后续动作。" :
                type == typeof(GameplaySkillSetContextValueActionAuthoring) ? "生成带新数值的不可变 Context，供本动作链后续步骤消费。" :
                type.Name == "GameplayCaptureValueActionAuthoring" ? "按实际动作/目标捕获表达式结果，后续 Context/Caller 读取该 Key。" :
                type.Name == "GameplayCurveFloatValueAuthoring" ? "严格递增控制点的线性数值曲线；范围外保持首尾值，输入使用共用数值来源。" :
                type == typeof(GameplaySkillSelfTargetAuthoring) ? "把规则持有者作为当前目标。" :
                type.Name.Contains("FloatValue") ? "配置规则数值的来源或运算；不包含可变运行状态。" :
                type.Name.Contains("Condition") ? "检查当前标签、属性或 Context 数据，决定规则是否执行。" :
                type.Name.Contains("Trigger") ? "配置真实事件或计数/间隔触发入口。" :
                type.Name.Contains("CueMarker") ? "在指定时刻发送表现事件；表现不推进规则时钟。" :
                type.Name.Contains("SkillStep") ? "在标记时刻按当前 Context 执行条件、目标和动作。" :
                name + "：配置静态策略，生命周期由已有规则容器管理。";
            if (property != null)
            {
                if (type == typeof(GameplaySkillIntervalTriggerAuthoring))
                    summary = "每 " + property.FindPropertyRelative("Interval")?.floatValue.ToString("0.##") + " 秒";
                else if (type == typeof(GameplaySkillOwnerEventCountTriggerAuthoring))
                    summary = "累计 " + property.FindPropertyRelative("RequiredCount")?.intValue + " 次事件";
                else if (type == typeof(GameplaySkillEventTriggerAuthoring))
                    summary = "事件 · " + (property.FindPropertyRelative("EventTag")?.FindPropertyRelative("Name")?.stringValue ?? "未指定");
                else if (type == typeof(GameplaySkillSelfTargetAuthoring)) summary = "以技能持有者为目标";
                else if (type == typeof(GameplaySkillApplyEffectActionAuthoring))
                    summary = "效果 · " + (property.FindPropertyRelative("Effect")?.objectReferenceValue?.name ?? "未指定");
                else if (type == typeof(GameplaySkillPlaySequenceActionAuthoring))
                    summary = "序列 · " + (property.FindPropertyRelative("Sequence")?.objectReferenceValue?.name ?? "未指定");
            }
            return new GameplaySkillNodeEditorDescriptor(name, "FRFrame", summary);
        }
    }
}
