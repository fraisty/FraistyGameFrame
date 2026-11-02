using System;
using System.Linq;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FRFrame.Gameplay.Editor
{
    /// <summary>Native field transactions for shared definitions and reference pickers.
    /// Formal behavior mutations belong to GameplaySkillAuthoringCommands.</summary>
    internal static class GameplaySharedAuthoringCommands
    {
        internal static string ActionArray(string path)
        {
            int at=(path??"").LastIndexOf(".Array.data[",StringComparison.Ordinal);
            return at<0?null:path.Substring(0,at);
        }
        internal static void Transaction(string label,Object[] sources,Action edit)
        {
            sources=sources.Where(s=>s).Distinct().ToArray();
            var before=sources.ToDictionary(s=>s,EditorJsonUtility.ToJson);
            Undo.IncrementCurrentGroup();int group=Undo.GetCurrentGroup();
            Undo.SetCurrentGroupName(label);Undo.RecordObjects(sources,label);
            try {
                edit();
                foreach(var source in sources)if(before[source]!=EditorJsonUtility.ToJson(source)){
                    EditorUtility.SetDirty(source);GameplayAuthoringSourceChanges.Notify(source);
                }
                Undo.FlushUndoRecordObjects();Undo.CollapseUndoOperations(group);
            } catch {Undo.RevertAllDownToGroup(group);throw;}
        }
        /// <summary>Unlinks one native array entry. Referenced shared assets are never deleted.</summary>
        internal static void Remove(Object source,string path)
        {
            string arrayPath=ActionArray(path);
            if(arrayPath==null||!path.EndsWith("]",StringComparison.Ordinal))
                throw new InvalidOperationException("请选择一个有效的数组条目。");
            int index=int.Parse(path.Substring(arrayPath.Length+12).TrimEnd(']'));
            Transaction("删除共享配置条目",new[]{source},()=>{
                using(var serialized=new SerializedObject(source)){
                    var array=serialized.FindProperty(arrayPath);
                    if(array==null||index<0||index>=array.arraySize)throw new InvalidOperationException("原条目已改变。");
                    int size=array.arraySize;array.DeleteArrayElementAtIndex(index);
                    if(array.arraySize==size)array.DeleteArrayElementAtIndex(index);
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
            });
        }
    }
}
