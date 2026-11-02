using FRFrame.Gameplay.Authoring;
using UnityEditor;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Editor-session preferences keyed by the actual document GUID/fileID. Observing,
    /// folding, scrolling and preview choices never dirty the formal skill or create a graph SO.</summary>
    public static class GameplaySkillEditorState
    {
        private static readonly System.Collections.Generic.HashSet<string> Migrated=new System.Collections.Generic.HashSet<string>();
        public static string Key(GameplaySkillDocumentAsset doc)
        {
            if(!doc||!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(doc,out string guid,out long id))
                return "FRFrame.SkillAuthoring.Transient."+(doc?doc.GetInstanceID():0);
            string key="FRFrame.SkillAuthoring."+guid+":"+id;
            if(Migrated.Add(key)&&!SessionState.GetBool(key+".CanonicalKeys",false)){
                string old="FRFrame.V2."+guid+":"+id;
                SessionState.SetVector3(key+".Pan",SessionState.GetVector3(old+".Pan",UnityEngine.Vector3.zero));
                SessionState.SetFloat(key+".Zoom",SessionState.GetFloat(old+".Zoom",1));
                foreach(string suffix in new[]{".Profile",".InlinePreview"})SessionState.SetString(key+suffix,SessionState.GetString(old+suffix,""));
                SessionState.SetBool(key+".TurnClock",SessionState.GetBool(old+".TurnClock",false));
                if(doc.AuthoringGraph!=null){
                    foreach(var node in doc.AuthoringGraph.Nodes){
                        SessionState.SetBool(key+".Card."+node.Id,SessionState.GetBool(old+".Card."+node.Id,node.Expanded));
                        SessionState.SetBool(key+".Standalone."+node.Id,SessionState.GetBool(old+".Standalone."+node.Id,false));
                    }
                    foreach(var timeline in doc.AuthoringGraph.Timelines)SessionState.SetString(key+".Timeline."+timeline.Id,SessionState.GetString(old+".Timeline."+timeline.Id,""));
                }
                SessionState.SetBool(key+".CanonicalKeys",true);
            }
            return key;
        }
        public static GameplayActorProfileAsset PreviewProfile(GameplaySkillDocumentAsset doc)=>AssetDatabase.LoadAssetAtPath<GameplayActorProfileAsset>(AssetDatabase.GUIDToAssetPath(SessionState.GetString(Key(doc)+".Profile","")));
        public static bool TurnClock(GameplaySkillDocumentAsset doc)=>SessionState.GetBool(Key(doc)+".TurnClock",false);
        public static void CreationPreferences(GameplaySkillDocumentAsset doc,GameplayActorProfileAsset profile,bool turns)
        {SessionState.SetString(Key(doc)+".Profile",profile?AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath(profile)):"");SessionState.SetBool(Key(doc)+".TurnClock",turns);}
    }
}
