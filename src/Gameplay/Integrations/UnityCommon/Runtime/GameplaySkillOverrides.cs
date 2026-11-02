using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Standard;

namespace FRFrame.Gameplay.Unity
{
    /// <summary>Game-side temporary Workbench installations. Disposal restores configuration, level and
    /// passive engagement, not the interrupted cast, cooldown, charges or elapsed effect state.</summary>
    public class GameplaySkillOverrides
    {
        private readonly Dictionary<(GameplayStandardActor actor,int id),Slot> _slots=new Dictionary<(GameplayStandardActor,int),Slot>();

        /// <summary>Build and validate the source before calling. A stale lease cannot remove a newer
        /// installation; a failed replacement reinstalls the previously engaged configuration.</summary>
        public Lease Install(GameplayStandardActor actor,GameplaySkillDocumentDefinition definition,int level)
        {
            if(actor==null||actor.IsDisposed)throw new ArgumentException("An active scene actor is required.");
            if(definition==null)throw new ArgumentNullException(nameof(definition));
            if(level<1||level>definition.Ability.MaxLevel)throw new ArgumentOutOfRangeException(nameof(level));
            var key=(actor,definition.SkillId);
            if(!_slots.TryGetValue(key,out var slot)||!slot.OwnsCurrent){
                slot=new Slot(actor,definition.SkillId,()=>{if(_slots.TryGetValue(key,out var current)&&ReferenceEquals(current,slot))_slots.Remove(key);});_slots[key]=slot;
            }
            return slot.Install(definition,level);
        }

        /// <summary>The current installation of this lease, including restoration after a nested override.
        /// Suspended leases expose a disposed skill and cannot cast until restored.</summary>
        public sealed class Lease:IDisposable
        {
            private readonly Slot _slot;
            internal Snapshot Saved;
            internal bool Released;
            public GameplayInstalledSkill Skill {get;internal set;}
            internal Lease(Slot slot,Snapshot saved){_slot=slot;Saved=saved;}
            public void Dispose(){if(Released)return;Released=true;_slot.Release(this);}
        }

        internal sealed class Snapshot
        {
            internal GameplaySkillDocumentDefinition Definition;
            internal int Level;
            internal bool PassiveActive;
            internal static Snapshot Capture(GameplayInstalledSkill skill)=>skill==null?null:new Snapshot{
                Definition=skill.Definition,
                Level=skill.Actor.Abilities.GetGrantedEntriesSnapshot().Single(s=>s.Handle==skill.Grant).Level,
                PassiveActive=skill.Actor.Abilities.IsActive(skill.Grant)};
            internal GameplayInstalledSkill Restore(GameplayStandardActor actor)=>actor.InstallDocument(Definition,Level,PassiveActive);
        }
        internal sealed class Slot
        {
            private readonly GameplayStandardActor _actor;
            private readonly int _id;
            private readonly Action _forget;
            private readonly Snapshot _baseline;
            private readonly List<Lease> _leases=new List<Lease>();
            private GameplayInstalledSkill Current=>_actor.InstalledSkills.FirstOrDefault(s=>s.Definition.SkillId==_id);
            internal bool OwnsCurrent=>_leases.Count==0||ReferenceEquals(Current,_leases[_leases.Count-1].Skill);
            internal Slot(GameplayStandardActor actor,int id,Action forget){_actor=actor;_id=id;_forget=forget;_baseline=Snapshot.Capture(Current);}
            internal Lease Install(GameplaySkillDocumentDefinition definition,int level)
            {
                var previous=Snapshot.Capture(Current);
                if(_leases.Count>0)_leases[_leases.Count-1].Saved=previous;
                Current?.Dispose();
                var lease=new Lease(this,new Snapshot{Definition=definition,Level=level,PassiveActive=true});
                try{lease.Skill=lease.Saved.Restore(_actor);}
                catch{
                    if(previous!=null){var restored=previous.Restore(_actor);if(_leases.Count>0)_leases[_leases.Count-1].Skill=restored;}
                    if(_leases.Count==0)_forget();
                    throw;
                }
                _leases.Add(lease);return lease;
            }
            internal void Release(Lease lease)
            {
                if(_leases.Count==0||!ReferenceEquals(_leases[_leases.Count-1],lease))return;
                bool owns=OwnsCurrent;
                while(_leases.Count>0&&_leases[_leases.Count-1].Released)_leases.RemoveAt(_leases.Count-1);
                if(owns&&!_actor.IsDisposed){
                    lease.Skill.Dispose();
                    var saved=_leases.Count>0?_leases[_leases.Count-1].Saved:_baseline;
                    if(saved!=null){var restored=saved.Restore(_actor);if(_leases.Count>0)_leases[_leases.Count-1].Skill=restored;}
                }
                if(_leases.Count==0)_forget();
            }
        }
    }
}
