using System.Collections.Generic;
using System.Linq;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FRFrame.Gameplay.Unity.Editor
{
    public sealed class GameplayUnitySourceInspector:IGameplayAnimationSourceInspector
    {
        public bool Supports(Object source)=>source is GameplayUnityAnimationAsset||source?.GetType().Name=="GameplayLive2DAnimationAsset";
        public void DrawCore(SerializedObject source)
        {
            if(source.targetObject is GameplayUnityAnimationAsset asset){
                EditorGUILayout.PropertyField(source.FindProperty("Backend"),new GUIContent("播放后端"));
                EditorGUILayout.PropertyField(source.FindProperty("PreviewPrefab"),new GUIContent("载体 Prefab / Controller"));
                if(asset.Backend==GameplayUnityAnimationAsset.PlaybackBackend.AnimatorState){
                    var controller=asset.PreviewPrefab?.GetComponentInChildren<Animator>(true)?.runtimeAnimatorController as AnimatorController;
                    if(controller==null){EditorGUILayout.HelpBox("请选择含真实AnimatorController的载体Prefab。",MessageType.Warning);return;}
                    var layer=source.FindProperty("Layer");var names=controller.layers.Select(l=>l.name).ToArray();
                    if(names.Length==0){EditorGUILayout.HelpBox("Controller没有动画层。",MessageType.Warning);return;}
                    int valid=layer.intValue>=0&&layer.intValue<names.Length?layer.intValue:-1;
                    int selected=EditorGUILayout.Popup("Animator 层",valid,names);
                    if(selected!=valid&&selected>=0)layer.intValue=selected;
                    if(layer.intValue<0||layer.intValue>=names.Length){EditorGUILayout.HelpBox("请选择Controller中实际存在的层。",MessageType.Warning);return;}
                    var states=new List<string>();Collect(controller.layers[layer.intValue].stateMachine,names[layer.intValue],states);
                    var state=source.FindProperty("StateName");int current=states.IndexOf(state.stringValue);
                    int next=EditorGUILayout.Popup("真实 State",current,states.ToArray());
                    if(next>=0&&next!=current){state.stringValue=states[next];var selectedState=FindState(controller.layers[layer.intValue].stateMachine,names[layer.intValue],state.stringValue);if(selectedState?.motion is AnimationClip selectedClip)source.FindProperty("Clip").objectReferenceValue=selectedClip;}
                    if(current<0)EditorGUILayout.HelpBox("Controller中未找到「"+state.stringValue+"」。请选择真实State。",MessageType.Warning);
                    var actual=FindState(controller.layers[layer.intValue].stateMachine,names[layer.intValue],state.stringValue)?.motion as AnimationClip;
                    if(actual!=null&&source.FindProperty("Clip").objectReferenceValue!=actual){
                        EditorGUILayout.HelpBox("所选State实际使用「"+actual.name+"」；当前Clip绑定不同。",MessageType.Warning);
                        if(GUILayout.Button("按真实State绑定Clip"))source.FindProperty("Clip").objectReferenceValue=actual;
                    }
                }
                EditorGUILayout.PropertyField(source.FindProperty("Clip"),new GUIContent("动画 Clip"));
            }else{
                EditorGUILayout.PropertyField(source.FindProperty("ModelPrefab"),new GUIContent("Cubism 模型 Prefab"));
                EditorGUILayout.PropertyField(source.FindProperty("Motion"),new GUIContent("Cubism 动画 Clip"));
                EditorGUILayout.HelpBox("同源Clip采样；不支持Cubism MotionFade混合。",MessageType.Info);
            }
        }
        private static void Collect(AnimatorStateMachine machine,string prefix,List<string> states)
        {foreach(var state in machine.states)states.Add(prefix+"."+state.state.name);foreach(var child in machine.stateMachines)Collect(child.stateMachine,prefix+"."+child.stateMachine.name,states);}
        private static AnimatorState FindState(AnimatorStateMachine machine,string prefix,string path)
        {foreach(var state in machine.states)if(prefix+"."+state.state.name==path)return state.state;foreach(var child in machine.stateMachines){var found=FindState(child.stateMachine,prefix+"."+child.stateMachine.name,path);if(found!=null)return found;}return null;}
        public void DrawAdvanced(SerializedObject source)
        {
            foreach(string path in new[]{"BlendDuration","Variants","WeightedVariants"}){
                var p=source.FindProperty(path);if(p!=null)FRFrame.Gameplay.Editor.GameplayAuthoringInspector.Field(p);
            }
        }
        public string[] SourceFields(Object source)=>source is GameplayUnityAnimationAsset?new[]{"Backend","PreviewPrefab","Layer","StateName","Clip","BlendDuration","Variants","WeightedVariants"}:new[]{"ModelPrefab","Motion"};
        public string Summary(Object source)
        {if(source is GameplayUnityAnimationAsset a)return a.Backend+" · "+(a.Backend==GameplayUnityAnimationAsset.PlaybackBackend.AnimatorState?a.StateName:a.Clip?.name);using(var so=new SerializedObject(source))return "Cubism · "+so.FindProperty("Motion").objectReferenceValue?.name;}
        public float ClipDuration(Object source)
        {if(source is GameplayUnityAnimationAsset a)return a.Clip?a.Clip.length:0;using(var so=new SerializedObject(source))return (so.FindProperty("Motion").objectReferenceValue as AnimationClip)?.length??0;}
        public string BindingWarning(Object source,GameObject subject)
        {if(subject==null||!(source is GameplayUnityAnimationAsset a))return null;return subject.GetComponentInChildren<Animator>(true)==null?"Scene载体没有Animator；Preview Prefab不替代实际Scene绑定。":null;}
    }
    [CustomPropertyDrawer(typeof(GameplayUnityAnimationAsset.LevelVariant))]
    public sealed class GameplayUnityAnimationVariantDrawer:FRFrame.Gameplay.Editor.GameplayStructuredAuthoringDrawer { }
}
