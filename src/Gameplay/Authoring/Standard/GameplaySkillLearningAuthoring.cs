using System;
using FRFrame.Gameplay.Standard;

namespace FRFrame.Gameplay.Authoring
{
    /// <summary>Persistent/build data requirements, evaluated only while learning/upgrading.
    /// These are separate from activation conditions and never become active GameplayEffects.</summary>
    [Serializable]
    public abstract class GameplaySkillLearningRequirementAuthoring
    {
        public string FailureReason="学习前置未满足";
        public abstract void Validate(GameplayAuthoringValidationContext validation,string path);
        public abstract GameplaySkillLearningRequirement Build();
    }
    [Serializable]
    public sealed class GameplayLearningScalarRequirementAuthoring:GameplaySkillLearningRequirementAuthoring
    {
        public GameplayTagReference Key;
        public float Minimum=1,PerLevel;
        public override void Validate(GameplayAuthoringValidationContext v,string p){v.Tag(Key,p+".Key");v.Finite(Minimum,p+".Minimum");v.Finite(PerLevel,p+".PerLevel");}
        public override GameplaySkillLearningRequirement Build()=>new GameplayLearningScalarRequirement(Key.Build(),Minimum,PerLevel,FailureReason);
    }
    [Serializable]
    public sealed class GameplayLearningSkillRequirementAuthoring:GameplaySkillLearningRequirementAuthoring
    {
        public int SkillId,Level=1;
        public override void Validate(GameplayAuthoringValidationContext v,string p){if(SkillId<=0||Level<=0)v.Error(p,"前置技能 ID 和等级必须为正数。");}
        public override GameplaySkillLearningRequirement Build()=>new GameplayLearningSkillRequirement(SkillId,Level,FailureReason);
    }
    [Serializable]
    public sealed class GameplayProfileSkillAuthoring
    {public GameplaySkillDocumentAsset Skill;public int Level=1;}
}
