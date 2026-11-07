using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using UnityEditor;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Copy-time review of typed, embedded business references. Paths address the
    /// source and its clone; external Unity objects are never traversed or modified.</summary>
    internal static class GameplaySkillCopyReferences
    {
        internal sealed class Reference
        {
            internal string Path, Description, CooldownGroup, TagSuffix;
            internal bool IsSelf, IsTag;
            internal string TargetTag(int id,string scope)=>CooldownGroup!=null
                ? GameplaySkillAuthoringCommands.GeneratedSkillTag(scope,"Cooldown.Skill.",id+TagSuffix)
                : GameplaySkillAuthoringCommands.GeneratedSkillTag(scope,"Event.Created.","S"+id);
        }

        internal static List<Reference> Audit(GameplaySkillDocumentAsset source)
        {
            var result=new List<Reference>();
            string scopedCooldown=GameplaySkillAuthoringCommands.GeneratedSkillTag(GameplayReferenceCatalogProvider.Scope(source),"Cooldown.Skill.",source.SkillId.ToString());
            var ability=(GameplayAbilityNodePayload)source.AuthoringGraph.Nodes.Single(n=>n.Id==source.AuthoringGraph.AbilityNodeId).Payload;
            var binding=ability.Definitions.SingleOrDefault(b=>b.PropertyPath=="CooldownEffect");
            var cooldown=source.AuthoringGraph.LocalDefinitions.SingleOrDefault(d=>d.Id==binding?.LocalId)?.Payload as GameplayEffectDefinitionData;
            // Binding, not a naming convention, establishes the local CD identity. Every
            // embedded use of a granted tag travels with that group, including business queries.
            var privateTags=cooldown?.GrantedTags.Select(t=>t.Name).Where(t=>!string.IsNullOrEmpty(t)).Distinct().ToList()??new List<string>();
            var sharedTags=(binding?.External as GameplayEffectDefinitionAsset)?.GrantedTags.Select(t=>t.Name).ToHashSet()??new HashSet<string>();
            using(var so=new SerializedObject(source)){
                var field=so.GetIterator();
                while(field.Next(true)){
                    if(field.propertyType==SerializedPropertyType.ManagedReference){
                        string member=null;
                        if(field.managedReferenceValue is GameplayApplyEffectOnCompletionAuthoring)member="OwnerSkillId";
                        else if(field.managedReferenceValue is GameplayCooldownActionAuthoring||field.managedReferenceValue is GameplayLearningSkillRequirementAuthoring)member="SkillId";
                        if(member!=null){
                            var id=field.FindPropertyRelative(member);
                            if(id.intValue>0)result.Add(new Reference{Path=id.propertyPath,IsSelf=id.intValue==source.SkillId,Description=member+" = "+id.intValue});
                        }
                    }
                    if(!field.isArray&&field.type==nameof(GameplayTagReference)&&field.propertyPath!="CastEvent"){
                        var tag=field.FindPropertyRelative("Name");string value=tag.stringValue;
                        // Retained, detached authoring drafts still keep the established
                        // explicit self-ID contract (e.g. 7002); arbitrary names require a binding.
                        int at=privateTags.IndexOf(value);bool cooldownTag=at>=0||value=="Cooldown.Skill."+source.SkillId||value==scopedCooldown;
                        bool own=(value==source.CastEvent.Name||cooldownTag)&&!sharedTags.Contains(value);
                        if(!string.IsNullOrEmpty(value))result.Add(new Reference{Path=tag.propertyPath,IsSelf=own,IsTag=true,CooldownGroup=own&&cooldownTag?value:null,TagSuffix=at>=0&&privateTags.Count>1?".Tag"+(at+1):"",Description="Tag = "+value+(own&&cooldownTag?"（自身冷却及其全部引用）":sharedTags.Contains(value)?"（外部冷却，共享只读）":"")});
                    }
                }
            }
            for(int i=0;i<source.Defaults.Length;i++){
                var value=source.Defaults[i];
                // Scalar keys are application contracts, not arbitrary numbers. Only an
                // explicitly named skill identity is proposed as self; other defaults stay.
                bool identity=value.Key.Name.EndsWith(".SourceSkillId")||value.Key.Name.EndsWith(".OwnerSkillId")||value.Key.Name.EndsWith(".SkillId");
                result.Add(new Reference{Path="Defaults.Array.data["+i+"].Value",IsSelf=identity&&value.Value==source.SkillId,Description=value.Key.Name+" = "+value.Value});
            }
            return result;
        }

        internal static List<string> Apply(GameplaySkillDocumentAsset copy,List<Reference> references,ICollection<string> preserve,string scope)
        {
            var tags=new List<string>();
            using(var so=new SerializedObject(copy)){
                foreach(var reference in references){
                    if(!reference.IsSelf||preserve.Contains(reference.Path))continue;
                    var field=so.FindProperty(reference.Path);
                    if(reference.IsTag){field.stringValue=reference.TargetTag(copy.SkillId,scope);tags.Add(field.stringValue);}
                    else if(field.propertyType==SerializedPropertyType.Integer)field.intValue=copy.SkillId;
                    else field.floatValue=copy.SkillId;
                }
                so.ApplyModifiedPropertiesWithoutUndo();
            }
            return tags;
        }
    }
}
