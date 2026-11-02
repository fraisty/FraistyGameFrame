using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;
using FRFrame.Gameplay.Authoring;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>In-memory migration comparator over immutable runtime values, callback captures,
    /// source maps, ordering and real resource GUID/fileID. No snapshots are written to disk.</summary>
    public static class GameplaySkillDefinitionSemantics
    {
        /// <summary>Normalizes only CLR type metadata for the explicit namespace move; never business string values.</summary>
        private static string TypeName(Type type)=>type.FullName.Replace("FRFrame.Gameplay.Authoring.V2.","FRFrame.Gameplay.Authoring.Graph.");
        public static string Capture(object definition,IReadOnlyDictionary<UnityEngine.Object,string> originalResourceIdentities=null)
        {
            var seen = new Dictionary<object,int>(); var output = new StringBuilder(); int count=0;
            Action<object> visit=null;
            visit=value=>{
                if(++count>100000)throw new InvalidOperationException("Definition snapshot exceeds bound.");
                if(value==null){output.Append("null;");return;}
                if(value is UnityEngine.Object missing&&missing==null){output.Append("null;");return;}
                var type=value.GetType();
                if(value is string text){output.Append("s").Append(text.Length).Append(':').Append(text).Append(';');return;}
                if(type.IsPrimitive||type.IsEnum||value is decimal){output.Append(TypeName(type)).Append(':').Append(Convert.ToString(value,CultureInfo.InvariantCulture)).Append(';');return;}
                var unity=value as UnityEngine.Object;
                if(unity){
                    string identity;
                    if(originalResourceIdentities!=null&&originalResourceIdentities.TryGetValue(unity,out var original))identity=original;
                    else {if(!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(unity,out string guid,out long id))throw new InvalidOperationException("Nonpersistent runtime resource: "+unity.name);identity=guid+":"+id;}
                    output.Append("resource:").Append(TypeName(type)).Append(':').Append(identity).Append(';');
                    if(!(unity is GameplayDefinitionAsset))return;
                }
                if(!type.IsValueType){if(seen.TryGetValue(value,out int old)){output.Append('@').Append(old).Append(';');return;}seen.Add(value,seen.Count);}
                if(value is Delegate factory){foreach(var invocation in factory.GetInvocationList()){
                    output.Append("factory:").Append(GameplaySkillDelegateSemantics.Signature(invocation.Method)).Append(';');visit(invocation.Target);
                }return;}
                if(value is IDictionary map){output.Append("map{");foreach(DictionaryEntry entry in map){visit(entry.Key);visit(entry.Value);}output.Append("};");return;}
                if(value is IEnumerable list){output.Append("list[");foreach(var item in list)visit(item);output.Append("];");return;}
                bool author=unity is GameplayDefinitionAsset;output.Append(author?"author-data:":TypeName(type)).Append('{');
                var fields=new List<FieldInfo>();
                for(var current=type;current!=null&&current!=typeof(ScriptableObject)&&current!=typeof(UnityEngine.Object);current=current.BaseType)
                    fields.AddRange(current.GetFields(BindingFlags.Public|BindingFlags.Instance|BindingFlags.DeclaredOnly|(author?0:BindingFlags.NonPublic)));
                foreach(var field in fields.Where(f=>!f.IsStatic&&!f.IsNotSerialized&&!f.IsDefined(typeof(FRFrame.Gameplay.Authoring.Graph.GameplayAuthoringIdentityAttribute),false)).OrderBy(f=>f.Name,StringComparer.Ordinal)){output.Append(field.Name).Append('=');visit(field.GetValue(value));}
                output.Append("};");
            };
            visit(definition);
            // Factories were moved once from an SO adapter into the same plain-data builder.
            // Compiler-generated closure ordinals are not gameplay semantics. Captures remain.
            string result=Regex.Replace(output.ToString(),"(Gameplay(?:Unity|Spine|Live2D)?Animation|GameplayUnitySummon)(?:Asset|Data)","$1Values");
            result=Regex.Replace(result,@"<>c__DisplayClass\d+_\d+","<>c__DisplayClass");
            result=Regex.Replace(result,@"<>9__\d+_\d+","<>9__");
            // Both values select the same root binding in the existing Unity consumer.
            return result.Replace("<Binding>k__BackingField=null;","<Binding>k__BackingField=s0:;");
        }
        public static void RequireEqual(string before, object after, string label)
        {
            string current=Capture(after);if(before==current)return;
            int at=0;while(at<Math.Min(before.Length,current.Length)&&before[at]==current[at])at++;
            throw new InvalidOperationException(label+" runtime semantics differ at "+at+". Before: "+before.Substring(Math.Max(0,at-60),Math.Min(180,before.Length-Math.Max(0,at-60)))+"; After: "+current.Substring(Math.Max(0,at-60),Math.Min(180,current.Length-Math.Max(0,at-60))));
        }
    }
}
