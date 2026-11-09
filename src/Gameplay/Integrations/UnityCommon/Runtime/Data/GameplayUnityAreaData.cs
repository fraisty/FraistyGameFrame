using FRFrame.Gameplay.Authoring.Graph;
using FRFrame.Gameplay.Authoring;
using FRFrame.Gameplay.Standard;
using UnityEngine;

namespace FRFrame.Gameplay.Unity
{
    [System.Serializable, UnityEngine.Scripting.Preserve]
    public sealed class GameplayUnityAreaData:GameplayAreaDefinitionData
    {
        public GameObject Prefab;
        [Tooltip("圆形区域使用直径为 1 的预制体，以实际求值后的规则半径缩放。")]
        public bool FitCircleToRadius;
        [Min(0)] public float FadeSeconds;
        protected override IGameplaySpatialViewFactory CreateView(GameplayAuthoringBuildContext context)=>Prefab==null?null:new GameplayUnitySpatialViewFactory(Prefab,Plane,FitCircleToRadius,FadeSeconds);
        protected override void ValidateContent(GameplayAuthoringValidationContext v,string p)
        {
            base.ValidateContent(v,p);if(Prefab==null)v.Error(p+".Prefab","Scene area requires an actual prefab; use Logical Area for no view.");
            if(FitCircleToRadius && Shape!=GameplayAreaAuthoringShape.Circle)v.Error(p+".FitCircleToRadius","适配半径只支持圆形区域。");
            if(!float.IsFinite(FadeSeconds)||FadeSeconds<0)v.Error(p+".FadeSeconds","淡入淡出时间必须为非负秒数。");
        }
    
    }
}
