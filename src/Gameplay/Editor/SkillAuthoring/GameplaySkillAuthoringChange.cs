using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Ephemeral editor invalidation, resolved to stable authoring IDs before Apply.
    /// It is never serialized or consumed by the compiler. Unknown writers retain full fallback.</summary>
    public sealed class GameplaySkillAuthoringChange
    {
        public bool Unknown {get;private set;}
        public bool Structural {get;private set;}
        public bool Context {get;private set;}
        public bool Layout {get;private set;}
        public IReadOnlyCollection<string> Owners {get;private set;}
        private GameplaySkillAuthoringChange() { }
        private static readonly HashSet<string> StructuralFields=new HashSet<string>{"Action","Payload","Settings","Target","Trigger","size","Mode","CastEvent","Scope","Once","Stacking","DurationPolicy","Period","Overflow"};
        private static readonly HashSet<string> ContextFields=new HashSet<string>{"Target","Trigger","Conditions","PreserveTriggerContext","Shape","Origin","Direction","Directional","RotateOwner","DurationPolicy","Motion","StraightDestination","Overflow","Period","PeriodValue","TickInterval","Cancel","OwnedByAbility","HopIntervalSeconds"};
        public static GameplaySkillAuthoringChange Full(bool structural=false)=>new GameplaySkillAuthoringChange{Unknown=true,Structural=structural,Context=true,Owners=Array.Empty<string>()};
        public static GameplaySkillAuthoringChange Local(IEnumerable<string> owners,bool layout=false)=>new GameplaySkillAuthoringChange{Owners=owners.Where(x=>!string.IsNullOrEmpty(x)).Distinct().ToArray(),Layout=layout};
        /// <summary>Array indexes are used only while resolving this immediate edit. Consumers
        /// receive stable IDs; later reorder/Undo cannot redirect an old invalidation.</summary>
        public static GameplaySkillAuthoringChange Fields(GameplaySkillDocumentAsset document,IEnumerable<string> paths,bool structural=false)
        {
            if(paths==null)return Full(structural);
            var change=new GameplaySkillAuthoringChange();var owners=new HashSet<string>();var graph=document.AuthoringGraph;
            foreach(string path in paths.Distinct()){
                if(string.IsNullOrEmpty(path))return Full(structural);
                string relative=path,owner=graph.AbilityNodeId;
                if(path.StartsWith("authoringGraph.",StringComparison.Ordinal)){
                    if(Resolve("Nodes",graph.Nodes.Count,i=>graph.Nodes[i].Id)||Resolve("LocalDefinitions",graph.LocalDefinitions.Count,i=>graph.LocalDefinitions[i].Id)||Resolve("Timelines",graph.Timelines.Count,i=>graph.Timelines[i].Id)) { }
                    else if(path=="authoringGraph.Scope")change.Structural=true;
                    else return Full(structural);
                }
                if(owner!=null)owners.Add(owner);
                string[] fields=relative.Split('.');string leaf=fields.Last();
                if(StructuralFields.Contains(leaf)||fields.Contains("Definitions")||fields.Contains("CastEvent")||fields.Contains("Trigger")&&fields.Contains("EventTag")||leaf=="Conditions")change.Structural=true;
                if(fields.Any(ContextFields.Contains))change.Context=true;
                if(fields.Any(f=>f=="Position"||f=="Size"))change.Layout=true;
                bool Resolve(string collection,int count,Func<int,string> identity){
                    string prefix="authoringGraph."+collection+".Array.data[";
                    if(!path.StartsWith(prefix,StringComparison.Ordinal))return false;
                    int end=path.IndexOf(']',prefix.Length);int index;
                    if(end<0||!int.TryParse(path.Substring(prefix.Length,end-prefix.Length),out index)||index<0||index>=count)return false;
                    owner=identity(index);relative=path.Substring(Math.Min(end+2,path.Length));return true;
                }
            }
            change.Owners=owners.ToArray();change.Structural|=structural;
            return change.Owners.Count==0?Full(change.Structural):change;
        }
    }
}
