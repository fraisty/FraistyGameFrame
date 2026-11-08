using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Authoring.Graph;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;

namespace FRFrame.Gameplay.Unity.Editor
{
    /// <summary>Unity backends expose their real plain-data sources to the shared V2 editor.</summary>
    public sealed class GameplaySkillUnityValueEditor : IGameplaySkillValueEditor, IGameplaySkillPreviewFactory, IGameplaySkillValueDescription, IGameplaySkillValueSections, IGameplaySkillValueDuration, IGameplaySkillCardFieldProvider, IGameplaySkillValueCreationPresets
    {
        public IEnumerable<GameplaySkillCardField> CardFields(object values)
        {
            if(values is GameplayAnimationData){yield return new GameplaySkillCardField("Channel","播放通道");yield return new GameplaySkillCardField("Speed","速度",true);yield return new GameplaySkillCardField("Loop","循环");}
            if(values is GameplayUnityAnimationData){yield return new GameplaySkillCardField("Clip","动画剪辑");yield return new GameplaySkillCardField("StateName","状态名");}
            if(values is GameplayPresentationCueData cue){
                bool audio=cue.Kind==GameplayPresentationCueAsset.CueKind.Audio;
                yield return audio?new GameplaySkillCardField("Clip","音频片段"):new GameplaySkillCardField("Prefab","特效预制体");
                if(!audio)yield return new GameplaySkillCardField("RecycleWhenParticlesFinish","播完回收");
                if(IsFieldVisible(cue,"Duration"))yield return new GameplaySkillCardField("Duration",audio?"停止时限":"回收时限",true);
                yield return new GameplaySkillCardField("Origin","播放位置");
                if(IsFieldVisible(cue,"Binding"))yield return new GameplaySkillCardField("Binding","挂点（空为根）");
                if(IsFieldVisible(cue,"Follow"))yield return new GameplaySkillCardField("Follow","跟随位置");
            }
            if(values is GameplayFloatingTextData text){yield return new GameplaySkillCardField("Prefab","战斗结算飘字");yield return new GameplaySkillCardField("Duration","持续秒数");yield return new GameplaySkillCardField("FixedPoint","固定世界位置");if(!text.FixedPoint){yield return new GameplaySkillCardField("Binding","结果目标挂点");yield return new GameplaySkillCardField("Follow","跟随结果目标");}yield return new GameplaySkillCardField("RequestedHealing","治疗显示请求值");}
        }
        public bool TryReadDuration(object values,out string path,out float seconds){
            if(values is GameplayUnityAnimationData animation&&animation.Clip&&animation.Speed is GameplayConstantFloatValueAuthoring speed&&speed.Value>0){path=null;seconds=animation.Clip.length/speed.Value;return true;}
            path=null;seconds=0;if(values is GameplayPresentationCueData cue&&IsFieldVisible(cue,"Duration")&&cue.Duration is GameplayConstantFloatValueAuthoring duration){path="Duration";seconds=duration.Value;return true;}return false;
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
            if(values is GameplayPresentationCueData cue){
                bool audio=cue.Kind==GameplayPresentationCueAsset.CueKind.Audio;
                yield return new GameplayBusinessInspectorGroup(null,audio?"音频资源":"特效资源",null,audio?"Clip":"Prefab");
                yield return new GameplayBusinessInspectorGroup(null,audio?"播放位置":"位置 / 缩放 / 朝向",null,"Origin","Binding","Offset","Scale","Follow","MirrorFacing");
                if(audio)
                    yield return new GameplayBusinessInspectorGroup(null,"音频播放",null,"Volume","Pitch","AudioLoop");
                yield return new GameplayBusinessInspectorGroup(null,audio?"停止与清理":"回收与清理",
                    audio?"时限以秒计。到期停止音频，随技能清理可能提前停止。":"一次性粒子可以播完回收。循环或其它特效请用手动时限，随技能清理可以提前回收。",
                    "RecycleWhenParticlesFinish","Duration","OwnedByAbility");
            }
        }
        public string Summary(object values) {
            if(values is GameplayUnityEffectPresentationAuthoring effect)return (effect.StatusPrefab?effect.StatusPrefab.name:"无持续视图")+(effect.FirstApplicationVfx?" · 首次 "+effect.FirstApplicationVfx.name:"");
            if(values is GameplayPlayEffectCueOnOverflowAuthoring overflow)return "溢出声光 · "+(overflow.Prefab?overflow.Prefab.name:"")+(overflow.Clip?" "+overflow.Clip.name:"");
            if(values is GameplayFloatingTextData text)return "真实战斗结算：伤害 / 治疗 / 暴击 / 护盾\n"+(text.FixedPoint?"固定世界位置":(string.IsNullOrEmpty(text.Binding)?"结果目标根节点":"结果目标挂点 "+text.Binding)+(text.Follow?" · 跟随":" · 不跟随"));
            if(values is GameplayAnimationData animation)return animation.BackendName+" · "+animation.ClipLabel+" · 通道 "+animation.Channel+"\n"+(animation.Loop?"循环":"单次")+" · 速度 "+GameplaySkillFieldSchema.FloatSummary(animation.Speed);
            if(values is GameplayPresentationCueData cue)return (cue.Kind==GameplayPresentationCueAsset.CueKind.Audio?"音效":"特效")+" · "+(cue.Kind==GameplayPresentationCueAsset.CueKind.Audio?(cue.Clip?cue.Clip.name:"待选择音频"):(cue.Prefab?cue.Prefab.name:"待选择特效"))+"\n"+(cue.Origin==GameplayPresentationCueAsset.PositionSource.ContextPoint?"固定上下文地点":(cue.Origin==GameplayPresentationCueAsset.PositionSource.Owner?"施法者":"主目标")+" · "+(string.IsNullOrEmpty(cue.Binding)?"根节点":cue.Binding)+(cue.Follow?" · 跟随":" · 不跟随"))+"\n"+(IsFieldVisible(cue,"Duration")?"时限 "+GameplaySkillFieldSchema.FloatSummary(cue.Duration)+"s":"播完回收");
            return null;
        }
        public string NodeTypeName(object values)=>values is GameplayPresentationCueData cue
            ?cue.Kind==GameplayPresentationCueAsset.CueKind.Audio?"播放音频":"播放特效"
            :null;
        public IEnumerable<(string Name, GameplayLocalDefinitionPayload Value)> CreationPresets(Type valueType)
        {
            if (valueType != typeof(GameplayPresentationCueData)) yield break;
            yield return ("播放特效", new GameplayPresentationCueData {
                Kind=GameplayPresentationCueAsset.CueKind.Vfx,RecycleWhenParticlesFinish=true,OwnedByAbility=false
            });
            yield return ("播放音频", new GameplayPresentationCueData { Kind = GameplayPresentationCueAsset.CueKind.Audio });
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
        public bool IsFieldVisible(object values,string field)
        {
            if(values is GameplayUnityEffectPresentationAuthoring effect&&(field=="FirstApplicationOffset"||field=="FirstApplicationSeconds"))return effect.FirstApplicationVfx!=null;
            if(values is GameplayPlayEffectCueOnOverflowAuthoring overflow&&field=="Volume")return overflow.Clip!=null;
            GameplayPresentationCueAsset.CueKind kind;
            GameplayPresentationCueAsset.PositionSource origin;
            bool autoRecycle;
            if(values is GameplayPresentationCueData cue){
                if(field=="Kind")return false;
                kind=cue.Kind;
                origin=cue.Origin;
                autoRecycle=cue.RecycleWhenParticlesFinish;
            }else if(values is GameplayPresentationCueAsset shared){
                kind=shared.Kind;
                origin=shared.Origin;
                autoRecycle=shared.RecycleWhenParticlesFinish;
            }else return true;

            switch(field){
                case "Prefab": case "Scale": case "MirrorFacing": case "RecycleWhenParticlesFinish":
                    return kind==GameplayPresentationCueAsset.CueKind.Vfx;
                case "Clip": case "Volume": case "Pitch": case "AudioLoop":
                    return kind==GameplayPresentationCueAsset.CueKind.Audio;
                case "Binding": case "Follow":
                    return origin!=GameplayPresentationCueAsset.PositionSource.ContextPoint;
                case "Duration":
                    return kind!=GameplayPresentationCueAsset.CueKind.Vfx||!autoRecycle;
                default:
                    return true;
            }
        }
        public bool DrawField(SerializedProperty root,SerializedProperty field)=>false;
        public bool Supports(object values) => values is GameplayAnimationData || values is GameplayPresentationCueData || values is GameplayFloatingTextData;
        public IGameplaySkillPreviewSession Create(GameplaySkillDocumentAsset document, object values, float startSeconds)
        { var session = new GameplayAnimationPreviewSession(); session.UpdateValues(document, values, startSeconds); return session; }
    }
}
