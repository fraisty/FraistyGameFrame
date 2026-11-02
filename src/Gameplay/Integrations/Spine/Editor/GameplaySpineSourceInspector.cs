using System.Linq;
using System.Collections.Generic;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using FRFrame.Gameplay.Editor;
using FRFrame.Gameplay.Authoring;
using Spine.Unity;
using UnityEditor;
using UnityEngine;

namespace FRFrame.Gameplay.Spine.Editor
{
    /// <summary>Author-facing picker over the actual Skeleton animations and source properties.</summary>
    public sealed class GameplaySpineSourceInspector:IGameplayAnimationSourceInspector,IGameplayCompactAnimationSourceInspector
    {
        internal sealed class ClipIndex
        {
            internal string[] Names;
            internal string[] Skins;
            internal Dictionary<string,float> Durations;
        }
        // Only explicitly selected skeletons enter this cache. Project changes invalidate it;
        // IMGUI Repaint and ClipDuration share the same single parse.
        private static readonly Dictionary<SkeletonDataAsset,ClipIndex> Clips=new Dictionary<SkeletonDataAsset,ClipIndex>();
        static GameplaySpineSourceInspector(){EditorApplication.projectChanged+=()=>Clips.Clear();}
        internal static ClipIndex Index(SkeletonDataAsset skeleton)
        {
            if(!skeleton)return null;
            if(Clips.TryGetValue(skeleton,out var index))return index;
            var data=skeleton.GetSkeletonData(false);
            index=new ClipIndex{Names=data==null?new string[0]:data.Animations.Select(a=>a.Name).ToArray(),Skins=data==null?new string[0]:data.Skins.Select(s=>s.Name).ToArray(),
                Durations=data==null?new Dictionary<string,float>():data.Animations.ToDictionary(a=>a.Name,a=>a.Duration)};
            Clips[skeleton]=index;return index;
        }
        private string _filter="";
        private Object _filterSource;
        private bool _skinsOpen;
        private string _skinFilter="";
        private Vector2 _skinScroll;
        public void DrawCompact(SerializedObject source)
        {
            if(_filterSource!=source.targetObject){_filterSource=source.targetObject;_filter="";}
            var skeletonProperty=source.FindProperty("Skeleton");
            GameplaySpineSkeletonPicker.Draw(skeletonProperty);
            var skeleton=source.FindProperty("Skeleton").objectReferenceValue as SkeletonDataAsset;
            var name=source.FindProperty("AnimationName");
            string[] names=Index(skeleton)?.Names??new string[0];
            _filter=EditorGUILayout.TextField(new GUIContent("筛选当前骨骼的动画名称","仅过滤下方动画列表；清空显示全部，不修改已选动画。"),_filter);
            var visible=names.Where(n=>string.IsNullOrEmpty(_filter)||n.IndexOf(_filter,System.StringComparison.OrdinalIgnoreCase)>=0).ToArray();
            if(visible.Length>0){
                int selected=System.Array.IndexOf(visible,name.stringValue);
                int next=EditorGUILayout.Popup("动画",selected,visible);
                if(next>=0&&next!=selected)name.stringValue=visible[next];
            }
            else EditorGUILayout.LabelField("动画",name.stringValue);
            if(!names.Contains(name.stringValue))EditorGUILayout.HelpBox("所选骨骼中不存在动画「"+name.stringValue+"」。请选择实际动画。",MessageType.Error);
            var skins=Index(skeleton)?.Skins??new string[0];
            if(skins.Length>1){
                var chosen=source.FindProperty("SkinNames");
                _skinsOpen=EditorGUILayout.Foldout(_skinsOpen,"皮肤组合（可选） · "+chosen.arraySize+" 项",true);
                if(_skinsOpen){
                    GUILayout.Label("未选组合时独立预览使用骨骼默认皮肤；运行载体保留已有皮肤。",EditorStyles.wordWrappedMiniLabel);
                    _skinFilter=EditorGUILayout.TextField("搜索皮肤",_skinFilter);
                    _skinScroll=EditorGUILayout.BeginScrollView(_skinScroll,GUILayout.Height(110));
                    foreach(string skin in skins.Where(s=>s.IndexOf(_skinFilter,System.StringComparison.OrdinalIgnoreCase)>=0)){
                        int at=-1;for(int i=0;i<chosen.arraySize;i++)if(chosen.GetArrayElementAtIndex(i).stringValue==skin){at=i;break;}
                        bool selected=EditorGUILayout.ToggleLeft(skin,at>=0);
                        if(selected&&at<0){int index=chosen.arraySize++;chosen.GetArrayElementAtIndex(index).stringValue=skin;}
                        else if(!selected&&at>=0)chosen.DeleteArrayElementAtIndex(at);
                    }
                    EditorGUILayout.EndScrollView();
                }
            }
        }
        public void DrawPlayback(SerializedObject source)
        {
            EditorGUILayout.PropertyField(source.FindProperty("Loop"),new GUIContent("循环播放 (Loop)"));
            var speed=source.FindProperty("Speed");
            if(speed.managedReferenceValue is GameplayConstantFloatValueAuthoring){
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.PropertyField(speed.FindPropertyRelative("Value"),new GUIContent("播放速度 (Speed)"));
                if(GUILayout.Button(new GUIContent("ƒx","改用数值公式或其它正式数值配置"),GUILayout.Width(30)))
                    GameplayAuthoringTypePicker.Property(typeof(GameplayFloatValueAuthoring),speed);
                EditorGUILayout.EndHorizontal();
            }else GameplayAuthoringInspector.Field(speed,new GUIContent("播放速度 (Speed)"));
            EditorGUILayout.PropertyField(source.FindProperty("TrackIndex"),new GUIContent("Spine 轨道"));
            EditorGUILayout.PropertyField(source.FindProperty("BlendDuration"),new GUIContent("混合秒数"));
        }
        public bool Supports(Object source)=>source is GameplaySpineAnimationAsset;
        public void DrawCore(SerializedObject source)
        { DrawCompact(source);DrawPlayback(source); }
        public void DrawAdvanced(SerializedObject source)
        {
            string[] paths={"SkinNames","Material","Variants","WeightedVariants","PreviewInitialAnimation"};
            string[] labels={"皮肤组合","材质","等级变体","随机变体权重","预览初始动画"};
            for(int i=0;i<paths.Length;i++){var p=source.FindProperty(paths[i]);if(p!=null)EditorGUILayout.PropertyField(p,new GUIContent(labels[i]),true);}
        }
        public string Summary(Object source){var a=(GameplaySpineAnimationAsset)source;return "Spine · "+a.AnimationName+" · Track"+a.TrackIndex;}
        public string[] SourceFields(Object source)=>new[]{"Skeleton","AnimationName","Loop","Speed","TrackIndex","BlendDuration","SkinNames","Material","Variants","WeightedVariants","PreviewInitialAnimation","RestorePrevious"};
        public float ClipDuration(Object source){var a=(GameplaySpineAnimationAsset)source;var index=Index(a.Skeleton);return index!=null&&index.Durations.TryGetValue(a.AnimationName??"",out float duration)?duration:0;}
        public string BindingWarning(Object source,GameObject subject)
        {
            if(subject==null)return null;
            var expected=((GameplaySpineAnimationAsset)source).Skeleton;var actual=subject.GetComponentInChildren<SkeletonAnimation>(true)?.skeletonDataAsset;
            return expected==actual?null:"Source骨骼「"+(expected?expected.name:"未指定")+"」与Scene载体「"+(actual?actual.name:"没有Spine")+"」不匹配。";
        }
    }
    [CustomPropertyDrawer(typeof(GameplaySpineAnimationAsset.LevelVariant))]
    public sealed class GameplaySpineAnimationVariantDrawer:FRFrame.Gameplay.Editor.GameplayStructuredAuthoringDrawer { }
}
