using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    /// <summary>Real AssetDatabase folders and typed document identities. Reuses V1's wrapped,
    /// virtualized native rows and bounded indentation; no dependency/art loading on repaint.</summary>
    internal sealed class GameplaySkillDocumentBrowser
    {
        private sealed class Folder {
            internal string Name, Path;
            internal readonly SortedDictionary<string,Folder> Children=new SortedDictionary<string,Folder>(StringComparer.OrdinalIgnoreCase);
            internal readonly List<GameplaySkillDocumentAsset> Entries=new List<GameplaySkillDocumentAsset>();
        }
        private sealed class Row { internal Folder Folder; internal GameplaySkillDocumentAsset Document; internal GUIContent Label; internal Rect Rect; internal float Indent; }
        private readonly HashSet<string> _closed=new HashSet<string>();
        private IReadOnlyList<GameplaySkillDocumentAsset> _documents;
        private Folder _root;
        private List<Row> _rows;
        private string _search="";
        private readonly Dictionary<int,string> _identities=new Dictionary<int,string>();
        private Vector2 _scroll;
        private float _width,_height;
        private GUIStyle _rowStyle;
        private GameplaySkillDocumentAsset _reveal;
        [Serializable] private sealed class State {public string Search="";public Vector2 Scroll;public string[] Closed=Array.Empty<string>();}
        private static string StateKey=>"FRFrame.SkillAuthoring."+Application.dataPath+".Browser";
        internal GameplaySkillDocumentBrowser(){var state=JsonUtility.FromJson<State>(EditorPrefs.GetString(StateKey,JsonUtility.ToJson(new State())));_search=state.Search;_scroll=state.Scroll;_closed.UnionWith(state.Closed);}
        internal void Save()=>EditorPrefs.SetString(StateKey,JsonUtility.ToJson(new State{Search=_search,Scroll=_scroll,Closed=_closed.ToArray()}));
        internal Action<GameplaySkillDocumentAsset> Select;
        internal void Reveal(GameplaySkillDocumentAsset document) { _reveal=document;if(document){string path=AssetDatabase.GetAssetPath(document);_closed.RemoveWhere(folder=>path.StartsWith(folder,StringComparison.OrdinalIgnoreCase));}_rows=null; }
        internal bool Changed(GameplaySkillDocumentAsset document) {
            if(!document||!AssetDatabase.Contains(document))return false;string identity=document.SkillId+":"+document.DisplayName+":"+(document.AuthoringGraph?.Scope??"待迁移");
            int id=document.GetInstanceID();if(_identities.TryGetValue(id,out var previous)&&previous==identity)return false;_identities[id]=identity;_root=null;_rows=null;return true;
        }
        internal void Draw(IReadOnlyList<GameplaySkillDocumentAsset> documents,GameplaySkillDocumentAsset selected,float width)
        {
            if(_rowStyle==null)_rowStyle=new GUIStyle(EditorStyles.miniButton){wordWrap=true,fontSize=13,alignment=TextAnchor.MiddleLeft,fixedHeight=0,padding=new RectOffset(8,5,5,5)};
            GUILayout.BeginHorizontal();GUI.SetNextControlName("SkillDocumentBrowser");
            string next=EditorGUILayout.TextField(_search,EditorStyles.toolbarSearchField);
            if(GUILayout.Button(new GUIContent("◎","展开并定位当前技能"),EditorStyles.miniButton,GUILayout.Width(24))){next="";Reveal(selected);}
            GUILayout.EndHorizontal();
            if(next!=_search){_search=next;_root=null;_rows=null;}
            if(!ReferenceEquals(_documents,documents)){_documents=documents;_root=null;_rows=null;}
            if(_root==null)BuildTree();
            width=Mathf.Max(100,width-18);
            if(_rows==null||Mathf.Abs(width-_width)>.5f){_width=width;_height=0;_rows=new List<Row>();Layout(_root,0);}
            var viewport=GUILayoutUtility.GetRect(0,100000,0,100000,GUILayout.ExpandHeight(true),GUILayout.ExpandWidth(true));
            if(_reveal&&Event.current.type==EventType.Repaint&&viewport.height>30){var row=_rows.Find(r=>r.Document==_reveal);if(row!=null)_scroll.y=Mathf.Max(0,row.Rect.y-viewport.height*.3f);_reveal=null;}
            _scroll=GUI.BeginScrollView(viewport,_scroll,new Rect(0,0,width,_height));
            foreach(var row in _rows){
                if(row.Rect.yMax<_scroll.y||row.Rect.y>_scroll.y+viewport.height)continue;
                var rect=new Rect(row.Indent,row.Rect.y,width-row.Indent,row.Rect.height);
                if(row.Folder!=null){
                    bool open=!_closed.Contains(row.Folder.Path)||_search.Length>0;
                    if(EditorGUI.Foldout(rect,open,new GUIContent(row.Folder.Name,row.Folder.Path),true)!=open){if(open)_closed.Add(row.Folder.Path);else _closed.Remove(row.Folder.Path);_rows=null;}
                    continue;
                }
                var document=row.Document;if(!document)continue;
                if(Event.current.type==EventType.ContextClick&&rect.Contains(Event.current.mousePosition)){
                    var menu=new GenericMenu();string path=AssetDatabase.GetAssetPath(document);
                    menu.AddItem(new GUIContent("在 Project 定位"),false,()=>{Selection.activeObject=document;EditorGUIUtility.PingObject(document);});
                    menu.AddItem(new GUIContent("复制真实资源路径"),false,()=>EditorGUIUtility.systemCopyBuffer=path);menu.ShowAsContext();Event.current.Use();continue;
                }
                if(Event.current.isMouse&&Event.current.button!=0)continue;
                var label=new Rect(rect.x,rect.y,Mathf.Max(40,rect.width-26),rect.height-3);
                if(GUI.Toggle(label,document==selected,row.Label,_rowStyle)&&document!=selected)Select?.Invoke(document);
                if(GUI.Button(new Rect(rect.xMax-24,rect.y+5,24,24),new GUIContent("◎","定位主技能文件"),EditorStyles.miniButton)){Selection.activeObject=document;EditorGUIUtility.PingObject(document);}
            }
            GUI.EndScrollView();
            if(Event.current.type==EventType.KeyDown&&GUI.GetNameOfFocusedControl()=="SkillDocumentBrowser"&&(Event.current.keyCode==KeyCode.DownArrow||Event.current.keyCode==KeyCode.UpArrow)){
                var entries=_rows.Where(r=>r.Document).ToList();int at=entries.FindIndex(r=>r.Document==selected);
                if(entries.Count>0){var document=entries[Mathf.Clamp(at+(Event.current.keyCode==KeyCode.DownArrow?1:-1),0,entries.Count-1)].Document;Select?.Invoke(document);_reveal=document;}Event.current.Use();
            }
        }
        private void BuildTree()
        {
            _root=new Folder{Name="",Path=""};
            foreach(var document in _documents.Where(d=>d)){
                string path=AssetDatabase.GetAssetPath(document);_identities[document.GetInstanceID()]=document.SkillId+":"+document.DisplayName+":"+(document.AuthoringGraph?.Scope??"待迁移");
                string search=document.SkillId+" "+document.DisplayName+" "+path+" "+(document.AuthoringGraph?.Scope??"待迁移");
                if(_search.Length>0&&search.IndexOf(_search,StringComparison.OrdinalIgnoreCase)<0)continue;
                var folder=_root;var parts=path.Split('/');
                for(int i=0;i<parts.Length-1;i++){
                    if(!folder.Children.TryGetValue(parts[i],out var child)){child=new Folder{Name=parts[i],Path=folder.Path+parts[i]+"/"};folder.Children.Add(parts[i],child);}folder=child;
                }folder.Entries.Add(document);
            }
        }
        private void Layout(Folder folder,int depth)
        {
            if(folder!=_root){_rows.Add(new Row{Folder=folder,Indent=Mathf.Min(depth-1,3)*8,Rect=new Rect(0,_height,_width,23)});_height+=23;if(_closed.Contains(folder.Path)&&_search.Length==0)return;}
            foreach(var child in folder.Children.Values)Layout(child,depth+1);
            foreach(var document in folder.Entries.OrderBy(d=>d.SkillId)){
                string path=AssetDatabase.GetAssetPath(document);string title=string.IsNullOrWhiteSpace(document.DisplayName)?document.name:document.DisplayName;
                var label=new GUIContent(title+"  #"+document.SkillId+"\n"+System.IO.Path.GetFileName(path),path+"\nScope: "+(document.AuthoringGraph?.Scope??"待迁移"));
                float indent=Mathf.Min(depth,3)*8;float height=Mathf.Max(46,_rowStyle.CalcHeight(label,Mathf.Max(60,_width-indent-26)))+3;
                _rows.Add(new Row{Document=document,Label=label,Indent=indent,Rect=new Rect(0,_height,_width,height)});_height+=height;
            }
        }
    }
}
