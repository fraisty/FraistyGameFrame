using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using UnityEditor;
using UnityEditor.ShortcutManagement;
using UnityEngine;
using UnityEngine.UIElements;

namespace FRFrame.Gameplay.Editor.SkillAuthoring
{
    public sealed partial class GameplaySkillWorkbench
    {
        [Serializable] private sealed class RecentDocument
        {
            public string Guid, Title, Path;
            public long FileId;
            [NonSerialized] internal GameplaySkillDocumentAsset Document;
        }
        [Serializable] private sealed class RecentDocuments { public List<RecentDocument> Items=new List<RecentDocument>(); }
        private RecentDocuments _recent;
        private bool _recentStale=true;
        private VisualElement _editingArea,_documentTools;
        private IMGUIContainer _welcome;
        private Vector2 _welcomeScroll;
        private static string ProjectKey=>"FRFrame.SkillAuthoring."+Application.dataPath;
        private static string RecentKey=>ProjectKey+".RecentDocuments";

        private void RecentSourcesChanged(){_recentStale=true;_welcome?.MarkDirtyRepaint();}
        private void LoadRecent()
        {
            if(_recent==null){
                _recent=JsonUtility.FromJson<RecentDocuments>(EditorPrefs.GetString(RecentKey,JsonUtility.ToJson(new RecentDocuments())));
                // Read the old Workspace index once. Only its explicit RuntimeDocument (or
                // an actual main Document) is accepted; never guess a child by path/name/ID.
                if(!EditorPrefs.HasKey(RecentKey)){
                    foreach(string guid in EditorPrefs.GetString("FRFrame.Workbench.Recent."+Application.dataPath,"").Split('|').Reverse()){
                        string path=AssetDatabase.GUIDToAssetPath(guid);if(string.IsNullOrEmpty(path))continue;
                        var source=AssetDatabase.LoadMainAssetAtPath(path);if(!source)continue;
                        var document=source as GameplaySkillDocumentAsset;
                        if(!document)using(var so=new SerializedObject(source))document=so.FindProperty("RuntimeDocument")?.objectReferenceValue as GameplaySkillDocumentAsset;
                        if(document)RecordRecent(document);
                    }
                    SaveRecent();
                }
            }
            if(!_recentStale)return;_recentStale=false;
            foreach(var item in _recent.Items.ToArray()){
                string path=AssetDatabase.GUIDToAssetPath(item.Guid);
                item.Document=string.IsNullOrEmpty(path)?null:AssetDatabase.LoadAllAssetsAtPath(path).OfType<GameplaySkillDocumentAsset>()
                    .FirstOrDefault(d=>AssetDatabase.TryGetGUIDAndLocalFileIdentifier(d,out string guid,out long id)&&guid==item.Guid&&id==item.FileId);
                if(!item.Document){_recent.Items.Remove(item);continue;}
                item.Path=path;item.Title=item.Document.DisplayName+"  #"+item.Document.SkillId;
            }
            SaveRecent();
        }
        private void RecordRecent(GameplaySkillDocumentAsset document)
        {
            if(!AssetDatabase.TryGetGUIDAndLocalFileIdentifier(document,out string guid,out long id))return;
            _recent.Items.RemoveAll(e=>e.Guid==guid&&e.FileId==id);
            _recent.Items.Insert(0,new RecentDocument{Guid=guid,FileId=id,Document=document,Path=AssetDatabase.GetAssetPath(document),Title=document.DisplayName+"  #"+document.SkillId});
            if(_recent.Items.Count>6)_recent.Items.RemoveRange(6,_recent.Items.Count-6);
        }
        private void RememberRecent(GameplaySkillDocumentAsset document)
        {
            if(!document||!EditorUtility.IsPersistent(document))return;
            LoadRecent();RecordRecent(document);SaveRecent();_welcome?.MarkDirtyRepaint();
        }
        private void SaveRecent()=>EditorPrefs.SetString(RecentKey,JsonUtility.ToJson(_recent));
        private void OpenRecent(RecentDocument entry)
        {
            _recentStale=true;LoadRecent();
            var current=_recent.Items.FirstOrDefault(e=>e.Guid==entry.Guid&&e.FileId==entry.FileId);
            if(current?.Document)SelectDocument(current.Document);
            else {_feedback="该技能已移除；最近列表已更新。";Repaint();}
        }
        private void ShowHome(){_sharedSource=null;SelectDocument(null);_welcome?.MarkDirtyRepaint();}
        private void UpdateSurface()
        {
            if(_welcome==null)return;
            bool editing=_document||_sharedSource;
            _welcome.style.display=editing?DisplayStyle.None:DisplayStyle.Flex;
            _editingArea.style.display=editing?DisplayStyle.Flex:DisplayStyle.None;
            _documentTools.style.display=_document?DisplayStyle.Flex:DisplayStyle.None;
        }
        private void ShowOpenDocumentMenu()
        {
            var menu=new GenericMenu();var documents=Documents();
            foreach(var doc in documents){
                var source=doc;string title=doc.DisplayName+" #"+doc.SkillId+" · "+doc.name;
                menu.AddItem(new GUIContent((doc.AuthoringGraph?.Scope??"未迁移")+"/"+title,AssetDatabase.GetAssetPath(doc)),doc==_document,()=>SelectDocument(source));
            }
            if(documents.Count==0)menu.AddDisabledItem(new GUIContent("尚无技能，请先新建"));
            menu.ShowAsContext();
        }
        private void ShowRecentDocumentMenu()
        {
            LoadRecent();
            var menu=new GenericMenu();
            foreach(var entry in _recent.Items)
                menu.AddItem(new GUIContent(entry.Title+" · "+System.IO.Path.GetFileName(entry.Path),entry.Path),
                    entry.Document==_document,()=>OpenRecent(entry));
            if(_recent.Items.Count==0)
                menu.AddDisabledItem(new GUIContent("暂无记录，打开技能后会出现在这里"));
            menu.ShowAsContext();
        }
        private void DrawWelcome()
        {
            LoadRecent();_welcomeScroll=EditorGUILayout.BeginScrollView(_welcomeScroll);
            GUILayout.Space(24);GUILayout.Label("开始创作技能",new GUIStyle(EditorStyles.boldLabel){fontSize=20});
            GUILayout.Space(8);GUILayout.Label("新建正式技能 → 创建业务行为 → 编排与连接 → 校验、预览和场景试装",EditorStyles.wordWrappedLabel);
            GUILayout.Space(14);GUILayout.BeginHorizontal();
            if(GUILayout.Button("＋ 新建技能",GUILayout.Width(160),GUILayout.Height(32)))NewDocument();
            if(GUILayout.Button("打开已有技能…",GUILayout.Width(160),GUILayout.Height(32)))ShowOpenDocumentMenu();
            using(new EditorGUI.DisabledScope(_recent.Items.Count==0))
                if(GUILayout.Button("继续上次",GUILayout.Width(130),GUILayout.Height(32)))OpenRecent(_recent.Items[0]);
            if(GUILayout.Button("技能范例 / 从零教程",GUILayout.Width(170),GUILayout.Height(32)))ShowExamples();
            GUILayout.EndHorizontal();GUILayout.Space(20);GUILayout.Label("最近使用 · 最多 6 份",EditorStyles.boldLabel);
            if(_recent.Items.Count==0)GUILayout.Label("打开一份技能后，这里会记住它的真实资产身份。",EditorStyles.wordWrappedMiniLabel);
            foreach(var entry in _recent.Items.ToArray())
                if(GUILayout.Button(new GUIContent(entry.Title+"\n"+System.IO.Path.GetFileName(entry.Path),entry.Path+"\n子资产 fileID: "+entry.FileId),new GUIStyle(EditorStyles.miniButton){alignment=TextAnchor.MiddleLeft,wordWrap=true,fixedHeight=0,fontSize=13,padding=new RectOffset(8,8,5,5)},GUILayout.Height(43)))
                    OpenRecent(entry);
            if(!string.IsNullOrEmpty(_feedback))EditorGUILayout.HelpBox(_feedback,MessageType.Info);
            EditorGUILayout.EndScrollView();
        }
        private void SavePanePreferences()
        {
            EditorPrefs.SetBool(ProjectKey+".BrowserOpen",_browserOpen);
            EditorPrefs.SetBool(ProjectKey+".BrowserForced",_browserForced);
            EditorPrefs.SetBool(ProjectKey+".InspectorOpen",_inspectorOpen);
            _documentBrowser?.Save();
        }
        // Window context takes precedence over Unity's global New Scene / Save Scene.
        [Shortcut("FRFrame/Skill Workbench/New",typeof(GameplaySkillWorkbench),KeyCode.N,ShortcutModifiers.Action)]
        private static void NewShortcut(ShortcutArguments args)
        {
            var window=(GameplaySkillWorkbench)args.context;
            if(window.Canvas?.EditingText()==true||!string.IsNullOrEmpty(Input.compositionString))return;
            window.NewDocument();
        }
        [Shortcut("FRFrame/Skill Workbench/Save",typeof(GameplaySkillWorkbench),KeyCode.S,ShortcutModifiers.Action)]
        private static void SaveShortcut(ShortcutArguments args)=>((GameplaySkillWorkbench)args.context).SaveDocument();
        private void SaveDocument()=>Attempt(()=>{if(GameplaySkillAuthoringCommands.IsReadOnly(_document)){_feedback="只读范例；请以此创建新技能后保存。";return;}if(_document){AssetDatabase.SaveAssetIfDirty(_document);_feedback="已保存同一主技能文件";}});
        private void OnWorkbenchKeyDown(KeyDownEvent e)
        {
            if(!(e.ctrlKey||e.commandKey))return;
            if(e.keyCode==KeyCode.S){
                SaveDocument();
                e.StopImmediatePropagation();e.PreventDefault();return;
            }
            if(e.keyCode!=KeyCode.N||Canvas?.EditingText()==true||!string.IsNullOrEmpty(Input.compositionString))return;
            NewDocument();e.StopImmediatePropagation();e.PreventDefault();
            // Ctrl+D/Copy/Paste/Delete keep GraphView's working native command route.
        }
    }
}
