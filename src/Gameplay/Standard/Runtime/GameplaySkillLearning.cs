using System;
using System.Collections.Generic;

namespace FRFrame.Gameplay.Standard
{
    /// <summary>Pre-battle learning prerequisite. Values come from an explicit player/build
    /// snapshot; no inventory, UI or active battle container is queried.</summary>
    public abstract class GameplaySkillLearningRequirement
    {
        public string Reason {get;}
        protected GameplaySkillLearningRequirement(string reason){Reason=reason??"学习前置未满足";}
        public abstract bool IsMet(int targetLevel,GameplayLearnedSkillSet learned,GameplayContextValues values);
    }
    public sealed class GameplayLearningScalarRequirement:GameplaySkillLearningRequirement
    {
        public GameplayTag Key {get;}
        public float Minimum {get;}
        public float PerLevel {get;}
        public GameplayLearningScalarRequirement(GameplayTag key,float minimum,float perLevel=0,string reason=null):base(reason)
        {if(!key.IsValid||!float.IsFinite(minimum)||!float.IsFinite(perLevel))throw new ArgumentException("学习数值需要合法 Key 和有限阈值。");Key=key;Minimum=minimum;PerLevel=perLevel;}
        public override bool IsMet(int targetLevel,GameplayLearnedSkillSet learned,GameplayContextValues values)
        {return values.TryGetValue(Key,out float value)&&value>=Minimum+PerLevel*(targetLevel-1);}
    }
    public sealed class GameplayLearningSkillRequirement:GameplaySkillLearningRequirement
    {
        public int SkillId {get;}
        public int Level {get;}
        public GameplayLearningSkillRequirement(int skillId,int level=1,string reason=null):base(reason)
        {if(skillId<=0||level<=0)throw new ArgumentOutOfRangeException(nameof(skillId));SkillId=skillId;Level=level;}
        public override bool IsMet(int targetLevel,GameplayLearnedSkillSet learned,GameplayContextValues values)=>learned.LevelOf(SkillId)>=Level;
    }
    /// <summary>Save-compatible plain grant data. No Definition, runtime handle, cooldown or
    /// active effect is persisted. Resolve SkillId against the current definitions when loading.</summary>
    [Serializable]
    public sealed class GameplayLearnedSkillEntry
    {
        public int SkillId;
        public int Level;
        public GameplayLearnedSkillEntry(){}
        public GameplayLearnedSkillEntry(int id,int level){SkillId=id;Level=level;}
    }
    /// <summary>Persistent learned levels separate from battle grants. Learning/upgrading checks
    /// every prerequisite before mutation; trusted saves restore atomically after full validation.</summary>
    public sealed class GameplayLearnedSkillSet
    {
        private Dictionary<int,int> _levels=new Dictionary<int,int>();
        public int LevelOf(int id)=>_levels.TryGetValue(id,out int level)?level:0;
        public bool TryLearnOrUpgrade(GameplaySkillDocumentDefinition skill,GameplayContextValues values,out string reason)
        {
            if(!CanLearnOrUpgrade(skill,values,out reason))return false;
            _levels[skill.SkillId]=LevelOf(skill.SkillId)+1;return true;
        }
        /// <summary>Read-only preview of the same gate used by commit; checking eligibility cannot upgrade.</summary>
        public bool CanLearnOrUpgrade(GameplaySkillDocumentDefinition skill,GameplayContextValues values,out string reason)
        {
            if(skill==null)throw new ArgumentNullException(nameof(skill));
            int next=LevelOf(skill.SkillId)+1;
            if(next>skill.Ability.MaxLevel){reason="技能已满级";return false;}
            foreach(var prerequisite in skill.LearningRequirements)
                if(!prerequisite.IsMet(next,this,values??GameplayContextValues.Empty)){reason=prerequisite.Reason;return false;}
            reason=null;return true;
        }
        public GameplayLearnedSkillEntry[] Snapshot()
        {
            var ids=new List<int>(_levels.Keys);ids.Sort();var result=new GameplayLearnedSkillEntry[ids.Count];
            for(int i=0;i<ids.Count;i++)result[i]=new GameplayLearnedSkillEntry(ids[i],_levels[ids[i]]);return result;
        }
        /// <summary>Restores trusted progression, without re-paying learning requirements.
        /// Unknown/duplicate IDs and invalid levels reject the entire snapshot.</summary>
        public void Restore(IEnumerable<GameplayLearnedSkillEntry> entries,Func<int,GameplaySkillDocumentDefinition> resolve)
        {
            if(entries==null||resolve==null)throw new ArgumentNullException();var copy=new Dictionary<int,int>();
            foreach(var entry in entries){
                if(entry==null||entry.SkillId<=0||entry.Level<=0||copy.ContainsKey(entry.SkillId))throw new ArgumentException("学习快照含无效或重复技能。");
                var definition=resolve(entry.SkillId);
                if(definition==null||definition.SkillId!=entry.SkillId||entry.Level>definition.Ability.MaxLevel)throw new ArgumentException("学习快照无法匹配当前技能定义/等级。");
                copy.Add(entry.SkillId,entry.Level);
            }
            _levels=copy;
        }
        /// <summary>Installs resolved learned levels onto a new battle actor, reusing its actual
        /// Ability container. Failure removes only grants installed by this call.</summary>
        public IReadOnlyList<GameplayInstalledSkill> Install(GameplayStandardActor actor,Func<int,GameplaySkillDocumentDefinition> resolve)
        {
            if(actor==null||resolve==null)throw new ArgumentNullException();
            var definitions=new List<(GameplaySkillDocumentDefinition skill,int level)>();
            foreach(var entry in Snapshot()){
                var skill=resolve(entry.SkillId);
                if(skill==null||skill.SkillId!=entry.SkillId||entry.Level>skill.Ability.MaxLevel)throw new ArgumentException("学习技能无法解析。");
                foreach(var existing in actor.InstalledSkills)if(existing.Definition.SkillId==entry.SkillId)throw new InvalidOperationException("角色已有该技能。");
                definitions.Add((skill,entry.Level));
            }
            var installed=new List<GameplayInstalledSkill>();
            try{foreach(var entry in definitions)installed.Add(actor.InstallDocument(entry.skill,entry.level));return installed.AsReadOnly();}
            catch{foreach(var skill in installed)skill.Dispose();throw;}
        }
    }
}
