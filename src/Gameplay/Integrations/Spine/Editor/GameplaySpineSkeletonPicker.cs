using System;
using System.Linq;
using Spine.Unity;
using UnityEditor;
using UnityEngine;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor;
using FRFrame.Gameplay.Editor.SkillAuthoring;

namespace FRFrame.Gameplay.Spine.Editor
{
    /// <summary>GUID/path search with 50-row pages. No skeleton or thumbnail is loaded while
    /// opening/filtering; only an explicitly chosen row is loaded after its target is validated.</summary>
    internal sealed class GameplaySpineSkeletonPicker : EditorWindow
    {
        private static string[] CachedPaths,CachedGuids;
        private static bool IndexStale=true;
        private string[] _paths=Array.Empty<string>(),_guids=Array.Empty<string>();
        private int[] _visible=Array.Empty<int>();
        private string _search="",_error;
        private int _page;
        private const int PageSize=50;
        private Vector2 _scroll;
        private Func<SkeletonDataAsset,bool> _apply;
        private Func<bool> _valid;
        static GameplaySpineSkeletonPicker(){EditorApplication.projectChanged+=()=>IndexStale=true;}

        internal static void Open(Func<SkeletonDataAsset,bool> apply,Func<bool> valid)
        {
            Rect host=focusedWindow?focusedWindow.position:new Rect(160,100,900,600);
            var window=CreateInstance<GameplaySpineSkeletonPicker>();window._apply=apply;window._valid=valid;
            window.titleContent=new GUIContent("安全选择 Spine 骨骼");window.minSize=new Vector2(520,360);window.ShowUtility();
            window.position=new Rect(host.center.x-340,host.center.y-260,680,520);
        }
        internal static void Draw(SerializedProperty property,Func<SkeletonDataAsset,bool> choose=null,Func<bool> valid=null)
        {
            EditorGUILayout.LabelField("Spine 骨骼 · 拖入或安全搜索",EditorStyles.boldLabel);
            Rect drop=GUILayoutUtility.GetRect(80,34,GUILayout.ExpandWidth(true));var current=property.objectReferenceValue;
            GUI.Box(drop,current?current.name:"从 Project 拖入 SkeletonDataAsset",EditorStyles.helpBox);
            var evt=Event.current;
            if(GUI.enabled&&(valid==null||valid())&&drop.Contains(evt.mousePosition)&&(evt.type==EventType.DragUpdated||evt.type==EventType.DragPerform)){
                var value=DragAndDrop.objectReferences.OfType<SkeletonDataAsset>().FirstOrDefault();
                DragAndDrop.visualMode=value?DragAndDropVisualMode.Link:DragAndDropVisualMode.Rejected;
                if(value&&evt.type==EventType.DragPerform){DragAndDrop.AcceptDrag();GUI.changed|=SetReference(property,value);}
                evt.Use();
            }
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("安全搜索骨骼…")){
                if(choose!=null)Open(choose,valid);
                else {
                    var handle=GameplaySourceHandle.Capture(property.serializedObject.targetObject,property.propertyPath);
                    Open(value=>{
                        if(!handle.TryResolve(out string path))return false;
                        using(var so=new SerializedObject(handle.Owner))return ApplyReference(so.FindProperty(path),value);
                    },()=>handle.Owner&&handle.TryResolve(out _)&&!EditorApplication.isPlayingOrWillChangePlaymode);
                }
            }
            using(new EditorGUI.DisabledScope(!current)){
                if(GUILayout.Button("定位",GUILayout.Width(48)))EditorGUIUtility.PingObject(current);
                if(GUILayout.Button("清空",GUILayout.Width(48))&&(valid==null||valid()))GUI.changed|=SetReference(property,null);
            }
            GUILayout.EndHorizontal();
        }
        // Both main SO and local Schema2 editors use this one pending-buffer mutation.
        // Clearing a skeleton intentionally leaves an authoring draft until a real clip is chosen.
        internal static bool SetReference(SerializedProperty property,SkeletonDataAsset selected)
        {
            if(property.objectReferenceValue==selected)return false;
            property.objectReferenceValue=selected;
            string prefix=property.propertyPath.Substring(0,property.propertyPath.Length-"Skeleton".Length);
            property.serializedObject.FindProperty(prefix+"AnimationName").stringValue="";
            property.serializedObject.FindProperty(prefix+"SkinNames").arraySize=0;
            var initial=property.serializedObject.FindProperty(prefix+"PreviewInitialAnimation");if(initial!=null)initial.stringValue="";
            return true;
        }
        internal static bool ApplyReference(SerializedProperty property,SkeletonDataAsset selected)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)return false;
            if(!SetReference(property,selected))return true;
            var so=property.serializedObject;string prefix=property.propertyPath.Substring(0,property.propertyPath.Length-"Skeleton".Length);
            Undo.SetCurrentGroupName("选择 Spine 骨骼");
            if(so.targetObject is GameplaySkillDocumentAsset document)
                return GameplaySkillFieldSchema.ApplyFields(so,document,false,new[]{property.propertyPath,prefix+"AnimationName",prefix+"SkinNames",prefix+"PreviewInitialAnimation"});
            bool changed=so.ApplyModifiedProperties();if(changed)GameplayAuthoringSourceChanges.Notify(so.targetObject,property.propertyPath);return changed;
        }
        private void OnEnable(){RefreshIndex();EditorApplication.projectChanged+=RefreshIndex;}
        private void OnDisable(){EditorApplication.projectChanged-=RefreshIndex;_apply=null;_valid=null;}
        private void RefreshIndex()
        {
            if(CachedPaths==null||IndexStale){
                CachedGuids=AssetDatabase.FindAssets("t:SkeletonDataAsset",new[]{"Assets"});
                CachedPaths=CachedGuids.Select(AssetDatabase.GUIDToAssetPath).ToArray();IndexStale=false;
            }
            _guids=CachedGuids;_paths=CachedPaths;Filter();Repaint();
        }
        private void Filter()
        {
            _visible=Enumerable.Range(0,_paths.Length).Where(i=>string.IsNullOrEmpty(_search)||_paths[i].IndexOf(_search,StringComparison.OrdinalIgnoreCase)>=0||_guids[i].IndexOf(_search,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            _page=0;_scroll=Vector2.zero;
        }
        private void OnGUI()
        {
            EditorGUILayout.LabelField("仅检索名称 / 路径 / GUID；选定一项后才加载",EditorStyles.wordWrappedMiniLabel);
            string next=EditorGUILayout.TextField("搜索",_search);if(next!=_search){_search=next;Filter();}
            bool valid=_apply!=null&&_valid!=null&&_valid();
            if(!valid)EditorGUILayout.HelpBox("原编辑位置已失效；请取消并从当前字段重新打开。",MessageType.Warning);
            if(!string.IsNullOrEmpty(_error))EditorGUILayout.HelpBox(_error,MessageType.Warning);
            int pages=Math.Max(1,(_visible.Length+PageSize-1)/PageSize);_page=Mathf.Clamp(_page,0,pages-1);
            GUILayout.BeginHorizontal();
            if(GUILayout.Button("上一页",GUILayout.Width(70)))_page=Math.Max(0,_page-1);
            GUILayout.Label(_visible.Length+" 项 · "+(_page+1)+" / "+pages);
            if(GUILayout.Button("下一页",GUILayout.Width(70)))_page=Math.Min(pages-1,_page+1);
            GUILayout.EndHorizontal();
            _scroll=EditorGUILayout.BeginScrollView(_scroll);
            using(new EditorGUI.DisabledScope(!valid))foreach(int index in _visible.Skip(_page*PageSize).Take(PageSize))
                if(GUILayout.Button(_paths[index],EditorStyles.miniButton,GUILayout.Height(34))){
                    var selected=AssetDatabase.LoadAssetAtPath<SkeletonDataAsset>(_paths[index]);
                    if(_apply(selected)){Close();GUIUtility.ExitGUI();}
                    _error="未应用选择；原字段可能已改变，请重新打开。";
                }
            EditorGUILayout.EndScrollView();if(GUILayout.Button("取消"))Close();
        }
    }
}
