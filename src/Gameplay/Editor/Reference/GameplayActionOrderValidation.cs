using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using UnityEditor;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>A native shared-definition diagnostic location; contains no editable graph state.</summary>
    internal sealed class GameplayWorkbenchDiagnostic
    {
        internal UnityEngine.Object Source;
        internal string Path,Message;
        internal GameplayAuthoringIssueSeverity Severity;
    }
    /// <summary>Read-only checks against the actual consumer's lifetime guard. End/Cancel ends
    /// an Ability, not every Runner. Unknown/unowned callers receive conditional guidance.</summary>
    internal static class GameplayActionOrderValidation
    {
        private static readonly Dictionary<UnityEngine.Object,GameplayDefinitionAssetReference[]> References=new Dictionary<UnityEngine.Object,GameplayDefinitionAssetReference[]>();
        private static bool _referencesReady;
        private static readonly Unity.Profiling.ProfilerMarker GuardSample=new Unity.Profiling.ProfilerMarker("FRFrame.Workbench.GuardedArray");
        internal static void InvalidateReferences()=>_referencesReady=false;
        internal static void SourceChanged(UnityEngine.Object source)
        {if(_referencesReady&&source is GameplayDefinitionAsset definition)References[source]=SequenceReferences(definition);}
        static GameplayActionOrderValidation()
        {
            EditorApplication.projectChanged+=()=>_referencesReady=false;
            Undo.undoRedoPerformed+=()=>_referencesReady=false;
            GameplayAuthoringSourceChanges.Changed+=changes=>{
                if(!_referencesReady)return;
                foreach(var source in changes.Select(c=>c.Source).OfType<GameplayDefinitionAsset>().Distinct())
                    References[source]=SequenceReferences(source);
            };
        }
        private static GameplayDefinitionAssetReference[] Callers(GameplaySequenceDefinitionAsset sequence)
        {
            if(!_referencesReady){
                References.Clear();
                var sources=AssetDatabase.FindAssets("t:"+nameof(GameplayDefinitionAsset),new[]{"Assets"}).Select(AssetDatabase.GUIDToAssetPath).Distinct().SelectMany(AssetDatabase.LoadAllAssetsAtPath).OfType<GameplayDefinitionAsset>()
                    .Concat(UnityEngine.Resources.FindObjectsOfTypeAll<GameplayDefinitionAsset>().Where(s=>!EditorUtility.IsPersistent(s)||EditorUtility.IsDirty(s))).Distinct();
                foreach(var source in sources)References[source]=SequenceReferences(source);
                _referencesReady=true;
            }
            return References.Where(p=>p.Key).SelectMany(p=>p.Value).Where(r=>r.Target==sequence&&StillReferences(r,sequence)).ToArray();
        }
        // This index answers only "who consumes this Sequence?". Recursively scanning every
        // root repeats shared closures and resolving unrelated Prefab/Skeleton/Material fields
        // can load their resources. Filter the serialized slot before resolving its value.
        private static GameplayDefinitionAssetReference[] SequenceReferences(GameplayDefinitionAsset source)
        {
            var result=new List<GameplayDefinitionAssetReference>();
            using(var so=new SerializedObject(source)){
                var property=so.GetIterator();
                while(property.Next(true)){
                    if(property.propertyType!=SerializedPropertyType.ObjectReference)continue;
                    string type=property.type;
                    bool sequenceSlot=type=="PPtr<$GameplaySequenceDefinitionAsset>"||type=="PPtr<$GameplayDefinitionAsset>"||type=="PPtr<$Object>"||type=="PPtr<Object>";
                    if(sequenceSlot&&property.objectReferenceValue is GameplaySequenceDefinitionAsset sequence)
                        result.Add(new GameplayDefinitionAssetReference(source,sequence,property.propertyPath));
                }
            }
            return result.ToArray();
        }
        private static bool StillReferences(GameplayDefinitionAssetReference reference,GameplaySequenceDefinitionAsset sequence)
        {using(var so=new SerializedObject(reference.Source))return so.FindProperty(reference.PropertyPath)?.objectReferenceValue==sequence;}
        internal const string AfterEnd = "本事件已有结束/取消动作；不能在它后面追加。请选择该 End 动作的『在此动作之前插入…』，或调整 End 位置。";
        internal const string EndBeforeSuccessors = "结束/取消动作必须放在当前动作组末尾；请先调整后继动作，不能让现有动作变为不可达。";
        internal const string Conditional = "End / Cancel 只结束 Ability；后续动作仍可能继续执行，取决于宿主的终止守卫。普通 Rule 允许结束后的清理动作；没有合法 Standard Grant 时 End 会被拒绝并停止当前链。";

        /// <summary>Only delayed, exclusively OwnedSequence consumers prove cancellation of
        /// this entry before its next action. StartSkill executes time-zero markers before the
        /// owning lease is attached. Shared/direct/unknown callers cannot be called unreachable.</summary>
        internal static bool GuardedArray(UnityEngine.Object source,string path)
        {
            using var sample=GuardSample.Auto();
            if(!(source is GameplaySequenceDefinitionAsset sequence)||string.IsNullOrEmpty(path)||!path.StartsWith("Markers.Array.data[",StringComparison.Ordinal))return false;
            int end=path.IndexOf(']');if(end<0)return false;
            using(var so=new SerializedObject(source)){
                var time=so.FindProperty(path.Substring(0,end+1)+".Time");
                if(time==null||time.floatValue<=0)return false;
                string root=path.Substring(0,end+1)+".Actions",cursor=path;
                // Only inline Branch/Search consumers inherit this marker's Runner guard.
                // A Move.OnArrive/OnCancelled callback has its own later lifetime, including
                // ending callbacks which deliberately permit cleanup after detachment.
                while(cursor!=root){
                    int dot=cursor.LastIndexOf('.');if(dot<0)return false;
                    string field=cursor.Substring(dot+1),parentPath=cursor.Substring(0,dot);
                    var parent=so.FindProperty(parentPath);
                    var value=parent?.propertyType==SerializedPropertyType.ManagedReference?parent.managedReferenceValue:null;
                    if(!(value is GameplaySkillBranchActionAuthoring&&(field=="TrueActions"||field=="FalseActions")||
                         value is GameplaySearchTargetActionAuthoring&&(field=="EachTarget"||field=="Completed"||field=="NoTargets")))return false;
                    cursor=GameplaySharedAuthoringCommands.ActionArray(parentPath);if(cursor==null)return false;
                }
            }
            // Include subassets and callers in other files; looking only at the local workspace
            // would incorrectly reject a shared sequence also used by an unowned PlaySequence.
            // Unity includes derived definitions in this single base-type query. Avoid the
            // catalog scanner's per-concrete-type searches on every Inspector repaint.
            var callers=Callers(sequence);
            return callers.Length>0&&callers.All(r=>{
                int dot=r.PropertyPath.LastIndexOf('.');if(dot<0)return false;
                using(var so=new SerializedObject(r.Source)){
                    var parent=so.FindProperty(r.PropertyPath.Substring(0,dot));
                    return parent?.propertyType==SerializedPropertyType.ManagedReference&&parent.managedReferenceValue is GameplayOwnedSequenceActionAuthoring;
                }
            });
        }

        internal static string PlacementReason(UnityEngine.Object source,string path,int at,object value,int removeAt=-1)
        {
            if(value!=null&&!(value is GameplaySkillActionAuthoring))return null;
            using(var so=new SerializedObject(source)){
                var array=so.FindProperty(path);
                if(array==null||!array.isArray)return "动作归属已改变，请重新选择。";
                var values=new List<object>();
                for(int i=0;i<array.arraySize;i++){
                    var item=array.GetArrayElementAtIndex(i);
                    if(item.propertyType!=SerializedPropertyType.ManagedReference)return null;
                    values.Add(item.managedReferenceValue);
                }
                if(removeAt>=0){values.RemoveAt(removeAt);if(removeAt<at)at--;}
                at=Math.Min(at,values.Count);
                bool after=values.Take(at).Any(a=>a is GameplayEndAbilityActionAuthoring);
                bool cuts=value is GameplayEndAbilityActionAuthoring&&at<values.Count;
                if(!after&&!cuts||!GuardedArray(source,path))return null;
                return after?AfterEnd:EndBeforeSuccessors;
            }
        }
        internal static string UnreachableReason(UnityEngine.Object source,string path)
        {
            var issue=OrderDiagnostic(source,path);
            return issue?.Severity==GameplayAuthoringIssueSeverity.Error?issue.Message:null;
        }
        internal static string ContinuationWarning(UnityEngine.Object source,string path)
        {
            var issue=OrderDiagnostic(source,path);
            return issue?.Severity==GameplayAuthoringIssueSeverity.Warning?issue.Message:null;
        }
        private static GameplayWorkbenchDiagnostic OrderDiagnostic(UnityEngine.Object source,string path)
        {
            using(var so=new SerializedObject(source))
                for(string cursor=path;!string.IsNullOrEmpty(cursor);){
                    int split=cursor.LastIndexOf(".Array.data[",StringComparison.Ordinal);
                    if(split<0)break;
                    string arrayPath=cursor.Substring(0,split);
                    int end=cursor.IndexOf(']',split);
                    if(end<0||!int.TryParse(cursor.Substring(split+12,end-split-12),out int at))break;
                    var array=so.FindProperty(arrayPath);
                    if(array!=null&&array.isArray)for(int i=0;i<Math.Min(at,array.arraySize);i++){
                        var item=array.GetArrayElementAtIndex(i);
                        if(item.propertyType==SerializedPropertyType.ManagedReference&&item.managedReferenceValue is GameplayEndAbilityActionAuthoring){
                            bool guarded=GuardedArray(source,arrayPath);
                            string location=source.name+" / "+path+" 位于 "+item.propertyPath+" 的结束/取消之后。";
                            return new GameplayWorkbenchDiagnostic{Source=source,Path=path,Severity=guarded?GameplayAuthoringIssueSeverity.Error:GameplayAuthoringIssueSeverity.Warning,
                                Message=guarded?"不可达动作（已知延时 OwnedSequence 守卫）："+location+"请将此动作移到 End 之前，或删除多余动作；不会自动调整配置。":"条件提示："+location+Conditional};
                        }
                    }
                    cursor=arrayPath;
                }
            return null;
        }
        internal static IReadOnlyList<GameplayWorkbenchDiagnostic> Scan(IEnumerable<UnityEngine.Object> sources)
        {
            var issues=new List<GameplayWorkbenchDiagnostic>();
            foreach(var source in sources.Where(s=>s!=null).Distinct())using(var so=new SerializedObject(source)){
                var item=so.GetIterator();
                while(item.Next(true))if(item.propertyType==SerializedPropertyType.ManagedReference&&item.managedReferenceValue is GameplaySkillActionAuthoring){
                    var issue=OrderDiagnostic(source,item.propertyPath);
                    if(issue!=null)issues.Add(issue);
                }
            }
            return issues;
        }
    }
}
