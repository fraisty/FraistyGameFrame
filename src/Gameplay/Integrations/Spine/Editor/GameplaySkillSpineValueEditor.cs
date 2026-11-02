using System;
using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using Spine.Unity;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FRFrame.Gameplay.Spine.Editor
{
    public sealed class GameplaySkillSpineValueEditor : IGameplaySkillValueEditor, IGameplaySkillValueDescription, IGameplaySkillCardFieldProvider, IGameplaySkillValueDuration, IGameplaySkillFieldControlProvider
    {
        // Resolve stable local identity only while the initiating node binding still owns it.
        // Direct source pages intentionally have an independent local-definition lifetime.
        private sealed class Location
        {
            internal readonly UnityEngine.Object Owner;
            private readonly string _localId,_field;
            private readonly GameplaySourceHandle _direct;
            private readonly Func<bool> _sourceGuard,_canWrite;
            internal Location(SerializedProperty property,Func<bool> canWrite=null){
                _sourceGuard=GameplaySkillFieldSchema.CurrentSourceGuard;_canWrite=canWrite;
                Owner=property.serializedObject.targetObject;_field=property.name;
                if(Owner is GameplaySkillDocumentAsset doc){
                    var match=System.Text.RegularExpressions.Regex.Match(property.propertyPath,@"^authoringGraph.LocalDefinitions.Array.data\[(\d+)\].Payload\.");
                    if(match.Success)_localId=doc.AuthoringGraph.LocalDefinitions[int.Parse(match.Groups[1].Value)].Id;
                }else _direct=GameplaySourceHandle.Capture(Owner,property.propertyPath);
            }
            internal string Path {
                get{
                    if(!Owner||_sourceGuard!=null&&!_sourceGuard())return null;
                    if(Owner is GameplaySkillDocumentAsset doc){
                        int index=doc.AuthoringGraph.LocalDefinitions.FindIndex(d=>d.Id==_localId);
                        return index>=0&&doc.AuthoringGraph.LocalDefinitions[index].Payload is GameplaySpineAnimationData?
                            "authoringGraph.LocalDefinitions.Array.data["+index+"].Payload."+_field:null;
                    }
                    return _direct!=null&&_direct.TryResolve(out string path)?path:null;
                }
            }
            internal bool Choose(SkeletonDataAsset skeleton){
                string path=Path;if(path==null||_canWrite!=null&&!_canWrite()||EditorApplication.isPlayingOrWillChangePlaymode)return false;
                using(var so=new SerializedObject(Owner))return GameplaySpineSkeletonPicker.ApplyReference(so.FindProperty(path),skeleton);
            }
        }
        private static Func<bool> WindowGuard(Location location){
            var window=EditorWindow.focusedWindow;
            return ()=>window&&location.Path!=null&&!EditorApplication.isPlayingOrWillChangePlaymode&&
                (!(window is GameplaySkillWorkbench workbench)||workbench.Document==location.Owner);
        }
        public bool TryReadDuration(object values,out string path,out float seconds)
        {path=null;seconds=0;if(values is GameplaySpineAnimationData animation&&animation.Speed is GameplayConstantFloatValueAuthoring speed&&speed.Value>0){var index=GameplaySpineSourceInspector.Index(animation.Skeleton);if(index!=null&&index.Durations.TryGetValue(animation.AnimationName??"",out var duration)){seconds=duration/speed.Value;return true;}}return false;}
        public IEnumerable<GameplaySkillCardField> CardFields(object values)
        {if(values is GameplaySpineAnimationData){yield return new GameplaySkillCardField("Skeleton","Spine 骨骼");yield return new GameplaySkillCardField("AnimationName","动画名称");yield return new GameplaySkillCardField("SkinNames","皮肤组合");yield return new GameplaySkillCardField("TrackIndex","轨道");}}
        public string Summary(object values)=>values is GameplaySpineAnimationData animation?"Spine · "+animation.AnimationName+" · 轨道 "+animation.TrackIndex+"\n"+(animation.Loop?"循环":"单次")+" · 速度 "+GameplaySkillFieldSchema.FloatSummary(animation.Speed):null;
        public IEnumerable<Type> ValueTypes(Type resource){if(resource.IsAssignableFrom(typeof(GameplaySpineAnimationAsset)))yield return typeof(GameplaySpineAnimationData);}
        public string Name(Type type)=>type==typeof(GameplaySpineAnimationData)?"Spine 动画":null;
        public bool SupportsControl(SerializedProperty property)
        {
            if(property.name!="Skeleton"&&property.name!="AnimationName"&&property.name!="SkinNames")return false;
            int dot=property.propertyPath.LastIndexOf('.');
            return dot>=0&&property.serializedObject.FindProperty(property.propertyPath.Substring(0,dot))?.managedReferenceValue is GameplaySpineAnimationData;
        }
        public VisualElement CreateControl(SerializedProperty property,string label,Func<bool> canWrite,out Action refresh)
        {
            var location=new Location(property,canWrite);
            var root=new VisualElement{style={marginBottom=2}};
            var error=new HelpBox("",HelpBoxMessageType.Warning);error.style.display=DisplayStyle.None;
            bool Active()=>canWrite()&&location.Path!=null&&!EditorApplication.isPlayingOrWillChangePlaymode;
            if(property.name=="Skeleton"){
                var row=new VisualElement{style={flexDirection=FlexDirection.Row,alignItems=Align.Center}};
                row.Add(new Label(label){style={minWidth=70,maxWidth=92}});
                var choose=new Button(()=>GameplaySpineSkeletonPicker.Open(value=>location.Choose(value),Active)){name="SafeSkeletonSearch",text="安全搜索骨骼…",style={flexGrow=1,flexShrink=1,minWidth=40,overflow=Overflow.Hidden,textOverflow=TextOverflow.Ellipsis,whiteSpace=WhiteSpace.NoWrap}};
                row.Add(choose);SkeletonDataAsset current=null;
                row.Add(new Button(()=>{if(current)EditorGUIUtility.PingObject(current);}){text="◎",tooltip="定位当前骨骼",style={width=22}});
                row.Add(new Button(()=>{if(Active())location.Choose(null);}){text="×",tooltip="清空骨骼、旧动画和皮肤组合",style={width=22}});
                row.RegisterCallback<DragUpdatedEvent>(evt=>{DragAndDrop.visualMode=Active()&&DragAndDrop.objectReferences.OfType<SkeletonDataAsset>().Any()?DragAndDropVisualMode.Link:DragAndDropVisualMode.Rejected;evt.StopPropagation();});
                row.RegisterCallback<DragPerformEvent>(evt=>{var value=DragAndDrop.objectReferences.OfType<SkeletonDataAsset>().FirstOrDefault();if(value&&Active()){DragAndDrop.AcceptDrag();location.Choose(value);}evt.StopPropagation();});
                root.Add(row);root.Add(error);
                refresh=()=>{
                    string path=location.Path;if(path==null){root.SetEnabled(false);return;}
                    using(var so=new SerializedObject(location.Owner))current=so.FindProperty(path).objectReferenceValue as SkeletonDataAsset;
                    choose.text=current?current.name+" …":"安全搜索骨骼…";choose.tooltip=current?AssetDatabase.GetAssetPath(current):"按名字/路径搜索，或从 Project 拖入；不打开原生资源大列表";
                };
            }else if(property.name=="SkinNames"){
                var filter=new TextField("筛选皮肤");filter.labelElement.style.minWidth=70;filter.labelElement.style.maxWidth=92;root.Add(filter);
                string[] selected=Array.Empty<string>(),all=Array.Empty<string>();
                var choose=new Button(()=>{
                    var menu=new GenericMenu();
                    foreach(string skin in all.Where(n=>n.IndexOf(filter.value??"",StringComparison.OrdinalIgnoreCase)>=0)){
                        string item=skin;menu.AddItem(new GUIContent(item),selected.Contains(item),()=>{
                            if(!Active())return;string path=location.Path;
                            using(var so=new SerializedObject(location.Owner)){
                                var field=so.FindProperty(path);int at=-1;
                                for(int i=0;i<field.arraySize;i++)if(field.GetArrayElementAtIndex(i).stringValue==item){at=i;break;}
                                if(at>=0)field.DeleteArrayElementAtIndex(at);else {int index=field.arraySize++;field.GetArrayElementAtIndex(index).stringValue=item;}
                                GameplaySkillCardFields.Apply(so,(GameplaySkillDocumentAsset)location.Owner,path,out string message);error.text=message??"";error.style.display=message==null?DisplayStyle.None:DisplayStyle.Flex;
                            }
                        });
                    }
                    if(all.Length==0)menu.AddDisabledItem(new GUIContent("先选择有皮肤的实际骨骼"));menu.ShowAsContext();
                });root.Add(choose);root.Add(error);
                refresh=()=>{
                    string path=location.Path;if(path==null){root.SetEnabled(false);return;}
                    using(var so=new SerializedObject(location.Owner)){
                        var field=so.FindProperty(path);selected=Enumerable.Range(0,field.arraySize).Select(i=>field.GetArrayElementAtIndex(i).stringValue).ToArray();
                        string prefix=path.Substring(0,path.Length-"SkinNames".Length);
                        all=GameplaySpineSourceInspector.Index(so.FindProperty(prefix+"Skeleton").objectReferenceValue as SkeletonDataAsset)?.Skins??Array.Empty<string>();
                    }
                    choose.text=label+"（"+selected.Length+"）· 选择 / 移除…";choose.tooltip=string.Join(" / ",selected);
                };
            }else{
                var filter=new TextField("筛选动画"){name="SpineAnimationFilter"};
                var names=new DropdownField(label,new List<string>{""},0){name="SpineAnimationChoice"};
                filter.labelElement.style.minWidth=70;filter.labelElement.style.maxWidth=92;names.labelElement.style.minWidth=70;names.labelElement.style.maxWidth=92;
                root.Add(filter);root.Add(names);root.Add(error);
                string selected="",query=null;string[] all=Array.Empty<string>(),previous=null;
                void Choices(){
                    if(ReferenceEquals(previous,all)&&query==filter.value)return;previous=all;query=filter.value;
                    var visible=all.Where(n=>n.IndexOf(query??"",StringComparison.OrdinalIgnoreCase)>=0).ToList();
                    if(!visible.Contains(selected))visible.Insert(0,selected);
                    names.choices=visible;names.SetValueWithoutNotify(selected);
                }
                filter.RegisterValueChangedCallback(_=>Choices());
                names.RegisterValueChangedCallback(evt=>{
                    if(!Active()||!all.Contains(evt.newValue))return;
                    string path=location.Path;using(var so=new SerializedObject(location.Owner)){
                        so.FindProperty(path).stringValue=evt.newValue;
                        bool applied=GameplaySkillCardFields.Apply(so,(GameplaySkillDocumentAsset)location.Owner,path,out string message);
                        error.text=message??"";error.style.display=message==null?DisplayStyle.None:DisplayStyle.Flex;
                        if(!applied)names.SetValueWithoutNotify(selected);
                    }
                });
                refresh=()=>{
                    string path=location.Path;if(path==null){root.SetEnabled(false);return;}
                    using(var so=new SerializedObject(location.Owner)){
                        var field=so.FindProperty(path);selected=field.stringValue;
                        string prefix=path.Substring(0,path.Length-"AnimationName".Length);
                        all=GameplaySpineSourceInspector.Index(so.FindProperty(prefix+"Skeleton").objectReferenceValue as SkeletonDataAsset)?.Names??Array.Empty<string>();
                    }
                    Choices();if(!names.choices.Contains(selected)){previous=null;Choices();}names.SetValueWithoutNotify(selected);names.tooltip=selected;
                };
            }
            return root;
        }
        public bool DrawSourceLink(SerializedProperty root,SerializedProperty field)
        {
            if(!(root.managedReferenceValue is GameplaySpineAnimationData)||field.name!="Skeleton")return false;
            EditorGUILayout.LabelField("骨骼来源",field.objectReferenceValue?field.objectReferenceValue.name:"未选择");
            if(GUILayout.Button("更换骨骼 · 安全搜索…")){
                var location=new Location(field);GameplaySpineSkeletonPicker.Open(location.Choose,WindowGuard(location));
            }
            return true;
        }
        private static readonly Dictionary<string,string> Filters=new Dictionary<string,string>();
        private sealed class SkinState{internal string Filter="";internal bool Open;internal int Page;}
        private static readonly Dictionary<string,SkinState> Skins=new Dictionary<string,SkinState>();
        public bool DrawField(SerializedProperty root,SerializedProperty field)
        {
            if(!(root.managedReferenceValue is GameplaySpineAnimationData))return false;
            if(field.name=="Skeleton"){
                // The embedded Inspector uses the same safe search and pending-buffer clear
                // as its card. The standalone SO Inspector keeps its existing entry point.
                var location=new Location(field);
                GameplaySpineSkeletonPicker.Draw(field,value=>location.Choose(value),WindowGuard(location));return true;
            }
            var skeleton=root.FindPropertyRelative("Skeleton").objectReferenceValue as SkeletonDataAsset;
            if(field.name=="AnimationName"){
                if(!skeleton){EditorGUILayout.HelpBox("先选择实际 Spine 骨骼资源。",MessageType.Info);return true;}
                string key=root.serializedObject.targetObject.GetInstanceID()+root.propertyPath;
                Filters.TryGetValue(key,out string filter);Filters[key]=EditorGUILayout.TextField("搜索动画",filter??"");
                var names=GameplaySpineSourceInspector.Index(skeleton)?.Names??Array.Empty<string>();
                var visible=names.Where(n=>n.IndexOf(Filters[key],StringComparison.OrdinalIgnoreCase)>=0).ToArray();
                if(visible.Length>0){int index=Array.IndexOf(visible,field.stringValue);int selected=EditorGUILayout.Popup("动画",index,visible);if(selected>=0&&selected!=index)field.stringValue=visible[selected];}
                else EditorGUILayout.LabelField("动画",field.stringValue);
                if(!names.Contains(field.stringValue))EditorGUILayout.HelpBox("请选择骨骼中真实存在的动画。",MessageType.Warning);return true;
            }
            if(field.name!="SkinNames")return false;
            string skinKey=root.serializedObject.targetObject.GetInstanceID()+root.propertyPath;
            if(!Skins.TryGetValue(skinKey,out var state))Skins[skinKey]=state=new SkinState();
            state.Open=EditorGUILayout.Foldout(state.Open,"皮肤组合（可选） · "+field.arraySize,true);if(!state.Open)return true;
            string next=EditorGUILayout.TextField("搜索皮肤",state.Filter);if(next!=state.Filter){state.Filter=next;state.Page=0;}
            var skins=(GameplaySpineSourceInspector.Index(skeleton)?.Skins??Array.Empty<string>()).Where(s=>s.IndexOf(state.Filter,StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            int pages=Math.Max(1,(skins.Length+49)/50);state.Page=Mathf.Clamp(state.Page,0,pages-1);
            GUILayout.BeginHorizontal();if(GUILayout.Button("上一页"))state.Page=Math.Max(0,state.Page-1);GUILayout.Label((state.Page+1)+" / "+pages);if(GUILayout.Button("下一页"))state.Page=Math.Min(pages-1,state.Page+1);GUILayout.EndHorizontal();
            foreach(string skin in skins.Skip(state.Page*50).Take(50)){
                int at=-1;for(int i=0;i<field.arraySize;i++)if(field.GetArrayElementAtIndex(i).stringValue==skin){at=i;break;}
                bool chosen=EditorGUILayout.ToggleLeft(skin,at>=0);
                if(chosen&&at<0){int index=field.arraySize++;field.GetArrayElementAtIndex(index).stringValue=skin;}
                else if(!chosen&&at>=0)field.DeleteArrayElementAtIndex(at);
            }
            return true;
        }
    }
}
