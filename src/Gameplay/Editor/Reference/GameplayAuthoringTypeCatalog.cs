using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;
using Object = UnityEngine.Object;

namespace FRFrame.Gameplay.Editor
{
    /// <summary>Explicit game-owned scope resolution. Raw assets can opt into their game's picker scope.</summary>
    public interface IGameplayAuthoringScopeProvider { string Scope(Object asset); }
    /// <summary>One discovery/filter policy for every managed authoring selector; Editor/test nodes cannot enter Player content.</summary>
    public static class GameplayAuthoringTypeCatalog
    {
        private static Dictionary<string,string> _documentScopes;
        static GameplayAuthoringTypeCatalog(){EditorApplication.projectChanged+=()=>_documentScopes=null;}
        public static Type BaseType(SkillNodeRole role) => role == SkillNodeRole.Trigger ? typeof(GameplaySkillTriggerAuthoring) :
            role == SkillNodeRole.Condition ? typeof(GameplaySkillConditionAuthoring) : role == SkillNodeRole.Target ? typeof(GameplaySkillTargetAuthoring) : typeof(GameplaySkillActionAuthoring);
        public static string Scope(Object asset)
        {
            if (asset is GameplaySkillDocumentAsset skill && skill.AuthoringSchemaVersion == 2) return skill.AuthoringGraph?.Scope;
            if(asset!=null){
                string path=AssetDatabase.GetAssetPath(asset);
                if(!string.IsNullOrEmpty(path)){
                    if(_documentScopes==null){
                        _documentScopes=new Dictionary<string,string>(StringComparer.Ordinal);
                        foreach(var document in SkillAuthoring.GameplaySkillWorkbench.Documents())
                            _documentScopes[AssetDatabase.GetAssetPath(document)]=document.AuthoringGraph?.Scope??"";
                    }
                    if(_documentScopes.TryGetValue(path,out var ownedScope)&&!string.IsNullOrEmpty(ownedScope))return ownedScope;
                }
            }
            foreach (var type in TypeCache.GetTypesDerivedFrom<IGameplayAuthoringScopeProvider>().Where(t => !t.IsAbstract))
            { string scope = ((IGameplayAuthoringScopeProvider)Activator.CreateInstance(type)).Scope(asset); if (!string.IsNullOrEmpty(scope)) return scope; }
            return GameplayAuthoringInspector.CurrentScope??"";
        }
        public static IReadOnlyList<Type> Types(Type baseType)
        {
            var players = new HashSet<string>(CompilationPipeline.GetAssemblies(AssembliesType.Player)
                .Where(a => (a.flags & AssemblyFlags.EditorAssembly) == 0 && !a.name.EndsWith(".Tests", StringComparison.Ordinal))
                .Select(a => a.name));
            return TypeCache.GetTypesDerivedFrom(baseType).Concat(new[] { baseType })
                .Where(t => t.IsClass && !t.IsAbstract && !t.ContainsGenericParameters && (t.IsPublic || t.IsNestedPublic) &&
                    t.IsSerializable && t.GetConstructor(Type.EmptyTypes) != null && players.Contains(t.Assembly.GetName().Name))
                .Distinct().OrderBy(t => t.FullName, StringComparer.Ordinal).ToArray();
        }
        public static IReadOnlyList<Type> Query(Type baseType, string scope, string search = "", bool otherGames = false, bool samples = false)
        {
            return Types(baseType).Where(t => {
                var d = GameplaySkillNodeDescriptors.Get(t);
                bool scopeMatch = string.IsNullOrEmpty(scope) || d.AuthoringScope == "FRFrame" || d.AuthoringScope == "Common" || d.AuthoringScope == scope || otherGames;
                return scopeMatch && (!d.IsSample || samples) && (string.IsNullOrEmpty(search) ||
                    (d.DisplayName + " " + d.CategoryPath + " " + d.Summary + " " + d.Keywords + " " + t.FullName).IndexOf(search, StringComparison.OrdinalIgnoreCase) >= 0);
            }).ToArray();
        }
    }
    /// <summary>Searchable, scoped picker shared by graph, Timeline, nested drawers and Cost.</summary>
    public sealed class GameplayAuthoringTypePicker : EditorWindow
    {
        private Type _baseType; private string _scope, _search = ""; private bool _other, _samples, _officialOnly;
        private Action<Type> _choose; private Vector2 _scroll;
        private Func<Type,string> _unavailable;private string _contextNote;
        public static void Show(Type baseType, Object source, Action<Type> choose, string scope = null, string search = "",Func<Type,string> unavailable=null,string contextNote=null)
        {
            var v2=Resources.FindObjectsOfTypeAll<SkillAuthoring.GameplaySkillWorkbench>().FirstOrDefault(w=>w.Document==source);
            if(v2!=null){
                GameplaySkillCreationMenu.Show(v2.Canvas,"选择配置",menu=>{
                    menu.AddItem(new GUIContent("清空配置"),false,()=>choose(null));
                    foreach(var type in GameplayAuthoringTypeCatalog.Query(baseType,scope??GameplayAuthoringTypeCatalog.Scope(source),search)){
                        var item=type;var descriptor=GameplaySkillNodeDescriptors.Get(item);
                        string reason=unavailable?.Invoke(item)??SkillAuthoring.GameplaySkillTypeAvailability.Reason(source as GameplaySkillDocumentAsset,item);
                        var label=new GUIContent(GameplaySkillNodeDescriptors.Category(item)+"/"+descriptor.DisplayName+(reason==null?"":" · 不可用"),reason??descriptor.Summary);
                        if(reason!=null)menu.AddDisabledItem(label);
                        else menu.AddItem(label,false,()=>{try{string current=unavailable?.Invoke(item)??SkillAuthoring.GameplaySkillTypeAvailability.Reason(source as GameplaySkillDocumentAsset,item);if(current!=null)throw new InvalidOperationException(current);choose(item);}catch(Exception error){v2.Canvas.Feedback?.Invoke(error.Message);}});
                    }
                });return;
            }
            var window = CreateInstance<GameplayAuthoringTypePicker>();
            window._baseType = baseType; window._scope = scope ?? GameplayAuthoringTypeCatalog.Scope(source); window._choose = choose;
            window._officialOnly=source is GameplaySkillDocumentAsset||source!=null&&AssetDatabase.LoadMainAssetAtPath(AssetDatabase.GetAssetPath(source)) is GameplaySkillDocumentAsset;
            window._search=search;
            window._unavailable=unavailable;window._contextNote=contextNote;
            window.titleContent = new GUIContent("选择配置 · "+window._scope);window.position = new Rect(300,180,530,560);window.Show();
        }
        private void OnGUI()
        {
            EditorGUILayout.HelpBox(GameplayAuthoringInspector.Help, MessageType.Info);
            if(!string.IsNullOrEmpty(_contextNote))EditorGUILayout.HelpBox(_contextNote,MessageType.Info);
            _search = EditorGUILayout.TextField("搜索", _search);
            GUILayout.BeginHorizontal(); if(!_officialOnly)_other = EditorGUILayout.ToggleLeft("显示其它游戏", _other);
            _samples = EditorGUILayout.ToggleLeft("显示示例 / 验证节点", _samples); GUILayout.EndHorizontal();
            if (GUILayout.Button("清空配置")) Pick(null);
            _scroll = EditorGUILayout.BeginScrollView(_scroll);
            foreach (var group in GameplayAuthoringTypeCatalog.Query(_baseType, _scope, _search, _other, _samples)
                .Where(t=>!_officialOnly||((GameplaySkillNodeDescriptors.Get(t).AuthoringScope=="FRFrame"||GameplaySkillNodeDescriptors.Get(t).AuthoringScope=="Common")&&
                    t!=typeof(GameplaySkillApplyEffectActionAuthoring)&&t!=typeof(GameplaySkillPlaySequenceActionAuthoring)))
                .GroupBy(t => GameplaySkillNodeDescriptors.Get(t).CategoryPath))
            {
                GUILayout.Label(group.Key, EditorStyles.boldLabel);
                foreach (var type in group)
                {
                    var d = GameplaySkillNodeDescriptors.Get(type);
                    string reason=_unavailable?.Invoke(type);
                    using(new EditorGUI.DisabledScope(reason!=null))if (GUILayout.Button(new GUIContent(d.DisplayName,reason??d.Summary), EditorStyles.miniButton)) Pick(type);
                    if(reason!=null)EditorGUILayout.LabelField(reason,EditorStyles.wordWrappedMiniLabel);
                    GUILayout.Label(d.Summary, EditorStyles.wordWrappedMiniLabel);
                    if(d.AuthoringScope=="Other") EditorGUILayout.HelpBox("缺少正式编辑描述："+type.FullName,MessageType.Warning);
                }
            }
            EditorGUILayout.EndScrollView();
        }
        private void Pick(Type type) {
            try {if(type!=null&&_unavailable?.Invoke(type) is string reason)throw new InvalidOperationException(reason);_choose?.Invoke(type); Close(); }
            catch(Exception e){EditorUtility.DisplayDialog("配置已改变，未替换原位置",e.Message,"了解");Close();}
        }
        public static void Property(Type baseType, SerializedProperty property, bool append = false)
        {
            Object source = property.serializedObject.targetObject;
            var handle=GameplaySourceHandle.Capture(source,property.propertyPath);
            string expected=append?null:property.managedReferenceFullTypename;
            Show(baseType, source, type => {
                if (source == null) return;
                string path=handle.ResolvePath();
                using (var serialized = new SerializedObject(source))
                {
                    var selected = serialized.FindProperty(path);
                    if(selected==null||!append&&selected.managedReferenceFullTypename!=expected)
                        throw new InvalidOperationException("原字段已被另一个编辑入口替换，请重新选择配置类型。");
                    if(type!=null&&!GameplayAuthoringTypeCatalog.Query(baseType,GameplayAuthoringTypeCatalog.Scope(source),samples:true).Contains(type))
                        throw new InvalidOperationException("此类型在当前 Scope 不可用。");
                    if(baseType==typeof(GameplaySkillActionAuthoring)&&type!=null){
                        string array=append?path:GameplaySharedAuthoringCommands.ActionArray(path);
                        if(array!=null){
                            int at=append?selected.arraySize:int.Parse(path.Substring(path.LastIndexOf('[')+1).TrimEnd(']'));
                            string order=GameplayActionOrderValidation.PlacementReason(source,array,at,Activator.CreateInstance(type),append?-1:at);
                            if(order!=null)throw new InvalidOperationException(order);
                        }
                    }
                    if(!append&&selected.managedReferenceValue!=null){
                        if(selected.managedReferenceValue.GetType()==type)return;
                        if(type==null&&(selected.managedReferenceValue is GameplaySkillTriggerAuthoring||selected.managedReferenceValue is GameplaySkillTargetAuthoring))
                            throw new InvalidOperationException("这是必需的触发 / 目标配置，请选择替代类型，不能清空。");
                        var labels=new System.Collections.Generic.List<string>();var field=selected.Copy();var end=field.GetEndProperty();
                        if(field.NextVisible(true))do{if(SerializedProperty.EqualContents(field,end))break;labels.Add(GameplayAuthoringInspector.Label(field).text);}while(field.NextVisible(false)&&labels.Count<8);
                        if(!EditorUtility.DisplayDialog("替换配置类型", "将替换「"+GameplaySkillNodeDescriptors.Get(selected.managedReferenceValue.GetType()).DisplayName+"」及其字段：\n"+string.Join("、",labels)+"\n已有配置资源保留，不自动清理；可用一次 Undo 恢复。","替换","取消"))return;
                        if(type==null&&(selected.managedReferenceValue is GameplaySkillActionAuthoring||selected.managedReferenceValue is GameplaySequenceMarkerAuthoring)){
                            GameplaySharedAuthoringCommands.Remove(source,path);return;
                        }
                    }
                    if (append) { if (type == null) return; int i = selected.arraySize; selected.arraySize++; selected = selected.GetArrayElementAtIndex(i); }
                    selected.managedReferenceValue = type == null ? null : Activator.CreateInstance(type);
                    selected.isExpanded = true; if(serialized.ApplyModifiedProperties()) GameplayAuthoringSourceChanges.Notify(source,path);
                }
            });
        }
    }
}
