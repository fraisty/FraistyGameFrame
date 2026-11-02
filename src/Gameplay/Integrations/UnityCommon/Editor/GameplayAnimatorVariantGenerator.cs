using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

namespace FRFrame.Gameplay.Unity.Editor
{
    /// <summary>Scans actual controller states into formal Source variants. Preview the impact first;
    /// applying records one Undo and never edits the controller, clips or prefab.</summary>
    public sealed class GameplayAnimatorVariantGenerator:EditorWindow
    {
        private GameplayUnityAnimationAsset _source;
        private GameplayUnityAnimationAsset.LevelVariant[] _preview;
        private string _error;
        private Vector2 _scroll;
        [MenuItem("FRFrame/Gameplay/Animation/Animator Variant Generator")]
        public static void Open()=>GetWindow<GameplayAnimatorVariantGenerator>("动画变体扫描");
        public static GameplayUnityAnimationAsset.LevelVariant[] Scan(GameplayUnityAnimationAsset source)
        {
            if(source==null||source.PreviewPrefab==null)throw new InvalidOperationException("选择有真实Prefab的Animator动画Source。");
            var animator=source.PreviewPrefab.GetComponentInChildren<Animator>(true);
            var controller=animator==null?null:animator.runtimeAnimatorController as AnimatorController;
            if(controller==null)throw new InvalidOperationException("需要实际AnimatorController；OverrideController请先选择其原Controller的Source。");
            if(source.Layer<0||source.Layer>=controller.layers.Length)throw new InvalidOperationException("Source Layer不在Controller中。");
            var layer=controller.layers[source.Layer];var result=new List<GameplayUnityAnimationAsset.LevelVariant>();
            Collect(layer.stateMachine,layer.name,result);
            if(result.Count==0)throw new InvalidOperationException("本层无直接AnimationClip状态；BlendTree需单独配置，扫描不会猜测其播放语义。");
            return result.ToArray();
        }
        private static void Collect(AnimatorStateMachine machine,string prefix,List<GameplayUnityAnimationAsset.LevelVariant> result)
        {
            foreach(var state in machine.states)if(state.state.motion is AnimationClip clip&&clip.length>0)
                result.Add(new GameplayUnityAnimationAsset.LevelVariant{MinimumLevel=1,Clip=clip,StateName=prefix+"."+state.state.name,Weight=1});
            foreach(var nested in machine.stateMachines)Collect(nested.stateMachine,prefix+"."+nested.stateMachine.name,result);
        }
        public static void Apply(GameplayUnityAnimationAsset source,GameplayUnityAnimationAsset.LevelVariant[] preview)
        {
            if(EditorApplication.isPlayingOrWillChangePlaymode)throw new InvalidOperationException("退出Play Mode后应用。");
            var actual=Scan(source);
            if(preview==null||preview.Length!=actual.Length)throw new InvalidOperationException("Controller已变化，请重新扫描。");
            for(int i=0;i<actual.Length;i++)if(preview[i]==null||preview[i].Clip!=actual[i].Clip||preview[i].StateName!=actual[i].StateName)
                throw new InvalidOperationException("Controller已变化，请重新扫描。");
            Undo.RecordObject(source,"生成正式动画变体");source.Variants=actual;source.WeightedVariants=true;
            EditorUtility.SetDirty(source);GameplayAuthoringSourceChanges.Notify(source);
        }
        private void OnGUI()
        {
            var selected=(GameplayUnityAnimationAsset)EditorGUILayout.ObjectField("正式动画Source",_source,typeof(GameplayUnityAnimationAsset),false);
            if(selected!=_source){_source=selected;_preview=null;_error=null;}
            EditorGUILayout.HelpBox("扫描实际Layer与嵌套状态路径。应用会替换此Source的Variants（Lv1、权重1），保留主动画；可一次Undo。不会修改Controller或动画资源。",MessageType.Info);
            if(GUILayout.Button("扫描 / Dry-run"))try{_preview=Scan(_source);_error=null;}catch(Exception ex){_preview=null;_error=ex.Message;}
            if(_error!=null)EditorGUILayout.HelpBox(_error,MessageType.Warning);
            if(_preview==null)return;
            EditorGUILayout.LabelField("仅影响Source",AssetDatabase.GetAssetPath(_source));
            _scroll=EditorGUILayout.BeginScrollView(_scroll);
            foreach(var variant in _preview)EditorGUILayout.LabelField(variant.StateName,variant.Clip.name);
            EditorGUILayout.EndScrollView();
            using(new EditorGUI.DisabledScope(EditorApplication.isPlayingOrWillChangePlaymode))
                if(GUILayout.Button("应用到正式Source（一次Undo）"))try{Apply(_source,_preview);_error=null;}catch(Exception ex){_error=ex.Message;}
        }
    }
}
