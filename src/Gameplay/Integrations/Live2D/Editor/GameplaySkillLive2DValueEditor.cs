using System;
using System.Collections.Generic;
using FRFrame.Gameplay.Editor.SkillAuthoring;
using UnityEditor;
namespace FRFrame.Gameplay.Live2D.Editor
{
    public sealed class GameplaySkillLive2DValueEditor : IGameplaySkillValueEditor, IGameplaySkillCardFieldProvider, IGameplaySkillValueDuration
    {
        public bool TryReadDuration(object values,out string path,out float seconds){path=null;seconds=0;if(values is GameplayLive2DAnimationData animation&&animation.Motion&&animation.Speed is FRFrame.Gameplay.Authoring.GameplayConstantFloatValueAuthoring speed&&speed.Value>0){seconds=animation.Motion.length/speed.Value;return true;}return false;}
        public IEnumerable<GameplaySkillCardField> CardFields(object values)
        {if(values is GameplayLive2DAnimationData){yield return new GameplaySkillCardField("ModelPrefab","Live2D 模型");yield return new GameplaySkillCardField("Motion","动作资源");}}
        public IEnumerable<Type> ValueTypes(Type resource) { if (resource.IsAssignableFrom(typeof(GameplayLive2DAnimationAsset))) yield return typeof(GameplayLive2DAnimationData); }
        public string Name(Type type) => type == typeof(GameplayLive2DAnimationData) ? "Live2D（需要已安装 SDK）" : null;
        public bool DrawField(SerializedProperty root, SerializedProperty field) => false;
    }
}
