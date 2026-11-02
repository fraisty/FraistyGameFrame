using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Authoring;
using UnityEditor;

namespace FRFrame.Gameplay.Unity.Editor
{
    public sealed partial class GameplayAnimationPreviewSession
    {
        private GameplayActorProfileAsset _casterProfile,_targetProfile;
        private bool _hasExplicitValueContext;
        private GameplayContextValues _previewInputs=GameplayContextValues.Empty;
        private string _inputKey="Data.Preview.Speed";
        private float _inputValue=1;
        private bool _showValueControls;
        /// <summary>Explicit preview-only input snapshots. Does not alter a Source or install gameplay
        /// containers; speed/level/variants still evaluate the exact Runtime definition.</summary>
        public void SetValueContext(GameplayActorProfileAsset caster,GameplayActorProfileAsset target,GameplayContextValues inputs)
        {_hasExplicitValueContext=true;_casterProfile=caster;_targetProfile=target;_previewInputs=inputs??GameplayContextValues.Empty;Release();Load();Seek(_time);}
        private GameplayEffectApplicationContext PreviewApplication()
        {
            var caster=new ValueTarget(new GameplaySourceId(1),_casterProfile);
            var target=new ValueTarget(new GameplaySourceId(2),_targetProfile??_casterProfile);
            var values=GameplayContextValues.Empty;
            foreach(var input in _document?.Defaults??Array.Empty<GameplayScalarInputAuthoring>())
                if(input!=null)values=values.WithValue(input.Key.Build(),input.Value);
            values=values.Merge(_previewInputs);
            var cast=new GameplayContext(caster.TargetId,new GameplayTargetData(target.TargetId),values:values,abilityLevel:_level);
            return new GameplayEffectApplicationContext(cast,target,cast.Values.AsSetByCallerValues(),caster.Attributes,caster.TargetId,instigatorTarget:caster);
        }
        private void DrawValueControls()
        {
            EditorGUILayout.LabelField("实际Source速度",_speed.ToString("0.###"));
            if(_captureVariantRoll)
            {
                _previewInputs.TryGetValue(GameplayAnimationVariantSelector.RollKey,out var roll);
                float selected=EditorGUILayout.Slider("变体采样（固定，0≤值<1）",roll,0,.99999994f);
                if(selected!=roll)SetValueContext(_casterProfile,_targetProfile,_previewInputs.WithValue(GameplayAnimationVariantSelector.RollKey,selected));
            }
            _showValueControls=EditorGUILayout.Foldout(_showValueControls,"预览数值 / 输入",true);if(!_showValueControls)return;
            var caster=(GameplayActorProfileAsset)EditorGUILayout.ObjectField("预览Caster数值配置",_casterProfile,typeof(GameplayActorProfileAsset),false);
            var target=(GameplayActorProfileAsset)EditorGUILayout.ObjectField("预览Target数值配置",_targetProfile,typeof(GameplayActorProfileAsset),false);
            if(caster!=_casterProfile||target!=_targetProfile)SetValueContext(caster,target,_previewInputs);
            _inputKey=EditorGUILayout.TextField("预览输入Key",_inputKey);_inputValue=EditorGUILayout.FloatField("预览输入值",_inputValue);
            if(UnityEngine.GUILayout.Button("应用预览输入（不修改资源）"))
                try{SetValueContext(_casterProfile,_targetProfile,_previewInputs.WithValue(new GameplayTag(_inputKey),_inputValue));}catch(Exception ex){_error=ex.Message;}
            if(UnityEngine.GUILayout.Button("清除预览输入"))SetValueContext(_casterProfile,_targetProfile,GameplayContextValues.Empty);
        }
        private sealed class ValueTarget:IGameplayResourceTarget
        {
            public GameplaySourceId TargetId {get;}
            public AttributeSet Attributes {get;}=new AttributeSet();
            public GameplayTagContainer Tags {get;}=new GameplayTagContainer();
            public IGameplayResourceStore Resources=>Attributes;
            internal ValueTarget(GameplaySourceId id,GameplayActorProfileAsset profile)
            {
                TargetId=id;
                if(profile!=null)foreach(var entry in profile.Attributes??Array.Empty<GameplayInitialAttributeAuthoring>())
                    if(entry!=null)Attributes.Register(entry.Attribute.Build(),entry.Value);
            }
        }
    }
}
