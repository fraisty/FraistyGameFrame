using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;

namespace FRFrame.Gameplay.Unity.Editor
{
    /// <summary>Unity backends expose their real plain-data sources to the shared V2 editor.</summary>
    public sealed class GameplaySkillUnityValueEditor : IGameplaySkillValueEditor, IGameplaySkillPreviewFactory, IGameplaySkillValueDescription, IGameplaySkillValueSections, IGameplaySkillValueDuration, IGameplaySkillCardFieldProvider
    {
        public IEnumerable<GameplaySkillCardField> CardFields(object values)
        {
            if(values is GameplayAnimationData){yield return new GameplaySkillCardField("Channel","播放通道");yield return new GameplaySkillCardField("Speed","速度",true);yield return new GameplaySkillCardField("Loop","循环");}
            if(values is GameplayUnityAnimationData){yield return new GameplaySkillCardField("Clip","动画剪辑");yield return new GameplaySkillCardField("StateName","状态名");}
            if(values is GameplayPresentationCueData cue){yield return new GameplaySkillCardField("Kind","表现类型");yield return new GameplaySkillCardField(cue.Kind==GameplayPresentationCueAsset.CueKind.Audio?"Clip":"Prefab","资源");yield return new GameplaySkillCardField("Duration","持续秒数",true);yield return new GameplaySkillCardField("Origin","播放位置");if(cue.Origin!=GameplayPresentationCueAsset.PositionSource.ContextPoint){yield return new GameplaySkillCardField("Binding","挂点（空为根）");yield return new GameplaySkillCardField("Follow","跟随位置");}}
            if(values is GameplayFloatingTextData text){yield return new GameplaySkillCardField("Prefab","战斗结算飘字");yield return new GameplaySkillCardField("Duration","持续秒数");yield return new GameplaySkillCardField("FixedPoint","固定世界位置");if(!text.FixedPoint){yield return new GameplaySkillCardField("Binding","结果目标挂点");yield return new GameplaySkillCardField("Follow","跟随结果目标");}yield return new GameplaySkillCardField("RequestedHealing","治疗显示请求值");}
        }
        public bool TryReadDuration(object values,out string path,out float seconds){
            if(values is GameplayUnityAnimationData animation&&animation.Clip&&animation.Speed is GameplayConstantFloatValueAuthoring speed&&speed.Value>0){path=null;seconds=animation.Clip.length/speed.Value;return true;}
            path=null;seconds=0;if(values is GameplayPresentationCueData cue&&cue.Duration is GameplayConstantFloatValueAuthoring duration){path="Duration";seconds=duration.Value;return true;}return false;
        }
        public IEnumerable<GameplayBusinessInspectorGroup> Sections(object values){
            if(values is GameplayAnimationData){
                yield return new GameplayBusinessInspectorGroup(null,"后端 / 模型 / 动画","选择实际资源后筛选真实动画；不创建资源副本。","Backend","Skeleton","PreviewPrefab","ModelPrefab","Clip","Motion","AnimationName","StateName","TrackIndex","Layer","PreviewInitialAnimation","Material","SkinNames");
                yield return new GameplayBusinessInspectorGroup(null,"播放 / 混合 / 所有权","预览与 Runtime 共用这些值；Loop 和 Speed 不改变正式 End 时点。","Loop","Speed","BlendDuration","BlendSeconds","Channel","Priority","RestorePrevious");
                yield return new GameplayBusinessInspectorGroup(null,"等级变体 / 权重","保持正式资源的等级选择及权重规则。","Variants","WeightedVariants");
            }
            if(values is GameplayFloatingTextData){
                yield return new GameplayBusinessInspectorGroup(null,"战斗结果 / 锚定","读取已经提交的 combat feedback；不使用任意上下文 Key。","Prefab","FixedPoint","Point","Binding","Follow","Offset","RequestedHealing","SuppressEmptyHealing");
                yield return new GameplayBusinessInspectorGroup(null,"格式 / 颜色 / 动效",null,"Duration","DamagePrefix","NumberFormat","CriticalPrefix","MissText","ImmuneText","ShieldPrefix","DamageColor","CriticalColor","SupportColor","MissColor","RiseSpeed","Acceleration","VelocitySpread","FadeStartRatio");
            }
            if(values is GameplayPresentationCueData){
                yield return new GameplayBusinessInspectorGroup(null,"特效 / 声音资源",null,"Kind","Prefab","Clip");
                yield return new GameplayBusinessInspectorGroup(null,"位置 / 挂点 / 朝向",null,"Origin","Binding","Offset","Scale","Follow","MirrorFacing");
                yield return new GameplayBusinessInspectorGroup(null,"持续 / 释放 / 音频","OwnedByAbility 可在配置时长之前释放；时长不等于技能结束时点。","Duration","OwnedByAbility","Volume","Pitch","AudioLoop");
            }
        }
        public string Summary(object values) {
            if(values is GameplayFloatingTextData text)return "真实战斗结算：伤害 / 治疗 / 暴击 / 护盾\n"+(text.FixedPoint?"固定世界位置":(string.IsNullOrEmpty(text.Binding)?"结果目标根节点":"结果目标挂点 "+text.Binding)+(text.Follow?" · 跟随":" · 不跟随"));
            if(values is GameplayAnimationData animation)return animation.BackendName+" · "+animation.ClipLabel+" · 通道 "+animation.Channel+"\n"+(animation.Loop?"循环":"单次")+" · 速度 "+GameplaySkillFieldSchema.FloatSummary(animation.Speed);
            if(values is GameplayPresentationCueData cue)return (cue.Kind==GameplayPresentationCueAsset.CueKind.Audio?"音效":"特效")+" · "+(cue.Kind==GameplayPresentationCueAsset.CueKind.Audio?(cue.Clip?cue.Clip.name:"待选择音频"):(cue.Prefab?cue.Prefab.name:"待选择特效"))+"\n"+(cue.Origin==GameplayPresentationCueAsset.PositionSource.ContextPoint?"固定上下文地点":(cue.Origin==GameplayPresentationCueAsset.PositionSource.Owner?"施法者":"主目标")+" · "+(string.IsNullOrEmpty(cue.Binding)?"根节点":cue.Binding)+(cue.Follow?" · 跟随":" · 不跟随"))+"\n时长 "+GameplaySkillFieldSchema.FloatSummary(cue.Duration)+"s";
            return null;
        }
        public IEnumerable<Type> ValueTypes(Type resource)
        {
            if (resource.IsAssignableFrom(typeof(GameplayUnityAnimationAsset))) yield return typeof(GameplayUnityAnimationData);
            if (resource.IsAssignableFrom(typeof(GameplayPresentationCueAsset))) yield return typeof(GameplayPresentationCueData);
            if (resource.IsAssignableFrom(typeof(GameplayFloatingTextAsset))) yield return typeof(GameplayFloatingTextData);
            if (resource.IsAssignableFrom(typeof(GameplayUnityProjectileAsset))) yield return typeof(GameplayUnityProjectileData);
            if (resource.IsAssignableFrom(typeof(GameplayUnityAreaAsset))) yield return typeof(GameplayUnityAreaData);
            if (resource.IsAssignableFrom(typeof(GameplayUnitySummonAsset))) yield return typeof(GameplayUnitySummonData);
        }
        public string Name(Type type) => type == typeof(GameplayUnityAnimationData) ? "Animator / Playables" : type == typeof(GameplayPresentationCueData) ? "特效 / 音效" :
            type == typeof(GameplayFloatingTextData) ? "战斗飘字" : type == typeof(GameplayUnityProjectileData) ? "Unity 弹体" : type == typeof(GameplayUnityAreaData) ? "Unity 区域" : type == typeof(GameplayUnitySummonData) ? "Unity 召唤" : null;
        public bool DrawField(SerializedProperty root, SerializedProperty field) => false;
        public bool Supports(object values) => values is GameplayAnimationData || values is GameplayPresentationCueData || values is GameplayFloatingTextData;
        public IGameplaySkillPreviewSession Create(GameplaySkillDocumentAsset document, object values, float startSeconds)
        { var session = new GameplayAnimationPreviewSession(); session.UpdateValues(document, values, startSeconds); return session; }
    }
}
