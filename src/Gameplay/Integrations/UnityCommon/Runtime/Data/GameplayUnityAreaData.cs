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
        protected override IGameplaySpatialViewFactory CreateView(GameplayAuthoringBuildContext context)=>Prefab==null?null:new GameplayUnitySpatialViewFactory(Prefab,Plane);
        protected override void ValidateContent(GameplayAuthoringValidationContext v,string p)
        {base.ValidateContent(v,p);if(Prefab==null)v.Error(p+".Prefab","Scene area requires an actual prefab; use Logical Area for no view.");}
    
    }
}
