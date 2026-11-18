using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Authoring
{
    [CreateAssetMenu(menuName = "FRFrame Gameplay/角色战斗配置")]
    public sealed class GameplayActorProfileAsset : GameplayDefinitionAsset
    {
        [InspectorName("配置模式")] public GameplayActorProfileMode Mode;
        public GameplayCombatMappingAuthoring Combat = new GameplayCombatMappingAuthoring();
        public GameplayProfileSkillAuthoring[] StartingSkills=Array.Empty<GameplayProfileSkillAuthoring>();
        public GameplayInitialAttributeAuthoring[] Attributes = {
            new GameplayInitialAttributeAuthoring { Attribute = new GameplayAttributeReference("Health"), Value = 1000 },
            new GameplayInitialAttributeAuthoring { Attribute = new GameplayAttributeReference("MaxHealth"), Value = 1000 },
            new GameplayInitialAttributeAuthoring { Attribute = new GameplayAttributeReference("Attack"), Value = 100 },
            new GameplayInitialAttributeAuthoring { Attribute = new GameplayAttributeReference("Defense"), Value = 0 },
            new GameplayInitialAttributeAuthoring { Attribute = new GameplayAttributeReference("Shield"), Value = 0 },
            new GameplayInitialAttributeAuthoring { Attribute = new GameplayAttributeReference("Mana"), Value = 100 } };
        public override object BuildSharedDefinition(GameplayAuthoringBuildContext context) => Build(context);
        protected override void ValidateContent(GameplayAuthoringValidationContext v, string p)
        {
            if (Mode == GameplayActorProfileMode.Combat && Combat == null) { v.Error(p + ".Combat", "Missing combat mapping."); return; }
            if (Attributes == null) { v.Error(p + ".Attributes", "Missing initial attributes."); return; }
            for (int i = 0; i < Attributes.Length; i++)
                if (Attributes[i] == null) v.Error(p + $".Attributes[{i}]", "Missing attribute.");
                else { v.Attribute(Attributes[i].Attribute, p + $".Attributes[{i}].Attribute"); v.Finite(Attributes[i].Value, p + $".Attributes[{i}].Value"); }
            if(StartingSkills==null)v.Error(p+".StartingSkills","Missing starting skills.");
            else {var ids=new HashSet<int>();for(int i=0;i<StartingSkills.Length;i++){
                var entry=StartingSkills[i];if(entry==null){v.Error(p,"Missing profile skill.");continue;}
                v.Asset(entry.Skill,p+".StartingSkills["+i+"].Skill");
                if(entry.Skill!=null&&(!ids.Add(entry.Skill.SkillId)||entry.Level<1||entry.Level>entry.Skill.EffectiveMaxLevel))v.Error(p,"角色技能 ID 必须唯一且等级在有效范围内。");
            }}
            try { CreateProfile(); } catch (ArgumentException ex) { v.Error(p, ex.Message); }
        }
        private GameplayActorProfile CreateProfile(IEnumerable<(GameplaySkillDocumentDefinition skill, int level)> skills = null)
        {
            var values = new List<KeyValuePair<GameplayAttributeId, float>>();
            foreach (var value in Attributes) { if (value == null) throw new ArgumentException("Missing initial attribute."); values.Add(new KeyValuePair<GameplayAttributeId, float>(value.Attribute.Build(), value.Value)); }
            return new GameplayActorProfile(values, Mode, Mode == GameplayActorProfileMode.Combat ? Combat.Build() : null, skills);
        }
        public GameplayActorProfile Build(GameplayAuthoringBuildContext context) => context.Build(this,()=>{
            var skills=new List<(GameplaySkillDocumentDefinition skill,int level)>();foreach(var entry in StartingSkills)skills.Add((entry.Skill.Build(context),entry.Level));
            return CreateProfile(skills);
        });
    }
}
