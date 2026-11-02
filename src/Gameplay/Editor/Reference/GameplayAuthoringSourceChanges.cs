using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FRFrame.Gameplay.Editor
{
    /// <summary>Editor-only invalidation, not a source cache. Only actual serialized mutations enqueue source/path pairs.
    /// Explicit writers and Unity Undo/ObjectChange callbacks converge into one deferred batch; no Repaint polling.</summary>
    [InitializeOnLoad]
    public static class GameplayAuthoringSourceChanges
    {
        public readonly struct Change
        {
            public readonly Object Source;
            public readonly string Path;
            public Change(Object source,string path) { Source=source;Path=path??""; }
        }
        private static readonly Dictionary<Object,HashSet<string>> Pending=new Dictionary<Object,HashSet<string>>();
        private static bool _scheduled;
        public static event Action<IReadOnlyList<Change>> Changed;
        static GameplayAuthoringSourceChanges()
        {
            Undo.postprocessModifications+=OnModifications;
            ObjectChangeEvents.changesPublished+=OnObjectChanges;
        }
        public static void Notify(Object source,string path="")
        {
            if(source==null || !(source is ScriptableObject)) return;
            GameplayActionOrderValidation.SourceChanged(source);
            if(!Pending.TryGetValue(source,out var paths)) Pending.Add(source,paths=new HashSet<string>());
            paths.Add(path??"");
            if(_scheduled) return;
            _scheduled=true;EditorApplication.update+=Flush;
        }
        private static UndoPropertyModification[] OnModifications(UndoPropertyModification[] modifications)
        {
            foreach(var modification in modifications)
            {
                var value=modification.currentValue;var previous=modification.previousValue;
                if(value!=null && (previous==null || value.value!=previous.value || value.objectReference!=previous.objectReference))
                    Notify(value.target,value.propertyPath);
            }
            return modifications;
        }
        private static void OnObjectChanges(ref ObjectChangeEventStream stream)
        {
            for(int i=0;i<stream.length;i++)
                if(stream.GetEventType(i)==ObjectChangeKind.ChangeAssetObjectProperties)
                {
                    stream.GetChangeAssetObjectPropertiesEvent(i,out var change);
                    var source=EditorUtility.InstanceIDToObject(change.instanceId);
                    // Document layout events are already covered precisely by Undo paths. They must not rebuild rule data.
                    Notify(source);
                }
        }
        private static void Flush()
        {
            _scheduled=false;EditorApplication.update-=Flush;
            var batch=Pending.Where(p=>p.Key!=null).SelectMany(p=>p.Value.Select(path=>new Change(p.Key,path))).ToArray();
            Pending.Clear();
            if(batch.Length>0) Changed?.Invoke(batch);
        }
    }
}
