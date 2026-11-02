using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Debugging;
using FRFrame.Gameplay.Standard;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Detached inspection of the actual world/container state. Capture never advances,
    /// publishes events or rebuilds Definitions; reusable by game-owned hosts and the default Unity host.</summary>
    public static class GameplayStandardDebugSnapshot
    {
        public static IReadOnlyList<GameplayDebugUnitSnapshot> Units(GameplayStandardWorld world,string group="Standard")
        {
            var result=new List<GameplayDebugUnitSnapshot>();if(world==null)return result.AsReadOnly();
            var spatial=world.GetSpatialSnapshot();
            foreach(var actor in world.Actors){
                var attributes=actor.Attributes.GetEntriesSnapshot().Select(a=>a.Id+" = "+a.CurrentValue);
                var resourceIds=new HashSet<GameplayAttributeId>(actor.Attributes.GetEntriesSnapshot().Select(a=>a.Id));
                if(actor.CombatAttributes!=null){resourceIds.Add(actor.CombatAttributes.Health);resourceIds.Add(actor.CombatAttributes.MaxHealth);resourceIds.Add(actor.CombatAttributes.Shield);}
                var resources=new List<string>();foreach(var id in resourceIds.OrderBy(id=>id.ToString()))if(id.IsValid&&actor.Resources.TryGetBalance(id,out float balance))resources.Add(id+" Balance="+balance);
                var skills=actor.InstalledSkills.Select(s=>"SkillId="+s.Definition.SkillId+" Phase="+s.Status.Phase+" LastActivation="+(s.LastCastContext?.ActivationSourceId.ToString()??"None"));
                var tags=actor.Tags.GetContributionsSnapshot().Select(t=>t.ExactTag+" / SourceIds="+string.Join(",",t.SourceIds));
                var effects=actor.Effects.GetActiveEntriesSnapshot().Select(e=>"Handle="+e.Handle.LocalSequence+" SourceId="+e.SourceId+" Root="+(e.ApplicationContext?.OriginSourceId.ToString()??"None")+" Level="+(e.ApplicationContext?.AbilityLevel.ToString()??"None")+
                    " Stack="+e.StackCount+" Remaining="+(e.Remaining?.ToString("0.###")??"Infinite")+" Period="+(e.PeriodRemaining?.ToString("0.###")??"None")+" Tags="+string.Join(",",e.Definition.GrantedTags));
                var abilities=actor.Abilities.GetGrantedEntriesSnapshot().Select(a=>"Grant="+a.Handle.LocalSequence+" Level="+a.Level+" Phase="+a.Status.Phase+" CD="+a.Status.CooldownRemaining+" Charge="+a.Status.CurrentCharges+" Recharge="+a.Status.RechargeRemaining+" OwnedEffects="+a.OwnedEffectsCount+" Layers="+a.OwnedLayersCount+" Lifetimes="+a.OwnedLifetimesCount);
                var sequences=actor.Sequences.GetActiveEntriesSnapshot().Select(s=>"Handle="+s.Handle.LocalSequence+" SourceId="+s.Context.EffectiveSourceId+" Elapsed="+s.Elapsed+" Next="+s.NextMarkerIndex+" / Time="+s.NextMarkerTime);
                var operations=new List<string>();foreach(var operation in spatial)if(ReferenceEquals(operation.Owner,actor)){
                    string line=operation.GetType().Name+" SourceId="+operation.Id+" Root="+operation.Context.OriginSourceId+" Activation="+operation.Context.ActivationSourceId+" Level="+operation.Context.AbilityLevel+" Position=("+operation.Position.X+","+operation.Position.Y+","+operation.Position.Z+")";
                    if(operation is GameplayProjectile projectile)line+=" Hits="+projectile.HitCount+" Distance="+projectile.DistanceTravelled;
                    if(operation is GameplayAreaOperation area)line+=" Inside="+area.InsideCount+" Armed="+area.IsArmed+" Triggers="+area.TriggerCount;
                    operations.Add(line);
                }
                var trace=actor.SkillTrace.Snapshot().Select(t=>t.order+" "+t.record.Stage+" "+t.record.Operation+"["+t.record.ActionIndex+"] Event="+t.record.EventTag+
                    (t.record.Context==null?"":" SourceId="+t.record.Context.EffectiveSourceId+" Root="+t.record.Context.OriginSourceId+" Activation="+t.record.Context.ActivationSourceId+" Level="+t.record.Context.AbilityLevel)+
                    (t.record.Source.IsValid?" SourceAsset="+Uri.EscapeDataString(t.record.Source.DefinitionId)+" NodeId="+t.record.Source.NodeId+" SourceDocument="+Uri.EscapeDataString(t.record.Source.DocumentId??""):"")+" "+t.record.Detail);
                result.Add(new GameplayDebugUnitSnapshot(actor.Id,group+" / Actor "+actor.Id,group,new[]{
                    new GameplayDebugSection("Attributes",attributes),new GameplayDebugSection("Resources",resources),new GameplayDebugSection("Skills",skills),new GameplayDebugSection("Tags / Sources",tags),new GameplayDebugSection("Effects",effects),new GameplayDebugSection("Abilities",abilities),
                    new GameplayDebugSection("Sequences",sequences),new GameplayDebugSection("Spatial / Aura",operations),new GameplayDebugSection("Skill Trace"+(actor.SkillTrace.Enabled?"":" (disabled)"),trace),
                    new GameplayDebugSection("Combat",new[]{actor.LastCombatOutcome.HasValue?actor.LastCombatOutcome.Value.Kind+" applied="+actor.LastCombatOutcome.Value.Applied:"No committed result"})}));
            }
            return result.AsReadOnly();
        }
    }
}
