using System;
using FRFrame.Gameplay.Standard;

namespace FRFrame.Gameplay.Authoring.Graph
{
    /// <summary>Static reachability of existing runtime callbacks. Editor compatibility and
    /// compiler validation share it; it does not simulate spatial queries or infer a host.</summary>
    public static class GameplayGraphPortPolicy
    {
        public static string Unavailable(object payload,string port)
        {
            if(string.IsNullOrEmpty(port))return null;
            if(payload is GameplayProjectileDefinitionData projectile && port=="OnArrive" &&
               projectile.Motion==GameplayProjectileMotionKind.Straight && projectile.StraightDestination==GameplayStraightDestinationMode.Direction)
                return "方向直射不会到达固定终点；请使用射程结束，或改为固定终点模式后连接到达。";
            if(payload is GameplayAreaDefinitionData area&&port=="OnTick"&&area.TickInterval is GameplayConstantFloatValueAuthoring tick&&tick.Value<=0)
                return "区域周期必须大于零；持续停留是每步回调，不等同于周期。";
            if(payload is GameplayEffectDefinitionData effect){
                if((port==GameplayGraphEffectCallbacks.Completion||port.StartsWith("Completion/",StringComparison.Ordinal))&&effect.DurationPolicy!=GameplayEffectDurationPolicy.Duration)
                    return "自然完成仅在 Duration 自然到期时执行；先断开此分支再切换策略。";
                if((port==GameplayGraphEffectCallbacks.Overflow||port.StartsWith("Overflow/",StringComparison.Ordinal))&&effect.Stacking?.Overflow!=GameplayEffectStackOverflowPolicy.ExecuteOverflow)
                    return "溢出分支仅由 ExecuteOverflow 策略消费。";
            }
            return null;
        }
        public static string Unavailable(GameplayAuthoringGraph graph,string owner,string port)
        {
            var local=graph.LocalDefinitions.Find(d=>d.Id==owner);
            return Unavailable(local?.Payload,port);
        }
    }
}
