using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Display labels only. Serialized enum indices and runtime names stay untouched.
    /// Ambiguous translations retain their English name so the reverse selection is exact.</summary>
    internal static class GameplaySkillBusinessLabels
    {
        private static readonly Dictionary<string,string> Labels=new Dictionary<string,string>{
            {"Line","线形"},{"Ray","射线"},{"Single","单体"},{"ToPrimaryTarget","指向主目标"},{"ToContextPoint","指向上下文地点"},{"AbsoluteAngle","绝对角度"},
            {"Circle","圆形"},{"Sector","扇形"},{"Box","矩形"},{"Sphere","球形"},{"Capsule","胶囊"},{"GridCells","格子集合"},{"Cells","格子集合"},
            {"Enemies","敌方"},{"Allies","友方"},{"Any","任意"},{"All","全部"},{"Self","自身"},{"None","无"},
            {"Physical","物理伤害"},{"Magical","魔法伤害"},{"Magic","魔法伤害"},{"True","真实伤害"},
            {"Owner","施法者"},{"Target","当前目标"},{"Context","调用上下文"},{"OwnerFacing","施法者朝向"},{"TargetDirection","目标方向"},
            {"PrimaryTarget","实际主目标"},{"ContextPoint","上下文地点"},{"CallbackSegment","回调起点 → 命中地点"},
            {"TargetToOwner","命中地点 → 施法者"},{"ConfirmedHit","同次伤害确认命中"},{"HealthDamage","同次伤害实扣生命"},
            {"Vfx","特效"},{"Audio","音效"},{"Body","身体"},{"Active","主动"},{"Passive","被动"},
            {"Instant","瞬时"},{"Duration","限时持续"},{"Infinite","无限持续"},
            {"Straight","直线"},{"Homing","追踪"},{"Arc","弧线"},{"Parabolic","抛物线"},{"Bezier","贝塞尔曲线"},
            {"TowardsPoint","朝目标点"},{"AwayFromPoint","远离目标点"},{"Push","击退"},{"Pull","牵引"},{"Teleport","瞬移"},
            {"World","世界位置"},{"FollowOwner","跟随施法者"},{"FollowTarget","跟随目标"},
            {"Nearest","最近"},{"Farthest","最远"},{"Random","随机"},{"Distance","按距离"},
            {"Restart","重新开始"},{"Reduce","减少"},{"Increase","增加"},{"Reset","重置"},{"Set","设置"},{"Clear","清除"},
            {"Add","加法"},{"Multiply","乘法"},{"Override","覆盖"},{"Replace","替换"},
            {"AnimatorState","Animator 状态"},{"PlayablesClip","Playables 剪辑"},
            {"AggregateByTarget","按目标"},{"AggregateBySource","按来源 ID"},{"AggregateByInstigator","按施法者"},
            {"SharedEntryLifetime","共享时长"},{"IndependentLayerLifetime","各层独立时长"},
            {"RefreshOnSuccessfulApplication","成功施加时刷新"},{"NeverRefresh","不刷新"},
            {"NeverReset","不重置周期"},{"ResetOnSuccessfulApplication","成功施加时重置周期"},
            {"AfterFirstPeriod","首次间隔后"},{"ExecuteOnApplication","应用时立即结算"},{"Immediately","立即"},
            {"Reject","拒绝新增"},{"RefreshExistingAtLimit","到上限时刷新"},{"ExecuteOverflow","执行溢出效果"},
            {"Immunity","免疫"},{"Miss","未命中"},{"Defense","防御减伤"},{"Critical","暴击"},{"Shield","护盾吸收"},{"Minimum","最小伤害"},{"Scale","倍率"}
        };
        internal static string Display(string name)=>Labels.TryGetValue(name,out var label)?label:name;
        internal static List<string> Options(SerializedProperty property)
        {
            var names=property.enumNames;var labels=names.Select(Display).ToArray();
            return labels.Select((label,i)=>labels.Count(s=>s==label)>1?label+" · "+names[i]:label).ToList();
        }
    }
}
